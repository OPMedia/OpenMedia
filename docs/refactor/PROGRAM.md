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
