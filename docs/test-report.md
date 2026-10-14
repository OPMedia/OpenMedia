# OpenMedia 系统测试报告（第二轮·最终版）

> 执行时间：2026-10-14 19:16–20:25　基址：`http://127.0.0.1:8096`（API 前缀 `/emby`）
> 最终受测实例：PID 25272（20:16:20 启动，`dotnet bin\Debug\net10.0\MediaBrowser.Server.dll`，ServerId `6225572626f94ea0b6e469c9ac63ef6f`）——Lead 在测试进行中多次重建/切换服务器（详见 §7 环境事件时间线），三套件最终结果均取自该实例（当前源码）。
> 环境：Windows 10.0.26200（16 核）　Kestrel 1644 rest paths / 65 services　PostgreSQL（PgData 层，条目库跨实例共享）　**ffmpeg/ffprobe 未安装**　**已装插件 0 个**
> 测试基建：`tests\`（scenarios.ps1 / run-tests.ps1 / contract-check.ps1，PS 5.1 兼容、幂等；test-plan.json 417 操作：P0=14 / P1=252 / P2=151）
> 认证：`POST /Users/AuthenticateByName`，admin/admin → 401，**admin/空密码 → 200**（初始用户无密码，S1 向导幂等补全后回退成功）
> 媒体库：Movie1/Movie2（Type=Movie，Id `c3f69628…`/`c40cea18…`）+ media Folder + Playlists（TotalRecordCount=4）；虚拟文件夹 5 个（OpenMediaTests/1/11/2/21，多轮幂等建库累积）

---

## 1. 执行摘要（两轮对比）

| 套件 | 第一轮（17:56–18:31） | 第二轮（最终） | 变化 |
|------|----------------------|----------------|------|
| scenarios.ps1（功能场景） | 16 PASS / 4 FAIL / 1 SKIP（21 项） | **22 PASS / 2 FAIL / 0 SKIP（24 项）** | 扫描/建库/WebSocket/Ping/图片/流式范围全部转 PASS；仅剩 nfo year ×2（BUG-16）。检查项数不同：电影条目存在后，元数据/海报/流式 3 类条件检查从"未执行/SKIP"变为实际执行 |
| run-tests.ps1（API 矩阵） | 53.5%（123/230）　P0 6/14　P1 117/99/36 | **63.9%（147/230）**　P0 **10/14**　P1 **137**/79/36 | +24 PASS；上下文 ItemId 为真实 Movie（第一轮为 Folder）；49 个"扫描 bug 下游"中 31 个转 PASS |
| contract-check.ps1（JSON 契约） | 16 PASS / 2 FAIL | **16 PASS / 2 FAIL**（同样 2 项：/Users 判定误报 + null 字段已知差异） | 持平；两项均为已知非缺陷 |

**总通过率（矩阵 PASS/(PASS+FAIL)）：53.5% → 63.9%（147/230）**

分级统计（第二轮）：

| 级别 | 执行 | PASS | FAIL | SKIP-EXPECTED | 备注 |
|------|------|------|------|---------------|------|
| P0（关键路径） | 14 | 10 | 4 | 0 | 复核后 **12/14 实际通过**（/Users、/Users/Public 为 PS 5.1 误报、AuthenticateByName 无 body 400 合理）；服务器责任 P0 失败仅 **POST /Items/{Id}/PlaybackInfo 1 个（BUG-16）** |
| P1（常规查询） | 252 | 137 | 79 | 36 | 36 个 404 预期（B6 暂缓/Sync/Connect/swagger）全部按预期返回 404 |
| P2（写/破坏性） | 151 | — | — | — | 全部跳过（按计划） |

**失败根因分类（矩阵 83 个 FAIL）**：

| 根因 | 数量 | 说明 |
|------|------|------|
| ① 服务器 bug（代码缺陷） | 22 | BUG-16 FK ×2（PlaybackInfo GET/POST）、BUG-9 NRE ×4、BUG-10 First() ×7、BUG-11 null 400 ×4、BUG-13 非法参数 500 ×3、Playlist 强转 ×1、Packages/{Name} null 400 ×1（另 3 个与④混合，计入④） |
| ② ffmpeg 未安装 | 11 | stream/m3u8/hls 路径 `Cannot start process because a file name has not been provided`（ffprobe 路径为空） |
| ③ 预期差异（spec/实现差异） | 6 | UserId 实际必填 ×3（加参后 200）、master.m3u8 MediaSourceId 必填 ×2、Items/File path 必填 ×1 |
| ④ 测试脚本/计划缺陷 | 12 | PS 5.1 单元素数组解包 ×2（实际 200 合法数组）、合成参数 404 合理 ×4、{Id} 解析为 UserId ×3（+NRE）、计划缺参数定义 ×1、Views 'test' FormatException ×1、POST 无 body ×1 |
| ⑤ 环境问题/fixture 限制 | 32 | 按名实体无图片 ×12、Movie 非音频条目（InstantMix/universal）×9、无插件/无 LiveTv 数据/服务器图库无资源 ×7、用户无图 ×2、Movie 非 Series ×2 |

> 调整口径：剔除④③⑤后，服务器责任失败 = ① 22 个 → 责任通过率 **87.0%（147/169）**（第一轮同口径 87.0%）；再剔除② ffmpeg 环境项后 = **81.7%**（第一轮同口径 65.7%——②从 49 降到 11 是最大改善来源）。

---

## 2. 第一轮 13 个 Bug 修复验证（8 个已修 + 宽容枚举）

| Bug | 第一轮状态 | 第二轮验证 | 结论 |
|-----|-----------|-----------|------|
| **BUG-1**【P0】GET /System/Ping → 500（路由已注册但服务方法缺失） | 500 `Could not find method Get(PingSystem)` | **200 `Emby Server`**（矩阵 [109] PASS、场景 S5 PASS、多次直接复测） | ✅ **已修复** |
| **BUG-2**【Critical】媒体库扫描失败：PgItemRepository 序列化对象循环 | 扫描 0 秒失败，Movie 永不入库，矩阵 49 个下游失败 | **扫描成功**：Movie1/Movie2 以 Type=Movie 入库（TotalRecordCount=4），场景 S3 count=2 耗时 0s；31 个下游转 PASS（Images×3、Similar、Download、File、Ancestors、ThemeMedia、MetadataEditor 等） | ✅ **已修复**（残留：nfo 元数据不入库 → 新 BUG-16，见 §3） |
| **BUG-3**【High】POST /Library/VirtualFolders 首次创建 → 500（操作实际生效） | 500 对象循环（文件夹实际建成） | **204**（新建 OpenMediaTests2 直接验证；幂等重复 POST → 204；场景 S3 20:22 新建 → 204） | ✅ **已修复** |
| **BUG-4**【High】/embywebsocket 404：WebSocket 路由未注册 | HTTP 404 无处理器 | **101 连接 + 消息回环 PASS**：发送 SessionsStart → 服务器回推 MessageType=UserUpdated（1708B，EndOfMessage=True），4 次场景运行全部通过 | ✅ **已修复** |
| **BUG-5**【High】按名查询端点 SQL 42883 uuid = bigint（6 个端点） | 500 `42883: 操作符不存在: uuid = bigint` | **6 个端点全部 200**（Artists/Persons/Genres/MusicGenres/GameGenres/Studios {Name}：矩阵 [28][50][52][86][94][103]） | ✅ **已修复** |
| **BUG-6**【High】标签/编解码器端点 500：行映射缺 genres 字段（7 个端点） | 500 `Field not found in row: genres` | **7 个端点全部 200**（Tags/AudioCodecs/VideoCodecs/SubtitleCodecs/Containers/OfficialRatings/Items/Prefixes：矩阵 [2][4][10][19][22][25][59]） | ✅ **已修复** |
| **BUG-7**【Medium】响应 GZipStream 已释放（3 个端点） | 500 `Cannot access a disposed object. GZipStream` | **/Packages 200、/Packages/Updates 200、SchedulesDirect/Countries 200**（返回真实数据）；/Packages/{Name} 由 500 GZip 变为 400 `Value cannot be null (result)`（包不存在 → 应 404，次要健壮性，非 GZip 问题） | ✅ **已修复**（残留 1 个次要健壮性） |
| **BUG-8**【Medium】GET /LiveTv/ChannelMappings → 500 服务方法缺失 | 500 `Could not find method Get(SetChannelMapping)` | 错误变为 `Sequence contains no matching element`（ProviderId='test' 查不到 → First() 空序列）——**方法已注册**，残留为 BUG-10 族健壮性 | ✅ **已修复**（残留健壮性） |
| **宽容枚举**（LenientEnumConverter） | Type 枚举数字/未知值解析 | 契约"Type 为枚举字符串" PASS；但 **19:13 构建引入 BUG-15**（ImageType 作字典键不支持，见 §3），**19:53 重建已修复**（ImageTags 正常序列化：`"ImageTags":{"Primary":"750bfc7d…"}`） | ✅ 修复后正常（中间构建有回归，已消除） |

**未修复（按任务口径接受/暂缓）**：BUG-9（NRE 500 ×4 端点，仍在）、BUG-10（First() 500 ×7 端点，仍在）、BUG-11（null 400 ×4 端点，仍在）、BUG-12（null 字段显式输出——**已知接受差异**：Guid Empty 输出 null 而非省略，语义等价，前端 falsy 判断不受影响）、BUG-13（非法参数 500 ×3+，仍在）。以上均为健壮性/契约次要问题，旧代码同样行为。

---

## 3. 第二轮新发现 Bug（3 个）

### BUG-14【Critical→数据状态相关】流式端点触发 StackOverflowException 进程死亡（4 次）

- **现象**：`GET /Videos/{MovieId}/stream?static=true`（含或不含 Range）→ 服务器进程**立即死亡**（连接重置，客户端 status=0），无 managed 错误报告、日志戛然而止。
- **证据**：Windows 事件日志 Application Error 1000 + WER CLR20r3：异常代码 **0xc00000fd（STATUS_STACK_OVERFLOW）**，P4=**Emby.Server.MediaEncoding**，P9=**System.StackOverflowException**（ReportArchive `AppCrash_dotnet.exe_7c1dff…`，19:24:29 崩溃，PID 0x31BC=12732）。
- **复现记录（4/4）**：19:18:39（场景 #1，Build 19:13）、19:24:29（场景 #2，WER 留档）、19:28:20（直接请求，Lead 19:26 实例）、20:01（直接请求，19:53 构建 + 旧序列化器写入的条目 blob）。共同条件：**真实 Movie 条目 + 条目缓存已预热 + 条目 blob 由 19:53 前的序列化器持久化**（冷缓存时同一请求为 500 NRE @ BaseStreamingService.cs:399，不崩溃）。
- **当前状态：不可复现（3/3 通过）**——20:03 全量重扫（当前构建重写条目 blob）后：`stream?static=true + Range` → **206 + `Content-Range: bytes 0-99/262144`**（video/x-matroska，100B）；含"refresh 进行中立即 stream"竞态测试也存活。场景最终轮 S5 该项 PASS。
- **风险提示**：从旧构建升级且保留条目库的部署，在完成一次全量重扫前，流式端点存在进程崩溃风险。根因定位（递归点）留给 Lead：WER 指向 Emby.Server.MediaEncoding，SO 不可捕获故无堆栈；建议用 LocalDumps 抓全量 dump 复现旧 blob 场景。

### BUG-15【Critical→已在中途修复】ImageType 字典键 + LenientEnumConverter → /Items 全量 500

- **现象**：条目带图片后（ImageTags 非空），`GET /Items`、`GET /Users/{UserId}/Items/{Id}`、`GET /Users/{UserId}/Suggestions` → 500：`The type 'MediaBrowser.Model.Entities.ImageType' is not a supported dictionary key using converter of type 'Emby.Common.Implementations.Serialization.LenientEnumConverter'`（System.Text.Json 不接受自定义枚举转换器作字典键；BaseItemDto.ImageTags 为 `Dictionary<ImageType,string>`）。
- **出现窗口**：19:13 构建（Build A）19:26:56 首现（浏览器请求）；19:53 部署竞态期间加剧（混合构建实例写入的 blob 使干净构建也报错，19:57 矩阵受污染——该轮矩阵 56.3% 已作废）。**19:53 重建 + 20:03 重扫后消除**：`/Items?Ids=…` 返回 `"ImageTags":{"Primary":"750bfc7d…"}` 正常序列化，`/Users/{uid}/Items/{id}` → 200。
- **结论**：宽容枚举修复在 19:13 构建引入的回归，Lead 的 19:53 重建已解决；最终三套件均在修复后状态运行。**教训**：枚举自定义 converter 注册需同步处理字典键场景（JsonConverter on key type 或改用字符串键字典）。

### BUG-16【High·未修复】item_extradata 外键违约 → 元数据保存中止（nfo year 不入库 + PlaybackInfo 500）

- **最小复现**：① `Movie1.nfo` 含 `<year>2020</year>`，扫描/刷新后 `GET /Items?IncludeItemTypes=Movie` → `ProductionYear` 为空（场景 S3 仅存的 2 个 FAIL）；② `GET/POST /Items/{Movie1}/PlaybackInfo` → 500。
- **期望**：nfo 的 year/genres 持久化；PlaybackInfo 200（MediaSources）。
- **实际**：500 `23503: 插入或更新表 "item_extradata" 违反外键约束 "item_extradata_fixed_extradata_type_id_fkey1"`。
- **完整堆栈**（server.txt）：`ProviderManager.StartProcessingRefreshQueue → RefreshItem → MetadataService.RefreshMetadata → SaveItem → BaseItem.UpdateToRepository → LibraryManager.UpdateItem(s) → PgItemRepository.SaveItems → PgItemRepository.UpdateItemValues（PgItemRepository.Aggregates.cs:585）→ Npgsql 23503`——**整个 SaveItems 事务回滚**，故 nfo 解析出的 year/genres 一并丢失。
- **根因**：`item_extradata.extradata_type_id` 外键引用 `item_extradata_types`（schema-deploy.sql L488-500），该类型表**未播种**（空表），任何 extradata 插入即违约。
- **影响面**：矩阵 2 个 FAIL（PlaybackInfo GET/POST，含 P0 1 个）+ 场景 2 个 FAIL（nfo year）——**第二轮仅存的场景失败**。修复方向：播种 item_extradata_types 或修 UpdateItemValues 的类型 id 解析。

### 次要新发现（健壮性，旧代码同族，不单列）

- `GET /Packages/{Name}`（包不存在）→ 400 `Value cannot be null (result)`（应 404；GZip 修复后的新表现）。
- `GET /Playlists/{Id}/Items`（Movie 当 Playlist）→ 500 强转（应 400/404）。

---

## 4. 场景测试结果（scenarios.ps1，最终轮 20:22：22 PASS / 2 FAIL / 0 SKIP，共 24 项）

| 场景 | 检查 | 结果 | 详情 |
|------|------|------|------|
| S0 预检 | GET /System/Info/Public | PASS | 200，Id 32hex 无连字符（ServerId `62255726…`，Debug 实例） |
| S1 启动向导 | GET /Startup/Configuration（无 token） | PASS | 200（该实例 ProgramData 全新，向导未完成，免认证放行） |
| S1 启动向导 | POST /Startup/User {"Name":"admin"} | PASS | 200 |
| S1 启动向导 | POST /Startup/Complete | PASS | 204 |
| S2 认证 | AuthenticateByName（admin/admin → 空密码回退） | PASS | admin/admin→401；空密码→200 |
| S2 认证 | AuthenticateByName（错误密码） | PASS | 401（认证判别正确） |
| S2 认证 | GET /Users（带 token / 无 token） | PASS×2 | 200 / 401 |
| S3 媒体库 | 测试媒体就绪 | PASS | Movie1/Movie2 mkv+nfo+poster |
| S3 媒体库 | GET /Library/VirtualFolders | PASS | 200（5 个文件夹） |
| S3 媒体库 | POST /Library/VirtualFolders（新建） | PASS | **204**（BUG-3 修复验证） |
| S3 媒体库 | POST /Library/Refresh | PASS | 204 |
| S3 媒体库 | 扫描出 2 部电影 | PASS | count=2，耗时 0s（BUG-2 修复验证） |
| S3 媒体库 | Movie1 元数据（nfo year） | **FAIL** | Year 为空（**BUG-16**） |
| S3 媒体库 | Movie2 元数据（nfo year） | **FAIL** | 同上 |
| S3 媒体库 | GET /Items/{Movie1}/Images/Primary | PASS | **200 image/jpeg 633B**（poster.jpg 入库并被服务——第一轮 404） |
| S4 WebSocket | /embywebsocket + SessionsStart 回环 | PASS | 收到推送 MessageType=UserUpdated（1708B）（BUG-4 修复验证） |
| S5 静态资源 | index.html / Range 206 / 前缀克隆 / favicon / robots / Ping | PASS×6 | Range: `bytes 0-99/2811`；Ping 200 `Emby Server` |
| S5 静态资源 | GET /Videos/{Movie1}/stream?static=true + Range | PASS | **206 + Content-Range: bytes 0-99/262144**（video/x-matroska，100B）——R3 流式范围语义正确（第一轮 SKIP） |

**场景结论**：启动向导（幂等）、认证判别、静态资源与 HTTP Range、**媒体库全链路（建库 204 → 扫描入库 → 海报服务 → 流式范围 206）与 WebSocket 回环全部打通**；仅剩 nfo year 元数据持久化失败（BUG-16 单一根因）。

---

## 5. JSON 契约抽查结果（contract-check.ps1，18 项：16 PASS / 2 FAIL）

| 端点 | 规则 | 结果 | 详情 |
|------|------|------|------|
| /System/Info/Public | 200+JSON / PascalCase / Id 32hex / ServerName / Version / OperatingSystem | 6×PASS | 全部合规 |
| /Users | HTTP 200 + 数组 | FAIL | **误报**（PS 5.1 单元素数组解包）；原始响应 `[{"Name":"admin",…}]` 为合法数组（HttpClient 原文验证 body 以 `[` 开头） |
| /Items | QueryResult{Items,TotalRecordCount} | PASS | Items=4 TotalRecordCount=4 |
| /Items | Type 枚举字符串 | PASS | Folder/Movie/PlaylistsFolder（非数字） |
| /Items | 无 null 值字段 | FAIL | null 字段：ChannelId,ParentId,SeriesId,SeasonId,AlbumId（×4 条目）——**已知接受差异**：Guid Empty 输出 null 而非省略，语义等价，前端 falsy 判断不受影响 |
| /Items | PascalCase / Id/Name/Type / Item.Id 32hex | 4×PASS | 合规 |
| /System/Info | 200+JSON / PascalCase / Id 32hex | 3×PASS | 合规 |

---

## 6. 失败清单（矩阵 83 个 FAIL，按根因分组）

### ① 服务器 bug（22 个）

| 操作 | 状态码 | 响应片段 | 说明 |
|------|--------|----------|------|
| `GET /Items/{Id}/PlaybackInfo` | 500 | 23503: 插入或更新表 "item_extradata" 违反外键约束… | **BUG-16**（元数据保存中止） |
| `POST /Items/{Id}/PlaybackInfo` | 500 | 同上 | **BUG-16**（P0） |
| `GET /LiveTv/ChannelMappings` | 500 | Sequence contains no matching element | BUG-10 族（BUG-8 方法已修，残留 First()） |
| `GET /LiveTv/ChannelMappingOptions` | 500 | Sequence contains no matching element | BUG-10 |
| `GET /Dlna/Profiles/{Id}` | 500 | Sequence contains no matching element | BUG-10 |
| `GET /System/Logs/Log` | 500 | Sequence contains no matching element | BUG-10 |
| `GET /Providers/Subtitles/Subtitles/{Id}` | 500 | Sequence contains no matching element | BUG-10 |
| `GET /Plugins/{Id}/Thumb` | 500 | Sequence contains no matching element | BUG-10 + 无插件 |
| `GET /Plugins/{Id}/Configuration` | 500 | Sequence contains no matching element | BUG-10 + 无插件 |
| `GET /Dlna/icons/{Filename}` | 500 | Object reference not set to an instance of an object. | BUG-9（NRE） |
| `GET /Dlna/{UuId}/icons/{Filename}` | 500 | Object reference not set… | BUG-9 |
| `GET /LiveTv/Recordings/Folders` | 500 | Object reference not set… | BUG-9 |
| `GET /LiveTv/LiveStreamFiles/{Id}/stream.{Container}` | 500 | Object reference not set… | BUG-9 |
| `GET /LiveTv/Channels/{Id}` | 400 | Value cannot be null. (Parameter 'id') | BUG-11 |
| `GET /LiveTv/Recordings/{Id}` | 400 | Value cannot be null. (Parameter 'id') | BUG-11 |
| `GET /LiveTv/Timers/{Id}` | 400 | Value cannot be null. (Parameter 'result') | BUG-11 |
| `GET /LiveTv/SeriesTimers/{Id}` | 400 | Value cannot be null. (Parameter 'result') | BUG-11 |
| `GET /Playback/BitrateTest` | 500 | The input string 'test' was not in a correct format. | BUG-13（harness test 值触发，服务器应 400） |
| `GET /Images/Remote` | 500 | Invalid URI: The format of the URI could not be determined. | BUG-13 |
| `GET /Videos/{Id}/index.bif` | 500 | The input string 'test' was not in a correct format. | BUG-13 |
| `GET /Playlists/{Id}/Items` | 500 | Unable to cast object of type 'Movie' to type 'IHasPlaylist…' | 健壮性（Movie 当 Playlist；fixture 无播放列表） |
| `GET /Packages/{Name}` | 400 | Value cannot be null. (Parameter 'result') | 健壮性（包不存在应 404；GZip 修复已验证） |

### ② ffmpeg 未安装（11 个）

| 操作 | 状态码 | 响应片段 |
|------|--------|----------|
| `GET /Audio/{Id}/stream.{Container}`、`GET /Audio/{Id}/stream` | 500 | Cannot start process because a file name has not been provided.（ffprobe 路径空） |
| `GET /Videos/{Id}/stream.{Container}`、`GET /Videos/{Id}/stream` | 500 | 同上 |
| `GET /Videos/{Id}/live.m3u8`、`GET /Audio/{Id}/hls1/…`、`GET /Videos/{Id}/hls1/…` | 500 | 同上（HLS 转码路径） |
| `GET /Videos/{Id}/subtitles.m3u8`、`GET /Videos/{Id}/{MediaSourceId}/Subtitles/…` ×2 | 500 | 同上/参数混合 |
| `GET /Videos/{Id}/hls/{PlaylistId}/{SegmentId}.{SegmentContainer}` | 404 | 转码临时文件不存在 |

> 注：`static=true` 直连流式已正常（206，见场景 S5）；失败均为需转码/探测的路径。安装 ffmpeg 后复测。

### ③ 预期差异（6 个）

`GET /Games/SystemSummaries`、`GET /Movies/Recommendations`、`GET /LiveTv/Programs/Recommended`（400：UserId 实际必填，spec 标可选，加参后 200 已验证）；`GET /Videos/{Id}/master.m3u8`、`GET /Audio/{Id}/master.m3u8`（400：MediaSourceId 必填）；`GET /Items/File`（400：path 必填）。

### ④ 测试脚本/计划缺陷（12 个）

`GET /Users`、`GET /Users/Public`（200，PS 5.1 单元素数组解包误报——原始响应合法数组）；`POST /Users/AuthenticateByName`（400：矩阵 POST 无 body，场景 S2 已验证 200）；`GET /Users/{UserId}/Views`（500：IncludeExternalContent='test' FormatException——无参数时 200 且视图非空已验证）；`GET /Users/{UserId}/Items/{Id}`、`/SpecialFeatures`、`/LocalTrailers`（500：harness 将 {Id} 解析为 UserId → 条目不存在 → NRE）；`GET /Environment/DirectoryContents`、`GET /System/Configuration/{Key}`、`GET /ScheduledTasks/{Id}`、`GET /Items/RemoteSearch/Image`（404/400：合成参数，合理）；`GET /Channels/{Id}/Items`（500：test-plan.json 缺参数定义，URL 未替换 {Id} → Guid 解析失败）。

### ⑤ 环境问题/fixture 限制（32 个）

按名实体无图片 ×12（`/Artists|GameGenres|Genres|MusicGenres|Persons|Studios/{Name}/Images/{Type}[/Index]` → 404 "does not have an image of type Primary"——按名实体存在但无图）；Movie 非音频条目 ×9（InstantMix ×7、Audio universal ×2 → 400 id null——需音频库 fixture）；无插件/无 LiveTv 数据/服务器图库无资源 ×7（web/ConfigurationPage、Lineups、LiveRecordings、Recordings/Groups 404；Images/General|Ratings|MediaInfo 404）；用户无图 ×2；Movie 非 Series ×2（Shows/Seasons、Episodes 404 "Series not found"）。

### SKIP-EXPECTED（36 个，全部按预期 404，通过）

B6 插件端点暂缓（user_usage_stats 17 + Reports 4 + ThumbnailSet 1）+ 跳过清单（Sync 8 + Connect 2 + swagger/openapi 4）+ 其余 4。

---

## 7. 测试期间环境事件时间线（重要上下文）

第二轮测试与 Lead 的重建/重启**并行发生**，服务器状态多次切换：

| 时间 | 事件 | 影响 |
|------|------|------|
| 19:13:46 | Lead 部署修复版 Release（Build A，8 修复 + 宽容枚举）→ D:\OpenMedia | 场景 #1/#2 的受测构建 |
| 19:16:04 | Lead 启动服务器（PID 28740；任务书所述 32484 已不存在） | — |
| 19:18:35 / 19:24:25 | 场景 #1/#2：17 PASS/4 FAIL/1 SKIP（两轮一致）；**末项 stream 请求触发 BUG-14 进程崩溃 ×2**（第二次留 WER 证据） | 服务器死亡 ×2，由我/Lead 重启 |
| 19:26:04 | Lead 重启实例；19:26:56 浏览器请求首现 **BUG-15**（ImageType 字典键 500） | Build A 回归确认 |
| 19:53:05-16 | **Lead 重建部署（Build B）**；我的 19:53:13 重启与 DLL 拷贝**竞态** → 混合构建实例 | 19:57 矩阵第一跑（56.3%）受污染**作废**；混合实例写入的条目 blob 使后续干净实例短暂复现 BUG-15/BUG-14（20:01 崩溃 #4） |
| 20:02-20:03 | 我重启干净 Build B；场景 #3：19/2/1——**全量重扫重写条目 blob 后 BUG-14/15 均不再复现** | 状态归一化 |
| 20:09 / 20:16:20 | Lead 先后切换实例；最终为**仓库 Debug 构建**（PID 25272，`bin\Debug\net10.0`，ServerId 62255726…，条目库共享） | 最终三套件受测实例 |
| 20:18 / 20:21 / 20:22 | 矩阵（63.9%）→ 契约（16/2）→ 场景（22/2/0），全部在 PID 25272 上依序完成 | 本报告最终数据 |

> 结论：最终数据反映**当前源码**（Debug 构建 = 19:53 修复后的最新源）。中途作废数据仅存档于 results\ 时间戳文件（api-results-20261014-195757.json 为受污染跑，勿引用）。

---

## 8. 复测建议（优先级排序）

1. **修复 BUG-16（item_extradata FK 23503）**：播种 `item_extradata_types` 类型表或修正 `PgItemRepository.UpdateItemValues`（Aggregates.cs:585）的类型 id 解析。修复后预期：nfo year/genres 入库（场景 24/24）、PlaybackInfo 200（P0 14/14）、矩阵 +2 PASS。
2. **BUG-14 根因定位**：用 WER LocalDumps（dotnet.exe，DumpType=2）+ 旧构建持久化的条目库复现 SO 递归点（Emby.Server.MediaEncoding）；升级部署文档注明"先全量重扫再开放流式端点"。
3. **安装 ffmpeg**：复测 PlaybackInfo MediaSources、stream/m3u8/hls 转码路径（当前 11 个②类失败的直接原因）。
4. **健壮性整改（BUG-9/10/11/13 + 新次要项）**：资源不存在统一 404、非法参数统一 400、避免裸 First()/强转（约 18 个 500/400 可消除）。
5. **测试基建改进建议（未改动，仅建议）**：ConvertFrom-JsonSafe 用 @() 包裹规避 PS 5.1 解包；Resolve-ParamValue 类型感知默认值；test-plan.json 补 `GET /Channels/{Id}/Items` 参数；`/Users/{UserId}/Items/{Id}` 的 {Id} 不应复用 UserId 解析。
6. **fixture 扩充**：音频条目（解 InstantMix/universal ×9）、播放列表、Series（解 Shows ×2）、按名实体图片——可再提升矩阵通过率约 25 个。

## 9. 产物清单

| 文件 | 内容 |
|------|------|
| `tests\results\scenarios.md` / `scenarios-results-20261014-202223.json`（最终轮）及历史 `…191835/192425/200324.json` | 场景明细（4 轮：17/4/1 ×2、19/2/1、22/2/0） |
| `tests\results\api-matrix.md` / `api-results-20261014-201840.json`（最终） | API 矩阵全量明细（266 操作 × 状态码/键名/字段/判定；63.9%） |
| `tests\results\api-results-20261014-195757.json` | **作废**（19:53 部署竞态污染跑，56.3%，勿引用） |
| `tests\results\contract.md` / `contract-results-latest.json` | 契约抽查明细（16/2） |
| `tests\results\fail-classified.md` | 第二轮 83 个失败根因分类表 |
| `D:\OpenMedia\ProgramData-Server\logs\server-63927602548.txt` 等 | 服务器日志（BUG-14/15/16 异常证据源） |
| Windows 事件日志 + `C:\ProgramData\Microsoft\Windows\WER\ReportArchive\AppCrash_dotnet.exe_7c1dff…` | BUG-14 崩溃证据（CLR20r3 / StackOverflowException / Emby.Server.MediaEncoding） |

> 执行说明：三脚本依序执行（场景 4 轮 → 矩阵 2 跑 [1 作废] → 契约 1 轮），失败不中断；最终轮上下文 UserId=admin（ac491424…）、ItemId=Movie1（c3f69628…，真实条目）、SessionId=29e9e0a4…、LogName=server.txt、PluginId=空（0 插件）。本报告未修改任何服务器源码；仅写入 tests\results\ 与本文件。服务器在测试期间因 BUG-14 死亡 4 次（均已重启/由 Lead 接管），最终实例 PID 25272 存活至报告完成。

---

## 终态补充（Lead，2026-10-14 20:35）

第二轮报告中的 3 个新发现 bug 已全部修复并验证：

| Bug | 修复 commit | 验证 |
|-----|------------|------|
| BUG-16 item_extradata FK 23503（元数据保存回滚） | 22fcd01242（字典种子） | Movie1 production_year=2020 持久化（DB 直查）；**PlaybackInfo 200**（P0 恢复） |
| BUG-14 流式 StackOverflow 进程死亡 | 6f9383a0c3（StreamState.Dispose base 调用） | stream+Range 206，服务器存活；测试期 4 次崩溃均发生在修复前 |
| BUG-15 ImageType 字典键 | 6e0ae3e0d3（ReadAsPropertyName） | ImageTags {"Primary":"..."} 正常序列化 |

最终部署实例（D:\OpenMedia，Release 全修复）：Movie1 Year=2020 + Primary 图 + PlaybackInfo 200 + WebSocket 回环 + 认证/Items/搜索全通。

剩余已知项（不阻断）：健壮性族（BUG-9/10/11/13，~18 端点 500 应 404——旧 Emby 同样行为的 First() 空序列等）；ffmpeg 未安装（转码/流探测不可用，static 直连流正常）；/Items Guid Empty 字段输出 null 而非省略（语义等价）。
