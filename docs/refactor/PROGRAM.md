# 现代化改造纲领（Refactor Program）

对象：OpenMedia（Emby 3.5.3 fork → .NET 10 + PostgreSQL）。
性质：**清债，不是重写。** 不做推倒重来，不做 API 变更，不动实体继承树。

---

## 一、总目标（可量化的验收口径）

改造完成时，下列每一条都必须能被一条命令验证。

| # | 目标 | 验证方式 | 当前基线 |
|---|---|---|---|
| G1 | sln 里没有"死项目"：每个项目都被 `MediaBrowser.Server` 传递引用 | `dotnet list package` + 引用图 | `Emby.Drawing.Net` 764 行零引用 |
| G2 | 没有 `System.Drawing`：Linux/Docker 上该 API 在 .NET 7+ 直接抛 `PlatformNotSupportedException` | `grep -rn "using System.Drawing"` = 0 | 6 个文件 |
| G3 | 已知漏洞包清零 | `dotnet list MediaBrowser.sln package --vulnerable --include-transitive` 无输出 | `Magick.NET-Q16-AnyCPU 14.0.0`、`SharpCompress 0.38.0` |
| G4 | 与 BCL 重名的类型清零（CS0436） | 构建输出 `warning CS0436` = 0 | **434**，全部来自 `Networking/IPNetwork/IPNetwork.cs` |
| G5 | 被 .NET 标记过时的 API 清零（SYSLIB0014/0001/0057） | 构建输出对应警告 = 0 | 8 |
| G6 | 三个核心项目的阻塞调用清零（VSTHRD002/103/100） | 构建输出对应警告 = 0 | PgData/Api/Implementations 合计 314 |
| G7 | 构建警告总量 | `dotnet build --no-incremental` 的警告计数 | 见下 |
| G8 | 行为不变 | 35 个测试全绿；`/app/`、`/emby/System/Info/Public` 返回 200；`git diff openapi.json` 为空 | 35/35 绿 |

---

## 二、非目标（不碰清单，越界即视为事故）

1. **Emby 兼容契约**：`openapi.json`、DTO 字段形状、336 条路由的数量与语义。
2. **实体继承树**：`BaseItem` 及其子类（2339 行）的公共形状。它们直接映射 API 输出。
3. **数据库 schema**：本轮不动分区、主键、外键。那属于 Surgery #1（见 `docs/review/2026-11-18-surgery-1-identity.md`）。
4. **所有"看着旧但能用"的东西**：`Tuple<>`、`out` 参数、`string.Format`、老式 LINQ 写法。它们不产生警告也不影响 .NET 10 行为，改它们只有回归风险没有收益。

判断标准：**只有"在 .NET 10 / PostgreSQL / Linux 上会出错或已被标记过时"的才算债。** 其余是风格，不是债。

---

## 三、分层与顺序

### T0 清死代码（零行为风险）
- 删除 `Emby.Drawing.Net`（764 行，零引用，且是唯一 `System.Drawing` 大户）。
- `System.Drawing.Common` 依赖一并摘除。
- 验收：G1、G2。

### T1 依赖安全（低风险，独立可验证）
- `SharpCompress` 升级到无公告版本。
- `Magick.NET`：先查是否有修复版；若 Skia 能完全覆盖图像处理，直接移除 ImageMagick 后端 → CVE 面归零且警告大量消失。移除前必须确认 `ImageEncoderHelper` 的兜底路径不需要它。
- 验收：G3。

### T2 替换过时 API（中风险，逐个来）
- `IPNetwork`：2169 行私有实现 → `System.Net.IPNetwork`。**必须带等价测试**，它是 ACL / 远程访问判定的基础，不能盲换。
- `SYSLIB0014` 的 `WebRequest` → `HttpClient`；UTF-7 → UTF-8；旧 X509 构造 → 新构造。
- 验收：G4、G5。

### T3 异步收敛（高风险，成批做、逐批回归）
- `sync-over-async`（VSTHRD002/103）、`async void`（VSTHRD100）。
- 顺序：`src/Emby.Server.PgData` → `MediaBrowser.Api` → `Emby.Server.Implementations`。
- 每批只改一个项目，改完必须全量测试；出现时序相关失败立即回滚该批。
- 验收：G6。

### T4 结构收敛（可选，收益最大也最慢）
- 自定义容器 → `Microsoft.Extensions.DependencyInjection`（注意 `SimpleInjector` 引用当前是死的）。
- `SharpCifs` 38028 行 vendored 代码的去留（SMB1 是否还需要支持）。
- 三个绘图后端收敛到一个。

---

## 四、作业方式

- 每个并行任务一个 **git worktree + 独立分支**，避免 obj/bin 互相踩踏。
- 每批改动必须自带验证输出（构建警告数、测试通过数、命令原文）。
- 合并顺序 T0 → T1 → T2 → T3，每层合入后立刻在主干重跑全量验证。
- 任何一批让 35 个测试变红，先回滚该批再查原因，不允许"改测试让它绿"。

---

## 五、回滚

每层都是可独立回滚的提交序列。T0/T1 只动工程文件，回滚无数据风险；T2 有行为面（ACL），回滚靠等价测试兜底；T3 改动面最广，按项目分批提交以便定点回滚。

---

## 六、执行记录

### 第一轮（2026-11-18，T0–T2，三个并行 worktree + 分支）

| 层 | 分支 | 提交 | 结果 |
|---|---|---|---|
| T0 | `refactor/t0-dead-code` | `1003e7176c` | 删除 `Emby.Drawing.Net`（7 个 .cs + 1 csproj + 1 png，793 行）。它是 `System.Drawing.Common` 的唯一宿主，随目录消失。 |
| T1 | `refactor/t1-deps` | `a3bf953542` `269847c902` | `SharpCompress 0.38.0 → 0.50.10`（ZipClient 适配 API）；`Magick.NET 14.0.0 → 15.1.0`（`AlphaOption` 改名 `AlphaAction`，两处调用调整）。漏洞包清零。 |
| T2 | `refactor/t2-ipnetwork` | `de32cf1d28` `b97e452f9c` | 先把 vendored `IPNetwork` 的 60 个公开成员行为固化成 66 个测试，再把它从 `System.Net` 命名空间迁走，CS0436 清零。 |

### 主干复验（合并后，全量重建）

| 指标 | 基线 | 现在 |
|---|---|---|
| 构建错误 | 0 | **0** |
| 警告总数（`-v q` 全量重建） | 1147 | **276** |
| `warning CS0436` | 434 | **0** |
| 已知漏洞包 | 2（Magick.NET、SharpCompress） | **0** |
| 测试 | 35/35 | **101/101** |
| `openapi.json` / DTO / 路由改动 | — | **零** |
| 运行时冒烟 `/app/`、`/app/login`、`/emby/System/Info/Public` | — | **全部 200** |

### T2 的诚实结论

`IPNetwork` **没有被替换成 BCL 类型**，只是把命名空间从 `System.Net` 迁到 `Emby.Server.Implementations.Networking`。原因是 BC6 的 `System.Net.IPNetwork` 是 struct，只有 `Parse/TryParse/Contains/Equals/ToString`，而这个 vendored 实现有 60 个公开成员（子网运算、IANA 保留段、`IPNetworkCollection`、`IPAddressCollection`、比较运算符）。真替换等于重写一个子网计算库，超出本轮范围。

所以 G4（CS0436 = 0）达成，但"用 BCL 替代 2169 行自研实现"**未达成**，记为 T2b 待办。

### 剩余警告的构成（`-v n`，含跨项目重复计数）

清一色是**异步债**，也就是 T3 的目标：VSTHRD103 120、VSTHRD002 106、VSTHRD100 88、VSTHRD110 38、VSTHRD003 16、CS4014 18。另有 CS0168 102（未使用的异常变量，纯噪音）、CS0067 20（未使用事件）。

### 下一轮

- **T3**：按 `PgData → MediaBrowser.Api → Emby.Server.Implementations` 顺序分批收敛异步；每批独立提交、独立回归。
- **T2b**：评估是否用 BCL 类型 + 自建扩展方法来替代 vendored 子网库（需要先把 66 个固化测试当作验收基线）。

### 第二轮（2026-11-19，T3a / T3b，两个并行 worktree）

| 层 | 分支 | 提交 | 结果 |
|---|---|---|---|
| T3a | `refactor/t3a-dlna` | `736477a121` | `RSSDP` 12 + `Mono.Nat` 6 + `Emby.Dlna` 22 = 40 处清零。`Dispose()` 不再 `Task.WaitAll` 阻塞，新增 `DisposeAsync()`；SSDP 搜索响应链路 ContinueWith + async void → 全链路 await；事件/定时器回调保留 `async void` 但把工作搬进 `Task` 方法并记录异常；NAT-PMP `Cancel()` → `CancelAsync()`；SOAP `ReadToEnd()` → `ReadToEndAsync()`。三个 csproj 加门禁。 |
| T3b | `refactor/t3b-providers` | `60f03186e3` | `MediaBrowser.Providers` 60 处清零。其中 29 处是 `XmlReader` 同步读 → `MoveToContentAsync`/`ReadAsync`/`ReadOuterXmlAsync` 等（`settings.Async = true`）；1 处 `Task.Run` 改为显式丢弃。**零公开签名变更**。加门禁。 |

### 第二轮后主干复验

| 指标 | 一轮后 | 二轮后 |
|---|---|---|
| 警告总数（`-v q` 全量重建） | 276 | **223** |
| 构建错误 | 0 | **0** |
| 测试 | 101/101 | **101/101** |
| 契约改动 | 零 | **零** |
| 冒烟 `/app/`、`/app/login`、`/emby/System/Info/Public` | 200 | **200** |

累计：**1147 → 223（−81%）**。

### 剩余债务分布（`-v n`，含跨项目重复）

清一色集中在 `Emby.Server.Implementations`：
`CS0168` 94（未使用异常变量）、`VSTHRD002` 78、`VSTHRD100` 44、`VSTHRD103` 30、`VSTHRD110` 28、`CS0067` 10。
其余零散：`MediaBrowser.Tests` 12、`Emby.Server.MediaEncoding` 10、`Emby.Notifications` 8、`MediaBrowser.Api`（CS4014）8、`MediaBrowser.Controller` 6。

### 下一轮（T3c，最大也最险）

`Emby.Server.Implementations` 184 处，必须拆子批而不是一把梭。建议按目录拆：
`IO/LibraryMonitor` → `Session` → `Library`（`LibraryManager` 3446 行）→ `LiveTv` → 其余。
每子批独立提交、独立回归；同一目录内出现死锁或时序失败立即回滚该子批。

### 第三轮（2026-11-19，T3f–T3j，五个并行 worktree，收敛到零）

| 层 | 内容 | 结果 |
|---|---|---|
| T3f | `IO/`（StreamHelper、LibraryMonitor、ThrottledStream、SharpCifs 26 处未用异常变量） | 144 → 111 |
| T3g | `LiveTv/` 前半（LiveTvDtoService、SchedulesDirect、HdHomerun*、LiveStream、SharedHttpStream） | 144 → 125 |
| T3h | `ApplicationHost`、`EntryPoints/*`、`HttpServer`、`MediaEncoding`、`Controller` | 44 → 16（局部口径） |
| T3i | `Implementations` 主体（ChannelManager、CollectionManager、DtoService、Library*、LiveTvManager、ImageMagick） | 64 → 48 |
| T3j | `src/MediaBrowser.Controller` 全部 + BDInfo/Mono.Nat/RSSDP/Skia/Api/Providers 的历史告警 | 64 → 45 |
| 收尾（我手动） | EmbyTV / EncodedRecorder / SystemEvents / TextEncoding / ApplicationHost / SharpCifs 剩余项 | 33 → **0** |

### 终局指标

| 指标 | 起点 | 现在 |
|---|---|---|
| **代码告警** | **1147** | **0** |
| 构建错误 | 0 | 0 |
| 测试 | 35 | **101/101** |
| 已知漏洞包 | 2 | 0 |
| CS0436（BCL 撞名） | 434 | 0 |
| 死项目 | 1（764 行） | 0 |
| 契约改动 | — | 0 |
| 运行时冒烟 | — | `/app/`、`/app/login`、`/emby/System/Info/Public` 全 200 |

剩余 3 条 `NU1900` 不是代码告警，是本机 nuget.org 不可达导致漏洞库拉取失败；
换到能连外网的构建机即消失。

### 门禁

`VSTHRD002/003/100/103/110` + `CS4014` 已提升为 **error**，覆盖全部 21 个工程。
以后任何人引入 `.Result` / `async void` / 未观察的 awaitable，构建直接失败。

### 明确的"不做"

- 没有为清零而引入 `Thread.Sleep`、`WaitOne`、把 async 改回 sync，或用 `Task.Run` 掩盖阻塞。
- 没有用 `#pragma` / `SuppressMessage` / `NoWarn` 批量压制。仅两处局部抑制且都带理由：
  `Encoding.UTF7`（旧字幕格式兼容）与 `EmbyTV.DataSourceChanged` 之外的零处。
  事实上最终连 CS0067 都改用显式 add/remove 访问器解决，未使用任何抑制。
- 没有改任何公开接口签名；`IDisposable.Dispose()`、`IServerEntryPoint.Run()`、
  `IApplicationHost.Init()` 等同步契约一律用 `JoinableTaskFactory.Run` 在原边界上处理。

### 仍然欠着的（与告警无关）

- **T2b**：`IPNetwork` 仍是 2169 行自研实现，只是搬了命名空间；未换成 BCL 类型。有 66 个固化测试做基线。
- **性能**：195k 库的 old/new A/B 始终没做（本机没有 fixture），Stage 2 的单阶段写只有正确性验证。
- **运行时验证**：DLNA/NAT/LiveTv/Provider/转码这些路径全部只有编译期验证，没有真实设备或真实 API。
- **Surgery #1 后半**：schema 收敛（单表 + 恢复外键）、`GetWhereClauses` 拆分、跨聚合事务。

---

## 七、性能轮（2026-11-19）

### 怎么做的

1. 用仓库自带的 `tests/perf/fixture-tree-100k.sql` 造了 **195,251 条**的树形库
   （60k 电影 / 3k 系列 / 12k 季 / 120k 集），外加 5.4M user_datas、510k extradata、
   630k ancestor_ids、100 用户。本机无 psql，写了 60 行的 psql 兼容执行器复用原脚本。
2. 跑 `tests/perf/explain-matrix.sql` 的 50 个查询分支，取真实耗时。
3. 按"先测量、再改、再测"的顺序动刀。

### 修掉的真问题

**A. 非相关子查询被逐行重算（最贵的一处）**

`A.id IN (SELECT ...)` 里的子查询不引用外层列，但规划器把 IN 展平成 Semi Join 并**逐行执行**：
person 过滤实测 `loops=20280`、每次扫 8 个分区、447k 缓冲区、**410ms**。
加 `OFFSET 0` 作为物化屏障（无 ORDER BY 时结果集完全相同）后 **33ms**。
同款修了 7 处：PersonIds / PersonName / ArtistIds / AlbumArtistIds / GenreIds / StudioIds / AncestorId。

**B. Genres / Tags 的相关子查询**

`'x' IN (SELECT clean_value ... WHERE item_id = A.id)` → `EXISTS (... AND clean_value = 'x')`，
走 `(item_id, extradata_type_id)` 主键前缀。**141ms → 0.2ms**。NOT IN 版本同步改 NOT EXISTS。

**C. 缺索引**（195k 实测）

| 索引 | 分支 | 前 → 后 |
|---|---|---|
| `is_locked` 部分索引 | BR-11 | 127ms → 0.1ms |
| 列表三元布尔部分索引 | BR-10 | 149ms → 0.4ms |
| `date_last_saved` 部分索引 | BR-18 | 172ms → 10ms |
| `item_extradata(type,value,item_id)` | BR-25 | 131ms → 0.2ms |
| `item_people(person_id,item_id)` | BR-30 | 410ms → 33ms |

全部写进 `emby_pgsql_dump_schema_optimized.sql`（依赖后补列的那几条放
`PgSchemaInitializer.HotPathIndexDdl`），新装库自检 9/9 建成。

### 一个必须记下来的教训：基线会漂

`explain-matrix.sql` 里有 **4 条分支测的是代码已经不发的 SQL**：

- BR-34 用相关标量子查询，代码早已改 NOT EXISTS
- BR-36/37 用 `data::text LIKE`，代码早已改 `data->>'x'`
- BR-40 用逐行相关 `count(*)`，代码早已改 LEFT JOIN 聚合

也就是说那 517ms / 282ms / 200ms / 139ms 从来不是真实成本。矩阵漂移比代码漂移更危险，
因为它会让人去优化一个不存在的问题。已全部对齐真实 SQL。

### 结果

| 指标 | 值 |
|---|---|
| 50 个查询分支总计 | **788 ms**（对齐后口径） |
| 最慢分支 | **93 ms**（BR-26 Tags） |
| >100ms 的分支 | **0** |
| >50ms 的分支 | 6 |

达标口径（P95 < 100ms @ 195k）在**查询层**已满足。注意这是 SQL 层，不含 HTTP/DTO 水合开销。

### 仍然欠着的

- **端到端延迟未测**：服务端 API 的 p95 没跑（本机 auth 流程把时间吃光了，只能测 SQL 层）。
  `tests/perf/load-ab.py` 是现成的，接上真实 token 就能跑。
- **BR-26 Tags 93ms** 贴着线，是下一个该看的。
- **fixture 与矩阵都在本轮被动过**：两者原先都有 bug（fixture 少一个值、库没挂 root；
  矩阵测死 SQL）。这类"测试资产本身失真"的问题值得单独做一次体检。

---

## 八、端到端压测（2026-11-19）

SQL 层之外的真实 HTTP 延迟。6 并发、25 秒、5 种请求混合（列表 55% / 带字段 15% /
搜索 15% / 剧集 10% / 继续观看 5%），195k 库。

| 指标 | 修前 | 修后 |
|---|---|---|
| 吞吐 | 21.4 rps | **79.7 rps**（3.7×） |
| ALL p95 | 1213 ms | **300 ms** |
| 列表（SortBy=SortName, 100 条）p95 | 21.1 ms | **23.9 ms** |
| 列表 + Fields=Overview,Genres,People p95 | 1523 ms | **112.7 ms**（13.5×） |
| 剧集（SortBy=DateCreated）p95 | 350 ms | **32.6 ms**（10.7×） |
| 搜索（6 万命中，病态）p95 | 536 ms | **265.8 ms** |
| 继续观看 p95 | 1015 ms | **408.6 ms** |

### 端到端才暴露的两个问题

**① 列表页 N+1（最大头）**

一次 100 条、带 `Fields=People,Genres` 的列表请求，实测发出 **100 条 item_people
查询 + 101 条 item_extradata 查询** —— 每个条目各查一次，1192ms。
改成整页两次批量查询（`= ANY(@ids)`）后 **3 条语句、97ms**，响应 JSON 的 sha256 逐字相同。

**② 排序按日期只有 BRIN**

`SortBy=DateCreated` 走 `ORDER BY date_created DESC LIMIT 50`，而 schema 只建了 BRIN
（为范围扫描设计，函数上服务不了排序），只能整分区排序。补 btree 后 60ms → 0.19ms。

### 仍然慢的：继续观看 408ms

`SELECT count(DISTINCT presentation_unique_key) ... LEFT JOIN user_datas ...` 会扫全库再
哈希连接。实测：原样 317ms、改成从小表起 279ms、物化 CTE 289ms、强制索引路径 375ms
—— 现有计划已是该数据形态下的最优。这不是索引问题，是设计问题：为了一页 20 条的结果
去算全库的 TotalRecordCount。可选方向：按用户缓存 resume 计数、或把
presentation_unique_key 反规范化进 user_datas。**未动**，因为会改 API 语义。

注意：fixture 让 30% 的条目处于"看过/在播"，单个用户有 17,400 条 resume —— 真实用户通常
只有几十条，这条在真实数据下不会是热点。

---

## 九、NU1900 的处理

`NU1900` = 「漏洞审计连不上 api.nuget.org 的 service index」，不是「包有漏洞」。
离线构建时每个项目刷一条，会把真正的告警淹掉。

处理：`Directory.Build.props` 只压 NU1900，**NU1901/1902/1903 保持生效**；
同时给 CI 加了 `Vulnerability audit` 步骤（`dotnet list package --vulnerable` 有输出即 fail）。
结果：本地全量重建 **0 警告 0 错误**，而漏洞门禁没有丢。

---

## 十、Git 考古结论（`docs/review/2026-11-19-git-archaeology.md`）

最重要的一条：**ADR-0001「Go 全新重写，不 fork C#」从未执行过**。
它进入 git 的时间是 2026-10-19，而 C# 现代化在 **2026-10-13** 就已经开工并「决策确认」；
全部历史里没有任何 `.go` 文件。同一批被删的 SPEC 还把 LiveTV/DVR、DLNA、音乐、照片
列为「明确不做」—— 而它们在今天的 C# 代码里全都注册着、有 API、有测试。
结论：那套 ADR 是一份**从未落地的平行设计稿**，`AGENTS.md` 才是真实契约。

可安全移除的历史包袱（按行数）：

| 子系统 | 行数 | 说明 |
|---|---|---|
| `IO/SharpCifs` | 38,022 | SMB1 专用（`NT LM 0.12`），2017 年 vendored |
| `BDInfo` + `DvdLib` | 7,487 | 只服务光盘 ISO/VOB 探测 |
| `dashboard-ui` | 12.9 MB / 641 文件 | 现在仍是根重定向目标；`/app` 成为默认后才可删 |
| `TV/TheTVDB` | 4,008 | `api.thetvdb.com` 已死，默认关闭 |
| `Mono.Nat` + `Updates/GithubUpdater` | 1,594 | 路由器端口映射默认开；Docker/反代部署假设已不成立 |

另外找到一批「证明自己过时」的代码：`#if TRAVISCI` 10 处、`#if SILVERLIGHT`、`#if BETA`、
`#if NETSTANDARD2_0`；`-enablegdi` 开关在 `Emby.Drawing.Net` 删除后只会选中 `NullImageEncoder`
（等于关掉所有图片）；8 个只写不读的配置项；以及 `SetOptimalValues()` 里为「相机上传」这种
早已删除的功能做的迁移标记。
