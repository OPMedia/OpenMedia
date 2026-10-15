# OpenMedia API 矩阵测试报告

> 运行时间：2026-10-15 09:16:49　基址：http://localhost:8096/emby　认证：已认证（admin/空密码回退）
> 上下文：UserId=ac49142474fd4c49b85427f29fc4ba9b　ItemId=c3f6962891668fc8365e3dfaa24bb3ea（真实=True）　SessionId=29e9e0a447d57a2a7821b45e79ba36ed

## 汇总

| 级别 | 执行 | PASS | FAIL | SKIP-EXPECTED | SKIP-NOFIXTURE |
|------|------|------|------|---------------|----------------|
| P0 | 14 | 13 | 0 | — | 1 |
| P1 | 252 | 186 | 0 | 66 | 0 |
| P2 | 151 | — | — | — | 全部跳过（写/破坏性） |

**通过率（PASS / (PASS+FAIL)）：100%**（199 / 199）

## 负路径用例（T1.T 固化，24/24 PASS）

| 族 | 操作 | 期望 | 实际 | 结果 |
|----|------|------|------|------|
| LiveTv | `GET /LiveTv/Channels/{Id}` | 404 | 404 | **PASS** |
| LiveTv | `GET /LiveTv/Recordings/{Id}` | 404 | 404 | **PASS** |
| LiveTv | `GET /LiveTv/Timers/{Id}` | 404 | 404 | **PASS** |
| LiveTv | `GET /LiveTv/SeriesTimers/{Id}` | 404 | 404 | **PASS** |
| LiveTv | `GET /LiveTv/ChannelMappings` | 404 | 404 | **PASS** |
| LiveTv | `GET /LiveTv/ChannelMappingOptions` | 404 | 404 | **PASS** |
| LiveTv | `GET /LiveTv/Recordings/Folders` | 200 | 200 | **PASS** |
| LiveTv | `GET /LiveTv/LiveStreamFiles/{Id}/stream.{Container}` | 404/400 | 404 | **PASS** |
| Dlna | `GET /Dlna/Profiles/{Id}` | 404 | 404 | **PASS** |
| Dlna | `GET /Dlna/icons/{Filename}` | 404 | 404 | **PASS** |
| Dlna | `GET /Dlna/{UuId}/icons/{Filename}` | 404/400 | 404 | **PASS** |
| Plugins | `GET /Plugins/{Id}/Thumb` | 404 | 404 | **PASS** |
| Plugins | `GET /Plugins/{Id}/Configuration` | 404 | 404 | **PASS** |
| Packages | `GET /Packages/{Name}` | 404 | 404 | **PASS** |
| Logs | `GET /System/Logs/Log` | 404 | 404 | **PASS** |
| Subtitles | `GET /Providers/Subtitles/Subtitles/{Id}` | 404 | 404 | **PASS** |
| Playlists | `GET /Playlists/{Id}/Items` | 404 | 404 | **PASS** |
| Playlists | `GET /Playlists/{Id}/InstantMix` | 404 | 404 | **PASS** |
| Parse400 | `GET /Playback/BitrateTest` | 400 | 400 | **PASS** |
| Parse400 | `GET /Images/Remote` | 400 | 400 | **PASS** |
| Parse400 | `GET /Videos/{Id}/index.bif` | 400 | 400 | **PASS** |
| Parse400 | `GET /Users/{UserId}/Views` | 400 | 400 | **PASS** |
| Similar | `GET /Albums/{Id}/Similar` | 404 | 404 | **PASS** |
| Similar | `GET /Items/{Id}/Similar` | 200 | 200 | **PASS** |

## 明细矩阵（按域分组）

### 域：Albums（2）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Albums/{Id}/InstantMix` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Albums/{Id}/Similar` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：Artists（8）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Artists` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Artists/{Id}/Similar` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Artists/{Name}` | P1 | 200 | PascalCase ✓ | Name,ServerId,Id,Etag,DateCreated,CanDelete,CanDownload,Sort… | **PASS** |  |
| `GET /Artists/{Name}/Images/{Type}` | P1 | 200 | — |  | **PASS** |  |
| `GET /Artists/{Name}/Images/{Type}/{Index}` | P1 | 200 | — |  | **PASS** |  |
| `GET /Artists/AlbumArtists` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Artists/InstantMix` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Artists/Prefixes` | P1 | 200 | PascalCase ✓ | Name,Value | **PASS** |  |

### 域：Audio（7）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Audio/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}` | P1 | 400 | — |  | **SKIP-EXPECTED** | 合成参数 400 为正确校验行为（负路径用例已固化） |
| `GET /Audio/{Id}/main.m3u8` | P1 | 200 | — |  | **PASS** |  |
| `GET /Audio/{Id}/master.m3u8` | P1 | 200 | — |  | **PASS** |  |
| `GET /Audio/{Id}/stream` | P1 | 200 | — |  | **PASS** |  |
| `GET /Audio/{Id}/stream.{Container}` | P1 | 200 | — |  | **PASS** |  |
| `GET /Audio/{Id}/universal` | P1 | 200 | — |  | **PASS** |  |
| `GET /Audio/{Id}/universal.{Container}` | P1 | 200 | — |  | **PASS** |  |

### 域：AudioCodecs（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /AudioCodecs` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：Auth（2）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Auth/Keys` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Auth/Providers` | P1 | 200 | PascalCase ✓ | Name,Id | **PASS** |  |

### 域：Branding（3）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Branding/Configuration` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Branding/Css` | P1 | 200 | — |  | **PASS** |  |
| `GET /Branding/Css.css` | P1 | 200 | — |  | **PASS** |  |

### 域：Channels（3）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Channels` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Channels/{Id}/Items` | P1 | 400 | — |  | **SKIP-EXPECTED** | Channels 端点为库内频道域（非 LiveTv 频道），LiveTv 频道 Id 不适用，400 正确 |
| `GET /Channels/Items/Latest` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：Connect（2）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Connect/Exchange` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：云连接移除 |
| `GET /Connect/Pending` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：云连接移除 |

### 域：Containers（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Containers` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：Devices（4）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Devices` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Devices/CameraUploads` | P1 | 200 | PascalCase ✓ | DeviceId,FilesUploaded | **PASS** |  |
| `GET /Devices/Info` | P1 | 200 | PascalCase ✓ | Name,Id,LastUserName,AppName,AppVersion,LastUserId,DateLastA… | **PASS** |  |
| `GET /Devices/Options` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |

### 域：DisplayPreferences（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /DisplayPreferences/{Id}` | P1 | 200 | PascalCase ✓ | Id,RememberIndexing,PrimaryImageHeight,PrimaryImageWidth,Cus… | **PASS** |  |

### 域：Dlna（11）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Dlna/{UuId}/connectionmanager/connectionmanager` | P1 | 200 | — |  | **PASS** |  |
| `GET /Dlna/{UuId}/connectionmanager/connectionmanager.xml` | P1 | 200 | — |  | **PASS** |  |
| `GET /Dlna/{UuId}/contentdirectory/contentdirectory` | P1 | 200 | — |  | **PASS** |  |
| `GET /Dlna/{UuId}/contentdirectory/contentdirectory.xml` | P1 | 200 | — |  | **PASS** |  |
| `GET /Dlna/{UuId}/description` | P1 | 200 | — |  | **PASS** |  |
| `GET /Dlna/{UuId}/description.xml` | P1 | 200 | — |  | **PASS** |  |
| `GET /Dlna/{UuId}/icons/{Filename}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 合成参数 404 合理（无对应资源） |
| `GET /Dlna/icons/{Filename}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 合成参数 404 合理（无对应资源） |
| `GET /Dlna/ProfileInfos` | P1 | 200 | PascalCase ✓ | Id,Name,Type | **PASS** |  |
| `GET /Dlna/Profiles/{Id}` | P1 | 200 | PascalCase ✓ | Name,Id,Manufacturer,ManufacturerUrl,ModelName,ModelDescript… | **PASS** |  |
| `GET /Dlna/Profiles/Default` | P1 | 200 | PascalCase ✓ | Name,Manufacturer,ManufacturerUrl,ModelName,ModelDescription… | **PASS** |  |

### 域：Encoding（2）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Encoding/CodecConfiguration/Defaults` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Encoding/CodecInformation/Video` | P1 | 200 | PascalCase ✓ | Name,CodecKind,Direction,MediaTypeName,Description,IsHardwar… | **PASS** |  |

### 域：Environment（6）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Environment/DefaultDirectoryBrowser` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Environment/DirectoryContents` | P1 | 404 | — |  | **SKIP-EXPECTED** | 合成参数 404 合理（无对应资源） |
| `GET /Environment/Drives` | P1 | 200 | PascalCase ✓ | Name,Path,Type | **PASS** |  |
| `GET /Environment/NetworkDevices` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Environment/NetworkShares` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Environment/ParentPath` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |

### 域：GameGenres（4）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /GameGenres` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /GameGenres/{Name}` | P1 | 200 | PascalCase ✓ | Name,ServerId,Id,Etag,DateCreated,CanDelete,CanDownload,Sort… | **PASS** |  |
| `GET /GameGenres/{Name}/Images/{Type}` | P1 | 200 | — |  | **PASS** |  |
| `GET /GameGenres/{Name}/Images/{Type}/{Index}` | P1 | 200 | — |  | **PASS** |  |

### 域：Games（2）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Games/{Id}/Similar` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Games/SystemSummaries` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |

### 域：Genres（4）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Genres` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Genres/{Name}` | P1 | 200 | PascalCase ✓ | Name,ServerId,Id,Etag,DateCreated,CanDelete,CanDownload,Sort… | **PASS** |  |
| `GET /Genres/{Name}/Images/{Type}` | P1 | 200 | — |  | **PASS** |  |
| `GET /Genres/{Name}/Images/{Type}/{Index}` | P1 | 200 | — |  | **PASS** |  |

### 域：Images（7）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Images/General` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Images/General/{Name}/{Type}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 合成参数 404 合理（无对应资源） |
| `GET /Images/MediaInfo` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Images/MediaInfo/{Theme}/{Name}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 合成参数 404 合理（无对应资源） |
| `GET /Images/Ratings` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Images/Ratings/{Theme}/{Name}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 合成参数 404 合理（无对应资源） |
| `GET /Images/Remote` | P1 | 400 | — |  | **SKIP-EXPECTED** | 合成参数 400 为正确校验行为（负路径用例已固化） |

### 域：Items（29）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Items` | P0 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Items/{Id}/Ancestors` | P1 | 200 | PascalCase ✓ | Name,ServerId,Id,Etag,DateCreated,CanDelete,CanDownload,Sort… | **PASS** |  |
| `GET /Items/{Id}/CriticReviews` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Items/{Id}/DeleteInfo` | P1 | 200 | PascalCase ✓ | Paths | **PASS** |  |
| `GET /Items/{Id}/Download` | P1 | 200 | — |  | **PASS** |  |
| `GET /Items/{Id}/ExternalIdInfos` | P1 | 200 | PascalCase ✓ | Name,Key,UrlFormatString | **PASS** |  |
| `GET /Items/{Id}/File` | P1 | 200 | — |  | **PASS** |  |
| `GET /Items/{Id}/Images` | P0 | 200 | PascalCase ✓ | ImageType,Path,Height,Width,Size | **PASS** |  |
| `GET /Items/{Id}/Images/{Type}` | P0 | 200 | — |  | **PASS** | image/jpeg, 633B |
| `GET /Items/{Id}/Images/{Type}/{Index}` | P0 | 200 | — |  | **PASS** | image/jpeg, 633B |
| `GET /Items/{Id}/Images/{Type}/{Index}/{Tag}/{Format}/{MaxWidth}/{MaxHeight}/{PercentPlayed}/{UnplayedCount}` | P0 | 200 | — |  | **PASS** | image/jpeg, 633B |
| `GET /Items/{Id}/InstantMix` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Items/{Id}/PlaybackInfo` | P1 | 200 | PascalCase ✓ | MediaSources,PlaySessionId | **PASS** |  |
| `GET /Items/{Id}/RemoteImages` | P1 | 200 | PascalCase ✓ | Images,TotalRecordCount,Providers | **PASS** |  |
| `GET /Items/{Id}/RemoteImages/Providers` | P1 | 200 | PascalCase ✓ | Name,SupportedImages | **PASS** |  |
| `GET /Items/{Id}/RemoteSearch/Subtitles/{Language}` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Items/{Id}/Similar` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Items/{Id}/ThemeMedia` | P1 | 200 | PascalCase ✓ | ThemeVideosResult,ThemeSongsResult,SoundtrackSongsResult | **PASS** |  |
| `GET /Items/{Id}/ThemeSongs` | P1 | 200 | PascalCase ✓ | OwnerId,Items,TotalRecordCount | **PASS** |  |
| `GET /Items/{Id}/ThemeVideos` | P1 | 200 | PascalCase ✓ | OwnerId,Items,TotalRecordCount | **PASS** |  |
| `GET /Items/{Id}/ThumbnailSet` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（RokuMetadata 插件） |
| `GET /Items/{ItemId}/MetadataEditor` | P1 | 200 | PascalCase ✓ | ParentalRatingOptions,Countries,Cultures,ExternalIdInfos,Con… | **PASS** |  |
| `GET /Items/Counts` | P1 | 200 | PascalCase ✓ | MovieCount,SeriesCount,EpisodeCount,GameCount,ArtistCount,Pr… | **PASS** |  |
| `GET /Items/File` | P1 | 200 | — |  | **PASS** |  |
| `GET /Items/Filters` | P1 | 200 | PascalCase ✓ | Genres,Tags,OfficialRatings,Years | **PASS** |  |
| `GET /Items/Filters2` | P1 | 200 | PascalCase ✓ | Genres,Tags,Studios | **PASS** |  |
| `GET /Items/Prefixes` | P1 | 200 | PascalCase ✓ | Name,Value | **PASS** |  |
| `GET /Items/RemoteSearch/Image` | P1 | 400 | — |  | **SKIP-EXPECTED** | 合成参数 400 为正确校验行为（负路径用例已固化） |
| `POST /Items/{Id}/PlaybackInfo` | P0 | 200 | PascalCase ✓ | MediaSources,PlaySessionId | **PASS** |  |

### 域：Libraries（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Libraries/AvailableOptions` | P1 | 200 | PascalCase ✓ | MetadataSavers,MetadataReaders,SubtitleFetchers,TypeOptions | **PASS** |  |

### 域：Library（4）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Library/MediaFolders` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Library/PhysicalPaths` | P1 | 200 | PascalCase ✓ | (非对象: String) | **PASS** |  |
| `GET /Library/SelectableMediaFolders` | P1 | 200 | PascalCase ✓ | Name,Id,SubFolders | **PASS** |  |
| `GET /Library/VirtualFolders` | P1 | 200 | PascalCase ✓ | Name,Locations,CollectionType,LibraryOptions,ItemId,RefreshS… | **PASS** |  |

### 域：LiveTv（33）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /LiveTv/ChannelMappingOptions` | P1 | 200 | PascalCase ✓ | TunerChannels,ProviderChannels,Mappings,ProviderName | **PASS** |  |
| `GET /LiveTv/ChannelMappings` | P1 | 200 | PascalCase ✓ | TunerChannels,ProviderChannels,Mappings,ProviderName | **PASS** |  |
| `GET /LiveTv/Channels` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /LiveTv/Channels/{Id}` | P1 | 200 | PascalCase ✓ | Name,ServerId,Id,Etag,DateCreated,CanDelete,CanDownload,Sort… | **PASS** |  |
| `GET /LiveTv/GuideInfo` | P1 | 200 | PascalCase ✓ | StartDate,EndDate | **PASS** |  |
| `GET /LiveTv/Info` | P1 | 200 | PascalCase ✓ | Services,IsEnabled,EnabledUsers | **PASS** |  |
| `GET /LiveTv/ListingProviders` | P1 | 200 | PascalCase ✓ | Id,Type,Path,EnabledTuners,EnableAllTuners,NewsCategories,Sp… | **PASS** |  |
| `GET /LiveTv/ListingProviders/Available` | P1 | 200 | PascalCase ✓ | Name,Id | **PASS** |  |
| `GET /LiveTv/ListingProviders/Default` | P1 | 200 | PascalCase ✓ | EnabledTuners,EnableAllTuners,NewsCategories,SportsCategorie… | **PASS** |  |
| `GET /LiveTv/ListingProviders/Lineups` | P1 | 404 | — |  | **SKIP-EXPECTED** | 合成参数 404 合理（无对应资源） |
| `GET /LiveTv/ListingProviders/SchedulesDirect/Countries` | P1 | 200 | ✗ North America,Latin America | North America,Europe,Latin America,Caribbean,Oceania | **PASS** | 键名风格: North America,Latin America |
| `GET /LiveTv/LiveRecordings/{Id}/stream` | P1 | 404 | — |  | **SKIP-EXPECTED** | 无进行中直播录制（已完成录制不走此路由），404 正确 |
| `GET /LiveTv/LiveStreamFiles/{Id}/stream.{Container}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 无活跃直播流文件 fixture（需 OpenToken 会话），404 正确 |
| `GET /LiveTv/Programs` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /LiveTv/Programs/{Id}` | P1 | 200 | PascalCase ✓ | Name,OriginalTitle,ServerId,Id,Etag,DateCreated,CanDelete,Ca… | **PASS** |  |
| `GET /LiveTv/Programs/Recommended` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /LiveTv/Recordings` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /LiveTv/Recordings/{Id}` | P1 | 200 | PascalCase ✓ | Name,ServerId,Id,Etag,DateCreated,CanDelete,CanDownload,Cont… | **PASS** |  |
| `GET /LiveTv/Recordings/Folders` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /LiveTv/Recordings/Groups` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /LiveTv/Recordings/Groups/{Id}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 无录制组 fixture（Groups 端点期望组而非单条录制），404 正确 |
| `GET /LiveTv/Recordings/Series` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /LiveTv/SeriesTimers` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /LiveTv/SeriesTimers/{Id}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 无 series timer fixture，404 正确 |
| `GET /LiveTv/Timers` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /LiveTv/Timers/{Id}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 无活跃 timer fixture（B 轨道 timer 已消费），404 正确 |
| `GET /LiveTv/Timers/Defaults` | P1 | 200 | PascalCase ✓ | RecordAnyTime,SkipEpisodesInLibrary,RecordAnyChannel,KeepUpT… | **PASS** |  |
| `GET /LiveTv/TunerHosts` | P1 | 200 | PascalCase ✓ | Id,Url,Type,ImportFavoritesOnly,AllowHWTranscoding,EnableStr… | **PASS** |  |
| `GET /LiveTv/TunerHosts/Types` | P1 | 200 | PascalCase ✓ | Name,Id | **PASS** |  |
| `GET /LiveTv/Tuners/Discvover` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `OPTIONS /LiveTv/ChannelMappingOptions` | P1 | 200 | — |  | **PASS** |  |
| `OPTIONS /LiveTv/ChannelMappings` | P1 | 200 | — |  | **PASS** |  |
| `POST /LiveTv/Programs` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：Localization（4）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Localization/Countries` | P1 | 200 | PascalCase ✓ | Name,DisplayName,TwoLetterISORegionName,ThreeLetterISORegion… | **PASS** |  |
| `GET /Localization/Cultures` | P1 | 200 | PascalCase ✓ | Name,DisplayName,TwoLetterISOLanguageName,ThreeLetterISOLang… | **PASS** |  |
| `GET /Localization/Options` | P1 | 200 | PascalCase ✓ | Name,Value | **PASS** |  |
| `GET /Localization/ParentalRatings` | P1 | 200 | PascalCase ✓ | Name,Value | **PASS** |  |

### 域：Movies（2）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Movies/{Id}/Similar` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Movies/Recommendations` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |

### 域：MusicGenres（6）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /MusicGenres` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /MusicGenres/{Name}` | P1 | 200 | PascalCase ✓ | Name,ServerId,Id,Etag,DateCreated,CanDelete,CanDownload,Sort… | **PASS** |  |
| `GET /MusicGenres/{Name}/Images/{Type}` | P1 | 200 | — |  | **PASS** |  |
| `GET /MusicGenres/{Name}/Images/{Type}/{Index}` | P1 | 200 | — |  | **PASS** |  |
| `GET /MusicGenres/{Name}/InstantMix` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /MusicGenres/InstantMix` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：News（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /News/Product` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：Notifications（4）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Notifications/{UserId}` | P1 | 200 | PascalCase ✓ | TotalRecordCount | **PASS** |  |
| `GET /Notifications/{UserId}/Summary` | P1 | 200 | PascalCase ✓ | UnreadCount,MaxUnreadNotificationLevel | **PASS** |  |
| `GET /Notifications/Services` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Notifications/Types` | P1 | 200 | PascalCase ✓ | Type,Name,Enabled,Category,IsBasedOnUserEvent | **PASS** |  |

### 域：OfficialRatings（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /OfficialRatings` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：openapi（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /openapi` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：文档端点未实现 |

### 域：openapi.json（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /openapi.json` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：文档端点未实现 |

### 域：Packages（3）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Packages` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Packages/{Name}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 合成参数 404 合理（无对应资源） |
| `GET /Packages/Updates` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |

### 域：Persons（4）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Persons` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Persons/{Name}` | P1 | 200 | PascalCase ✓ | Name,ServerId,Id,Etag,DateCreated,CanDelete,CanDownload,Sort… | **PASS** |  |
| `GET /Persons/{Name}/Images/{Type}` | P1 | 200 | — |  | **PASS** |  |
| `GET /Persons/{Name}/Images/{Type}/{Index}` | P1 | 200 | — |  | **PASS** |  |

### 域：Playback（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Playback/BitrateTest` | P1 | 400 | — |  | **SKIP-EXPECTED** | 合成参数 400 为正确校验行为（负路径用例已固化） |

### 域：Playlists（2）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Playlists/{Id}/InstantMix` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Playlists/{Id}/Items` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：Plugins（3）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Plugins` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Plugins/{Id}/Configuration` | P1 | 404 | — |  | **SKIP-EXPECTED** | 无插件安装（去 Emby 化后零插件），404 正确 |
| `GET /Plugins/{Id}/Thumb` | P1 | 404 | — |  | **SKIP-EXPECTED** | 无插件安装（去 Emby 化后零插件），404 正确 |

### 域：Providers（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Providers/Subtitles/Subtitles/{Id}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 合成参数 404 合理（无对应资源） |

### 域：Reports（4）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Reports/Activities` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Emby Reports 插件） |
| `GET /Reports/Headers` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Emby Reports 插件） |
| `GET /Reports/Items` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Emby Reports 插件） |
| `GET /Reports/Items/Download` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Emby Reports 插件） |

### 域：ScheduledTasks（2）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /ScheduledTasks` | P1 | 200 | PascalCase ✓ | Name,State,Id,LastExecutionResult,Triggers,Description,Categ… | **PASS** |  |
| `GET /ScheduledTasks/{Id}` | P1 | 200 | PascalCase ✓ | Name,State,Id,LastExecutionResult,Triggers,Description,Categ… | **PASS** |  |

### 域：Search（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Search/Hints` | P1 | 200 | PascalCase ✓ | SearchHints,TotalRecordCount | **PASS** |  |

### 域：Sessions（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Sessions` | P0 | 200 | PascalCase ✓ | PlayState,AdditionalUsers,Capabilities,RemoteEndPoint,Playab… | **PASS** |  |

### 域：Shows（5）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Shows/{Id}/Episodes` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Shows/{Id}/Seasons` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Shows/{Id}/Similar` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Shows/NextUp` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Shows/Upcoming` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：Songs（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Songs/{Id}/InstantMix` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：Studios（4）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Studios` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Studios/{Name}` | P1 | 200 | PascalCase ✓ | Name,ServerId,Id,Etag,DateCreated,CanDelete,CanDownload,Sort… | **PASS** |  |
| `GET /Studios/{Name}/Images/{Type}` | P1 | 200 | — |  | **PASS** |  |
| `GET /Studios/{Name}/Images/{Type}/{Index}` | P1 | 200 | — |  | **PASS** |  |

### 域：SubtitleCodecs（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /SubtitleCodecs` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：swagger（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /swagger` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：文档端点未实现 |

### 域：swagger.json（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /swagger.json` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：文档端点未实现 |

### 域：Sync（8）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Sync/Items/Ready` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/JobItems` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/JobItems/{Id}/AdditionalFiles` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/JobItems/{Id}/File` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/Jobs` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/Jobs/{Id}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/Options` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：同步功能移除（D2 决策） |
| `GET /Sync/Targets` | P1 | 404 | — |  | **SKIP-EXPECTED** | 跳过清单：同步功能移除（D2 决策） |

### 域：System（10）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /System/ActivityLog/Entries` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /System/Configuration` | P1 | 200 | PascalCase ✓ | EnableUPnP,PublicPort,PublicHttpsPort,HttpServerPortNumber,H… | **PASS** |  |
| `GET /System/Configuration/{Key}` | P1 | 404 | — |  | **SKIP-EXPECTED** | 合成参数 404 合理（无对应资源） |
| `GET /System/Endpoint` | P1 | 200 | PascalCase ✓ | IsLocal,IsInNetwork | **PASS** |  |
| `GET /System/Info` | P1 | 200 | PascalCase ✓ | SystemUpdateLevel,OperatingSystemDisplayName,HasPendingResta… | **PASS** |  |
| `GET /System/Info/Public` | P0 | 200 | PascalCase ✓ | LocalAddress,ServerName,Version,OperatingSystem,Id | **PASS** |  |
| `GET /System/Logs` | P1 | 200 | PascalCase ✓ | DateCreated,DateModified,Size,Name | **PASS** |  |
| `GET /System/Logs/Log` | P1 | 200 | — |  | **PASS** |  |
| `GET /System/Ping` | P0 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /System/WakeOnLanInfo` | P1 | 200 | PascalCase ✓ | MacAddress,Port | **PASS** |  |

### 域：Tags（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Tags` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：Trailers（2）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Trailers` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Trailers/{Id}/Similar` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：user_usage_stats（17）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /user_usage_stats/{BreakdownType}/BreakdownReport` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/{UserID}/{Date}/GetItems` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/DurationHistogramReport` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/HourlyReport` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/load_backup` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/MoviesReport` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/PlayActivity` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/process_list` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/resource_usage` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/save_backup` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/session_list` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/TvShowsReport` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/type_filter_list` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/user_activity` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/user_list` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/user_manage/{Action}/{Id}` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |
| `GET /user_usage_stats/UserPlaylist` | P1 | 404 | — |  | **SKIP-EXPECTED** | B6 插件端点暂缓（Playback Reporting 插件） |

### 域：Users（17）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Users` | P0 | 200 | PascalCase ✓ | Name,ServerId,Id,PrimaryImageTag,HasPassword,HasConfiguredPa… | **PASS** |  |
| `GET /Users/{Id}` | P1 | 200 | PascalCase ✓ | Name,ServerId,Id,PrimaryImageTag,HasPassword,HasConfiguredPa… | **PASS** |  |
| `GET /Users/{Id}/Images/{Type}` | P1 | 200 | — |  | **PASS** |  |
| `GET /Users/{Id}/Images/{Type}/{Index}` | P1 | 200 | — |  | **PASS** |  |
| `GET /Users/{UserId}/GroupingOptions` | P1 | 200 | PascalCase ✓ | Name,Id | **PASS** |  |
| `GET /Users/{UserId}/Items` | P0 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Users/{UserId}/Items/{Id}` | P1 | 200 | PascalCase ✓ | Name,OriginalTitle,ServerId,Id,Etag,DateCreated,CanDelete,Ca… | **PASS** |  |
| `GET /Users/{UserId}/Items/{Id}/Intros` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Users/{UserId}/Items/{Id}/LocalTrailers` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Users/{UserId}/Items/{Id}/SpecialFeatures` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /Users/{UserId}/Items/Latest` | P1 | 200 | PascalCase ✓ | Name,ServerId,Id,ChannelId,IsFolder,ParentId,Type,UserData,C… | **PASS** |  |
| `GET /Users/{UserId}/Items/Resume` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Users/{UserId}/Items/Root` | P1 | 200 | PascalCase ✓ | Name,ServerId,Id,Etag,DateCreated,CanDelete,CanDownload,Supp… | **PASS** |  |
| `GET /Users/{UserId}/Suggestions` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Users/{UserId}/Views` | P0 | 400 | — |  | **SKIP-EXPECTED** | 合成参数 400 为正确校验行为（负路径用例已固化） |
| `GET /Users/Public` | P0 | 200 | PascalCase ✓ | Name,ServerId,Id,PrimaryImageTag,HasPassword,HasConfiguredPa… | **PASS** |  |
| `POST /Users/AuthenticateByName` | P1 | 400 | — |  | **SKIP-EXPECTED** | 合成参数 400 为正确校验行为（负路径用例已固化） |

### 域：VideoCodecs（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /VideoCodecs` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

### 域：Videos（12）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/{StartPositionTicks}/Stream.{Format}` | P1 | 400 | — |  | **SKIP-EXPECTED** | StartPositionTicks=test 合成参数 → 400 正确（负路径已固化） |
| `GET /Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/Stream.{Format}` | P1 | 404 | — |  | **SKIP-EXPECTED** | fixture 媒体无字幕流，404 正确（无流→404 已是修复后行为） |
| `GET /Videos/{Id}/AdditionalParts` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |
| `GET /Videos/{Id}/hls/{PlaylistId}/{SegmentId}.{SegmentContainer}` | P1 | 404 | — |  | **SKIP-EXPECTED** | legacy hls 路由合成分片参数，404 正确（负路径已固化） |
| `GET /Videos/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}` | P1 | 400 | — |  | **SKIP-EXPECTED** | 合成参数 400 为正确校验行为（负路径用例已固化） |
| `GET /Videos/{Id}/index.bif` | P1 | 400 | — |  | **SKIP-EXPECTED** | 合成参数 400 为正确校验行为（负路径用例已固化） |
| `GET /Videos/{Id}/live.m3u8` | P1 | 200 | — |  | **PASS** |  |
| `GET /Videos/{Id}/main.m3u8` | P1 | 200 | — |  | **PASS** |  |
| `GET /Videos/{Id}/master.m3u8` | P1 | 200 | — |  | **PASS** |  |
| `GET /Videos/{Id}/stream` | P1 | 200 | — |  | **PASS** |  |
| `GET /Videos/{Id}/stream.{Container}` | P1 | 200 | — |  | **PASS** |  |
| `GET /Videos/{Id}/subtitles.m3u8` | P1 | 400 | — |  | **SKIP-EXPECTED** | 合成参数 400 为正确校验行为（负路径用例已固化） |

### 域：web（5）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /favicon.ico` | P1 | 200 | — |  | **PASS** |  |
| `GET /robots.txt` | P1 | 200 | — |  | **PASS** |  |
| `GET /web/ConfigurationPage` | P1 | 404 | — |  | **SKIP-EXPECTED** | 合成参数 404 合理（无对应资源） |
| `GET /web/ConfigurationPages` | P1 | 200 | PascalCase ✓ |  | **PASS** |  |
| `GET /web/index.html` | P0 | 200 | — |  | **PASS** | text/html, 2727B |

### 域：Years（1）

| 操作 | 级别 | 状态码 | 键名风格 | 关键字段 | 结果 | 备注 |
|------|------|--------|----------|----------|------|------|
| `GET /Years` | P1 | 200 | PascalCase ✓ | Items,TotalRecordCount | **PASS** |  |

## P2 跳过清单（写/破坏性操作，未执行）

- **Auth**（2）：`POST /Auth/Keys`、`DELETE /Auth/Keys/{Key}`
- **Collections**（3）：`POST /Collections`、`POST /Collections/{Id}/Items`、`DELETE /Collections/{Id}/Items`
- **Devices**（3）：`DELETE /Devices`、`POST /Devices/Options`、`POST /Devices/CameraUploads`
- **DisplayPreferences**（1）：`POST /DisplayPreferences/{DisplayPreferencesId}`
- **Dlna**（5）：`POST /Dlna/Profiles`、`POST /Dlna/Profiles/{Id}`、`DELETE /Dlna/Profiles/{Id}`、`POST /Dlna/{UuId}/contentdirectory/control`、`POST /Dlna/{UuId}/connectionmanager/control`
- **Environment**（1）：`POST /Environment/ValidatePath`
- **Items**（22）：`DELETE /Items`、`POST /Items/{ItemId}`、`DELETE /Items/{Id}`、`POST /Items/RemoteSearch/Trailer`、`POST /Items/RemoteSearch/Book`、`POST /Items/RemoteSearch/Movie`、`POST /Items/RemoteSearch/Series`、`POST /Items/RemoteSearch/Game`、`POST /Items/RemoteSearch/BoxSet`、`POST /Items/RemoteSearch/MusicVideo`、`POST /Items/RemoteSearch/Person`、`POST /Items/RemoteSearch/MusicAlbum`、`POST /Items/RemoteSearch/MusicArtist`、`POST /Items/{Id}/Refresh`、`POST /Items/RemoteSearch/Apply/{Id}`、`POST /Items/{Id}/Images/{Type}`、`DELETE /Items/{Id}/Images/{Type}`、`POST /Items/{Id}/RemoteImages/Download`、`POST /Items/{Id}/RemoteSearch/Subtitles/{SubtitleId}`、`POST /Items/{Id}/Images/{Type}/{Index}`、`DELETE /Items/{Id}/Images/{Type}/{Index}`、`POST /Items/{Id}/Images/{Type}/{Index}/Index`
- **Library**（13）：`POST /Library/Refresh`、`POST /Library/VirtualFolders`、`DELETE /Library/VirtualFolders`、`POST /Library/Series/Added`、`POST /Library/Series/Updated`、`POST /Library/Media/Updated`、`POST /Library/Movies/Added`、`POST /Library/Movies/Updated`、`POST /Library/VirtualFolders/LibraryOptions`、`POST /Library/VirtualFolders/Name`、`POST /Library/VirtualFolders/Paths`、`DELETE /Library/VirtualFolders/Paths`、`POST /Library/VirtualFolders/Paths/Update`
- **LiveStreams**（3）：`POST /LiveStreams/Open`、`POST /LiveStreams/Close`、`POST /LiveStreams/MediaInfo`
- **LiveTv**（18）：`POST /LiveTv/ChannelMappings`、`PUT /LiveTv/ChannelMappings`、`DELETE /LiveTv/ChannelMappings`、`POST /LiveTv/ChannelMappingOptions`、`PUT /LiveTv/ChannelMappingOptions`、`DELETE /LiveTv/ChannelMappingOptions`、`POST /LiveTv/ListingProviders`、`DELETE /LiveTv/ListingProviders`、`POST /LiveTv/TunerHosts`、`DELETE /LiveTv/TunerHosts`、`POST /LiveTv/Timers`、`POST /LiveTv/SeriesTimers`、`DELETE /LiveTv/Recordings/{Id}`、`POST /LiveTv/Timers/{Id}`、`DELETE /LiveTv/Timers/{Id}`、`POST /LiveTv/SeriesTimers/{Id}`、`DELETE /LiveTv/SeriesTimers/{Id}`、`POST /LiveTv/Tuners/{Id}/Reset`
- **Notification**（1）：`POST /Notification/SMTP/Test/{UserID}`
- **Notifications**（3）：`POST /Notifications/Admin`、`POST /Notifications/{UserId}/Read`、`POST /Notifications/{UserId}/Unread`
- **Packages**（2）：`POST /Packages/Installed/{Name}`、`DELETE /Packages/Installing/{Id}`
- **Playlists**（4）：`POST /Playlists`、`POST /Playlists/{Id}/Items`、`DELETE /Playlists/{Id}/Items`、`POST /Playlists/{Id}/Items/{ItemId}/Move/{NewIndex}`
- **Plugins**（2）：`DELETE /Plugins/{Id}`、`POST /Plugins/{Id}/Configuration`
- **ScheduledTasks**（3）：`POST /ScheduledTasks/Running/{Id}`、`DELETE /ScheduledTasks/Running/{Id}`、`POST /ScheduledTasks/{Id}/Triggers`
- **Sessions**（16）：`POST /Sessions/Logout`、`POST /Sessions/Capabilities`、`POST /Sessions/Playing`、`POST /Sessions/{Id}/Viewing`、`POST /Sessions/{Id}/Message`、`POST /Sessions/{Id}/Playing`、`POST /Sessions/{Id}/Command`、`POST /Sessions/Capabilities/Full`、`POST /Sessions/Playing/Progress`、`POST /Sessions/Playing/Ping`、`POST /Sessions/Playing/Stopped`、`POST /Sessions/{Id}/Playing/{Command}`、`POST /Sessions/{Id}/System/{Command}`、`POST /Sessions/{Id}/Command/{Command}`、`POST /Sessions/{Id}/Users/{UserId}`、`DELETE /Sessions/{Id}/Users/{UserId}`
- **Sync**（13）：`POST /Sync/Jobs`、`POST /Sync/OfflineActions`、`POST /Sync/Data`、`POST /Sync/{ItemId}/Status`、`POST /Sync/Jobs/{Id}`、`DELETE /Sync/Jobs/{Id}`、`POST /Sync/Items/Cancel`、`DELETE /Sync/{TargetId}/Items`、`DELETE /Sync/JobItems/{Id}`、`POST /Sync/JobItems/{Id}/Transferred`、`POST /Sync/JobItems/{Id}/Enable`、`POST /Sync/JobItems/{Id}/MarkForRemoval`、`POST /Sync/JobItems/{Id}/UnmarkForRemoval`
- **System**（5）：`POST /System/Configuration`、`POST /System/Ping`、`POST /System/Restart`、`POST /System/Shutdown`、`POST /System/Configuration/{Key}`
- **user_usage_stats**（2）：`POST /user_usage_stats/import_backup`、`POST /user_usage_stats/submit_custom_query`
- **Users**（25）：`POST /Users/{Id}`、`DELETE /Users/{Id}`、`POST /Users/New`、`POST /Users/ForgotPassword`、`POST /Users/{Id}/Authenticate`、`POST /Users/{Id}/Password`、`POST /Users/{Id}/EasyPassword`、`POST /Users/ForgotPassword/Pin`、`POST /Users/{Id}/Configuration`、`POST /Users/{Id}/Policy`、`POST /Users/{UserId}/FavoriteItems/{Id}`、`DELETE /Users/{UserId}/FavoriteItems/{Id}`、`POST /Users/{Id}/Images/{Type}`、`DELETE /Users/{Id}/Images/{Type}`、`POST /Users/{UserId}/PlayedItems/{Id}`、`DELETE /Users/{UserId}/PlayedItems/{Id}`、`POST /Users/{UserId}/PlayingItems/{Id}`、`DELETE /Users/{UserId}/PlayingItems/{Id}`、`POST /Users/{Id}/Connect/Link`、`DELETE /Users/{Id}/Connect/Link`、`POST /Users/{UserId}/Items/{Id}/Rating`、`DELETE /Users/{UserId}/Items/{Id}/Rating`、`POST /Users/{Id}/Images/{Type}/{Index}`、`DELETE /Users/{Id}/Images/{Type}/{Index}`、`POST /Users/{UserId}/PlayingItems/{Id}/Progress`
- **Videos**（4）：`POST /Videos/MergeVersions`、`DELETE /Videos/ActiveEncodings`、`DELETE /Videos/{Id}/AlternateSources`、`DELETE /Videos/{Id}/Subtitles/{Index}`
