# OpenMedia API 矩阵失败分类（第二轮·最终：83 个 FAIL）

> 运行：2026-10-14 20:18:40　受测：PID 25272（Debug 构建，ServerId 62255726…）　上下文 ItemId=Movie1（真实）
> 汇总：① 服务器 bug 22　② ffmpeg 未安装 11　③ 预期差异 6　④ 测试脚本/计划 12　⑤ 环境/fixture 32

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

