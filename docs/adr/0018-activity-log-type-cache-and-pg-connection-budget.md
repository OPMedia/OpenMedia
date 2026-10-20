# ADR 0018：活动日志类型字典缓存化与 PostgreSQL 连接预算自检

- 状态：已接受
- 日期：2026-10-20
- 影响面：`src/Emby.Server.PgData/PgActivityRepository.cs`、`src/Emby.Server.PgData/BasePgRepository.cs`、`MediaBrowser.Tests/PgData/ActivityLogTypeCacheTests.cs`
- 上游依据：`docs/perf-baseline.md` §6（pg_stat_statements 慢查询 Top-20）、`docs/p8-analysis.md` ①（连接池三参数的取值依据）
- 不变量来源：`docs/spec/core.md` 的「单写路径 + PG 唯一存储」约束（活动日志仍每次落库，只缓存**类型字典**）

## 背景：全量回归取证挖出的两个热点

2026-10-20 的全量回归（Release / `openmedia_perf` 195k 条目 / 60s 负载）在
`pg_stat_statements` 上暴露两处问题，都不是「慢查询」意义上的慢，而是**并发下的结构性串行化**。

### 问题 1：活动日志类型字典的 UPSERT 热点（Top-1）

```
INSERT INTO activity_log_types (name) VALUES ($1)
  ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name RETURNING type_id
calls=36567  mean=77.19ms  max=8715.3ms  total=2822702.4ms
```

同一批负载里真正的日志写入 `INSERT INTO activity_log` 只有 `mean=0.27ms`。
也就是说**每写一条活动日志，99% 以上的时间花在取类型 id 这一步**。

根因：`ON CONFLICT ... DO UPDATE` 会对冲突行取**排他更新锁**。活动类型名是
`UserActivity` / `AuthenticationSucceeded` 这类低基数枚举，所有事务都写同一行，
于是并发写彼此排队，锁等待被计入语句耗时——`max=8.7s` 就是排出来的。

而这条 UPSERT **完全没必要**：字典表是低基数（实测全库只有 4 种类型），
`type_id` 由 identity 分配后终身不变。

### 问题 2：应用连接池上限 ≥ PG 服务端 `max_connections`

本机 PG `max_connections=100`，应用侧 `MaxPoolSize` 默认也是 100
（`OPENMEDIA_PG_MAX_POOL_SIZE`，依据见 `docs/p8-analysis.md` ①）。
两者相等意味着**给 psql / 监控 / 其它客户端的余量为 0**。
并发认证负载（L4，20 并发）下服务端直接报：

```
Npgsql.PostgresException (0x80004005): 53300: sorry, too many clients already
```

表现为零星 500（10077 个请求里 4 个 500），日志里就是这条。

## 决策

### D1：类型字典 name → type_id 走进程内缓存

`PgActivityRepository` 增加 `ConcurrentDictionary<string,int>` 缓存：

- 命中 → **零 SQL** 直接返回 type_id
- 未命中 → 无锁 `SELECT type_id FROM activity_log_types WHERE name=@name LIMIT 1`
- 仍不存在 → `INSERT ... ON CONFLICT (name) DO NOTHING RETURNING type_id`
  （用 `DO NOTHING` 不用 `DO UPDATE`：不升级排他更新锁，并发下只等冲突事务结束）
- `DO NOTHING` 未返回行（别人先插了）→ 回查胜者的 id

语义等价性：`name` 唯一索引保证字典行唯一存在，`DO UPDATE` 原本也只是把 name 写成
同值（并 bump 一个死元组），改成 `DO NOTHING` 后行为等价且更省。

### D2：缓存必须按 `search_path` 分桶

集成测试每个用例用独立 schema（`pgtest_xxx`），各 schema 的
`activity_log_types` 序列独立、**同名 type 会得到不同 type_id**。
不分桶就会把 A schema 的 type_id 用到 B schema 上 → 外键违约。

故缓存键 = `search_path + "|" + type 名`，`search_path` 取自
`NpgsqlConnectionStringBuilder.SearchPath`（连接串维度，与测试的隔离方式一致）。

`ActivityLogTypeCacheTests.DistinctSchemas_UseDistinctTypeIds` 专门守这条：
A schema 先插一个 `OtherType` 把 `UserActivity` 的 id 顶到 2，B schema 的
`UserActivity` 是 1；若缓存串味，B 会拿到 2 并触发外键违约。
**已做反向验证**：临时去掉分桶后该用例确实变红。

### D3：连接预算只告警，不改默认值

在 `BasePgRepository.GetDataSource()` 建池后异步探测服务端 `max_connections`：

- 池上限 ≥ `max_connections` → `Error`（必然 53300）
- 池上限 + 保留(3) + 其它客户端垫(10) > `max_connections` → `Warn`（余量不足）
- 探测失败 → `Debug`，不影响启动

**不擅自下调 `MaxPoolSize` 默认值**：连接池容量是容量承诺的一部分
（`docs/p8-analysis.md` ① 记录了 100 这个数的依据），改默认值等于替所有部署
改容量口径。该决策属部署侧（上调 PG `max_connections` 或下调
`OPENMEDIA_PG_MAX_POOL_SIZE`），代码侧只负责把问题喊出来。

### D4：`/Items` 的 Guid Empty → null 字段不改

`contract-check.ps1` 长期报 `/Items` 返回含 `ChannelId/ParentId/SeriesId/...` 的 null 字段。
`docs/test-report.md` §5 已把它记为**已知接受差异**（Guid Empty 输出 null 而非省略，
语义等价，前端 falsy 判断不受影响）。改动会影响 JSON 契约与前端，本轮**不动**，
维持现状并在契约报告里保留该条 FAIL 作为可见的已知差异。

## 结果（实测，同一台机 / Release / `openmedia_perf` 195k / 60s 负载）

| 指标 | 修复前 | 修复后 |
| --- | --- | --- |
| `activity_log_types` UPSERT 调用数 | 36567 | **0**（缓存全命中） |
| 该语句 mean / max | 77.19ms / 8715.3ms | — |
| 该语句累计耗时 | 2822.7s | **0.055s**（仅 2 次冷启动 SELECT，mean 0.027ms） |
| `INSERT INTO activity_log` mean | 0.27ms | 0.24ms（写入路径未变慢） |
| `activity_log` 累计行数 | — | 90604（**日志未丢**） |
| `activity_log_types` 行数 | — | 4（印证低基数，缓存命中率≈100%） |

L4 认证档位 20 并发下 500 归零（修复前 10077 请求里 4 个 500 → 0）。

## 后果与风险

- **缓存不失效**：`type_id` 稳定，只有手工改 `activity_log_types` 的 name→id 映射才会失效
  （正常流程不会发生：id 由 identity 分配、name 唯一且不可变）。已加 256 条上限防异常输入膨胀。
- **多实例部署**：各实例独立缓存，但读到的是同一个字典表的同一批 id，一致。
- **测试隔离**：D2 的分桶保证独立 schema 不串味，有反向验证。
- **连接预算告警**只在建池那一刻打一次；调小 `max_connections` 后需重启才复检。

## 复现方式

```powershell
# 取证（负载前重置）
psql -h localhost -p 5432 -U postgres -d openmedia_perf -c "SELECT pg_stat_statements_reset();"
powershell -ExecutionPolicy Bypass -File tests\perf\run-ab-load.ps1 -Tag release -Db openmedia_perf -User perf-user-001 -Seconds 60
psql -h localhost -p 5432 -U postgres -d openmedia_perf -c "SELECT calls, mean_exec_time, total_exec_time FROM pg_stat_statements WHERE query LIKE '%activity_log_types%';"

# 单元/集成回归
dotnet test MediaBrowser.Tests -c Release --filter "FullyQualifiedName~ActivityLogTypeCacheTests"
```
