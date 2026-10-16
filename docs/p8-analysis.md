# P8 预分析：U2 雪崩根因与并发防护方案（soak 期间代码级分析，未动 8098/scale）

> 分析：Developer，2026-10-16 07:30–08:10。依据：代码实读 + Npgsql 10.0.3 默认值实测（tests/perf/pool-defaults.cs）+ Tester U2 事故报告 + S6 测量数据。
> 状态：**预分析（Reviewer 审 + Chief 拍板后进实施）**

## ① Npgsql 池耗尽的复现数学（U2 雪崩机理）

**实测默认值**（BasePgRepository 零覆盖，全部 Npgsql 默认）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| MaxPoolSize | **100** | 每连接串的池上限 |
| Timeout | 15s | 获取连接的等待上限 |
| CommandTimeout | 30s | 单命令超时 |
| ConnectionIdleLifetime | 300s | 空闲连接存活 |

**U2 雪崩的排队数学**（500 并发 × 用户全库查询）：

- 用户全库查询单发 **576ms**（P5 后，S6 实测）——500 并发同时到达
- 池 100 条连接 → 100 个查询并行执行，400 个排队等 `Timeout=15s`
- **但**：100 并行查询打 PG（PG max_connections=100，服务器进程独占）——PG 侧 100 活跃会话互相争 CPU/IO → **单查询从 576ms 恶化到数秒**
- 恶化后：15s 内完不成的查询堆积 → 池等待超时（`NpgsqlException: The connection pool has been exhausted`）→ **上层不释放已借出的连接**（查询还在跑）→ 池彻底干涸
- 后续一切请求（含认证的 token INSERT）排队等池 → **60s 超时雪崩**（Tester 观测的口径）→ 进程挂死

**关键放大器**：`DataSources` 字典按连接串共享——**7 个仓储类共用同一个池**（同连接串 = 同一个 NpgsqlDataSource）✓ 不是 7×100。但池 100 vs PG max_connections=100 是**贴脸配置**——池满时 PG 也满，无余量给运维/后台。

## ② 池扩容的配置面与代价

**改动点**：BasePgRepository.CreateConnection 的 NpgsqlConnectionStringBuilder（一行处）：

```csharp
var builder = new NpgsqlConnectionStringBuilder(cs)
{
    Pooling = true,
    MaxPoolSize = 200,          // 池上限（默认 100）
    // Timeout/CommandTimeout 保持默认（15s/30s）
};
```

**代价评估**：
- PG 侧 max_connections=100 是硬顶——池 200 会撞 PG 拒绝（`FATAL: sorry, too many clients`）。**必须同步扩 PG max_connections**（postgresql.conf，需重启 PG——部署窗口操作）
- 每连接 ~10MB PG 内存（work_mem 等会话内存）——200 连接峰值 ~2GB PG 内存
- **建议**：池 200 + PG max_connections 250（留运维余量）；或者**不扩池，做③的并发防护**（更根本）

## ③ 全库查询的并发防护（止损层——推荐主方案）

U2 的本质：**长查询（576ms+）× 高并发（500）= 池耗尽**。防护三选一：

**方案 A（推荐）：查询级并发信号量（semaphore throttle）**
- PgItemRepository.GetItems/GetItemList 入口加 `SemaphoreSlim(50)`（可配置）
- 超过 50 并发的查询在信号量排队（快速失败或等待 5s 后 503）
- **优点**：池永不耗尽（50 < 100 池上限）、PG 压力可控、实现 ~20 行
- **缺点**：高并发下部分请求 503（客户端重试）——但比 60s 雪崩挂死好得多

**方案 B：CommandTimeout 收紧 + 池等待快速失败**
- 池等待 Timeout 从 15s 收到 3s——快速失败不雪崩
- **缺点**：只是止损不防护，高峰期大量 3s 错误

**方案 C：全库查询的降级（LIMIT 强制 + 分页游标）**
- 用户全库查询强制 LIMIT 500 + 游标分页——576ms 的查询变 ~50ms
- **缺点**：语义变化（客户端要适配游标），改动面大

**建议组合**：A（信号量）+ B（池等待 3s）——防护 + 止损双保险，不动查询语义。

## ④ 2000 认证档的串行点（顺带分析）

认证链（CreateToken）：`RunInTransaction`（**每认证一个事务**）内三步：
1. `GetOrInsertDevice`（devices upsert，ON CONFLICT）
2. `INSERT INTO tokens`（RETURNING id）
3. `ResolveInternalUserId`（users 查找）

**串行点定位**：
- **事务持有连接**：RunInTransaction 借出连接直到 commit——2000 并发认证 = 2000 连接需求，池 100 → 1900 排队
- **devices 的 ON CONFLICT 行锁**：同 DeviceId 的并发认证在 devices 行上串行（upsert 锁）——U1 用不同 DeviceId 所以没撞
- **token INSERT 无锁问题**（独立行），但**事务包裹放大了连接持有时间**（三步串行 ~5-10ms/认证 × 池排队）

**2000 档 P95 21.3s 的构成**：池排队（1900/100 × 平均等待）主导——**不是 DB 慢，是池不够**。③的信号量方案同样覆盖认证（或认证单独加池优先级）。

## 实施清单（拍板后执行）

| # | 改动 | 文件 | 风险 |
|---|---|---|---|
| 1 | 池参数显式化（MaxPoolSize=200 + 注释） | BasePgRepository.cs | 低（需同步 PG max_connections） |
| 2 | 查询信号量（50 并发上限，可配置） | PgItemRepository.Query.cs | 低（~20 行） |
| 3 | 池等待快速失败（Timeout 15s→3s） | BasePgRepository.cs | 低（一行） |
| 4 | 认证事务瘦身（三步合并或去事务） | PgAuthenticationRepository.cs | 中（事务语义） |
| 5 | PG max_connections 100→250 | postgresql.conf | 部署窗口（PG 重启） |

**验收**：U2 复跑（500 并发全库）——零雪崩、错误率 <5%（503 可接受）、恢复时间 <5s；U1 2000 档 P95 <5s。

---

## 实施后备注（Reviewer 审查意见入档）

**门的覆盖面清单**（Reviewer 记录在案①——后人加新查询入口时按此清单接线）：

| 入口 | 进门？ | 理由 |
|---|---|---|
| GetItems | ✅ 公共面包门 → GetItemsCore | 主列表入口 |
| GetItemList | ✅ 公共面包门 → GetItemListCore | 主列表入口（GetItems 内部委托走 Core 不双占） |
| GetItemValues（Aggregates） | ✅ 包门 → Core | 1M 规模重查询入口（Reviewer 备注②：不留第二个 U2 形态） |
| GetCount | ❌ 不进 | 轻查询（count），池占用短 |
| GetItemIds | ❌ 不进 | 主要服务扫描侧，非用户请求路径 |
| RetrieveItem（单条） | ❌ 不进 | 单条水合，池占用极短 |
| 认证/写入（Create/Update/Save） | ❌ 不进 | 写路径短事务，信号量 50 < 池 100 留余量 |

**接线模式**：新入口一律 `WithQueryGate(() => XxxCore(...))`——公共面包门、Core 裸奔、内部委托走 Core。**禁止**在 Core 内再调另一个进门入口（自死锁）。

**最坏路径有界**（Reviewer 记录在案②）：门等 3s + 池等 3s = 6s 上限，可接受。
