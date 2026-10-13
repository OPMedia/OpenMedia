# Emby API 规范对齐差距地图（openapi.json 4.1.1.0）

> 产出：api-spec（task-7 第一步 / 计划 C29）
> 基线：仓库根 `openapi.json`（Emby Server API **4.1.1.0**，OpenAPI 3.0.1，356 路径 / 445 操作 / 201 schema）vs 现有代码（Emby 3.5.3 开源部分）
> 目标：D7——官方 Emby 客户端（4.1.x）零适配直连
> 方法：脚本化全量提取（Python 解析 openapi.json + 正则扫描 5 个路由承载程序集的全部 `[Route]`/DTO 属性/`IReturn<T>`，含基类链与跨程序集基类合并），路径按 `{Param}→{}` 规范化后逐操作比对 query/path/body 参数与响应 schema 字段。中间数据存 `%TEMP%\apigap\`（spec-ops.json / routes.json / op-diffs.json / schema-diffs2.json）。

---

## 0. 摘要：对初步 diff 的修正

初步 diff（upgrade-net10-plan.md §0 D7）只扫描了 `MediaBrowser.Api/`，**遗漏了 4 个同样承载 `[Route]` 的程序集**。全量扫描后真实差距比初步结论小一半：

| 项 | 初步 diff | 本次全量核对 | 差异原因 |
|----|-----------|--------------|----------|
| 规范化匹配路径 | 236 | **284** | 流媒体/播放上报/Notifications/Dlna/web 端点在 MediaEncoding、Emby.Notifications、Emby.Dlna、WebDashboard 程序集里 |
| 规范有而代码无 | 117 | **69** | 同上（其中 Sync 18、Connect 3、swagger/openapi 4 跳过 → **44 需实现**，且其中 25 个是插件端点） |
| 代码有而规范无 | 17 | **27** | 其余 10 个是 MediaEncoding/Dlna/Notifications/WebDashboard 的旧端点 |
| 需新实现端点 | ~95 | **44**（核心 19 + 插件 25） | 见 §4 |

**路由承载程序集全景**（共 394 个 `[Route]`，311 个规范化路径）：

| 程序集 | 路由数 | 覆盖域 |
|--------|--------|--------|
| `MediaBrowser.Api/`（61 文件） | 315 | Users/Items/Images/Library/LiveTv/Sessions/System/… |
| `src/Emby.Server.MediaEncoding/`（Api + Api.Hls + Api.Progressive，24 文件） | ~60 | **流媒体**（stream/hls1/master.m3u8/universal）、**PlaybackInfo**、**LiveStreams**、**/Sessions/Playing 上报**、BitrateTest |
| `Emby.Notifications/Api/` | 8 | Notifications 域 |
| `Emby.Dlna/Api/`（DlnaServerService + DlnaService） | ~20 | DLNA 描述/控制/事件（含 SUBSCRIBE/UNSUBSCRIBE） |
| `MediaBrowser.WebDashboard/Api/` | 4 | /web/ConfigurationPages、/web/{}、favicon、robots |

> ⚠️ 客户端最关键的播放链路（`/Items/{Id}/PlaybackInfo`、`/Videos/{Id}/stream`、`/Audio/{Id}/universal`、`/Sessions/Playing` 系列、`/LiveStreams/Open`）**全部已存在**于 MediaEncoding 程序集——初步 diff 以为缺失的"Videos 10、Audio 7、Sessions 4、LiveStreams 3、Playback/BitrateTest"实际已实现，只需参数/schema 对齐。

---

## 1. 路径级对账总表

| 分类 | 数量 | 说明 |
|------|------|------|
| spec 路径（规范化后） | 353 | 356 原始路径中 3 组仅路径参数名不同 |
| 代码路径（规范化后） | 311 | |
| **匹配** | **284** | 需 §2/§3 的参数与 schema 级核对 |
| spec-only | 69 | = Sync 18（跳过）+ Connect 3（跳过）+ swagger/openapi 4（跳过）+ **44 需实现**（§4） |
| code-only | 27 | 处置见 §5 |
| 匹配路径中 verb 缺失 | 2 | `GET /LiveTv/ListingProviders`、`GET /LiveTv/TunerHosts`（代码只有 POST/DELETE，补 GET 即可） |

---

## 2. 匹配端点的请求参数级差异

445 个 spec 操作中 443 个有对应代码路由。参数比对（spec query/path 参数 vs 旧 DTO 属性，含基类链合并）后：**29 个操作存在缺失参数**，142 个操作有 spec 之外的冗余参数（3.5.3 独有，客户端不发送，无害保留）。

### 2.1 共享 items 过滤参数集缺口（13 参数 × 13 端点）——最大的一块

`BaseItemsRequest`（`MediaBrowser.Api/UserLibrary/BaseItemsRequest.cs`，GetItems/GetArtists/GetGenres/… 共同基类）缺 4.1 新增的 13 个过滤参数：

```
AnyProviderIdEquals, ArtistType, AudioCodecs, Containers, GroupItemsIntoCollections,
IsKids, IsMovie, IsNews, IsSeries, IsSports, Path, SubtitleCodecs, VideoCodecs
```

受影响端点（13 个，全部客户端高频）：`GET /Items`、`GET /Artists`、`GET /Artists/AlbumArtists`、`GET /GameGenres`、`GET /Genres`、`GET /MusicGenres`、`GET /Persons`、`GET /Studios`、`GET /Trailers`、`GET /Years`、`GET /Users/{UserId}/Items`、`GET /Users/{UserId}/Items/Resume`、`GET /LiveTv/Recordings`（后者见 2.2）。

**修法**：在 `BaseItemsRequest` 补 13 个属性（类型：`string` AnyProviderIdEquals/ArtistType/Path、`bool?` IsKids/IsMovie/IsNews/IsSeries/IsSports/GroupItemsIntoCollections、`string[]` AudioCodecs/Containers/SubtitleCodecs/VideoCodecs），并把它们映射进 `ItemFilter`/`InternalItemsQuery` 转换逻辑（`BaseItemsRequest` → `ItemQuery` 的映射处，即 `ItemsService`/`BaseItemsByNameService` 的请求转换）。查询引擎（SqliteItemRepository → PG 仓储）需支持对应过滤字段（多数已有列，AnyProviderIdEquals 需 JSON/provider 表查询）。

### 2.2 GET /LiveTv/Recordings 缺 71 个参数

`GetRecordings`（`MediaBrowser.Api/LiveTv/LiveTvService.cs`）不是 `BaseItemsRequest` 子类，缺整个 items 过滤/分页/排序参数集（AdjacentTo、AiredDuringSeason、Albums、Filters、SortBy、StartIndex、Limit、Fields、EnableImages…共 71 个）。

**修法**：让 `GetRecordings` 继承 `BaseItemsRequest`（或组合其参数），在录像查询里应用分页/过滤。难度：中（录像查询走 ILiveTvManager，过滤逻辑要桥接到 items 查询）。

### 2.3 零散参数缺口

| 操作 | 缺失参数 | 修法 |
|------|----------|------|
| 15 个 InstantMix/Similar 端点（GET /Albums/{Id}/InstantMix、/Artists/{Id}/Similar、/Movies/{Id}/Similar、/Shows/{Id}/Similar、…） | `IncludeItemTypes` | `SimilarItemsRequest`/InstantMix 请求基类补 `string[] IncludeItemTypes` 属性（易） |
| GET /Items/{Id}/RemoteSearch/Subtitles/{Language} | `IsForced`、`IsPerfectMatch` | `SearchRemoteSubtitles` DTO 补 2 个 `bool?`（易） |
| POST /Users/{UserId}/PlayedItems/{Id} | `DatePlayed` | `MarkPlayedItem` DTO 补 `DateTime? DatePlayed`（易） |

### 2.4 请求体（body）契约

51 个匹配 POST 操作的 requestBody schema 与旧 DTO 一一对应（`POST /Sessions/Playing` ↔ `PlaybackStartInfo`、`POST /Items/{Id}/PlaybackInfo` ↔ `PlaybackInfoRequest`、`POST /LiveStreams/Open` ↔ `LiveStreamRequest` 等），字段级缺口见 §3（同一批 DTO）。无路径级 body 缺失。

---

## 3. 响应 schema 字段级差异

105 个在用响应/请求 schema 与旧 DTO 类做了属性集比对（含基类链）。**14 个 schema 存在真实字段缺失**（另 10 个 `Providers.RemoteSearchQuery_*` 为泛型误报——`RemoteSearchQuery<T>` 的 SearchInfo/ItemId/SearchProviderName/IncludeDisabledProviders 字段齐全）：

| Schema（旧类） | 缺失字段 | 客户端影响 | 难度 |
|----------------|----------|------------|------|
| `UserPolicy`（Model/Users/UserPolicy.cs） | DisablePremiumFeatures, EnableSubtitleDownloading, EnableSubtitleManagement, ExcludedSubFolders, IsHiddenRemotely | 用户策略保存/回显（admin 客户端） | 易：补 5 属性（bool?/string[]），策略存储序列化自动带上 |
| `ServerConfiguration`（Model/Configuration/ServerConfiguration.cs） | CollapseVideoFolders, EnableOriginalTrackTitles, LogAllQueryTimes, SubtitlePermissionsUpgraded | /System/Configuration 保存回显 | 易：补 4 属性（bool） |
| `BaseItemDto`（Model/Dto/BaseItemDto.cs） | GameSystemId, IsNew, SupportsResume | item 详情（IsNew 录像标记、SupportsResume 播放按钮状态） | 易：补 3 属性（string/int?/bool?），DtoOptions 填充逻辑补映射 |
| `LiveStreamRequest`（Model/MediaInfo/LiveStreamRequest.cs） | AllowAudioStreamCopy, AllowVideoStreamCopy, EnableTranscoding | /LiveStreams/Open 请求 | 易：补 3 个 bool? |
| `UserDto`（Model/Dto/UserDto.cs） | ConnectLinkType, ConnectUserName | Connect 域遗留字段 | 跳过或补 2 个占位 string（客户端登录页不再解析） |
| `ListingsProviderInfo`（Model/LiveTv/ListingsProviderInfo.cs） | Name, SetupUrl | LiveTv 向导 | 易：补 2 属性 |
| `ThemeMediaResult`（Model/Querying/ThemeMediaResult.cs） | Items, TotalRecordCount | /Items/{Id}/ThemeMedia | 易：补 2 属性（BaseItemDto[]/int） |
| `PlaybackStartInfo` / `PlaybackProgressInfo`（Model/Session/） | RunTimeTicks | 播放上报回显 | 易：补 long? |
| `PluginInfo`（Model/Plugins/PluginInfo.cs） | ImageTag | 插件列表图标 | 易 |
| `QueryFilters`（Model/Querying/QueryFilters.cs） | Studios | /Items/Filters2 | 易 |
| `WakeOnLanInfo`（Model/System/WakeOnLanInfo.cs） | BroadcastAddress | /System/WakeOnLanInfo | 易 |
| `SystemInfo`（Model/System/SystemInfo.cs） | HardwareAccelerationRequiresPremiere | /System/Info | 易：补 bool（恒 false，无授权体系） |
| `RemoteSubtitleInfo`（Model/Providers/RemoteSubtitleInfo.cs） | IsForced | 字幕搜索 | 易 |

**类型级差异（已与 docs/json-migration-map.md 协调）**：

| 字段 | spec | 旧代码 | 结论 |
|------|------|--------|------|
| `BaseItemDto.AlbumArtists/ArtistItems` | `NameIdPair[]`（Id: string） | `NameGuidPair[]`（Id: Guid） | **JSON 输出等价，无需改造**：属性名同为 `Name`/`Id`，Guid 经 json-migration 的 `EmbyGuidConverter` 输出 "N" 格式字符串，客户端拿到即 string。Model 里 `NameIdPair`（string Id）已并存，两者序列化形态相同 |
| `BaseItemDto.GenreItems` | `NameLongIdPair[]`（Id: integer） | `NameGuidPair[]`（Id: Guid） | **真实差异但低风险**：客户端 genre 过滤主通道是 `Genres`（string[]）；如需严格对齐可后续把 GenreItems 的 Id 改 long（PG 仓储 genre 表主键即 long） |
| `BaseItemDto.AirDays/LockedFields/LocationType/PlayAccess/ImageOrientation` 等枚举 | string / `string[]` | 同名 C# 枚举 | **已解决**：json-migration-map §3.1 确认 `JsonStringEnumConverter`（枚举输出字符串名），与 spec 一致 |
| 属性命名风格 | 全部 **PascalCase** | C# PascalCase | **一致**：json-migration-map 结论 `PropertyNamingPolicy = null`（保持原名），无 camelCase 改名压力 |

> 与 json-migration 的协调结论：字段命名（PascalCase）、枚举字符串化、Guid "N" 格式字符串三项序列化契约由其兼容层统一保证，本 gap map 的 DTO 工作只剩**补字段**（§3 表），无字段改名任务。

---

## 4. 缺失端点实现清单（44 个）

> 关键发现：spec 从**装了插件的实例**导出，`user_usage_stats`（tag=UserActivityAPI，即 Playback Reporting 插件）、`Reports`（Emby Reports 插件）、`Items/{Id}/ThumbnailSet`（RokuMetadata 插件）、`Notification/SMTP/Test`（SMTP 通知插件）共 **25 个是插件端点**，官方客户端不调用（仅 dashboard 插件页用）。核心缺失实为 **19 个**。

### 4.1 核心端点（19 个，必须实现）

> **实现状态（B3/B4 已完成）**：14 个新端点已实现（TagService 8、Encoding 2、DeleteInfo、SelectableMediaFolders、Plugins/Thumb、LiveTv 3 个 GET）；`/LiveTv/Programs` 与 `/LiveTv/ChannelMappingOptions`+`/LiveTv/ChannelMappings` 经核实**已存在**（多 verb Route `"GET,POST"` 与单参数 Route 两种写法未被脚本扫描识别，属盘点误报，DTO 与处理方法齐全）；`/Videos/{Id}/subtitles.m3u8` 已实现（SubtitleService 扩展，自动选第一条字幕流）；`/Videos/{Id}/index.bif` 已建 404 占位端点（BIF 生成待 ffmpeg 抽帧，客户端可优雅降级）。

| # | 端点 | verb | 响应 | 难度 | 建议位置 |
|---|------|------|------|------|----------|
| 1-8 | `/AudioCodecs` `/VideoCodecs` `/SubtitleCodecs` `/Containers` `/OfficialRatings` `/Tags` `/Artists/Prefixes` `/Items/Prefixes` | GET | `QueryResult<TagItem>` / `NameValuePair[]` | **易**（ItemRepository distinct 查询拼装；参数集复用 BaseItemsRequest） | 新建 `MediaBrowser.Api/UserLibrary/TagService.cs`（8 端点一个文件，复用 BaseItemsByNameService 模式） |
| 9 | `GET+POST /LiveTv/Programs` | GET/POST | `QueryResult<BaseItemDto>` | **易**（已有 GetPrograms 单台查询逻辑，加批量 ChannelIds 查询） | `LiveTvService.cs` 扩展 |
| 10 | `GET /LiveTv/ListingProviders/Available` | GET | `ListingProviderTypeInfo[]` | **易**（枚举静态列表） | `LiveTvService.cs` 扩展 |
| 11 | `GET /LiveTv/ListingProviders` | GET | `ListingsProviderInfo[]` | **易**（已有 POST/DELETE，ILiveTvManager.ListingProviders 补 GET） | `LiveTvService.cs` 补 Get 方法 |
| 12 | `GET /LiveTv/TunerHosts` | GET | `TunerHostInfo[]` | **易**（同上） | `LiveTvService.cs` 补 Get 方法 |
| 13 | `GET+POST /LiveTv/ChannelMappingOptions`（ProviderId query） | GET/POST | `ChannelMappingOptions`（新 DTO：TunerChannels/ProviderChannels/Mappings） | **中**（ILiveTvManager 需 channel 映射查询/设置；spec 列了 7 个 verb，实现 GET+POST 即可，其余 verb 同路由自动 405/同响应） | `LiveTvService.cs` 扩展 + Model 新增 `ChannelMappingOptionsDto` |
| 14 | `GET+POST /LiveTv/ChannelMappings`（body `SetChannelMapping`） | GET/POST | — | **中**（同上，SetChannelMapping 调 tuner provider） | `LiveTvService.cs` 扩展 |
| 15 | `GET /Encoding/CodecInformation/Video` | GET | `VideoCodecBase[]` | **易**（MediaEncoder 支持的编解码静态表） | 新建 `MediaBrowser.Api/EncodingService.cs` |
| 16 | `GET /Encoding/CodecConfiguration/Defaults` | GET | `CodecConfiguration[]` | **易**（默认编解码配置表） | 同上 |
| 17 | `GET /Items/{Id}/DeleteInfo` | GET | `DeleteInfo`（新 DTO：CanDelete/PathToFiles[]） | **易** | `ItemLookupService.cs` 扩展 |
| 18 | `GET /Library/SelectableMediaFolders` | GET | `MediaFolder[]` | **易**（LibraryManager.SelectableMediaFolders 已有类似逻辑） | `LibraryService.cs` 扩展 |
| 19 | `GET /Plugins/{Id}/Thumb` | GET | 图片流 | **易**（插件图标文件） | `PluginService.cs` 扩展 |

另有两个"难"级核心端点单列：

| 端点 | verb | 难度 | 说明 |
|------|------|------|------|
| `GET /Videos/{Id}/index.bif`（Width query） | GET | **难**（BIF 缩略图文件生成：ffmpeg 定时抽帧打包，Roku 客户端进度条预览用） | 新建 `MediaBrowser.Api/BifService.cs` + MediaEncoding 抽帧；可先返回 404 占位再补 |
| `GET /Videos/{Id}/subtitles.m3u8`（SubtitleSegmentLength/ManifestSubtitles query） | GET | **中**（字幕 HLS 清单；`SubtitleService.cs` 已有 `/Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/subtitles.m3u8` 实现可改造） | `SubtitleService.cs` 扩展 |

### 4.2 插件端点（25 个，低优先级/可暂缓）

| 域 | 端点数 | 明细 | 处置建议 |
|----|--------|------|----------|
| user_usage_stats | 19 | PlayActivity/HourlyReport/DurationHistogramReport/TvShowsReport/MoviesReport/UserPlaylist/BreakdownReport/user_activity/user_list/session_list/resource_usage/process_list/type_filter_list/load_backup/save_backup/import_backup/submit_custom_query/user_manage/GetItems | **暂缓**（dashboard 插件页用；若要做：PG 对 activitylog/session 表聚合查询，新建 `UserUsageStatsService.cs`，难度中） |
| Reports | 4 | /Reports/Activities、/Headers、/Items、/Items/Download | **暂缓**（Emby Reports 插件；CSV 报表导出，难度中） |
| ThumbnailSet | 1 | /Items/{Id}/ThumbnailSet | **暂缓**（RokuMetadata 插件） |
| SMTP Test | 1 | POST /Notification/SMTP/Test/{UserID} | **暂缓**（SMTP 通知插件） |

### 4.3 跳过清单（25 个，不实现）

- **Sync 18**：`/Sync/Jobs`、`/Sync/JobItems`、`/Sync/Items`、`/Sync/Data`、`/Sync/OfflineActions`、`/Sync/Options`、`/Sync/Targets`、`/Sync/{Id}/Items`、`/Sync/{Id}/Status`——D2 决策：同步功能移除，表照建留空。
- **Connect 3**：`/Connect/Exchange`、`/Connect/Pending`、`/Users/{Id}/Connect/Link`——云连接移除。
- **swagger/openapi 4**：`/swagger`、`/swagger.json`、`/openapi`、`/openapi.json`——文档端点，Kestrel 迁移后可选做静态托管（非必须）。

---

## 5. 旧有端点处置表（27 个 code-only）

| 端点 | verb | 处置 | 理由 |
|------|------|------|------|
| `/Plugins/SecurityInfo` | GET/POST | **删除** | Emby 授权体系（死功能，D7 决策） |
| `/Plugins/RegistrationRecords/{Id}` | GET | **删除** | 同上 |
| `/Registrations/{Id}` | GET | **删除** | 同上 |
| `/Appstore/Register` | POST | **删除** | 同上 |
| `/Startup/Complete` `/Startup/Configuration` `/Startup/RemoteAccess` `/Startup/User` | POST/GET | **保留** | dashboard 启动向导自用 |
| `/System/Configuration/MetadataOptions/Default` | GET | **保留** | dashboard 自用 |
| `/System/MediaEncoder/Path` | POST | **保留** | dashboard 自用 |
| `/Dlna/{Id}/connectionmanager/events`、`/Dlna/{Id}/contentdirectory/events`、`/Dlna/{Id}/mediareceiverregistrar/*`（events SUBSCRIBE/UNSUBSCRIBE + control POST + 描述 GET×2） | 8 组 | **保留** | DLNA 协议必需（GENA 事件订阅、Xbox/PlayStation 的 mediareceiverregistrar），spec 未收录但 4.1 服务器实际有 |
| `/Channels/Features`、`/Channels/{Id}/Features` | GET | **保留** | 4.x 客户端频道特性探测（spec 4.1.1.0 未收录，无害） |
| `/Items/{Id}/ContentType` | POST | **保留** | 旧客户端用 |
| `/Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/subtitles.m3u8` | GET | **保留** | 旧版字幕 HLS（spec 版本无 MediaSourceId 段，两者并存） |
| `/Years/{Id}`、`/Years/{Id}/Images/*` | GET/HEAD | **保留** | items-by-name 完整性 |
| `/favicon.ico`、`/robots.txt`、`/web/{Path}`、`/web/Package` | GET | **保留** | Web 静态资源 |

---

## 6. 认证契约核对

spec `components.securitySchemes` vs 旧 `Emby.Server.Implementations/HttpServer/Security/AuthorizationContext.cs`：

| spec 定义 | 旧实现 | 结论 |
|-----------|--------|------|
| `apikeyauth`（apiKey，query 参数 `api_key`；文档注明也可用 `X-Emby-Token` 头） | `AuthorizationContext.GetAuthorization`：`api_key` query（L75）→ `X-Emby-Token` 头（L66）→ `X-MediaBrowser-Token` 头（L71，旧名兼容） | ✅ 已覆盖 |
| `embyauth`（http bearer，`Emby UserId="(guid)", Client="(string)", Device="(string)", DeviceId="(string)", Version="string", Token="(string)"`） | `GetAuthorizationDictionary`：解析 `X-Emby-Authorization` 头或 `Authorization` 头，接受 `MediaBrowser`/`Emby` 前缀 + 逗号分隔 `K="V"` 键值对（DeviceId/Device/Client/Version/Token/UserId 任意键），token 命中 `_authRepo` 后回填用户 | ✅ 已覆盖（含 UserId 键解析） |

**结论：认证契约无需新增逻辑，零差距。** 唯一约束：Kestrel 迁移（C13）时必须原样保留这套头解析（`X-Emby-Token` / `X-Emby-Authorization` / `Authorization` / `api_key` 四通道），CORS `Access-Control-Allow-Headers` 已含 `X-Emby-Authorization`、`X-MediaBrowser-Token`（`ResponseFilter.cs` L29、`HttpListenerHost.cs` L543），Kestrel 版需带上 `X-Emby-Token`。

---

## 7. 优先级排序与实现批次（第二步计划）

客户端高频路径对齐状态（登录→浏览→播放链路）：

| 链路 | 端点 | 状态 |
|------|------|------|
| 登录 | POST /Users/AuthenticateByName、GET /Users/Public、GET /System/Info/Public | ✅ 路径/参数/schema 就绪（UserDto 补 2 字段可选） |
| 浏览 | GET /Users/{UserId}/Items、GET /Users/{UserId}/Views、GET /Items、GET /Shows/NextUp | ⚠️ 路径在，**缺 13 个过滤参数**（§2.1）+ BaseItemDto 3 字段 |
| 图片 | GET /Items/{Id}/Images/{Type} | ✅ 参数齐全（Height/Width/Quality 等在 ImageRequest 基类） |
| 播放决策 | GET/POST /Items/{Id}/PlaybackInfo | ✅ 已存在（MediaEncoding.Api），PlaybackInfoRequest 补 5 字段（§3） |
| 流媒体 | /Videos/{Id}/stream、/Audio/{Id}/universal、hls1、master.m3u8 | ✅ 已存在，转码参数在 BaseEncodingJobOptions（跨程序集基类已核对齐全） |
| 播放上报 | POST /Sessions/Playing、/Progress、/Stopped、/Ping | ✅ 已存在（MediaEncoding.Api），PlaybackStartInfo/ProgressInfo 各补 RunTimeTicks |
| 会话 | GET /Sessions、POST /Sessions/Capabilities/Full | ✅ 就绪 |
| 系统 | GET /System/Ping、/System/Info | ✅ 就绪（SystemInfo 补 1 字段） |

**建议批次**（每批 dotnet build 0 错误后交 lead 提交）——**B1–B5 已全部完成并提交**：

| 批次 | 内容 | 文件 | 规模 | 状态 |
|------|------|------|------|------|
| **B1 DTO 字段对齐** | §3 全部易级字段（UserPolicy 5、ServerConfiguration 4、BaseItemDto 3、LiveStreamRequest 3、ListingsProviderInfo 2、PlaybackStart/Progress RunTimeTicks、PluginInfo/QueryFilters/WakeOnLanInfo/SystemInfo/RemoteSubtitleInfo 各 1）+ §2.3 零散参数（Similar/InstantMix 的 IncludeItemTypes、字幕 2 参数） | `src/MediaBrowser.Model/`（约 14 个 DTO 文件）+ 对应 Api 请求类 | ~30 字段，纯加属性 | ✅ 已提交（fab6087884） |
| **B2 items 过滤参数集** | BaseItemsRequest 补 13 参数 + ItemFilter/查询映射 + GET /LiveTv/Recordings 接入 BaseItemsRequest | `MediaBrowser.Api/UserLibrary/BaseItemsRequest.cs`、`ItemsService.cs`、`BaseItemsByNameService.cs`、`LiveTvService.cs` + SqliteItemRepository WHERE 分支 | 中 | ✅ 已提交（dd2c5b6c19） |
| **B3 核心新端点·易** | TagService 8 端点 + Encoding 2 + Items/DeleteInfo + Library/SelectableMediaFolders + Plugins/Thumb + LiveTv 3 个 GET（ListingProviders/TunerHosts/Available） | 新建 `TagService.cs`、`EncodingService.cs`；扩展 LiveTvService/ItemLookupService/LibraryService/PluginService | 14 端点 | ✅ 已提交（765edea551） |
| **B4 字幕清单** | /Videos/{Id}/subtitles.m3u8（自动选第一条字幕流，复用现有 m3u8 构建） | SubtitleService.cs | 中 | ✅ 本轮 |
| **B5 BIF 占位** | index.bif 404 占位端点（BIF 生成待 ffmpeg 抽帧） | 新建 `BifService.cs` | 难/后续 | ✅ 占位完成 |
| ~~B5 GenreItems~~ | ~~GenreItems 的 Id 改 long~~ | — | — | ⛔ 不做：Sqlite 过渡期无 long genre id 数据源（genre 存 ItemValues 表无独立主键）；JSON 层 Guid "N" 字符串与 spec 的 string Id 形态兼容、客户端把 Id 当不透明值，实际影响为零。若 PG 域层（pg-data）将来以 bigint 作 genre 主键，属 C16+ 域层重构再联动 |
| **B6 插件端点（可选）** | user_usage_stats 19 + Reports 4 + ThumbnailSet + SMTP Test | 视需求 | 暂缓 | ⏸ lead 已拍板暂缓 |

**验收**（P5 扩展）：以 spec 445 操作做冒烟——443 匹配操作参数/响应字段核对通过 + 44 缺失端点中核心 19 个上线 + 25 个插件端点显式 404（或实现）+ 25 个跳过端点返回与 4.1 一致的 404/空响应。

---

## 附录 A：数据与复现

中间产物（`%TEMP%\apigap\`）：`spec-ops.json`（445 操作含参数/响应）、`spec-schemas.json`（201 schema 属性表）、`routes.json`（394 路由含 DTO 属性/IReturn）、`op-diffs.json`（443 操作参数 diff）、`schema-diffs2.json`（105 schema 字段 diff）、`spec-security.json`。脚本：`parse_spec.py`、`scan_code.py`（含跨程序集基类合并）、`scan_model.py`、`scan_controller.py`、`diff.py`、`schema_diff2.py`、`breakdown.py`、`detail.py`。

## 附录 B：核对中排除的误报（供后续复核）

1. nullable 属性（`int?` 等）初版正则漏扫——已修复，全部重扫。
2. abstract 基类（`BaseItemsRequest`）初版 class 正则漏扫——已修复。
3. `RemoteSearchQuery<T>` 泛型字段（SearchInfo/ItemId/…）被误记到具体 Info 类——已确认为误报。
4. MediaEncoding 程序集的转码参数（AudioBitRate 等 27 个）在 `BaseEncodingJobOptions`（Model）——跨程序集基类合并后确认齐全，非缺口。
5. spec 中 `/LiveTv/ChannelMappings`、`/ChannelMappingOptions` 各列 7 个 verb（含 OPTIONS/PATCH/HEAD）——为 spec 生成器全 verb 展开，实现 GET+POST 即可。
