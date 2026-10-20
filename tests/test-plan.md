# OpenMedia API 测试计划（openapi.json 4.1.1.0 解析产物）

> 生成时间：2026-10-20 13:18:14　来源：openapi.json（Emby Server API 4.1.1.0，356 路径 / 414 核心操作 / 201 schema）
> 分级规则：P0 = 客户端关键路径（必须 200 且结构正确）；P1 = 常规查询（200/204/405 可接受，404 需核对 B6 暂缓清单）；P2 = 写/破坏性操作（一律跳过）

## 总览

| 项 | 数量 |
|----|------|
| spec 核心操作（get/post/put/delete/options） | 414 |
| 补充 code-only 端点（/web/index.html 等） | 3 |
| **计划总数** | **417** |
| P0（关键路径） | 14 |
| P1（常规查询，其中 404 预期 60 个 / 400 预期 11 个） | 252 |
| P2（写/破坏性，跳过） | 151 |

## 分域统计

| 域 | P0 | P1 | P2 | 合计 |
|----|----|----|----|------|
| Albums | 0 | 2 | 0 | 2 |
| Artists | 0 | 8 | 0 | 8 |
| Audio | 0 | 7 | 0 | 7 |
| AudioCodecs | 0 | 1 | 0 | 1 |
| Auth | 0 | 2 | 2 | 4 |
| Branding | 0 | 3 | 0 | 3 |
| Channels | 0 | 3 | 0 | 3 |
| Collections | 0 | 0 | 3 | 3 |
| Connect | 0 | 2 | 0 | 2 |
| Containers | 0 | 1 | 0 | 1 |
| Devices | 0 | 4 | 3 | 7 |
| DisplayPreferences | 0 | 1 | 1 | 2 |
| Dlna | 0 | 11 | 5 | 16 |
| Encoding | 0 | 2 | 0 | 2 |
| Environment | 0 | 6 | 1 | 7 |
| GameGenres | 0 | 4 | 0 | 4 |
| Games | 0 | 2 | 0 | 2 |
| Genres | 0 | 4 | 0 | 4 |
| Images | 0 | 7 | 0 | 7 |
| Items | 6 | 23 | 22 | 51 |
| Libraries | 0 | 1 | 0 | 1 |
| Library | 0 | 4 | 13 | 17 |
| LiveStreams | 0 | 0 | 3 | 3 |
| LiveTv | 0 | 33 | 18 | 51 |
| Localization | 0 | 4 | 0 | 4 |
| Movies | 0 | 2 | 0 | 2 |
| MusicGenres | 0 | 6 | 0 | 6 |
| News | 0 | 1 | 0 | 1 |
| Notification | 0 | 0 | 1 | 1 |
| Notifications | 0 | 4 | 3 | 7 |
| OfficialRatings | 0 | 1 | 0 | 1 |
| openapi | 0 | 1 | 0 | 1 |
| openapi.json | 0 | 1 | 0 | 1 |
| Packages | 0 | 3 | 2 | 5 |
| Persons | 0 | 4 | 0 | 4 |
| Playback | 0 | 1 | 0 | 1 |
| Playlists | 0 | 2 | 4 | 6 |
| Plugins | 0 | 3 | 2 | 5 |
| Providers | 0 | 1 | 0 | 1 |
| Reports | 0 | 4 | 0 | 4 |
| ScheduledTasks | 0 | 2 | 3 | 5 |
| Search | 0 | 1 | 0 | 1 |
| Sessions | 1 | 0 | 16 | 17 |
| Shows | 0 | 5 | 0 | 5 |
| Songs | 0 | 1 | 0 | 1 |
| Studios | 0 | 4 | 0 | 4 |
| SubtitleCodecs | 0 | 1 | 0 | 1 |
| swagger | 0 | 1 | 0 | 1 |
| swagger.json | 0 | 1 | 0 | 1 |
| Sync | 0 | 8 | 13 | 21 |
| System | 2 | 8 | 5 | 15 |
| Tags | 0 | 1 | 0 | 1 |
| Trailers | 0 | 2 | 0 | 2 |
| user_usage_stats | 0 | 17 | 2 | 19 |
| Users | 4 | 13 | 25 | 42 |
| VideoCodecs | 0 | 1 | 0 | 1 |
| Videos | 0 | 12 | 4 | 16 |
| web | 1 | 4 | 0 | 5 |
| Years | 0 | 1 | 0 | 1 |

## P0 清单（必须 200 且结构正确）

| 操作 | 说明 | 响应 schema |
|------|------|--------------|
| `GET /Items` | 条目查询 | QueryResult_BaseItemDto |
| `GET /Sessions` | 会话列表 | Session.SessionInfo[] |
| `GET /Users` | 用户列表 | UserDto[] |
| `GET /Items/{Id}/Images/{Type}/{Index}/{Tag}/{Format}/{MaxWidth}/{MaxHeight}/{PercentPlayed}/{UnplayedCount}` | 图片 |  |
| `GET /System/Ping` | 健康检查 |  |
| `GET /Users/Public` | 公共用户列表 | UserDto[] |
| `GET /Items/{Id}/Images` | 图片 | ImageInfo[] |
| `POST /Items/{Id}/PlaybackInfo` | 播放决策 | MediaInfo.PlaybackInfoResponse |
| `GET /System/Info/Public` | 登录前服务器探测 | PublicSystemInfo |
| `GET /Users/{UserId}/Items` | 用户条目（浏览链路） | QueryResult_BaseItemDto |
| `GET /Users/{UserId}/Views` | 用户视图（浏览链路） | QueryResult_BaseItemDto |
| `GET /Items/{Id}/Images/{Type}` | 图片 |  |
| `GET /Items/{Id}/Images/{Type}/{Index}` | 图片 |  |
| `GET /web/index.html` | Web 首页（任务 P0；spec 未收录，code-only） | text/html |

## 404 属预期的操作（SKIP-EXPECTED 判定依据）

| 操作 | 优先级 | 预期原因 |
|------|--------|----------|
| `GET /openapi` | P1 | 跳过清单：文档端点未实现 |
| `GET /openapi.json` | P1 | 跳过清单：文档端点未实现 |
| `GET /swagger` | P1 | 跳过清单：文档端点未实现 |
| `GET /swagger.json` | P1 | 跳过清单：文档端点未实现 |
| `GET /Connect/Pending` | P1 | 跳过清单：云连接移除 |
| `GET /Connect/Exchange` | P1 | 跳过清单：云连接移除 |
| `GET /Environment/DirectoryContents` | P1 | 合成参数 404 合理（无对应资源） |
| `GET /GameGenres/{Name}` | P1 | 环境限制：库内无游戏媒体 → 无 GameGenre 实体，404 预期（列表端点 /GameGenres 已 200 验证端点存活） |
| `GET /Packages/{Name}` | P1 | 合成参数 404 合理（无对应资源） |
| `GET /Reports/Activities` | P1 | B6 插件端点暂缓（Emby Reports 插件） |
| `GET /Reports/Headers` | P1 | B6 插件端点暂缓（Emby Reports 插件） |
| `GET /Reports/Items` | P1 | B6 插件端点暂缓（Emby Reports 插件） |
| `GET /Sync/Targets` | P1 | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/Jobs` | P1 | 跳过清单：同步功能移除（D2 决策） |
| `POST /Sync/Jobs` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/JobItems` | P1 | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/Options` | P1 | 跳过清单：同步功能移除（D2 决策） |
| `POST /Sync/OfflineActions` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `POST /Sync/Data` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `GET /user_usage_stats/type_filter_list` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/user_activity` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/user_list` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `POST /user_usage_stats/import_backup` | P2 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/load_backup` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/save_backup` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/PlayActivity` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/HourlyReport` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/DurationHistogramReport` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/TvShowsReport` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/MoviesReport` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `POST /user_usage_stats/submit_custom_query` | P2 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/UserPlaylist` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/session_list` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/resource_usage` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/process_list` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /web/ConfigurationPage` | P1 | 合成参数 404 合理（无对应资源） |
| `GET /Dlna/icons/{Filename}` | P1 | 合成参数 404 合理（无对应资源） |
| `GET /Items/{Id}/ThumbnailSet` | P1 | B6 插件端点暂缓（RokuMetadata 插件） |
| `GET /LiveTv/ListingProviders/Lineups` | P1 | 合成参数 404 合理（无对应资源） |
| `GET /LiveTv/Timers/{Id}` | P1 | 无活跃 timer fixture（B 轨道 timer 已消费），404 正确 |
| `GET /LiveTv/SeriesTimers/{Id}` | P1 | 无 series timer fixture，404 正确 |
| `GET /Plugins/{Id}/Thumb` | P1 | 无插件安装（去 Emby 化后零插件），404 正确 |
| `GET /Plugins/{Id}/Configuration` | P1 | 无插件安装（去 Emby 化后零插件），404 正确 |
| `GET /Reports/Items/Download` | P1 | B6 插件端点暂缓（Emby Reports 插件） |
| `POST /Sync/{ItemId}/Status` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/Jobs/{Id}` | P1 | 跳过清单：同步功能移除（D2 决策） |
| `POST /Sync/Jobs/{Id}` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `DELETE /Sync/Jobs/{Id}` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `POST /Sync/Items/Cancel` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `DELETE /Sync/{TargetId}/Items` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/Items/Ready` | P1 | 跳过清单：同步功能移除（D2 决策） |
| `DELETE /Sync/JobItems/{Id}` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `GET /System/Configuration/{Key}` | P1 | 合成参数 404 合理（无对应资源） |
| `GET /user_usage_stats/{BreakdownType}/BreakdownReport` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /Videos/{Id}/index.bif` | P1 | B5 占位：BIF 生成未实现，404 预期 |
| `GET /Dlna/{UuId}/icons/{Filename}` | P1 | 合成参数 404 合理（无对应资源） |
| `GET /GameGenres/{Name}/Images/{Type}` | P1 | 环境限制：库内无游戏媒体 → 无 GameGenre 实体，404 预期（列表端点 /GameGenres 已 200 验证端点存活） |
| `GET /Images/General/{Name}/{Type}` | P1 | 合成参数 404 合理（无对应资源） |
| `GET /Images/Ratings/{Theme}/{Name}` | P1 | 合成参数 404 合理（无对应资源） |
| `GET /Images/MediaInfo/{Theme}/{Name}` | P1 | 合成参数 404 合理（无对应资源） |
| `GET /LiveTv/LiveRecordings/{Id}/stream` | P1 | 无进行中直播录制（已完成录制不走此路由），404 正确 |
| `GET /LiveTv/LiveStreamFiles/{Id}/stream.{Container}` | P1 | 无活跃直播流文件 fixture（需 OpenToken 会话），404 正确 |
| `GET /LiveTv/Recordings/Groups/{Id}` | P1 | 无录制组 fixture（Groups 端点期望组而非单条录制），404 正确 |
| `POST /Notification/SMTP/Test/{UserID}` | P2 | B6 插件端点暂缓（SMTP 通知插件） |
| `GET /Providers/Subtitles/Subtitles/{Id}` | P1 | 合成参数 404 合理（无对应资源） |
| `POST /Sync/JobItems/{Id}/Transferred` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/JobItems/{Id}/File` | P1 | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/JobItems/{Id}/AdditionalFiles` | P1 | 跳过清单：同步功能移除（D2 决策） |
| `POST /Sync/JobItems/{Id}/Enable` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `POST /Sync/JobItems/{Id}/MarkForRemoval` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `POST /Sync/JobItems/{Id}/UnmarkForRemoval` | P2 | 跳过清单：同步功能移除（D2 决策） |
| `POST /Users/{Id}/Connect/Link` | P2 | 跳过清单：云连接移除 |
| `DELETE /Users/{Id}/Connect/Link` | P2 | 跳过清单：云连接移除 |
| `GET /user_usage_stats/user_manage/{Action}/{Id}` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/{UserID}/{Date}/GetItems` | P1 | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /GameGenres/{Name}/Images/{Type}/{Index}` | P1 | 环境限制：库内无游戏媒体 → 无 GameGenre 实体，404 预期（列表端点 /GameGenres 已 200 验证端点存活） |
| `GET /Videos/{Id}/hls/{PlaylistId}/{SegmentId}.{SegmentContainer}` | P1 | legacy hls 路由合成分片参数，404 正确（负路径已固化） |
| `GET /Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/Stream.{Format}` | P1 | fixture 媒体无字幕流，404 正确（无流→404 已是修复后行为） |

## 全量操作清单（按域分组）

### 域：Albums（2 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Albums/{Id}/InstantMix` | P1 | Id(path)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Albums/{Id}/Similar` | P1 | Id(path)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |

### 域：Artists（8 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Artists` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Artists/{Id}/Similar` | P1 | Id(path)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Artists/{Name}` | P1 | Name(path)*,UserId(query) |  | BaseItemDto |  |  |
| `GET /Artists/{Name}/Images/{Type}` | P1 | Name(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(query) |  |  |  |  |
| `GET /Artists/{Name}/Images/{Type}/{Index}` | P1 | Name(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(path)* |  |  |  |  |
| `GET /Artists/AlbumArtists` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Artists/InstantMix` | P1 | Id(query)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Artists/Prefixes` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | NameValuePair[] |  |  |

### 域：Audio（7 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Audio/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}` | P1 | PlaylistId(path)*,SegmentId(path)*,DeviceProfileId(query),SegmentContainer(path)*,Id(path)*,DeviceId(query),Container(query)*,AudioCodec(query),EnableAutoStreamCopy(query),AudioSampleRate(query),AudioBitRate(query),AudioChannels(query),MaxAudioChannels(query),Static(query),Profile(query),Level(query),Framerate(query),MaxFramerate(query),CopyTimestamps(query),StartTimeTicks(query),Width(query),Height(query),MaxWidth(query),MaxHeight(query),VideoBitRate(query),SubtitleStreamIndex(query),SubtitleMethod(query),MaxRefFrames(query),MaxVideoBitDepth(query),VideoCodec(query),AudioStreamIndex(query),VideoStreamIndex(query) |  |  |  | 是 |
| `GET /Audio/{Id}/main.m3u8` | P1 | DeviceProfileId(query),Id(path)*,DeviceId(query),Container(query)*,AudioCodec(query),EnableAutoStreamCopy(query),AudioSampleRate(query),AudioBitRate(query),AudioChannels(query),MaxAudioChannels(query),Static(query),Profile(query),Level(query),Framerate(query),MaxFramerate(query),CopyTimestamps(query),StartTimeTicks(query),Width(query),Height(query),MaxWidth(query),MaxHeight(query),VideoBitRate(query),SubtitleStreamIndex(query),SubtitleMethod(query),MaxRefFrames(query),MaxVideoBitDepth(query),VideoCodec(query),AudioStreamIndex(query),VideoStreamIndex(query) |  |  |  |  |
| `GET /Audio/{Id}/master.m3u8` | P1 | DeviceProfileId(query),Id(path)*,DeviceId(query),Container(query)*,AudioCodec(query),EnableAutoStreamCopy(query),AudioSampleRate(query),AudioBitRate(query),AudioChannels(query),MaxAudioChannels(query),Static(query),Profile(query),Level(query),Framerate(query),MaxFramerate(query),CopyTimestamps(query),StartTimeTicks(query),Width(query),Height(query),MaxWidth(query),MaxHeight(query),VideoBitRate(query),SubtitleStreamIndex(query),SubtitleMethod(query),MaxRefFrames(query),MaxVideoBitDepth(query),VideoCodec(query),AudioStreamIndex(query),VideoStreamIndex(query) |  |  |  |  |
| `GET /Audio/{Id}/stream` | P1 | DeviceProfileId(query),Id(path)*,DeviceId(query),Container(query)*,AudioCodec(query),EnableAutoStreamCopy(query),AudioSampleRate(query),AudioBitRate(query),AudioChannels(query),MaxAudioChannels(query),Static(query),Profile(query),Level(query),Framerate(query),MaxFramerate(query),CopyTimestamps(query),StartTimeTicks(query),Width(query),Height(query),MaxWidth(query),MaxHeight(query),VideoBitRate(query),SubtitleStreamIndex(query),SubtitleMethod(query),MaxRefFrames(query),MaxVideoBitDepth(query),VideoCodec(query),AudioStreamIndex(query),VideoStreamIndex(query) |  |  |  |  |
| `GET /Audio/{Id}/stream.{Container}` | P1 | DeviceProfileId(query),Id(path)*,DeviceId(query),Container(path)*,AudioCodec(query),EnableAutoStreamCopy(query),AudioSampleRate(query),AudioBitRate(query),AudioChannels(query),MaxAudioChannels(query),Static(query),Profile(query),Level(query),Framerate(query),MaxFramerate(query),CopyTimestamps(query),StartTimeTicks(query),Width(query),Height(query),MaxWidth(query),MaxHeight(query),VideoBitRate(query),SubtitleStreamIndex(query),SubtitleMethod(query),MaxRefFrames(query),MaxVideoBitDepth(query),VideoCodec(query),AudioStreamIndex(query),VideoStreamIndex(query) |  |  |  |  |
| `GET /Audio/{Id}/universal` | P1 | Id(path)*,DeviceId(query),StartTimeTicks(query) |  |  |  |  |
| `GET /Audio/{Id}/universal.{Container}` | P1 | Id(path)*,DeviceId(query),Container(path)*,StartTimeTicks(query) |  |  |  |  |

### 域：AudioCodecs（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /AudioCodecs` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_UserLibrary.TagItem |  |  |

### 域：Auth（4 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Auth/Keys/{Key}` | P2 | Key(path)* |  |  |  |  |
| `GET /Auth/Keys` | P1 |  |  |  |  |  |
| `GET /Auth/Providers` | P1 |  |  | NameIdPair[] |  |  |
| `POST /Auth/Keys` | P2 | App(query)* |  |  |  |  |

### 域：Branding（3 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Branding/Configuration` | P1 |  |  | Branding.BrandingOptions |  |  |
| `GET /Branding/Css` | P1 |  |  |  |  |  |
| `GET /Branding/Css.css` | P1 |  |  |  |  |  |

### 域：Channels（3 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Channels` | P1 | UserId(query),StartIndex(query),Limit(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Channels/{Id}/Items` | P1 |  |  |  |  | 是 |
| `GET /Channels/Items/Latest` | P1 |  |  |  |  |  |

### 域：Collections（3 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Collections/{Id}/Items` | P2 | Ids(query)*,Id(path)* |  |  |  |  |
| `POST /Collections` | P2 | IsLocked(query),Name(query),ParentId(query),Ids(query) |  | Collections.CollectionCreationResult |  |  |
| `POST /Collections/{Id}/Items` | P2 | Ids(query)*,Id(path)* |  |  |  |  |

### 域：Connect（2 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Connect/Exchange` | P1 | ConnectUserId(query)* |  | Connect.ConnectAuthenticationExchangeResult | 是 |  |
| `GET /Connect/Pending` | P1 |  |  |  | 是 |  |

### 域：Containers（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Containers` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_UserLibrary.TagItem |  |  |

### 域：Devices（7 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Devices` | P2 | Id(query)* |  |  |  |  |
| `GET /Devices` | P1 |  |  | QueryResult_Devices.DeviceInfo |  |  |
| `GET /Devices/CameraUploads` | P1 | DeviceId(query)* |  | Devices.ContentUploadHistory |  |  |
| `GET /Devices/Info` | P1 | Id(query)* |  | Devices.DeviceInfo |  |  |
| `GET /Devices/Options` | P1 | Id(query)* |  | Devices.DeviceOptions |  |  |
| `POST /Devices/CameraUploads` | P2 | DeviceId(query)*,Album(query)*,Name(query)*,Id(query)* |  |  |  |  |
| `POST /Devices/Options` | P2 |  | Devices.DeviceOptions |  |  |  |

### 域：DisplayPreferences（2 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /DisplayPreferences/{Id}` | P1 | Id(path)*,UserId(query)*,Client(query)* |  | DisplayPreferences |  |  |
| `POST /DisplayPreferences/{DisplayPreferencesId}` | P2 | DisplayPreferencesId(path)*,UserId(query)* | DisplayPreferences |  |  |  |

### 域：Dlna（16 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Dlna/Profiles/{Id}` | P2 | Id(path)* |  |  |  |  |
| `GET /Dlna/{UuId}/connectionmanager/connectionmanager` | P1 | UuId(path)* |  |  |  |  |
| `GET /Dlna/{UuId}/connectionmanager/connectionmanager.xml` | P1 | UuId(path)* |  |  |  |  |
| `GET /Dlna/{UuId}/contentdirectory/contentdirectory` | P1 | UuId(path)* |  |  |  |  |
| `GET /Dlna/{UuId}/contentdirectory/contentdirectory.xml` | P1 | UuId(path)* |  |  |  |  |
| `GET /Dlna/{UuId}/description` | P1 | UuId(path)* |  |  |  |  |
| `GET /Dlna/{UuId}/description.xml` | P1 | UuId(path)* |  |  |  |  |
| `GET /Dlna/{UuId}/icons/{Filename}` | P1 | UuId(path)*,Filename(path)* |  |  | 是 |  |
| `GET /Dlna/icons/{Filename}` | P1 | UuId(query),Filename(path)* |  |  | 是 |  |
| `GET /Dlna/ProfileInfos` | P1 |  |  | Dlna.DeviceProfileInfo[] |  |  |
| `GET /Dlna/Profiles/{Id}` | P1 | Id(path)* |  | Dlna.DeviceProfile |  |  |
| `GET /Dlna/Profiles/Default` | P1 |  |  | Dlna.DeviceProfile |  |  |
| `POST /Dlna/{UuId}/connectionmanager/control` | P2 | UuId(path)* |  |  |  |  |
| `POST /Dlna/{UuId}/contentdirectory/control` | P2 | UuId(path)* |  |  |  |  |
| `POST /Dlna/Profiles` | P2 |  | Dlna.DeviceProfile |  |  |  |
| `POST /Dlna/Profiles/{Id}` | P2 | Id(path)* | Dlna.DeviceProfile |  |  |  |

### 域：Encoding（2 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Encoding/CodecConfiguration/Defaults` | P1 |  |  | Configuration.CodecConfiguration[] |  |  |
| `GET /Encoding/CodecInformation/Video` | P1 |  |  | MediaEncoding.Codecs.VideoCodecs.VideoCodecBase[] |  |  |

### 域：Environment（7 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Environment/DefaultDirectoryBrowser` | P1 |  |  | DefaultDirectoryBrowserInfo |  |  |
| `GET /Environment/DirectoryContents` | P1 | Path(query)*,IncludeFiles(query),IncludeDirectories(query) |  | IO.FileSystemEntryInfo[] | 是 |  |
| `GET /Environment/Drives` | P1 |  |  | IO.FileSystemEntryInfo[] |  |  |
| `GET /Environment/NetworkDevices` | P1 |  |  | IO.FileSystemEntryInfo[] |  |  |
| `GET /Environment/NetworkShares` | P1 | Path(query)* |  | IO.FileSystemEntryInfo[] |  |  |
| `GET /Environment/ParentPath` | P1 | Path(query)* |  | string |  |  |
| `POST /Environment/ValidatePath` | P2 | Path(query)* | ValidatePath |  |  |  |

### 域：GameGenres（4 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /GameGenres` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /GameGenres/{Name}` | P1 | Name(path)*,UserId(query) |  | BaseItemDto | 是 |  |
| `GET /GameGenres/{Name}/Images/{Type}` | P1 | Name(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(query) |  |  | 是 |  |
| `GET /GameGenres/{Name}/Images/{Type}/{Index}` | P1 | Name(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(path)* |  |  | 是 |  |

### 域：Games（2 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Games/{Id}/Similar` | P1 | Id(path)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Games/SystemSummaries` | P1 | UserId(query) |  | GameSystemSummary[] |  |  |

### 域：Genres（4 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Genres` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Genres/{Name}` | P1 | Name(path)*,UserId(query) |  | BaseItemDto |  |  |
| `GET /Genres/{Name}/Images/{Type}` | P1 | Name(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(query) |  |  |  |  |
| `GET /Genres/{Name}/Images/{Type}/{Index}` | P1 | Name(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(path)* |  |  |  |  |

### 域：Images（7 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Images/General` | P1 |  |  | ImageByNameInfo[] |  |  |
| `GET /Images/General/{Name}/{Type}` | P1 | Name(path)*,Type(path)* |  |  | 是 |  |
| `GET /Images/MediaInfo` | P1 |  |  | ImageByNameInfo[] |  |  |
| `GET /Images/MediaInfo/{Theme}/{Name}` | P1 | Name(path)*,Theme(path)* |  |  | 是 |  |
| `GET /Images/Ratings` | P1 |  |  | ImageByNameInfo[] |  |  |
| `GET /Images/Ratings/{Theme}/{Name}` | P1 | Name(path)*,Theme(path)* |  |  | 是 |  |
| `GET /Images/Remote` | P1 | ImageUrl(query)* |  |  |  | 是 |

### 域：Items（51 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Items` | P2 | Ids(query)* |  |  |  |  |
| `DELETE /Items/{Id}` | P2 | Id(path)* |  |  |  |  |
| `DELETE /Items/{Id}/Images/{Type}` | P2 | Id(path)*,Type(path)*,Index(query) |  |  |  |  |
| `DELETE /Items/{Id}/Images/{Type}/{Index}` | P2 | Id(path)*,Type(path)*,Index(path)* |  |  |  |  |
| `GET /Items` | P0 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Items/{Id}/Ancestors` | P1 | UserId(query),Id(path)* |  | BaseItemDto[] |  |  |
| `GET /Items/{Id}/CriticReviews` | P1 | Id(path)*,StartIndex(query),Limit(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Items/{Id}/DeleteInfo` | P1 | Id(path)* |  | Library.DeleteInfo |  |  |
| `GET /Items/{Id}/Download` | P1 | Id(path)* |  |  |  |  |
| `GET /Items/{Id}/ExternalIdInfos` | P1 | Id(path)* |  | ExternalIdInfo[] |  |  |
| `GET /Items/{Id}/File` | P1 | Id(path)* |  |  |  |  |
| `GET /Items/{Id}/Images` | P0 | Id(path)* |  | ImageInfo[] |  |  |
| `GET /Items/{Id}/Images/{Type}` | P0 | Id(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(query) |  |  |  |  |
| `GET /Items/{Id}/Images/{Type}/{Index}` | P0 | Id(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(path)* |  |  |  |  |
| `GET /Items/{Id}/Images/{Type}/{Index}/{Tag}/{Format}/{MaxWidth}/{MaxHeight}/{PercentPlayed}/{UnplayedCount}` | P0 | Id(path)*,MaxWidth(path)*,MaxHeight(path)*,Width(query),Height(query),Quality(query),Tag(path)*,CropWhitespace(query),EnableImageEnhancers(query),Format(path)*,AddPlayedIndicator(query),PercentPlayed(path)*,UnplayedCount(path)*,BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(path)* |  |  |  |  |
| `GET /Items/{Id}/InstantMix` | P1 | Id(path)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Items/{Id}/PlaybackInfo` | P1 | Id(path)*,UserId(query)* |  | MediaInfo.PlaybackInfoResponse |  |  |
| `GET /Items/{Id}/RemoteImages` | P1 | Id(path)*,Type(query),StartIndex(query),Limit(query),ProviderName(query),IncludeAllLanguages(query) |  | RemoteImageResult |  |  |
| `GET /Items/{Id}/RemoteImages/Providers` | P1 | Id(path)* |  | ImageProviderInfo[] |  |  |
| `GET /Items/{Id}/RemoteSearch/Subtitles/{Language}` | P1 | Id(path)*,Language(path)*,IsPerfectMatch(query),IsForced(query) |  | RemoteSubtitleInfo[] |  |  |
| `GET /Items/{Id}/Similar` | P1 | Id(path)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Items/{Id}/ThemeMedia` | P1 | UserId(query),Id(path)*,InheritFromParent(query) |  | AllThemeMediaResult |  |  |
| `GET /Items/{Id}/ThemeSongs` | P1 | UserId(query),Id(path)*,InheritFromParent(query) |  | ThemeMediaResult |  |  |
| `GET /Items/{Id}/ThemeVideos` | P1 | UserId(query),Id(path)*,InheritFromParent(query) |  | ThemeMediaResult |  |  |
| `GET /Items/{Id}/ThumbnailSet` | P1 | Width(query)*,Id(path)* |  | RokuMetadata.Api.ThumbnailSetInfo | 是 |  |
| `GET /Items/{ItemId}/MetadataEditor` | P1 | ItemId(path)* |  | MetadataEditorInfo |  |  |
| `GET /Items/Counts` | P1 | UserId(query),IsFavorite(query) |  | ItemCounts |  |  |
| `GET /Items/File` | P1 |  |  |  |  |  |
| `GET /Items/Filters` | P1 | UserId(query),ParentId(query),IncludeItemTypes(query),MediaTypes(query) |  | QueryFiltersLegacy |  |  |
| `GET /Items/Filters2` | P1 | UserId(query),ParentId(query),IncludeItemTypes(query),MediaTypes(query) |  | QueryFilters |  |  |
| `GET /Items/Prefixes` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | NameValuePair[] |  |  |
| `GET /Items/RemoteSearch/Image` | P1 | ImageUrl(query)*,ProviderName(query)* |  |  |  | 是 |
| `POST /Items/{Id}/Images/{Type}` | P2 | Id(path)*,Type(path)*,Index(query) |  |  |  |  |
| `POST /Items/{Id}/Images/{Type}/{Index}` | P2 | Id(path)*,Type(path)*,Index(path)* |  |  |  |  |
| `POST /Items/{Id}/Images/{Type}/{Index}/Index` | P2 | Id(path)*,Type(path)*,Index(path)*,NewIndex(query)* |  |  |  |  |
| `POST /Items/{Id}/PlaybackInfo` | P0 | Id(path)* | MediaInfo.PlaybackInfoRequest | MediaInfo.PlaybackInfoResponse |  |  |
| `POST /Items/{Id}/Refresh` | P2 | Recursive(query),Id(path)*,MetadataRefreshMode(query),ImageRefreshMode(query),ReplaceAllMetadata(query),ReplaceAllImages(query) |  |  |  |  |
| `POST /Items/{Id}/RemoteImages/Download` | P2 | Id(path)*,Type(query)*,ProviderName(query),ImageUrl(query) |  |  |  |  |
| `POST /Items/{Id}/RemoteSearch/Subtitles/{SubtitleId}` | P2 | Id(path)*,SubtitleId(path)* |  |  |  |  |
| `POST /Items/{ItemId}` | P2 | ItemId(path)* | BaseItemDto |  |  |  |
| `POST /Items/RemoteSearch/Apply/{Id}` | P2 | Id(path)*,ReplaceAllImages(query) | RemoteSearchResult |  |  |  |
| `POST /Items/RemoteSearch/Book` | P2 |  | Providers.RemoteSearchQuery_Providers.BookInfo | RemoteSearchResult[] |  |  |
| `POST /Items/RemoteSearch/BoxSet` | P2 |  | Providers.RemoteSearchQuery_Providers.BoxSetInfo | RemoteSearchResult[] |  |  |
| `POST /Items/RemoteSearch/Game` | P2 |  | Providers.RemoteSearchQuery_Providers.GameInfo | RemoteSearchResult[] |  |  |
| `POST /Items/RemoteSearch/Movie` | P2 |  | Providers.RemoteSearchQuery_Providers.MovieInfo | RemoteSearchResult[] |  |  |
| `POST /Items/RemoteSearch/MusicAlbum` | P2 |  | Providers.RemoteSearchQuery_Providers.AlbumInfo | RemoteSearchResult[] |  |  |
| `POST /Items/RemoteSearch/MusicArtist` | P2 |  | Providers.RemoteSearchQuery_Providers.ArtistInfo | RemoteSearchResult[] |  |  |
| `POST /Items/RemoteSearch/MusicVideo` | P2 |  | Providers.RemoteSearchQuery_Providers.MusicVideoInfo | RemoteSearchResult[] |  |  |
| `POST /Items/RemoteSearch/Person` | P2 |  | Providers.RemoteSearchQuery_Providers.PersonLookupInfo | RemoteSearchResult[] |  |  |
| `POST /Items/RemoteSearch/Series` | P2 |  | Providers.RemoteSearchQuery_Providers.SeriesInfo | RemoteSearchResult[] |  |  |
| `POST /Items/RemoteSearch/Trailer` | P2 |  | Providers.RemoteSearchQuery_Providers.TrailerInfo | RemoteSearchResult[] |  |  |

### 域：Libraries（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Libraries/AvailableOptions` | P1 |  |  | Library.LibraryOptionsResult |  |  |

### 域：Library（17 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Library/VirtualFolders` | P2 |  |  |  |  |  |
| `DELETE /Library/VirtualFolders/Paths` | P2 |  |  |  |  |  |
| `GET /Library/MediaFolders` | P1 | IsHidden(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Library/PhysicalPaths` | P1 |  |  | array |  |  |
| `GET /Library/SelectableMediaFolders` | P1 |  |  | Library.MediaFolder[] |  |  |
| `GET /Library/VirtualFolders` | P1 |  |  | VirtualFolderInfo[] |  |  |
| `POST /Library/Media/Updated` | P2 |  | Library.PostUpdatedMedia |  |  |  |
| `POST /Library/Movies/Added` | P2 |  |  |  |  |  |
| `POST /Library/Movies/Updated` | P2 |  |  |  |  |  |
| `POST /Library/Refresh` | P2 |  |  |  |  |  |
| `POST /Library/Series/Added` | P2 |  |  |  |  |  |
| `POST /Library/Series/Updated` | P2 |  |  |  |  |  |
| `POST /Library/VirtualFolders` | P2 |  | Library.AddVirtualFolder |  |  |  |
| `POST /Library/VirtualFolders/LibraryOptions` | P2 |  | Library.UpdateLibraryOptions |  |  |  |
| `POST /Library/VirtualFolders/Name` | P2 |  | Library.RenameVirtualFolder |  |  |  |
| `POST /Library/VirtualFolders/Paths` | P2 |  | Library.AddMediaPath |  |  |  |
| `POST /Library/VirtualFolders/Paths/Update` | P2 |  | Library.UpdateMediaPath |  |  |  |

### 域：LiveStreams（3 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `POST /LiveStreams/Close` | P2 | LiveStreamId(query)* |  |  |  |  |
| `POST /LiveStreams/MediaInfo` | P2 | LiveStreamId(query)* |  |  |  |  |
| `POST /LiveStreams/Open` | P2 |  | MediaInfo.LiveStreamRequest | MediaInfo.LiveStreamResponse |  |  |

### 域：LiveTv（51 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /LiveTv/ChannelMappingOptions` | P2 | ProviderId(query)* |  |  |  |  |
| `DELETE /LiveTv/ChannelMappings` | P2 | ProviderId(query)* |  |  |  |  |
| `DELETE /LiveTv/ListingProviders` | P2 | Id(query) |  |  |  |  |
| `DELETE /LiveTv/Recordings/{Id}` | P2 | Id(path)* |  |  |  |  |
| `DELETE /LiveTv/SeriesTimers/{Id}` | P2 | Id(path)* |  |  |  |  |
| `DELETE /LiveTv/Timers/{Id}` | P2 | Id(path)* |  |  |  |  |
| `DELETE /LiveTv/TunerHosts` | P2 | Id(query) |  |  |  |  |
| `GET /LiveTv/ChannelMappingOptions` | P1 | ProviderId(query)* |  |  |  |  |
| `GET /LiveTv/ChannelMappings` | P1 | ProviderId(query)* |  |  |  |  |
| `GET /LiveTv/Channels` | P1 | Type(query),UserId(query),StartIndex(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),Limit(query),IsFavorite(query),IsLiked(query),IsDisliked(query),EnableFavoriteSorting(query),EnableImages(query),ImageTypeLimit(query),EnableImageTypes(query),Fields(query),AddCurrentProgram(query),EnableUserData(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /LiveTv/Channels/{Id}` | P1 | Id(path)*,UserId(query) |  | BaseItemDto |  |  |
| `GET /LiveTv/GuideInfo` | P1 |  |  | LiveTv.GuideInfo |  |  |
| `GET /LiveTv/Info` | P1 |  |  | LiveTv.LiveTvInfo |  |  |
| `GET /LiveTv/ListingProviders` | P1 |  |  | LiveTv.ListingsProviderInfo[] |  |  |
| `GET /LiveTv/ListingProviders/Available` | P1 |  |  | LiveTv.ListingProviderTypeInfo[] |  |  |
| `GET /LiveTv/ListingProviders/Default` | P1 |  |  | LiveTv.ListingsProviderInfo |  |  |
| `GET /LiveTv/ListingProviders/Lineups` | P1 | Id(query),Type(query),Location(query),Country(query) |  | NameIdPair[] | 是 |  |
| `GET /LiveTv/ListingProviders/SchedulesDirect/Countries` | P1 |  |  |  |  |  |
| `GET /LiveTv/LiveRecordings/{Id}/stream` | P1 | Id(path)* |  |  | 是 |  |
| `GET /LiveTv/LiveStreamFiles/{Id}/stream.{Container}` | P1 | Id(path)*,Container(path)* |  |  | 是 |  |
| `GET /LiveTv/Programs` | P1 | ChannelIds(query),UserId(query),MinStartDate(query),HasAired(query),MaxStartDate(query),MinEndDate(query),MaxEndDate(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),StartIndex(query),Limit(query),SortBy(query),SortOrder(query),GenreIds(query),EnableImages(query),ImageTypeLimit(query),EnableImageTypes(query),EnableUserData(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /LiveTv/Programs/{Id}` | P1 | Id(path)* |  | BaseItemDto |  |  |
| `GET /LiveTv/Programs/Recommended` | P1 | UserId(query),Limit(query),IsAiring(query),HasAired(query),IsSeries(query),IsMovie(query),IsNews(query),IsKids(query),IsSports(query),EnableImages(query),ImageTypeLimit(query),EnableImageTypes(query),GenreIds(query),Fields(query),EnableUserData(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /LiveTv/Recordings` | P1 | ChannelId(query),Status(query),IsInProgress(query),SeriesTimerId(query),ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  |  |  |  |
| `GET /LiveTv/Recordings/{Id}` | P1 | Id(path)*,UserId(query) |  | BaseItemDto |  |  |
| `GET /LiveTv/Recordings/Folders` | P1 | UserId(query) |  | BaseItemDto[] |  |  |
| `GET /LiveTv/Recordings/Groups` | P1 | UserId(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /LiveTv/Recordings/Groups/{Id}` | P1 | Id(path)* |  | BaseItemDto | 是 |  |
| `GET /LiveTv/Recordings/Series` | P1 | ChannelId(query),UserId(query),GroupId(query),StartIndex(query),Limit(query),Status(query),IsInProgress(query),SeriesTimerId(query),EnableImages(query),ImageTypeLimit(query),EnableImageTypes(query),Fields(query),EnableUserData(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /LiveTv/SeriesTimers` | P1 | SortBy(query),SortOrder(query) |  | QueryResult_LiveTv.SeriesTimerInfoDto |  |  |
| `GET /LiveTv/SeriesTimers/{Id}` | P1 | Id(path)* |  | LiveTv.TimerInfoDto | 是 |  |
| `GET /LiveTv/Timers` | P1 | ChannelId(query),SeriesTimerId(query) |  | QueryResult_LiveTv.TimerInfoDto |  |  |
| `GET /LiveTv/Timers/{Id}` | P1 | Id(path)* |  | LiveTv.TimerInfoDto | 是 |  |
| `GET /LiveTv/Timers/Defaults` | P1 | ProgramId(query) |  | LiveTv.SeriesTimerInfoDto |  |  |
| `GET /LiveTv/TunerHosts` | P1 |  |  | LiveTv.TunerHostInfo[] |  |  |
| `GET /LiveTv/TunerHosts/Types` | P1 |  |  | NameIdPair[] |  |  |
| `GET /LiveTv/Tuners/Discvover` | P1 |  |  | LiveTv.TunerHostInfo[] |  |  |
| `OPTIONS /LiveTv/ChannelMappingOptions` | P1 | ProviderId(query)* |  |  |  |  |
| `OPTIONS /LiveTv/ChannelMappings` | P1 | ProviderId(query)* |  |  |  |  |
| `POST /LiveTv/ChannelMappingOptions` | P2 | ProviderId(query)* |  |  |  |  |
| `POST /LiveTv/ChannelMappings` | P2 | ProviderId(query)* | LiveTv.SetChannelMapping |  |  |  |
| `POST /LiveTv/ListingProviders` | P2 |  | LiveTv.ListingsProviderInfo | LiveTv.ListingsProviderInfo |  |  |
| `POST /LiveTv/Programs` | P1 | ChannelIds(query),UserId(query),MinStartDate(query),MaxStartDate(query),MinEndDate(query),MaxEndDate(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),GenreIds(query) | LiveTv.GetPrograms | QueryResult_BaseItemDto |  |  |
| `POST /LiveTv/SeriesTimers` | P2 |  | LiveTv.SeriesTimerInfoDto |  |  |  |
| `POST /LiveTv/SeriesTimers/{Id}` | P2 | Id(path)* | LiveTv.SeriesTimerInfoDto |  |  |  |
| `POST /LiveTv/Timers` | P2 |  | LiveTv.TimerInfoDto |  |  |  |
| `POST /LiveTv/Timers/{Id}` | P2 | Id(path)* | LiveTv.TimerInfoDto |  |  |  |
| `POST /LiveTv/TunerHosts` | P2 |  | LiveTv.TunerHostInfo | LiveTv.TunerHostInfo |  |  |
| `POST /LiveTv/Tuners/{Id}/Reset` | P2 | Id(path)* |  |  |  |  |
| `PUT /LiveTv/ChannelMappingOptions` | P2 | ProviderId(query)* |  |  |  |  |
| `PUT /LiveTv/ChannelMappings` | P2 | ProviderId(query)* | LiveTv.SetChannelMapping |  |  |  |

### 域：Localization（4 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Localization/Countries` | P1 |  |  | Globalization.CountryInfo[] |  |  |
| `GET /Localization/Cultures` | P1 |  |  | Globalization.CultureDto[] |  |  |
| `GET /Localization/Options` | P1 |  |  | Globalization.LocalizatonOption[] |  |  |
| `GET /Localization/ParentalRatings` | P1 |  |  | ParentalRating[] |  |  |

### 域：Movies（2 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Movies/{Id}/Similar` | P1 | Id(path)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Movies/Recommendations` | P1 | CategoryLimit(query),ItemLimit(query),UserId(query),ParentId(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query) |  | RecommendationDto[] |  |  |

### 域：MusicGenres（6 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /MusicGenres` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /MusicGenres/{Name}` | P1 | Name(path)*,UserId(query) |  | BaseItemDto |  |  |
| `GET /MusicGenres/{Name}/Images/{Type}` | P1 | Name(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(query) |  |  |  |  |
| `GET /MusicGenres/{Name}/Images/{Type}/{Index}` | P1 | Name(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(path)* |  |  |  |  |
| `GET /MusicGenres/{Name}/InstantMix` | P1 | Name(path)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /MusicGenres/InstantMix` | P1 | Id(query)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |

### 域：News（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /News/Product` | P1 | StartIndex(query),Limit(query) |  | QueryResult_News.NewsItem |  |  |

### 域：Notification（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `POST /Notification/SMTP/Test/{UserID}` | P2 | UserID(path)* |  |  | 是 |  |

### 域：Notifications（7 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Notifications/{UserId}` | P1 | UserId(path)*,IsRead(query),StartIndex(query),Limit(query) |  | Emby.Notifications.Api.NotificationResult |  |  |
| `GET /Notifications/{UserId}/Summary` | P1 | UserId(path)* |  | Emby.Notifications.Api.NotificationsSummary |  |  |
| `GET /Notifications/Services` | P1 |  |  | NameIdPair[] |  |  |
| `GET /Notifications/Types` | P1 |  |  | Notifications.NotificationTypeInfo[] |  |  |
| `POST /Notifications/{UserId}/Read` | P2 | UserId(path)*,Ids(query)* |  |  |  |  |
| `POST /Notifications/{UserId}/Unread` | P2 | UserId(path)*,Ids(query)* |  |  |  |  |
| `POST /Notifications/Admin` | P2 | Name(query)*,Description(query)*,ImageUrl(query),Url(query),Level(query) |  |  |  |  |

### 域：OfficialRatings（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /OfficialRatings` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_UserLibrary.OfficialRatingItem |  |  |

### 域：openapi（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /openapi` | P1 |  |  | string | 是 |  |

### 域：openapi.json（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /openapi.json` | P1 |  |  | string | 是 |  |

### 域：Packages（5 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Packages/Installing/{Id}` | P2 | Id(path)* |  |  |  |  |
| `GET /Packages` | P1 | PackageType(query),TargetSystems(query),IsPremium(query),IsAdult(query) |  | Updates.PackageInfo[] |  |  |
| `GET /Packages/{Name}` | P1 | Name(path)*,AssemblyGuid(query) |  | Updates.PackageInfo | 是 |  |
| `GET /Packages/Updates` | P1 | PackageType(query)* |  | Updates.PackageVersionInfo[] |  |  |
| `POST /Packages/Installed/{Name}` | P2 | Name(path)*,AssemblyGuid(query),Version(query),UpdateClass(query) |  |  |  |  |

### 域：Persons（4 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Persons` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Persons/{Name}` | P1 | Name(path)*,UserId(query) |  | BaseItemDto |  |  |
| `GET /Persons/{Name}/Images/{Type}` | P1 | Name(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(query) |  |  |  |  |
| `GET /Persons/{Name}/Images/{Type}/{Index}` | P1 | Name(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(path)* |  |  |  |  |

### 域：Playback（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Playback/BitrateTest` | P1 | Size(query)* |  |  |  | 是 |

### 域：Playlists（6 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Playlists/{Id}/Items` | P2 | Id(path)*,EntryIds(query)* |  |  |  |  |
| `GET /Playlists/{Id}/InstantMix` | P1 | Id(path)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Playlists/{Id}/Items` | P1 | Id(path)*,UserId(query),StartIndex(query),Limit(query),Fields(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query) |  | QueryResult_BaseItemDto |  |  |
| `POST /Playlists` | P2 | Name(query),Ids(query),MediaType(query) |  | Playlists.PlaylistCreationResult |  |  |
| `POST /Playlists/{Id}/Items` | P2 | Ids(query)*,Id(path)*,UserId(query) |  |  |  |  |
| `POST /Playlists/{Id}/Items/{ItemId}/Move/{NewIndex}` | P2 | ItemId(path)*,Id(path)*,NewIndex(path)* |  |  |  |  |

### 域：Plugins（5 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Plugins/{Id}` | P2 | Id(path)* |  |  |  |  |
| `GET /Plugins` | P1 |  |  | Plugins.PluginInfo[] |  |  |
| `GET /Plugins/{Id}/Configuration` | P1 | Id(path)* |  |  | 是 |  |
| `GET /Plugins/{Id}/Thumb` | P1 | Id(path)* |  |  | 是 |  |
| `POST /Plugins/{Id}/Configuration` | P2 | Id(path)* |  |  |  |  |

### 域：Providers（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Providers/Subtitles/Subtitles/{Id}` | P1 | Id(path)* |  |  | 是 |  |

### 域：Reports（4 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Reports/Activities` | P1 | ReportView(query)*,DisplayType(query)*,HasQueryLimit(query),GroupBy(query),ReportColumns(query),StartIndex(query),Limit(query),IncludeItemTypes(query) |  | EmbyReports.Api.Model.ReportResult | 是 |  |
| `GET /Reports/Headers` | P1 | ReportView(query)*,DisplayType(query)*,IncludeItemTypes(query),ReportColumns(query) |  | EmbyReports.Api.Model.ReportHeader[] | 是 |  |
| `GET /Reports/Items` | P1 | MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),CollapseBoxSetItems(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query),ReportView(query)*,DisplayType(query)*,HasQueryLimit(query),GroupBy(query),ReportColumns(query) |  | EmbyReports.Api.Model.ReportResult | 是 |  |
| `GET /Reports/Items/Download` | P1 | MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),CollapseBoxSetItems(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query),ReportView(query)*,DisplayType(query)*,HasQueryLimit(query),GroupBy(query),ReportColumns(query) |  |  | 是 |  |

### 域：ScheduledTasks（5 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /ScheduledTasks/Running/{Id}` | P2 | Id(path)* |  |  |  |  |
| `GET /ScheduledTasks` | P1 | IsHidden(query),IsEnabled(query) |  | Tasks.TaskInfo[] |  |  |
| `GET /ScheduledTasks/{Id}` | P1 | Id(path)* |  | Tasks.TaskInfo |  |  |
| `POST /ScheduledTasks/{Id}/Triggers` | P2 | Id(path)* |  |  |  |  |
| `POST /ScheduledTasks/Running/{Id}` | P2 | Id(path)* |  |  |  |  |

### 域：Search（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Search/Hints` | P1 | StartIndex(query),Limit(query),UserId(query),SearchTerm(query)*,IncludePeople(query),IncludeMedia(query),IncludeGenres(query),IncludeStudios(query),IncludeArtists(query),IncludeItemTypes(query),ExcludeItemTypes(query),MediaTypes(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query) |  | Search.SearchHintResult |  |  |

### 域：Sessions（17 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Sessions/{Id}/Users/{UserId}` | P2 | Id(path)*,UserId(path)* |  |  |  |  |
| `GET /Sessions` | P0 | ControllableByUserId(query),DeviceId(query) |  | Session.SessionInfo[] |  |  |
| `POST /Sessions/{Id}/Command` | P2 | Id(path)* | GeneralCommand |  |  |  |
| `POST /Sessions/{Id}/Command/{Command}` | P2 | Id(path)*,Command(path)* |  |  |  |  |
| `POST /Sessions/{Id}/Message` | P2 | Id(path)*,Text(query)*,Header(query)*,TimeoutMs(query) |  |  |  |  |
| `POST /Sessions/{Id}/Playing` | P2 | Id(path)*,ItemIds(query)*,StartPositionTicks(query),PlayCommand(query)* | PlayRequest |  |  |  |
| `POST /Sessions/{Id}/Playing/{Command}` | P2 | Id(path)*,Command(path)* | PlaystateRequest |  |  |  |
| `POST /Sessions/{Id}/System/{Command}` | P2 | Id(path)*,Command(path)* |  |  |  |  |
| `POST /Sessions/{Id}/Users/{UserId}` | P2 | Id(path)*,UserId(path)* |  |  |  |  |
| `POST /Sessions/{Id}/Viewing` | P2 | Id(path)*,ItemType(query)*,ItemId(query)*,ItemName(query)* |  |  |  |  |
| `POST /Sessions/Capabilities` | P2 | Id(query)*,PlayableMediaTypes(query),SupportedCommands(query),SupportsMediaControl(query),SupportsSync(query),SupportsPersistentIdentifier(query) |  |  |  |  |
| `POST /Sessions/Capabilities/Full` | P2 | Id(query)* | ClientCapabilities |  |  |  |
| `POST /Sessions/Logout` | P2 |  |  |  |  |  |
| `POST /Sessions/Playing` | P2 |  | PlaybackStartInfo |  |  |  |
| `POST /Sessions/Playing/Ping` | P2 | PlaySessionId(query) |  |  |  |  |
| `POST /Sessions/Playing/Progress` | P2 |  | PlaybackProgressInfo |  |  |  |
| `POST /Sessions/Playing/Stopped` | P2 |  | PlaybackStopInfo |  |  |  |

### 域：Shows（5 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Shows/{Id}/Episodes` | P1 | UserId(query)*,Fields(query),Id(path)*,Season(query),SeasonId(query),IsMissing(query),AdjacentTo(query),StartItemId(query),StartIndex(query),Limit(query),EnableImages(query),ImageTypeLimit(query),EnableImageTypes(query),EnableUserData(query),SortBy(query),SortOrder(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Shows/{Id}/Seasons` | P1 | UserId(query)*,Fields(query),Id(path)*,IsSpecialSeason(query),IsMissing(query),AdjacentTo(query),EnableImages(query),ImageTypeLimit(query),EnableImageTypes(query),EnableUserData(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Shows/{Id}/Similar` | P1 | Id(path)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Shows/NextUp` | P1 | UserId(query)*,StartIndex(query),Limit(query),Fields(query),SeriesId(query),ParentId(query),EnableImages(query),ImageTypeLimit(query),EnableImageTypes(query),EnableUserData(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Shows/Upcoming` | P1 | UserId(query)*,StartIndex(query),Limit(query),Fields(query),ParentId(query),EnableImages(query),ImageTypeLimit(query),EnableImageTypes(query),EnableUserData(query) |  | QueryResult_BaseItemDto |  |  |

### 域：Songs（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Songs/{Id}/InstantMix` | P1 | Id(path)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |

### 域：Studios（4 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Studios` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Studios/{Name}` | P1 | Name(path)*,UserId(query) |  | BaseItemDto |  |  |
| `GET /Studios/{Name}/Images/{Type}` | P1 | Name(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(query) |  |  |  |  |
| `GET /Studios/{Name}/Images/{Type}/{Index}` | P1 | Name(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(path)* |  |  |  |  |

### 域：SubtitleCodecs（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /SubtitleCodecs` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_UserLibrary.TagItem |  |  |

### 域：swagger（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /swagger` | P1 |  |  | string | 是 |  |

### 域：swagger.json（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /swagger.json` | P1 |  |  | string | 是 |  |

### 域：Sync（21 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Sync/{TargetId}/Items` | P2 | TargetId(path)* |  |  | 是 |  |
| `DELETE /Sync/JobItems/{Id}` | P2 | Id(path)* |  |  | 是 |  |
| `DELETE /Sync/Jobs/{Id}` | P2 | Id(path)* |  |  | 是 |  |
| `GET /Sync/Items/Ready` | P1 | TargetId(query)* |  | Sync.Model.SyncedItem[] | 是 |  |
| `GET /Sync/JobItems` | P1 |  |  | QueryResult_Sync.Model.SyncJobItem | 是 |  |
| `GET /Sync/JobItems/{Id}/AdditionalFiles` | P1 | Id(path)*,Name(query)* |  |  | 是 |  |
| `GET /Sync/JobItems/{Id}/File` | P1 | Id(path)* |  |  | 是 |  |
| `GET /Sync/Jobs` | P1 |  |  | QueryResult_Sync.SyncJob | 是 |  |
| `GET /Sync/Jobs/{Id}` | P1 | Id(path)* |  | Sync.SyncJob | 是 |  |
| `GET /Sync/Options` | P1 | UserId(query)*,ItemIds(query),ParentId(query),TargetId(query),Category(query) |  | Sync.Model.SyncDialogOptions | 是 |  |
| `GET /Sync/Targets` | P1 | UserId(query)* |  | Sync.SyncTarget[] | 是 |  |
| `POST /Sync/{ItemId}/Status` | P2 | ItemId(path)* | Sync.Model.SyncedItemProgress |  | 是 |  |
| `POST /Sync/Data` | P2 |  | Sync.Model.SyncDataRequest | Sync.Model.SyncDataResponse | 是 |  |
| `POST /Sync/Items/Cancel` | P2 | ItemIds(query) |  |  | 是 |  |
| `POST /Sync/JobItems/{Id}/Enable` | P2 | Id(path)* |  |  | 是 |  |
| `POST /Sync/JobItems/{Id}/MarkForRemoval` | P2 | Id(path)* |  |  | 是 |  |
| `POST /Sync/JobItems/{Id}/Transferred` | P2 | Id(path)* |  |  | 是 |  |
| `POST /Sync/JobItems/{Id}/UnmarkForRemoval` | P2 | Id(path)* |  |  | 是 |  |
| `POST /Sync/Jobs` | P2 |  | Sync.Model.SyncJobRequest | Sync.Model.SyncJobCreationResult | 是 |  |
| `POST /Sync/Jobs/{Id}` | P2 | Id(path)* | Sync.SyncJob |  | 是 |  |
| `POST /Sync/OfflineActions` | P2 |  |  |  | 是 |  |

### 域：System（15 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /System/ActivityLog/Entries` | P1 | StartIndex(query),Limit(query),MinDate(query) |  | QueryResult_ActivityLogEntry |  |  |
| `GET /System/Configuration` | P1 |  |  | Configuration.ServerConfiguration |  |  |
| `GET /System/Configuration/{Key}` | P1 | Key(path)* |  |  | 是 |  |
| `GET /System/Endpoint` | P1 |  |  | Net.EndPointInfo |  |  |
| `GET /System/Info` | P1 |  |  | SystemInfo |  |  |
| `GET /System/Info/Public` | P0 |  |  | PublicSystemInfo |  |  |
| `GET /System/Logs` | P1 |  |  | LogFile[] |  |  |
| `GET /System/Logs/Log` | P1 | Name(query)* |  |  |  |  |
| `GET /System/Ping` | P0 |  |  |  |  |  |
| `GET /System/WakeOnLanInfo` | P1 |  |  | WakeOnLanInfo[] |  |  |
| `POST /System/Configuration` | P2 |  | Configuration.ServerConfiguration |  |  |  |
| `POST /System/Configuration/{Key}` | P2 | Key(path)* |  |  |  |  |
| `POST /System/Ping` | P2 |  |  |  |  |  |
| `POST /System/Restart` | P2 |  |  |  |  |  |
| `POST /System/Shutdown` | P2 |  |  |  |  |  |

### 域：Tags（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Tags` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_UserLibrary.TagItem |  |  |

### 域：Trailers（2 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Trailers` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Trailers/{Id}/Similar` | P1 | Id(path)*,IncludeItemTypes(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),UserId(query),Limit(query),Fields(query) |  | QueryResult_BaseItemDto |  |  |

### 域：user_usage_stats（19 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /user_usage_stats/{BreakdownType}/BreakdownReport` | P1 | days(query),end_date(query),BreakdownType(path)* |  | object | 是 |  |
| `GET /user_usage_stats/{UserID}/{Date}/GetItems` | P1 | UserID(path)*,Date(path)*,Filter(query) |  | object | 是 |  |
| `GET /user_usage_stats/DurationHistogramReport` | P1 | days(query),end_date(query),filter(query) |  | object | 是 |  |
| `GET /user_usage_stats/HourlyReport` | P1 | days(query),end_date(query),filter(query) |  | object | 是 |  |
| `GET /user_usage_stats/load_backup` | P1 | backupfile(query)* |  | object | 是 |  |
| `GET /user_usage_stats/MoviesReport` | P1 | days(query),end_date(query) |  | object | 是 |  |
| `GET /user_usage_stats/PlayActivity` | P1 | days(query),end_date(query),filter(query),data_type(query) |  | object | 是 |  |
| `GET /user_usage_stats/process_list` | P1 |  |  | object | 是 |  |
| `GET /user_usage_stats/resource_usage` | P1 | hours(query) |  | object | 是 |  |
| `GET /user_usage_stats/save_backup` | P1 |  |  | object | 是 |  |
| `GET /user_usage_stats/session_list` | P1 |  |  | object | 是 |  |
| `GET /user_usage_stats/TvShowsReport` | P1 | days(query),end_date(query) |  | object | 是 |  |
| `GET /user_usage_stats/type_filter_list` | P1 |  |  | object | 是 |  |
| `GET /user_usage_stats/user_activity` | P1 | days(query),end_date(query) |  | object | 是 |  |
| `GET /user_usage_stats/user_list` | P1 |  |  | object | 是 |  |
| `GET /user_usage_stats/user_manage/{Action}/{Id}` | P1 | Action(path)*,Id(path)* |  | object | 是 |  |
| `GET /user_usage_stats/UserPlaylist` | P1 | user_id(query)*,days(query),end_date(query),filter(query) |  | object | 是 |  |
| `POST /user_usage_stats/import_backup` | P2 |  |  |  | 是 |  |
| `POST /user_usage_stats/submit_custom_query` | P2 |  | playback_reporting.Api.CustomQuery | object | 是 |  |

### 域：Users（42 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Users/{Id}` | P2 | Id(path)* |  |  |  |  |
| `DELETE /Users/{Id}/Connect/Link` | P2 | Id(path)* |  |  | 是 |  |
| `DELETE /Users/{Id}/Images/{Type}` | P2 | Id(path)*,Type(path)*,Index(query) |  |  |  |  |
| `DELETE /Users/{Id}/Images/{Type}/{Index}` | P2 | Id(path)*,Type(path)*,Index(path)* |  |  |  |  |
| `DELETE /Users/{UserId}/FavoriteItems/{Id}` | P2 | UserId(path)*,Id(path)* |  | UserItemDataDto |  |  |
| `DELETE /Users/{UserId}/Items/{Id}/Rating` | P2 | UserId(path)*,Id(path)* |  | UserItemDataDto |  |  |
| `DELETE /Users/{UserId}/PlayedItems/{Id}` | P2 | UserId(path)*,Id(path)* |  | UserItemDataDto |  |  |
| `DELETE /Users/{UserId}/PlayingItems/{Id}` | P2 | UserId(path)*,Id(path)*,MediaSourceId(query)*,NextMediaType(query)*,PositionTicks(query),LiveStreamId(query),PlaySessionId(query) |  |  |  |  |
| `GET /Users` | P0 | IsHidden(query),IsDisabled(query) |  | UserDto[] |  |  |
| `GET /Users/{Id}` | P1 | Id(path)* |  | UserDto |  |  |
| `GET /Users/{Id}/Images/{Type}` | P1 | Id(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(query) |  |  |  |  |
| `GET /Users/{Id}/Images/{Type}/{Index}` | P1 | Id(path)*,MaxWidth(query),MaxHeight(query),Width(query),Height(query),Quality(query),Tag(query),CropWhitespace(query),EnableImageEnhancers(query),Format(query),AddPlayedIndicator(query),PercentPlayed(query),UnplayedCount(query),BackgroundColor(query),ForegroundLayer(query),Type(path)*,Index(path)* |  |  |  |  |
| `GET /Users/{UserId}/GroupingOptions` | P1 | UserId(path)* |  | BaseItemDto[] |  |  |
| `GET /Users/{UserId}/Items` | P0 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(path)*,MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Users/{UserId}/Items/{Id}` | P1 | UserId(path)*,Id(path)* |  | BaseItemDto |  |  |
| `GET /Users/{UserId}/Items/{Id}/Intros` | P1 | UserId(path)*,Id(path)* |  | QueryResult_BaseItemDto |  |  |
| `GET /Users/{UserId}/Items/{Id}/LocalTrailers` | P1 | UserId(path)*,Id(path)* |  | BaseItemDto[] |  |  |
| `GET /Users/{UserId}/Items/{Id}/SpecialFeatures` | P1 | UserId(path)*,Id(path)* |  | BaseItemDto[] |  |  |
| `GET /Users/{UserId}/Items/Latest` | P1 | UserId(path)*,Limit(query),ParentId(query),Fields(query),IncludeItemTypes(query),IsFolder(query),IsPlayed(query),GroupItems(query),EnableImages(query),ImageTypeLimit(query),EnableImageTypes(query),EnableUserData(query) |  | BaseItemDto[] |  |  |
| `GET /Users/{UserId}/Items/Resume` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(path)*,MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_BaseItemDto |  |  |
| `GET /Users/{UserId}/Items/Root` | P1 | UserId(path)* |  | BaseItemDto |  |  |
| `GET /Users/{UserId}/Suggestions` | P1 | UserId(path)* |  | QueryResult_BaseItemDto |  |  |
| `GET /Users/{UserId}/Views` | P0 | UserId(path)*,IncludeExternalContent(query)* |  | QueryResult_BaseItemDto |  | 是 |
| `GET /Users/Public` | P0 |  |  | UserDto[] |  |  |
| `POST /Users/{Id}` | P2 | Id(path)* | UserDto |  |  |  |
| `POST /Users/{Id}/Authenticate` | P2 | Id(path)* | AuthenticateUser | Authentication.AuthenticationResult |  |  |
| `POST /Users/{Id}/Configuration` | P2 | Id(path)* | Configuration.UserConfiguration |  |  |  |
| `POST /Users/{Id}/Connect/Link` | P2 | Id(path)*,ConnectUsername(query)* |  | Connect.UserLinkResult | 是 |  |
| `POST /Users/{Id}/EasyPassword` | P2 | Id(path)* | UpdateUserEasyPassword |  |  |  |
| `POST /Users/{Id}/Images/{Type}` | P2 | Id(path)*,Type(path)*,Index(query) |  |  |  |  |
| `POST /Users/{Id}/Images/{Type}/{Index}` | P2 | Id(path)*,Type(path)*,Index(path)* |  |  |  |  |
| `POST /Users/{Id}/Password` | P2 | Id(path)* | UpdateUserPassword |  |  |  |
| `POST /Users/{Id}/Policy` | P2 | Id(path)* | Users.UserPolicy |  |  |  |
| `POST /Users/{UserId}/FavoriteItems/{Id}` | P2 | UserId(path)*,Id(path)* |  | UserItemDataDto |  |  |
| `POST /Users/{UserId}/Items/{Id}/Rating` | P2 | UserId(path)*,Id(path)*,Likes(query)* |  | UserItemDataDto |  |  |
| `POST /Users/{UserId}/PlayedItems/{Id}` | P2 | UserId(path)*,DatePlayed(query),Id(path)* |  | UserItemDataDto |  |  |
| `POST /Users/{UserId}/PlayingItems/{Id}` | P2 | UserId(path)*,Id(path)*,MediaSourceId(query)*,CanSeek(query),AudioStreamIndex(query),SubtitleStreamIndex(query),PlayMethod(query),LiveStreamId(query),PlaySessionId(query) |  |  |  |  |
| `POST /Users/{UserId}/PlayingItems/{Id}/Progress` | P2 | UserId(path)*,Id(path)*,MediaSourceId(query)*,PositionTicks(query),IsPaused(query),IsMuted(query),AudioStreamIndex(query),SubtitleStreamIndex(query),VolumeLevel(query),PlayMethod(query),LiveStreamId(query),PlaySessionId(query),RepeatMode(query) |  |  |  |  |
| `POST /Users/AuthenticateByName` | P1 | X-Emby-Authorization(header)* | AuthenticateUserByName | Authentication.AuthenticationResult |  | 是 |
| `POST /Users/ForgotPassword` | P2 |  | ForgotPassword | Users.ForgotPasswordResult |  |  |
| `POST /Users/ForgotPassword/Pin` | P2 |  | ForgotPasswordPin | Users.PinRedeemResult |  |  |
| `POST /Users/New` | P2 |  | CreateUserByName | UserDto |  |  |

### 域：VideoCodecs（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /VideoCodecs` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_UserLibrary.TagItem |  |  |

### 域：Videos（16 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `DELETE /Videos/{Id}/AlternateSources` | P2 | Id(path)* |  |  |  |  |
| `DELETE /Videos/{Id}/Subtitles/{Index}` | P2 | Id(path)*,Index(path)* |  |  |  |  |
| `DELETE /Videos/ActiveEncodings` | P2 | DeviceId(query)*,PlaySessionId(query)* |  |  |  |  |
| `GET /Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/{StartPositionTicks}/Stream.{Format}` | P1 | Id(path)*,MediaSourceId(path)*,Index(path)*,Format(path)*,StartPositionTicks(path)*,EndPositionTicks(query),CopyTimestamps(query) |  |  |  | 是 |
| `GET /Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/Stream.{Format}` | P1 | Id(path)*,MediaSourceId(path)*,Index(path)*,Format(path)*,StartPositionTicks(query),EndPositionTicks(query),CopyTimestamps(query) |  |  | 是 |  |
| `GET /Videos/{Id}/AdditionalParts` | P1 | UserId(query),Id(path)* |  | QueryResult_BaseItemDto |  |  |
| `GET /Videos/{Id}/hls/{PlaylistId}/{SegmentId}.{SegmentContainer}` | P1 | PlaylistId(path)*,SegmentId(path)*,DeviceProfileId(query),SegmentContainer(path)*,Id(path)*,DeviceId(query),Container(query)*,AudioCodec(query),EnableAutoStreamCopy(query),AudioSampleRate(query),AudioBitRate(query),AudioChannels(query),MaxAudioChannels(query),Static(query),Profile(query),Level(query),Framerate(query),MaxFramerate(query),CopyTimestamps(query),StartTimeTicks(query),Width(query),Height(query),MaxWidth(query),MaxHeight(query),VideoBitRate(query),SubtitleStreamIndex(query),SubtitleMethod(query),MaxRefFrames(query),MaxVideoBitDepth(query),VideoCodec(query),AudioStreamIndex(query),VideoStreamIndex(query) |  |  | 是 |  |
| `GET /Videos/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}` | P1 | PlaylistId(path)*,SegmentId(path)*,DeviceProfileId(query),SegmentContainer(path)*,Id(path)*,DeviceId(query),Container(query)*,AudioCodec(query),EnableAutoStreamCopy(query),AudioSampleRate(query),AudioBitRate(query),AudioChannels(query),MaxAudioChannels(query),Static(query),Profile(query),Level(query),Framerate(query),MaxFramerate(query),CopyTimestamps(query),StartTimeTicks(query),Width(query),Height(query),MaxWidth(query),MaxHeight(query),VideoBitRate(query),SubtitleStreamIndex(query),SubtitleMethod(query),MaxRefFrames(query),MaxVideoBitDepth(query),VideoCodec(query),AudioStreamIndex(query),VideoStreamIndex(query) |  |  |  | 是 |
| `GET /Videos/{Id}/index.bif` | P1 | Width(query)*,Id(path)* |  |  | 是 | 是 |
| `GET /Videos/{Id}/live.m3u8` | P1 | DeviceProfileId(query),Id(path)*,DeviceId(query),Container(query)*,AudioCodec(query),EnableAutoStreamCopy(query),AudioSampleRate(query),AudioBitRate(query),AudioChannels(query),MaxAudioChannels(query),Static(query),Profile(query),Level(query),Framerate(query),MaxFramerate(query),CopyTimestamps(query),StartTimeTicks(query),Width(query),Height(query),MaxWidth(query),MaxHeight(query),VideoBitRate(query),SubtitleStreamIndex(query),SubtitleMethod(query),MaxRefFrames(query),MaxVideoBitDepth(query),VideoCodec(query),AudioStreamIndex(query),VideoStreamIndex(query) |  |  |  |  |
| `GET /Videos/{Id}/main.m3u8` | P1 | DeviceProfileId(query),Id(path)*,DeviceId(query),Container(query)*,AudioCodec(query),EnableAutoStreamCopy(query),AudioSampleRate(query),AudioBitRate(query),AudioChannels(query),MaxAudioChannels(query),Static(query),Profile(query),Level(query),Framerate(query),MaxFramerate(query),CopyTimestamps(query),StartTimeTicks(query),Width(query),Height(query),MaxWidth(query),MaxHeight(query),VideoBitRate(query),SubtitleStreamIndex(query),SubtitleMethod(query),MaxRefFrames(query),MaxVideoBitDepth(query),VideoCodec(query),AudioStreamIndex(query),VideoStreamIndex(query) |  |  |  |  |
| `GET /Videos/{Id}/master.m3u8` | P1 | DeviceProfileId(query),Id(path)*,DeviceId(query),Container(query)*,AudioCodec(query),EnableAutoStreamCopy(query),AudioSampleRate(query),AudioBitRate(query),AudioChannels(query),MaxAudioChannels(query),Static(query),Profile(query),Level(query),Framerate(query),MaxFramerate(query),CopyTimestamps(query),StartTimeTicks(query),Width(query),Height(query),MaxWidth(query),MaxHeight(query),VideoBitRate(query),SubtitleStreamIndex(query),SubtitleMethod(query),MaxRefFrames(query),MaxVideoBitDepth(query),VideoCodec(query),AudioStreamIndex(query),VideoStreamIndex(query) |  |  |  |  |
| `GET /Videos/{Id}/stream` | P1 | DeviceProfileId(query),Id(path)*,DeviceId(query),Container(query)*,AudioCodec(query),EnableAutoStreamCopy(query),AudioSampleRate(query),AudioBitRate(query),AudioChannels(query),MaxAudioChannels(query),Static(query),Profile(query),Level(query),Framerate(query),MaxFramerate(query),CopyTimestamps(query),StartTimeTicks(query),Width(query),Height(query),MaxWidth(query),MaxHeight(query),VideoBitRate(query),SubtitleStreamIndex(query),SubtitleMethod(query),MaxRefFrames(query),MaxVideoBitDepth(query),VideoCodec(query),AudioStreamIndex(query),VideoStreamIndex(query) |  |  |  |  |
| `GET /Videos/{Id}/stream.{Container}` | P1 | DeviceProfileId(query),Id(path)*,DeviceId(query),Container(path)*,AudioCodec(query),EnableAutoStreamCopy(query),AudioSampleRate(query),AudioBitRate(query),AudioChannels(query),MaxAudioChannels(query),Static(query),Profile(query),Level(query),Framerate(query),MaxFramerate(query),CopyTimestamps(query),StartTimeTicks(query),Width(query),Height(query),MaxWidth(query),MaxHeight(query),VideoBitRate(query),SubtitleStreamIndex(query),SubtitleMethod(query),MaxRefFrames(query),MaxVideoBitDepth(query),VideoCodec(query),AudioStreamIndex(query),VideoStreamIndex(query) |  |  |  |  |
| `GET /Videos/{Id}/subtitles.m3u8` | P1 | Id(path)*,SubtitleSegmentLength(query)*,ManifestSubtitles(query)* |  |  |  | 是 |
| `POST /Videos/MergeVersions` | P2 | Ids(query) |  |  |  |  |

### 域：web（5 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /favicon.ico` | P1 |  |  | image |  |  |
| `GET /robots.txt` | P1 |  |  | text |  |  |
| `GET /web/ConfigurationPage` | P1 |  |  |  | 是 |  |
| `GET /web/ConfigurationPages` | P1 |  |  | WebDashboard.Api.ConfigurationPageInfo[] |  |  |
| `GET /web/index.html` | P0 |  |  | text/html |  |  |

### 域：Years（1 个操作）

| 操作 | 优先级 | 参数 | 请求体 | 响应 schema | 404 预期 | 400 预期 |
|------|--------|------|--------|--------------|----------|----------|
| `GET /Years` | P1 | ArtistType(query),MaxOfficialRating(query),HasThemeSong(query),HasThemeVideo(query),HasSubtitles(query),HasSpecialFeature(query),HasTrailer(query),AdjacentTo(query),MinIndexNumber(query),MinPlayers(query),MaxPlayers(query),ParentIndexNumber(query),HasParentalRating(query),IsHD(query),LocationTypes(query),ExcludeLocationTypes(query),IsMissing(query),IsUnaired(query),MinCommunityRating(query),MinCriticRating(query),AiredDuringSeason(query),MinPremiereDate(query),MinDateLastSaved(query),MinDateLastSavedForUser(query),MaxPremiereDate(query),HasOverview(query),HasImdbId(query),HasTmdbId(query),HasTvdbId(query),ExcludeItemIds(query),StartIndex(query),Limit(query),Recursive(query),SortOrder(query),ParentId(query),Fields(query),ExcludeItemTypes(query),IncludeItemTypes(query),AnyProviderIdEquals(query),Filters(query),IsFavorite(query),IsMovie(query),IsSeries(query),IsNews(query),IsKids(query),IsSports(query),MediaTypes(query),ImageTypes(query),SortBy(query),IsPlayed(query),Genres(query),OfficialRatings(query),Tags(query),Years(query),EnableImages(query),EnableUserData(query),ImageTypeLimit(query),EnableImageTypes(query),Person(query),PersonIds(query),PersonTypes(query),Studios(query),StudioIds(query),Artists(query),ArtistIds(query),Albums(query),Ids(query),VideoTypes(query),Containers(query),AudioCodecs(query),VideoCodecs(query),SubtitleCodecs(query),Path(query),UserId(query),MinOfficialRating(query),IsLocked(query),IsPlaceHolder(query),HasOfficialRating(query),GroupItemsIntoCollections(query),Is3D(query),SeriesStatus(query),NameStartsWithOrGreater(query),NameStartsWith(query),NameLessThan(query) |  | QueryResult_UserLibrary.TagItem |  |  |

