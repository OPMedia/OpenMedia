# OpenMedia API 矩阵失败分类（第二轮·最终：83 个 FAIL）

> 运行：2026-10-14 20:18:40　受测：PID 25272（Debug 构建，ServerId 62255726…）　上下文 ItemId=Movie1（真实）
> 汇总：① 服务器 bug 22　② ffmpeg 未安装 11　③ 预期差异 6　④ 测试脚本/计划 12　⑤ 环境/fixture 32

## ⑥ 二期新增 bug（T3.2/T2/T4 测试发现，2026-10-14 22:30-23:20，受测 PID 6748）

> 编号接一期 BUG-19；Similar 500（similarity_score 列缺失，Developer 处理中）未占号。证据：`D:\OpenMedia\ProgramData-Server\logs\`（server.txt + ffmpeg-*.txt）与 tests/results/retest-ffmpeg-latest.json

| 编号 | 端点/路径 | 现象 | 根因（代码证据） |
|------|-----------|------|------------------|
| BUG-20 | `GET /Users/{uid}/Items?IncludeItemTypes=Movie` | 返回空（TotalRecordCount=0），同参数 `/Items`（无 UserId）正常返回 2 部 | 用户作用域查询 + IncludeItemTypes 分支组合失效（查询层，ExcludeItemTypes/MediaTypes 正常） |
| BUG-21 | `GET /Videos/{Id}/live.m3u8`（无 SegmentContainer） | 500 "Error starting ffmpeg" | BaseHlsService.cs:176 `text5=SegmentContainer` 为空 → 命令行 `-segment_format ` 空值 → ffmpeg 参数移位（`-segment_list_entry_prefix` 被吞，输出 URL 变 `hls/xxx/`）→ "Output file does not contain any stream" |
| BUG-22 | `GET /Videos/{Id}/live.m3u8`（真实参数） | 请求挂起（客户端 20s/120s 超时，服务器侧无限等待） | ffmpeg 9 播放列表用 `.tmp`+rename 原子写；服务器 WaitForMinimumSegmentCount（BaseHlsService.cs:124）轮询打开 `.m3u8` 无 FILE_SHARE_DELETE → rename 失败（日志 "failed to rename ... Operation not permitted"）→ 播放列表停在 2 条 < MinSegments=3 → while 循环无超时死等 |
| BUG-23 | `GET /Videos/{Id}/hls1/{PlaylistId}/{SegmentId}.ts`（分片首取） | 首次 500 "Error starting ffmpeg"，2s 后重试 200 | StartFfMpeg 轮询（BaseStreamingService.cs:255-270）50ms 周期错过短视频（8s）分片文件存在窗口（ffmpeg ~90ms 跑完退出，`while(!hasProcessExited)` 先退出且未观测到文件）→ 误判启动失败；失败路径不注册 TranscodingJob → 后续 DELETE ActiveEncodings 无 job 可杀 → 文件泄漏 |
| BUG-24 | 分片重启路径（seek 后取分片，如 segment 1 首次缺失） | 确定性 500 | 命令行 `-segment_time_delta -6` 负值被 ffmpeg 9 拒绝（"out of range [0 - …]"→"Could not write header"→Conversion failed）。旧 ffmpeg 接受负值。T4 并发中 50% 分片请求因此 500 |
| BUG-25 | transcoding-temp 临时文件 | progressive remux（/stream）与 HLS 会话结束后文件不清理（3 个 .mkv 残留 35min+；T4 会话 6 文件 70s+ 后仍在） | ffmpeg 先于 kill timer 退出 → PingTimer（ApiEntryPoint.cs:346）`if (job.HasExited) { StopKillTimer; return; }` → 永不触发 KillTranscodingJob(delete:true)；BUG-23 失败路径则根本无 job |
| BUG-26 | `GET /Videos/{Id}/stream?static=true` + 越界 Range | `Content-Length: 1048576` 但实际发送 809,858B（RFC 7233 违例：应按文件尾截断 Content-Range/Content-Length） | 严格 HTTP 客户端（Python urllib）IncompleteRead；curl 容忍。L3 负载首跑 100% 失败即此因 |

**T3.2 复测结果**（② 类 11 端点，tests/results/retest-ffmpeg-latest.json）：
- 转绿 4：Audio stream ×2、Video stream ×2 → 200（真实 remux 字节）
- 转 400 共 4（Developer T1.1 FormatException→400 映射已生效）：subtitles.m3u8(test)、Audio/Video hls1(test/test.test)、Subtitles StartPositionTicks(test)
- 仍 500 共 2：live.m3u8?Container=mkv（BUG-21）、Subtitles/0/Stream.original（① 类 First()，无字幕流 fixture）
- 合理 404 共 1：legacy /hls/（无活跃会话）
- 端到端（web 播放器实际链路 master→main→分片）：PlaybackInfo 200（h264 640x360+aac）、master/main 200、segment 0 重试后 200（889KB 真实 TS）、segment 1 因 BUG-24 失败、DELETE ActiveEncodings 204 但清理链因 BUG-23 断裂

## ① 服务器 bug（代码缺陷）

| 操作 | 状态码 | 响应片段 | 分类说明 |
|------|--------|----------|----------|
| `GET /Images/Remote` | 500 | Invalid URI: The format of the URI could not be determined. | BUG-13 非法参数 500 |
| `GET /LiveTv/ChannelMappings` | 500 | Sequence contains no matching element | BUG-10 First() 空序列 500 |
| `GET /LiveTv/ChannelMappingOptions` | 500 | Sequence contains no matching element | BUG-10 First() 空序列 500 |
| `GET /Packages/{Name}` | 400 | Value cannot be null. (Parameter 'result') | 包不存在 400 null（应 404；GZip 修复已验证） |
| `GET /Playback/BitrateTest` | 500 | The input string 'test' was not in a correct format. | BUG-13 非法参数 500 |
| `GET /Dlna/icons/{Filename}` | 500 | Object reference not set to an instance of an object. | BUG-9 NRE 500 |
| `GET /Dlna/Profiles/{Id}` | 500 | Sequence contains no matching element | BUG-10 First() 空序列 500 |
| `GET /Items/{Id}/PlaybackInfo` | 500 | 23503: 插入或更新表 "item_extradata" 违反外键约束 "item_extradata_fixed_extradata_type_id_fkey1" DETAIL: Detail redacted as it may c… | BUG-16 item_extradata FK 23503 |
| `POST /Items/{Id}/PlaybackInfo` | 500 | 23503: 插入或更新表 "item_extradata" 违反外键约束 "item_extradata_fixed_extradata_type_id_fkey1" DETAIL: Detail redacted as it may c… | BUG-16 item_extradata FK 23503 |
| `GET /LiveTv/Recordings/Folders` | 500 | Object reference not set to an instance of an object. | BUG-9 NRE 500 |
| `GET /LiveTv/Channels/{Id}` | 400 | Value cannot be null. (Parameter 'id') | BUG-11 资源不存在 400 null |
| `GET /LiveTv/Recordings/{Id}` | 400 | Value cannot be null. (Parameter 'id') | BUG-11 资源不存在 400 null |
| `GET /LiveTv/Timers/{Id}` | 400 | Value cannot be null. (Parameter 'result') | BUG-11 资源不存在 400 null |
| `GET /LiveTv/SeriesTimers/{Id}` | 400 | Value cannot be null. (Parameter 'result') | BUG-11 资源不存在 400 null |
| `GET /Playlists/{Id}/Items` | 500 | Unable to cast object of type 'MediaBrowser.Controller.Entities.Movies.Movie' to type 'MediaBrowser.Controller.Playlists… | Movie 强转 Playlist（健壮性） |
| `GET /Plugins/{Id}/Thumb` | 500 | Sequence contains no matching element | BUG-10 First() 空序列 500 |
| `GET /Plugins/{Id}/Configuration` | 500 | Sequence contains no matching element | BUG-10 First() 空序列 500 |
| `GET /System/Logs/Log` | 500 | Sequence contains no matching element | BUG-10 First() 空序列 500 |
| `GET /Videos/{Id}/index.bif` | 500 | The input string 'test' was not in a correct format. | BUG-13 非法参数 500 |
| `GET /Dlna/{UuId}/icons/{Filename}` | 500 | Object reference not set to an instance of an object. | BUG-9 NRE 500 |
| `GET /LiveTv/LiveStreamFiles/{Id}/stream.{Container}` | 500 | Object reference not set to an instance of an object. | BUG-9 NRE 500 |
| `GET /Providers/Subtitles/Subtitles/{Id}` | 500 | Sequence contains no matching element | BUG-10 First() 空序列 500 |

## ② ffmpeg 未安装（转码/流路径）

| 操作 | 状态码 | 响应片段 | 分类说明 |
|------|--------|----------|----------|
| `GET /Audio/{Id}/stream.{Container}` | 500 | Cannot start process because a file name has not been provided. | ffprobe 路径空（ffmpeg 未安装） |
| `GET /Audio/{Id}/stream` | 500 | Cannot start process because a file name has not been provided. | ffprobe 路径空（ffmpeg 未安装） |
| `GET /Videos/{Id}/stream.{Container}` | 500 | Cannot start process because a file name has not been provided. | ffprobe 路径空（ffmpeg 未安装） |
| `GET /Videos/{Id}/stream` | 500 | Cannot start process because a file name has not been provided. | ffprobe 路径空（ffmpeg 未安装） |
| `GET /Videos/{Id}/subtitles.m3u8` | 500 | The input string 'test' was not in a correct format. | HLS/字幕转码路径（ffmpeg） |
| `GET /Videos/{Id}/live.m3u8` | 500 | Cannot start process because a file name has not been provided. | ffprobe 路径空（ffmpeg 未安装） |
| `GET /Audio/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}` | 500 | The input string 'test' was not in a correct format. | HLS/字幕转码路径（ffmpeg） |
| `GET /Videos/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}` | 500 | The input string 'test' was not in a correct format. | HLS/字幕转码路径（ffmpeg） |
| `GET /Videos/{Id}/hls/{PlaylistId}/{SegmentId}.{SegmentContainer}` | 404 | Could not find file '\ProgramData-Server\transcoding-temp\test.test'. | HLS/字幕转码路径（ffmpeg） |
| `GET /Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/Stream.{Format}` | 500 | Sequence contains no matching element | HLS/字幕转码路径（ffmpeg） |
| `GET /Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/{StartPositionTicks}/Stream.{Format}` | 500 | The input string 'test' was not in a correct format. | HLS/字幕转码路径（ffmpeg） |

## ③ 预期差异（spec/实现差异）

| 操作 | 状态码 | 响应片段 | 分类说明 |
|------|--------|----------|----------|
| `GET /Games/SystemSummaries` | 400 | Value cannot be null. (Parameter 'id') | UserId 实际必填（加参 200 已验证） |
| `GET /Items/File` | 400 | Value cannot be null. (Parameter 'path') | path 实际必填 |
| `GET /Movies/Recommendations` | 400 | Value cannot be null. (Parameter 'id') | UserId 实际必填（加参 200 已验证） |
| `GET /Audio/{Id}/master.m3u8` | 400 | MediaSourceId is required | MediaSourceId 必填（spec 标可选） |
| `GET /LiveTv/Programs/Recommended` | 400 | Value cannot be null. (Parameter 'id') | UserId 实际必填（加参 200 已验证） |
| `GET /Videos/{Id}/master.m3u8` | 400 | MediaSourceId is required | MediaSourceId 必填（spec 标可选） |

## ④ 测试脚本/计划缺陷

| 操作 | 状态码 | 响应片段 | 分类说明 |
|------|--------|----------|----------|
| `GET /Users` | 200 | [{"Name":"admin","ServerId":"6225572626f94ea0b6e469c9ac63ef6f","Id":"ac49142474fd4c49b85427f29fc4ba9b","HasPassword":fal… | PS5.1 单元素数组解包误报（实际 200 合法数组） |
| `GET /Environment/DirectoryContents` | 404 | Could not find a part of the path 'D:\Dev\OpenMedia\MediaBrowser.Server\test'. | 合成参数 404/400 合理 |
| `GET /ScheduledTasks/{Id}` | 404 | Task not found | 合成参数 404/400 合理 |
| `GET /Users/Public` | 200 | [{"Name":"admin","ServerId":"6225572626f94ea0b6e469c9ac63ef6f","Id":"ac49142474fd4c49b85427f29fc4ba9b","HasPassword":fal… | PS5.1 单元素数组解包误报（实际 200 合法数组） |
| `POST /Users/AuthenticateByName` | 400 | Value cannot be null. (Parameter 'username') | POST 无 body 400 合理（S2 已验证 200） |
| `GET /Channels/{Id}/Items` | 500 | Unrecognized Guid format. | 计划缺参数定义（URL 未替换 {Id}） |
| `GET /Items/RemoteSearch/Image` | 400 | Search provider not found. | 合成参数 404/400 合理 |
| `GET /System/Configuration/{Key}` | 404 | Configuration with key test not found. | 合成参数 404/400 合理 |
| `GET /Users/{UserId}/Views` | 500 | The input string 'test' was not in a correct format. | IncludeExternalContent='test' FormatException（无参 200 已验证） |
| `GET /Users/{UserId}/Items/{Id}` | 500 | Object reference not set to an instance of an object. | {Id} 解析为 UserId → NRE（+① 健壮性） |
| `GET /Users/{UserId}/Items/{Id}/SpecialFeatures` | 500 | Object reference not set to an instance of an object. | {Id} 解析为 UserId → NRE（+① 健壮性） |
| `GET /Users/{UserId}/Items/{Id}/LocalTrailers` | 500 | Object reference not set to an instance of an object. | {Id} 解析为 UserId → NRE（+① 健壮性） |

## ⑤ 环境问题/fixture 限制

| 操作 | 状态码 | 响应片段 | 分类说明 |
|------|--------|----------|----------|
| `GET /Artists/InstantMix` | 400 | Value cannot be null. (Parameter 'id') | Movie 非音频条目（需音频 fixture） |
| `GET /MusicGenres/InstantMix` | 400 | Value cannot be null. (Parameter 'id') | Movie 非音频条目（需音频 fixture） |
| `GET /web/ConfigurationPage` | 404 | Exception of type 'MediaBrowser.Common.Extensions.ResourceNotFoundException' was thrown. | 无插件/无 LiveTv 数据/图库无资源 |
| `GET /Albums/{Id}/InstantMix` | 400 | Value cannot be null. (Parameter 'id') | Movie 非音频条目（需音频 fixture） |
| `GET /Audio/{Id}/universal.{Container}` | 400 | Value cannot be null. (Parameter 'id') | Movie 非音频条目（需音频 fixture） |
| `GET /Audio/{Id}/universal` | 400 | Value cannot be null. (Parameter 'id') | Movie 非音频条目（需音频 fixture） |
| `GET /Items/{Id}/InstantMix` | 400 | Value cannot be null. (Parameter 'id') | Movie 非音频条目（需音频 fixture） |
| `GET /LiveTv/ListingProviders/Lineups` | 404 | Exception of type 'MediaBrowser.Common.Extensions.ResourceNotFoundException' was thrown. | 无插件/无 LiveTv 数据/图库无资源 |
| `GET /MusicGenres/{Name}/InstantMix` | 400 | Value cannot be null. (Parameter 'id') | Movie 非音频条目（需音频 fixture） |
| `GET /Playlists/{Id}/InstantMix` | 500 | Unable to cast object of type 'MediaBrowser.Controller.Entities.Movies.Movie' to type 'MediaBrowser.Controller.Playlists… | Movie 非音频条目（需音频 fixture） |
| `GET /Shows/{Id}/Seasons` | 404 | Series not found | Movie 非 Series |
| `GET /Shows/{Id}/Episodes` | 404 | Series not found | Movie 非 Series |
| `GET /Songs/{Id}/InstantMix` | 400 | Value cannot be null. (Parameter 'id') | Movie 非音频条目（需音频 fixture） |
| `GET /Artists/{Name}/Images/{Type}` | 404 | Movie1 does not have an image of type Primary | 按名实体无图片 |
| `GET /GameGenres/{Name}/Images/{Type}` | 404 | Movie1 does not have an image of type Primary | 按名实体无图片 |
| `GET /Genres/{Name}/Images/{Type}` | 404 | Movie1 does not have an image of type Primary | 按名实体无图片 |
| `GET /Images/General/{Name}/{Type}` | 404 | Could not find a part of the path '\ProgramData-Server\metadata\general\Movie1\folder.png'. | 无插件/无 LiveTv 数据/图库无资源 |
| `GET /Images/Ratings/{Theme}/{Name}` | 404 | MediaInfo image not found: Movie1 | 无插件/无 LiveTv 数据/图库无资源 |
| `GET /Images/MediaInfo/{Theme}/{Name}` | 404 | MediaInfo image not found: Movie1 | 无插件/无 LiveTv 数据/图库无资源 |
| `GET /LiveTv/LiveRecordings/{Id}/stream` | 404 | Unable to find the specified file. | 无插件/无 LiveTv 数据/图库无资源 |
| `GET /LiveTv/Recordings/Groups/{Id}` | 404 | Unable to find the specified file. | 无插件/无 LiveTv 数据/图库无资源 |
| `GET /MusicGenres/{Name}/Images/{Type}` | 404 | Movie1 does not have an image of type Primary | 按名实体无图片 |
| `GET /Persons/{Name}/Images/{Type}` | 404 | Movie1 does not have an image of type Primary | 按名实体无图片 |
| `GET /Studios/{Name}/Images/{Type}` | 404 | Movie1 does not have an image of type Primary | 按名实体无图片 |
| `GET /Users/{Id}/Images/{Type}` | 404 | admin does not have an image of type Primary | 用户无图片 |
| `GET /Artists/{Name}/Images/{Type}/{Index}` | 404 | Movie1 does not have an image of type Primary | 按名实体无图片 |
| `GET /GameGenres/{Name}/Images/{Type}/{Index}` | 404 | Movie1 does not have an image of type Primary | 按名实体无图片 |
| `GET /Genres/{Name}/Images/{Type}/{Index}` | 404 | Movie1 does not have an image of type Primary | 按名实体无图片 |
| `GET /MusicGenres/{Name}/Images/{Type}/{Index}` | 404 | Movie1 does not have an image of type Primary | 按名实体无图片 |
| `GET /Persons/{Name}/Images/{Type}/{Index}` | 404 | Movie1 does not have an image of type Primary | 按名实体无图片 |
| `GET /Studios/{Name}/Images/{Type}/{Index}` | 404 | Movie1 does not have an image of type Primary | 按名实体无图片 |
| `GET /Users/{Id}/Images/{Type}/{Index}` | 404 | admin does not have an image of type Primary | 用户无图片 |

