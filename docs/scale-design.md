# OpenMedia 规模化与内存设计（硬约束：媒体库 100 万条 / 用户 1 万+）

> 依据：用户规模目标（2026-10-15 群聊点名"内存会不会泄露检查，未来媒体库可能达到 100w，用户 1w 以上"）
> 代码证据核验日期：2026-10-15（Architect 实读 LibraryManager/SessionManager/ImageProcessor/ApiEntryPoint/BasePgRepository）
> 定位：五期核心设计；四期在飞任务（T4.7 守卫、3 个 500、部署窗口）不受影响照旧

## 0. 目标与约束

| 项 | 当前 | 目标（硬约束） |
|----|------|----------------|
| 媒体条目 | 34 | **1,000,000** |
| 用户 | 1（admin） | **10,000+** |
| 已知架构债 | LibraryItemsCache 无逐出（D3 诊断） | 容量上限 + 逐出 + 回源 |
| 内存泄漏检测 | 无方法论 | 三件套脚本化、可复现 |

**总验收**：warmup 后内存曲线斜率 ≈ 0（soak 4h）；嫌疑类型零单调增长（堆快照 diff）；1M 库列表/详情/搜索全链路可用且 P95 达标（S6 定数）；10k 用户登录风暴错误率 < 1%；连接池零耗尽。

---

## 1. 泄漏检测方法论（三件套，脚本入库 tests\perf\）

### 1.1 堆快照 diff（dotnet-gcdump）

```
warmup（一轮混合负载）→ gcdump #1 → N 轮负载（扫描+刷新+浏览+播放+转码）→ gcdump #2
→ dotnet-gcdump analyze 双 dump diff → 按 (类型, 计数增量) 排序
```

判定：嫌疑类型（§2 清单）计数增量与负载轮数成**线性** = 泄漏；持平 = 正常缓存。非嫌疑类型出现单调增长 = 新发现，入嫌疑清单。

### 1.2 soak 长跑

混合负载循环 ≥ 4h（扫描进行中 + 分页浏览 + 图片 + 流式 + 3 路转码 + 播放上报），每 5min 采样：GC 堆（gen0/1/2）、LOH 大小、Working Set、GC 暂停时间。**曲线判定**：warmup（前 30min）后平台期 = 通过；持续爬升 = 泄漏定位到 1.1 复核。

### 1.3 dotnet-counters 实时监控

`dotnet-counters monitor`：System.Runtime（GC/线程池/异常率）+ Npgsql（active/pooled connections——**连接泄漏直接可见**）+ ASP.NET Core（current requests）。soak 与 k6 全程挂表。

三件套全部脚本化（leak-check.ps1 / soak.ps1 / 计数器配置），Reviewer 可复现。

---

## 2. 已知嫌疑清单（代码证据 + 处置）

| # | 嫌疑 | 代码证据 | 1M/10k 下的风险 | 处置 |
|---|------|---------|----------------|------|
| 1 | **LibraryItemsCache** | LibraryManager.cs:145 `ConcurrentDictionary<Guid, BaseItem>`，:308 AddOrUpdate、:448 TryRemove——**无逐出、无容量上限** | 1M × BaseItem（196 公有成员，含图片字典/列表）≈ 估 1-2KB+/条 → **1-2GB+ 常驻**，且只增不减（除删除） | **S2 头号**：容量上限（如 100k）+ LRU 逐出 + 未命中回源 DB（GetItemById 已有 DB 路径，先验证回源正确性）；T4.9 的写后逐出/TTL 并入 |
| 2 | **SessionManager._activeConnections** | SessionManager.cs:71 `ConcurrentDictionary<string, SessionInfo>` | 10k 用户 × 多设备 → 数万 SessionInfo（持 NowPlayingItem/TranscodingInfo/AdditionalUsers） | **S4**：空闲超时清理（LastActivityDate 已有字段，查清理任务是否存在）+ 容量策略 |
| 3 | **ImageProcessor._locks** | Emby.Drawing/ImageProcessor.cs:863 `Dictionary<string, LockInfo>` 按图片路径键 | 1M 条 × 图片类型数 → 无上界锁条目 | **S3**：用后移除（finally）或 lock striping（固定桶数） |
| 4 | **ApiEntryPoint._transcodingLocks** | ApiEntryPoint.cs:42 `Dictionary<string, SemaphoreSlim>` | 转码会话累积（移除路径待查证） | **S3**：同上；_activeTranscodingJobs（:40）已有 BUG-25 kill timer 清理，soak 复验 |
| 5 | **水合路径** | PgItemRepository.cs:1030 逐行全量 JSONB 反序列化 | 1M 库列表页 50 条 × 全量 blob → **CPU+GC 双杀**；P7 从"五期再说"升必做 | **S6 先测量后实施**：列表页按 DtoOptions 需要的字段做 partial hydration 或投影直出 DTO |
| 6 | **DtoService 中间对象** | BaseItemDto 161 公有成员 × 每请求 N 条 | gen0/1 GC 压力；大数组（图片 tags）LOH 风险 | soak 观测 GC 暂停；必要时 DTO 池化（低优先，S8 后评估） |
| 7 | **WebSocket 广播扇出** | SessionsStart 全订阅者广播会话变更 | 10k 会话 × 每变更广播 → **O(N²) 风险** | **S7 压测坐实**；若坐实属架构级（评估是否六期） |
| 8 | **Npgsql 连接池** | BasePgRepository.cs:58 Pooling=true，无显式 MaxPoolSize → 默认 100 | U2 实证雪崩链：500 并发长查询 → 池 100 **贴脸** PG max_connections=100（7 仓储共用一个池）→ 15s 池等待超时连接不释放 → 池干涸 60s 雪崩；2000 认证=排队主导（RunInTransaction 三步串行持连接） | **✅ 定案（P8，2026-10-16）**：主方案=查询级信号量（PgData 查询入口，50 并发可配置）+ 池等待 15s→3s 快速失败——不扩池、不动查询语义、雪崩结构上不可能；池扩容（需 PG 重启+每连接 10MB）等信号量上线后看真实需求。详见 docs/p8-analysis.md；Architect 评审护栏见 §5 S9 |

---

## 3. 100 万条 fixture 生成策略（S5，✅ 已落地 as-built 2026-10-16，commit `224464ebf8`）

**BR-45 方法论教训直接适用：祖先分布必须贴近真实库形态，否则 EXPLAIN/负载结论全部失真。**

- **分布模型**：~1000 库（CollectionFolder）× 每库 ~1000 系列/集合 × 深度 3-4 层（库→系列→季→集）
- **类型配比**（实际落地）：Movie 20w / Episode 50w / Audio 15w / MusicAlbum 5w / Season 5w / Series 2w ≈ 102w 条 + 880k 占位文件（30 万目录，11.5 分钟生成）
- **blob 形态**：真实 Movie/Episode/Audio 序列化模板（phase2 R5 教训）+ 字段随机化（名字/年份/路径）
- **生成方式（as-built 关键变更）**：`tests\perf\prepare-scale-full.ps1` 九步管线固化可复现；独立库，测完即弃，**严禁碰共享库**（安全红线）
- **⚠️ 三条 as-built 铁律（catch-22 解法，D2 实证）**：
  1. **fixture 建库严禁依赖库扫描**——扫描需要 PhysicalFolderIds、PhysicalFolderIds 又要扫描填充（catch-22）；且扫描对假路径条目按 missing 级联删除（验证期两次事故：57w+13w 行，均全量重建恢复）。**正解：CollectionFolder 的 data jsonb 直写 CollectionType + PhysicalFolderIds 持久化**（对齐 8096 实测形态，运行时从 data 加载），再挂 UserRootFolder（视图生成前提）
  2. **top_parent_id 统一 N 格式**（无连字符）——查询参数是 `Guid.ToString()` 的 N 格式，DB 存带连字符格式永远不匹配（静默 0 结果陷阱）；Episode 的 top_parent 必须指向 folder 而非 lib（祖先链全量修正）
  3. **占位文件必须先于任何扫描在位**——880k 假媒体文件是"扫描零删除"的前提；psql LIKE 反斜杠转义（`'\%'`=字面 %，UPDATE 匹配 0 行静默失败）和 PowerShell 字符串转义两个坑已记入 commit message
- **终验口径**：用户作用域全类型查询 6/6 全通（Movie 200,000 / Episode 500,000 / Audio 150,000 / MusicAlbum 50,000 / Series 20,000 / Season 50,000），DB 零删除（1,102,010 行）

---

## 4. 1 万用户压力方案（S7）

| 场景 | 设计 | 关注 |
|------|------|------|
| L5 登录风暴 | 1w 用户 10min 内登录（≈17/s，早高峰形态） | L4 基线 P99 402ms @ 20 并发——密码哈希 CPU 密集；处置选项：哈希迭代数配置化（**安全权衡需 Chief 拍板**）、登录限速、会话缓存 |
| L6 混合浏览 | 1w 已认证会话（token 复用）并发分页/详情/搜索/图片 | 连接池排队、水合 CPU、缓存命中 |
| L7 播放上报风暴 | Sessions/Playing POST 并发 + WebSocket 长连接 1w | 广播扇出 O(N²) 坐实/排除、句柄与内存 |

用户 fixture：1w 用户 + token 预生成（脚本幂等，独立库或共享库用户表——用户表跨实例共享但无根 GUID 同构问题，写前对账计数）。

---

## 5. 任务清单（五期轨道 S1–S8）

| 任务 | 内容 | 依赖 | 工作量 | 验收 |
|------|------|------|--------|------|
| S1 | 泄漏三件套脚本入库 + 当前规模基线轮 | — | 0.5d | 脚本可复现；34 条/1 用户基线数据入档 |
| S2 | LibraryItemsCache 容量上限+LRU+回源 | S1（基线） | 1.5d | 上限生效；未命中回源正确（矩阵回归）；命中率报告 |
| S3 | _locks/_transcodingLocks 泄漏点修复 | — | 0.5d | 堆快照 diff 该类型零增长 |
| S4 | 会话空闲清理+容量策略 | — | 1d | soak 中空闲会话被清；活跃会话不受影响 |
| S5 | 1M fixture 生成器（真实祖先分布） | — | 1.5d | 100w 条入库独立库；分布参数可复现 |
| S6 | 1M 全链路测量（EXPLAIN 复跑+水合占比+缓存命中）→ P7 实施决策 | S5 | 1.5d | 测量报告；P7 改造范围定案 |
| S7 | 10k 用户 k6 三场景 | S5（用户 fixture） | 1d | L5/L6/L7 数字入档；池零耗尽 |
| S8 | soak 4h 收口 + 堆快照 diff 终验 | S2–S4, S6 | 1d | §0 总验收全过 |
| S9 | **P8 实施**（池防护）：查询信号量 + 池等待 3s + 池参数显式化 + 认证事务瘦身 | Reviewer 审 + Chief 拍板（✅ 已倾向③） | 1d | U2 复跑零雪崩、错误率 <5%（503 可接受）、恢复 <5s；U1 2000 档 P95 <5s；正常负载（L1 暖 21ms）零回归。**Architect 护栏四条**：① 信号量单一全局门（7 仓储一个池→一个门，可配置默认 50，不按仓储 ×7）；② 只守 DB 执行点不包整个请求管道；③ 实施前验证持门槽位内无嵌套二次取连接（防死锁）；④ 503 语义进中间件映射 + U2 复跑进负路径用例 |

**分工建议**：S1/S3 先行（小）；S2 独立大项；S5 生成器给一人专做（S6/S7 都等它）；S8 收尾全员回归。四期部署窗口与 L1b 复测不受影响，先行。

---

## 6. 风险登记

| 风险 | 应对 |
|------|------|
| R1 LRU 逐出改变 GetItemById 语义 | 回源路径先做正确性验证（矩阵全量回归）再上上限 |
| R2 1M fixture 生成时长/磁盘（~5GB） | 分批提交；生成器幂等可断点续跑 |
| R3 广播 O(N²) 坐实 | 架构级改动（订阅过滤/分片广播）可能溢出五期 → 六期立项，S7 只负责坐实 |
| R4 密码哈希降迭代是安全权衡 | Chief 拍板，不默认降 |
| R5 partial hydration 动 DtoOptions 填充链 | 回归面大，S6 测量数据决定改造深度 |
| R6 10k 用户写共享库用户表 | 写前对账；用户表无根 GUID 同构问题（安全红线不适用），但计数断言要防漂移 |
