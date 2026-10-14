# OpenMedia JSON 契约抽查报告

> 运行时间：2026-10-14 20:21:50　基址：http://127.0.0.1:8096/emby

**结果：PASS 16 / FAIL 2 / SKIP 0（共 18）**

| 端点 | 契约规则 | 实际 | 结果 | 详情 |
|------|----------|------|------|------|
| `/System/Info/Public` | HTTP 200 + JSON | status=200 | **PASS** |  |
| `/System/Info/Public` | 键名 PascalCase | 全部 PascalCase（LocalAddress,ServerName,Version,OperatingSystem,Id） | **PASS** |  |
| `/System/Info/Public` | Id = 32 位无连字符 hex（Guid N 格式） | Id=6225572626f94ea0b6e469c9ac63ef6f | **PASS** |  |
| `/System/Info/Public` | 关键字段 ServerName | 存在（DESKTOP-5CAN3E7） | **PASS** |  |
| `/System/Info/Public` | 关键字段 Version | 存在（3.5.3.0） | **PASS** |  |
| `/System/Info/Public` | 关键字段 OperatingSystem | 存在（Windows） | **PASS** |  |
| `/Users` | HTTP 200 + 数组 | status=200 | **FAIL** |  |
| `/Items` | HTTP 200 + QueryResult{Items,TotalRecordCount} | Items=4 TotalRecordCount=4 | **PASS** |  |
| `/Items` | Type 为枚举字符串（非数字） | 前 5 个 Type: Folder,Movie,Movie,PlaylistsFolder | **PASS** |  |
| `/Items` | 无 null 值字段（序列化应省略 null） | null 字段: ChannelId,ParentId,SeriesId,SeasonId,AlbumId,ChannelId,ParentId,SeriesId,SeasonId,AlbumId,ChannelId,ParentId,SeriesId,SeasonId,AlbumId,ChannelId,ParentId,SeriesId,SeasonId,AlbumId | **FAIL** |  |
| `/Items` | 键名 PascalCase（首元素） | 全部 PascalCase | **PASS** |  |
| `/Items` | 关键字段 Id | 存在 | **PASS** |  |
| `/Items` | 关键字段 Name | 存在 | **PASS** |  |
| `/Items` | 关键字段 Type | 存在 | **PASS** |  |
| `/Items` | Item.Id = Guid N 格式（32 hex） | 全部合规 | **PASS** |  |
| `/System/Info` | HTTP 200 + JSON | status=200 | **PASS** |  |
| `/System/Info` | 键名 PascalCase | 全部 PascalCase | **PASS** |  |
| `/System/Info` | Id = Guid N 格式 | Id=6225572626f94ea0b6e469c9ac63ef6f | **PASS** |  |
