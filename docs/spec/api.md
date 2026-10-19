# API 兼容契约（SPEC/api）

> 状态：骨架 + 自有客户端基准栏〔定稿〕；全端点矩阵〔草案，待对照 4.8 OpenAPI 展开〕。
> 锚点：Emby 4.8 stable OpenAPI（ADR-0004）。兼容原则：DTO/Id 形态照搬、行为自有。
> 被砍端点返回结构性空数据（不 404）。铁律：凡"照 Emby"必须完整展开并附出处。

## 1. 自有客户端（LinWeb）实测依赖基准 〔定稿〕

LinWeb（本产品配套播放器）在真实 Emby 上的实测依赖，是兼容矩阵的**最低验收集**。
证据：D:\WorkProgram\webplayer 源码盘点（client.js/emby.html/play.html，行号见
盘点报告）。

### 1.1 鉴权与 CORS（三件铁律）
- 双通道鉴权：`Authorization: MediaBrowser Client="…", Device="…", DeviceId="…", Version="…", Token="…"` 头 + 所有请求 `?api_key=<token>` 查询参数。
- CORS 预检只放行 `Authorization` 头（浏览器不接受 `X-Emby-*` 进预检）。
- 必须返回 `Access-Control-Expose-Headers: Accept-Ranges, Content-Length, Content-Range`（否则拖动进度条失效）。

### 1.2 PlaybackInfo 契约
- 响应：`MediaSources[]`（`Id/Path/Protocol/IsRemote/Container/Size/Name/Bitrate/MediaStreams[]`）+ `PlaySessionId` + **`DirectStreamUrl`（唯一权威交付地址，URL 必须可重签 api_key）**。
- 客户端以"空 TranscodingProfiles 的 DeviceProfile"逼迫直连：服务器必须尊重——**不得**在这种 Profile 下返回 TranscodingUrl。
- `.strm` 形状契约：首播未探测时 `Container='strm'`/缺失、`Size=null`、`MediaStreams=[]` 三态都必须能活；`Protocol='Http'`/`IsRemote=true`/Path 形状用于远程判定。

### 1.3 外挂字幕契约
- `MediaStreams[]` 字幕轨：`DeliveryMethod: External` 时必须给出 `DeliveryUrl`，指向**原格式原始字节**（ass/ssa/srt/vtt 直传，永不转码转格式）；无 DeliveryUrl = 该轨不提供。
- 位图字幕（pgs/vobsub/dvbsub）按 Emby 规则标记，由客户端判定 unsupported。

### 1.4 进度上报
- `POST /Sessions/Playing`、`/Sessions/Playing/Progress`、`/Sessions/Playing/Stopped`；字段 `ItemId/MediaSourceId/PlaySessionId/PositionTicks/IsPaused/PlayMethod/EventName`；上报失败必须不影响播放（服务端幂等、宽容乱序）。

### 1.5 必须支持的查询参数组合（`/Users/{userId}/Items`）
- `ParentId/IncludeItemTypes/Recursive/Fields/StartIndex/Limit/SortBy/SortOrder/Filters/Genres/SearchTerm/IsHD/Is4K/HasSubtitles`
- `Filters=IsResumable,IsUnplayed` + `SortBy=DatePlayed`（继续观看横栏的活路）
- `Fields` 至少含：`PrimaryImageAspectRatio,Overview,Genres,ProductionYear,CommunityRating,OfficialRating,RunTimeTicks,MediaSources,People`

### 1.6 LinWeb 调用的端点全集（兼容矩阵行级清单）

鉴权/会话：`POST /Users/AuthenticateByName`、`GET /Users/Public`、
`GET /Users/{id}`、`GET /Users/{id}/Views`
浏览：`GET /Users/{id}/Items`、`GET /Users/{id}/Items/Latest`、
`GET /Users/{id}/Items/{id}`、`GET /Shows/NextUp`、
`GET /Shows/{seriesId}/Seasons`、`GET /Shows/{seriesId}/Episodes`、
`GET /Genres`、`GET /Items/{id}/Similar`
播放：`POST /Items/{id}/PlaybackInfo`
条目操作：`POST/DELETE /Users/{id}/PlayedItems/{id}`、
`POST/DELETE /Users/{id}/FavoriteItems/{id}`、
`POST/DELETE /Users/{id}/Items/{id}/Rating`、`POST /Playlists`、
`POST /Playlists/{id}/Items`、`DELETE /Items/{id}`、
`POST /Items/{id}/Refresh`、`POST /Items/{id}`、
`POST /Items/RemoteSearch/{Movie|Series|Episode|BoxSet}`、
`POST /Items/RemoteSearch/Apply/{id}`
管理：`GET /System/Info`、`GET /Items/Counts`、`GET /Users`、
`POST /Users/{id}/Policy`、`GET /Library/VirtualFolders`、
`POST /Library/Refresh`、`GET/POST/DELETE /ScheduledTasks(/Running/{id})`、
`GET /Sessions`、`POST /Sessions/{id}/Message`、
`POST /Sessions/{id}/Playing/Stop`、`GET/DELETE /Devices`、
`GET /System/ActivityLog/Entries`、`POST /System/Restart`、
`GET /Branding/Splashscreen`
图片：`GET /Items/{id}/Images/{Type}`（tag/quality/maxWidth/maxHeight）、
`GET /Users/{id}/Images/Primary`

### 1.7 自有扩展端点（Emby 没有，20 册第二栏）
- 弹幕：弹弹play 兼容 `GET /api/v2/search/episodes?anime=`、
  `GET /api/v2/comment/{id}?withRelated=true&format=json`（LinWeb 现成消费端）。
- 房间：WebSocket + REST（定义见 room-danmaku 册）。
- **多线路**：`GET /System/Ext/ServerDomains` → `[{url,name}]`（LinWeb 零改动获得线路切换）。
  契约来源：开源项目 [uhdnow/emby_ext_domains](https://github.com/uhdnow/emby_ext_domains)
  （独立 Go 应用，客户端以 Emby Token 验证后取配置的域名列表）。实现策略：
  **Serata 内置同契约端点**（照抄其响应形状与鉴权行为，致谢上游、实现前核对其许可证），
  同时兼容"外部 sidecar 版 emby_ext_domains 指向 Serata API"的既有部署。
- 扫描 webhook：API Key 鉴权的库刷新触发端点。

## 2. 全端点兼容矩阵 〔草案〕

对照 4.8 OpenAPI（openapi.json，356+ 路径）逐端点标注三级：
**实现 / 空数据（结构性空响应，形状见 4.8 抓包）/ 不存在**。
矩阵在 20 册第二稿补全；LinWeb 依赖清单（1.6）全部为"实现"。