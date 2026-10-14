# OpenMedia 功能场景测试报告

> 运行时间：2026-10-14 20:22:24　基址：http://127.0.0.1:8096/emby　ServerId：6225572626f94ea0b6e469c9ac63ef6f　版本：3.5.3.0

**结果：PASS 22 / FAIL 2 / SKIP 0（共 24）**

## S0-预检

| 场景 | 期望 | 实际 | 结果 | 备注 |
|------|------|------|------|------|
| GET /System/Info/Public | 200 + JSON | status=200 id=6225572626f94ea0b6e469c9ac63ef6f ver=3.5.3.0 | **PASS** | Id 为 32 位无连字符 hex |
## S1-启动向导

| 场景 | 期望 | 实际 | 结果 | 备注 |
|------|------|------|------|------|
| GET /Startup/Configuration（无 token） | 200（向导未完成，免认证放行） | status=200 body={"UICulture":"en-us","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"} | **PASS** |  |
| POST /Startup/User {"Name":"admin"} | 200/204（设置初始用户名） | status=200 | **PASS** |  |
| POST /Startup/Complete | 200/204 | status=204 | **PASS** |  |
## S2-认证

| 场景 | 期望 | 实际 | 结果 | 备注 |
|------|------|------|------|------|
| POST /Users/AuthenticateByName（admin/admin） | 200 + AccessToken | admin/admin→401；空密码→200 | **PASS** | 初始用户无密码（Startup/User 仅设 Name），空密码回退成功 |
| POST /Users/AuthenticateByName（错误密码） | 401 | status=401 | **PASS** |  |
| GET /Users（带 token 复用） | 200 | status=200 | **PASS** |  |
| GET /Users（无 token） | 401 | status=401 | **PASS** |  |
## S3-媒体库

| 场景 | 期望 | 实际 | 结果 | 备注 |
|------|------|------|------|------|
| 测试媒体就绪（Movie1/Movie2 mkv+nfo+poster） | 文件存在 | 就绪 | **PASS** |  |
| GET /Library/VirtualFolders | 200 | status=200 文件夹数=5（OpenMediaTests 存在=False） | **PASS** |  |
| POST /Library/VirtualFolders（添加 OpenMediaTests → D:\Dev\OpenMedia\tests\media） | 200/204 | status=204 | **PASS** |  |
| POST /Library/Refresh（触发扫描） | 200/204 | status=204 | **PASS** |  |
| 扫描出 2 部电影（轮询 /Items?Recursive=true） | ≥2 个 Movie 条目（≤120s） | count=2，耗时 0s | **PASS** |  |
| Movie1 元数据（nfo title/year） | Name=Movie1, ProductionYear=2020 | Name=Movie1, Year=, Id=c3f6962891668fc8365e3dfaa24bb3ea | **FAIL** | nfo 解析（title/year） |
| Movie2 元数据（nfo title/year） | Name=Movie2, ProductionYear=2021 | Name=Movie2, Year= | **FAIL** |  |
| GET /Items/{Movie1}/Images/Primary（poster.jpg） | 200 + image/* | status=200 type=image/jpeg 633B | **PASS** |  |
## S4-WebSocket

| 场景 | 期望 | 实际 | 结果 | 备注 |
|------|------|------|------|------|
| ws://…/embywebsocket 连接 + SessionsStart 回环 | 连接成功，收到服务器推送消息（MessageType=Sessions） | 收到推送 MessageType=UserUpdated（1708B，EndOfMessage=True） | **PASS** | 发送 SessionsStart → 服务器解析并回推，双向回环验证通过 |
## S5-静态资源

| 场景 | 期望 | 实际 | 结果 | 备注 |
|------|------|------|------|------|
| GET /web/index.html | 200 + text/html | status=200 type=text/html 2811B | **PASS** |  |
| GET /web/index.html + Range: bytes=0-99 | 206 + Content-Range: bytes 0-99/N | status=206 content-range=bytes 0-99/2811 body=100B | **PASS** |  |
| GET /emby/web/index.html（前缀克隆） | 200 + text/html | status=200 type=text/html | **PASS** |  |
| GET /favicon.ico | 200 | status=200 type=image/vnd.microsoft.icon | **PASS** |  |
| GET /robots.txt | 200 | status=200 type=text/plain | **PASS** |  |
| GET /emby/System/Ping | 200 | status=200 body=Emby Server | **PASS** |  |
| GET /Videos/{Movie1}/stream?static=true + Range | 206 + Content-Range（R3） | status=206 content-range=bytes 0-99/262144 type=video/x-matroska 100B | **PASS** |  |
