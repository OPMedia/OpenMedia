# OpenMedia JSON 契约抽查报告

> 运行时间：2026-10-20 13:35:51　基址：http://localhost:8096/emby

**结果：PASS 20 / FAIL 1 / SKIP 0（共 21）**

| 端点 | 契约规则 | 实际 | 结果 | 详情 |
|------|----------|------|------|------|
| `/System/Info/Public` | HTTP 200 + JSON | status=200 | **PASS** |  |
| `/System/Info/Public` | 键名 PascalCase | 全部 PascalCase（LocalAddress,ServerName,Version,OperatingSystem,Id） | **PASS** |  |
| `/System/Info/Public` | Id = 32 位无连字符 hex（Guid N 格式） | Id=fd14d7a30f4d4ac1be068b8f9d9acdc8 | **PASS** |  |
| `/System/Info/Public` | 关键字段 ServerName | 存在（DESKTOP-B0SMJI3） | **PASS** |  |
| `/System/Info/Public` | 关键字段 Version | 存在（3.5.3.0） | **PASS** |  |
| `/System/Info/Public` | 关键字段 OperatingSystem | 存在（Windows） | **PASS** |  |
| `/Users` | HTTP 200 + 数组 | status=200 count=1 | **PASS** |  |
| `/Users` | 键名 PascalCase（首元素） | 全部 PascalCase | **PASS** |  |
| `/Users` | User.Id = Guid N 格式（32 hex） | Id=c07f0f0b79934559a70dbb9ecf8f946b | **PASS** |  |
| `/Users` | 关键字段 Name | 存在（admin） | **PASS** |  |
| `/Items` | HTTP 200 + QueryResult{Items,TotalRecordCount} | Items=20 TotalRecordCount=28 | **PASS** |  |
| `/Items` | Type 为枚举字符串（非数字） | 前 5 个 Type: Folder,Folder,Folder,Folder,Folder | **PASS** |  |
| `/Items` | 无 null 值字段（序列化应省略 null） | null 字段: ChannelId,ParentId,SeriesId,SeasonId,AlbumId,ChannelId,ParentId,SeriesId,SeasonId,AlbumId,ChannelId,ParentId,SeriesId,SeasonId,AlbumId,ChannelId,ParentId,SeriesId,SeasonId,AlbumId,ChannelId,ParentId,SeriesId,SeasonId,AlbumId,ChannelId,ParentId,SeriesId,SeasonId,AlbumId,ChannelId,ParentId,SeriesId,SeasonId,AlbumId,ChannelId,ParentId,SeriesId,SeasonId,AlbumId,ChannelId,ParentId,SeriesId,SeasonId,AlbumId,ChannelId,ParentId,SeasonId,AlbumId,ChannelId,ParentId,SeasonId,AlbumId,ChannelId,ParentId,AlbumId,ChannelId,ParentId,AlbumId,ChannelId,ParentId,AlbumId,ChannelId,ParentId,AlbumId,ChannelId,ParentId,SeriesId,SeasonId,ChannelId,ParentId,SeriesId,SeasonId,ChannelId,ParentId,SeriesId,SeasonId,ChannelId,ParentId,SeriesId,SeasonId,ChannelId,ParentId,SeriesId,SeasonId | **FAIL** |  |
| `/Items` | 键名 PascalCase（首元素） | 全部 PascalCase | **PASS** |  |
| `/Items` | 关键字段 Id | 存在 | **PASS** |  |
| `/Items` | 关键字段 Name | 存在 | **PASS** |  |
| `/Items` | 关键字段 Type | 存在 | **PASS** |  |
| `/Items` | Item.Id = Guid N 格式（32 hex） | 全部合规 | **PASS** |  |
| `/System/Info` | HTTP 200 + JSON | status=200 | **PASS** |  |
| `/System/Info` | 键名 PascalCase | 全部 PascalCase | **PASS** |  |
| `/System/Info` | Id = Guid N 格式 | Id=fd14d7a30f4d4ac1be068b8f9d9acdc8 | **PASS** |  |
