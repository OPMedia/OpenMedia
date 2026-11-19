# 历史包袱 → 现有依赖

> 起因：用户要求"深入分析整个代码库的 Git 历史、关键 PR 和架构演变过程，找出哪些设计是有意为之、哪些是历史遗留、哪些是为了解决已经不存在的问题而引入的，识别可以安全移除的历史包袱。现在有很多代码都是历史遗留，我想换成现有的依赖。"

**底盘**：217,002 行 C#，1,532 个 `.cs`。

**前情**：`docs/review/2026-11-19-git-archaeology.md` 已经做过历史考古（时间线、ADR 恢复、`#if` 普查）。上一轮"包袱 agent"推翻了 5 个候选中的 4 个（SharpCifs / BDInfo / Mono.Nat / TheTVDB 都还活着）。**本轮问题变了**：不是"能不能删"，而是"能不能换成维护中的依赖"。结论因此完全不同——BDInfo 和 Mono.Nat 确实还活着，但 BDInfo 在 NuGet 上已经有可用的包。

---

## 0. 已经做掉的

| 项 | 证据 | 结果 |
|---|---|---|
| `ThirdParty/7zip/7za.exe`（576K） | 全仓库源码零引用；压缩走 `SharpCompress.Archives.SevenZip`（`Archiving/ZipClient.cs:4`）。还是个 Windows 二进制，Linux 上跑不了 | 删除，`84a0c72eb2` |
| `ThirdParty/emby/Emby.Server.MediaEncoding.dll`（252K） | 源码项目 `src/Emby.Server.MediaEncoding` 已在仓库里，这份是反编译前的原始产物；csproj 无 HintPath 指向它 | 删除 |
| `ThirdParty/taglib/TagLib.Portable.dll`（408K） | 唯一提及是 `Emby.Photos/StreamFileAbstraction.cs:8` 的注释；实际用 NuGet `TagLibSharp 2.3.0` | 删除 |
| `IO/SharpCifs/`（38,022 行，SMB1 只协商 `NT LM 0.12`） | 现代 Windows/NAS 默认禁用 SMB1 | 换成 `SMBLibrary 1.5.8.1`，1,098 行 |

---

## 1. 内嵌第三方源码 → 换包（可执行）

| # | 目录 | 行数 | 上游 / 许可 | 换成 | 调用点 | 净删 | 风险 |
|---|---|---:|---|---|---:|---:|---|
| 1 | `Networking/IPNetwork/` | 2,549 | lduchosal/IPNetwork, BSD-2 | **BCL `System.Net.IPNetwork`**（.NET 8+，**免费**） | **1** | 3,087¹ | 低 |
| 2 | `BDInfo/` | 6,802 | Cinema Squid, LGPL-2.1+ | `BDInfo 0.8.0`（LGPL-2.1, net6.0, 2024-03） | 1 文件 | 6,802 | 低 |
| 3 | `TextEncoding/UniversalDetector/` | 9,093 | Mozilla universalchardet（Rudi Pettazzi 的 Ude 移植）, MPL-1.1/GPL-2.0/LGPL-2.1 | `UTF.Unknown 2.8.0`（MPL-1.1, **原生 net10.0**, 2026-10） | 1 方法 | 9,093 | 低 |
| 4 | `TextEncoding/NLangDetect/` | 1,938 + **2.1MB / 53 个 profile** | langdetect(Java) 的 C# 移植 | `NTextCat 0.3.65`（MIT）或**直接删** | 2 | 1,938 | 中（产品取舍） |
| 5 | `DvdLib/` | 701 | DVD IFO 解析，上游/许可未确认 | ❌ **NuGet 上没有** | 2 文件 | 741 | 中（产品取舍） |
| 6 | `RSSDP/` | 3,473 | Shane Ryan, MIT | `Rssdp 5.0.0` 有包，**但 API 不够** | 2 文件 | — | **保留** |
| 7 | `Mono.Nat/` | 1,602 | Alan McGovern, MIT | `Mono.Nat 3.0.4` 有包，**但 API 不够** | 1 文件 | — | **保留** |
| 8 | `TextEncoding/TextEncodingDetect.cs` | 409 | AutoIt text-encoding-detect, Apache-2.0 | ❌ **无替代品**（UTF-16 无 BOM 只有它能判） | 1 | — | **保留** |

¹ 含 `MediaBrowser.Tests/Networking/IpNetworkEquivalenceTests.cs` 的 538 行。

### 为什么 RSSDP / Mono.Nat 换不了（这是本轮最重要的反直觉结论）

两者都是 MIT、都有维护中的包、都"看起来能换"。**但本仓库的 fork 已经被改得比上游多了**：

**RSSDP**（`Rssdp 5.0.0`）：
- 上游 `SsdpCommunicationsServer(ISocketFactory, int, int)` —— 仓库版多了 `INetworkManager` 和 **`enableMultiSocketBinding`**（Windows 多网卡同时绑定）
- 上游没有 `LogFunction`，只有 `ISsdpLogger`
- 上游 `SsdpDeviceLocator` 没有 `RestartBroadcastTimer(dueTime, interval)` → **`DlnaConfiguration.ClientDiscoveryIntervalSeconds` 会失效**
- 上游 `ISocketFactory` 签名完全不同 → `Net/SocketFactory.cs` 278 行要重写
- 换包 = 2 处功能回退 + 278 行重写，只省 3,473 行。不划算。

**Mono.Nat**（`Mono.Nat 3.0.4`）：
- 仓库版 `INatDevice` 有 `LocalAddress`（`ExternalPortForwarding.cs:268,297` 在用）—— 上游**没有这个成员**，换包编译不过
- 仓库的 `NatManager` 是**自创类**，不在上游；它复用 DLNA SSDP 已发现的设备 + 共用 `IHttpClient` 连接池（`ExternalPortForwarding.cs:113,150,65`）
- 上游 3.0.4 是静态 `NatUtility.StartDiscovery()`，自己发 SSDP、自己开 socket 做 SOAP → 要放弃"复用发现总线"，`ExternalPortForwarding.cs` 345 行全重写
- `Mapping.Description` 从属性变成构造函数参数
- 换包 = 功能降级 + 重写 345 行，只省 1,602 行。不划算。

---

## 2. 自己造轮子 → BCL / 共享框架（无新依赖）

| # | 文件:行 | 行数 | 现在干什么 | 换成 | 调用点 |
|---|---|---:|---|---|---:|
| 9 | `MediaBrowser.Providers/Manager/GenericPriorityQueue.cs:16` + 4 个同族文件 | 775 | BlueRaja High-Speed-Priority-Queue 的整份拷贝 | BCL `PriorityQueue<T,TPriority>`（仓库自己已在 `PgItemRepository.cs:365` 用了） | 1 |
| 10 | `src/MediaBrowser.Model/.../MyHttpUtility.cs:9` | 458（**280 是死码**） | Mono HttpUtility 移植：253 条 HTML 实体存成**魔数 long 数组**（`4703284585813770240L`） | `QueryHelpers.ParseQuery`（共享框架内）+ `WebUtility.HtmlDecode` | 5 |
| 11 | `src/MediaBrowser.Model/.../MimeTypes.cs:9` | 256 | 手写 ext↔MIME 表（82 条） | `FileExtensionContentTypeProvider` | 35 |
| 12 | `Library/ItemLruCache.cs:15` | 154 | 手写 LRU（Dictionary+LinkedList 单锁） | `Microsoft.Extensions.Caching.Memory` | 1 |
| 13 | `SortExtensions.cs:10` 内嵌私有 `AlphanumComparator` | ~99 | 与 `Sorting/AlphanumComparator.cs:9` **同源重复实现** | 复用已有那份 | 8 |
| 14 | `IO/StreamHelper.cs:69,90` | ~40 | 死重载 | 删 | **0** |
| 15 | `Networking/NetworkManager.cs:173-219,284` | ~50 | **字符串前缀**判私网：`StartsWith("172.16.")` → `"172.16.evil.com"` 会被误判 | `IPNetwork.Parse("172.16.0.0/12").Contains(ip)` | 内部 |
| 16 | `Playlists/PlaylistManager.cs:461` | 24 | `Uri.MakeRelativeUri` 手工算相对路径 | `Path.GetRelativePath` | 1 |
| 17 | `HttpClientManager.cs:695` | 16 | 手写 host 提取（`IndexOf("://")` → `Substring` → `Split('/')`），userinfo/端口/IPv6 全错 | `new Uri(url).Host` | 2 |
| 18 | `BaseApiService.cs:353` + `DlnaServerService.cs:236` | 30×2 | 逐字节重复的手写 URL 拆解 | `Request.Path` / `Uri.AbsolutePath` | 各 1 |
| 19 | `DtoService.cs:491`、`UserManager.cs:866`、`HdHomerunManager.cs:156`、`MusicBrainzAlbumProvider.cs:738` | 4 | `new Random()` | `Random.Shared` | 4 |
| 20 | `MusicBrainzAlbumProvider.cs:822`、`MovieDbProvider.cs:413-432` | ~60 | 手写指数退避 + 手写节流；**`static long _lastRequestTicks` 读写无同步** | `Polly` / `Microsoft.Extensions.Http.Resilience` | 2 |

**#9 的坑**：vendored 队列是**稳定队列**（`GenericPriorityQueue.cs:224` 用 `InsertionIndex` 做 FIFO 平手），BCL 版**不稳定**——同优先级元数据刷新顺序会变。要用 `PriorityQueue<T,(TPriority, long seq)>` 包一层保序。

**#19 是真 bug**：`UserManager.cs:866` 用 `new Random()` 生成设备 ID 后缀，同毫秒播种会撞。

---

## 3. 死代码（直接删，~630 行）

| # | 项 | 行数 | 证据 |
|---|---|---:|---|
| 21 | `Security/EncryptionManager.cs` + `IEncryptionManager` | 59 | 名字叫 Encrypt/Decrypt，实际就是 Base64（`:37` 注释自认 "this isn't good"）。全仓唯一提及是 `ApplicationHost.cs:1012` 的注册，**从未被 Resolve**。留着比删掉危险 |
| 22 | 3 份 `Replace(old,new,StringComparison)` 扩展方法 | ~72 | C# 实例方法优先于扩展方法，`string` 从 .NET Core 2.1 起就有这个实例方法 → **从未被调用**。已用 net10.0 小工程实测确认绑定到 BCL |
| 23 | `MyHttpUtility.UrlDecode` / `HtmlDecode` / 253 条实体表 | ~280 | 两者全仓 0 调用 |
| 24 | `IO/StreamHelper.cs:69,90` | ~40 | 0 调用 |
| 25 | `LocalizablePluginHelper.cs` | 12 | `.GetDictionary(` 全仓 0 调用（同名命中都是接口和私有方法） |
| 26 | `IPNetworkCollection` / `IPAddressCollection` / `BigIntegerExt` | 415 | `IPNetwork/` 目录外零引用 |

---

## 4. 顺带挖出来的真 bug（与"换依赖"无关，但必须记）

| # | 位置 | 问题 |
|---|---|---|
| A | `TextEncoding/TextEncoding.cs:62-83` | **UTF-16 全被当 UTF-32 解**：`Utf16BeBom`/`Utf16LeBom`/`Utf16LeNoBom`/`Utf16BeNoBom` 四个 case 全 `return Encoding.UTF32`。`:34` BOM 判定也反了（`0xFE 0xFF` 是 BE，却 `return Encoding.Unicode` = LE）。UTF-16 字幕读出来是乱码 + 大量 NUL |
| B | 全仓库 | **`CodePagesEncodingProvider` 从未注册**（`grep CodePagesEncodingProvider\|Encoding.RegisterProvider` = 0 命中）。net10.0 下 `Encoding.GetEncoding("windows-1251")` 直接抛 `ArgumentException`。而 `TextEncoding.GetEncodingFromCharset()` 正是把检测结果直接丢给 `Encoding.GetEncoding()`，调用链 `SubtitleEncoder.cs:167` 是真实路径 → **非 UTF-8 字幕读不出来**。这条要单独验证 |
| C | `DefaultAuthenticationProvider.cs:101` | **用户密码 = 无盐 SHA1**（`:94` 挂着注释掉的 BCrypt）。换 `BCrypt.Net-Next` 需要 `user.Salt` 版本化 + 登录时惰性 rehash，属独立安全工单 |
| D | `MovieDbProvider.cs:413` | `static long _lastRequestTicks` 读写无任何同步 → 并发下节流失效 |
| E | `NetworkManager.cs:173-219` | 字符串前缀判私网，`"172.16.evil.com"` 会被判成内网 |

---

## 5. 许可证账本（GPLv2 项目）

| 包 | 许可 | 与 GPLv2 | 判断 |
|---|---|---|---|
| `BDInfo 0.8.0` | LGPL-2.1-only | ✅ | 可用 |
| `UTF.Unknown 2.8.0` | MPL-1.1 | ⚠️ FSF 认为不兼容 | **Jellyfin（同为 GPLv2）在生产里用了它**，有先例但不干净 |
| `Ude.NetStandard 1.2.0` | MPL-1.1 / GPL-2.0 / LGPL-2.1 三选 | ✅（走 GPL-2.0） | 许可干净，但 2019 年后没更新，TFM 只到 netstandard2.0 |
| `NTextCat 0.3.65` | MIT | ✅ | 可用 |
| `LanguageDetection.NETStandard 1.3.1` | Apache-2.0 | ⚠️ 与 GPLv2 不兼容 | 不用 |
| `Rssdp 5.0.0` / `Mono.Nat 3.0.4` | MIT | ✅ | 但 API 不够 |

---

## 6. 执行批次

**第一批（零产品决策，纯净收益）—— 已派发**
- `refactor/ipnetwork`：IPNetwork → BCL，2,549 行 + 538 行测试，顺带修 #15
- `refactor/bdinfo`：BDInfo → `BDInfo 0.8.0` + ~180 行 IO 适配，6,802 行
- `refactor/deadcode`：#21/#22/#23/#24/#25/#19，约 600 行死码 + 4 处 `Random.Shared`

**第二批（需拍板）**
- `TextEncoding/UniversalDetector` → `UTF.Unknown`（9,093 行），顺带修 bug A/B
- `NLangDetect` → 删（Jellyfin 先例）还是换 `NTextCat`
- `DvdLib` → 删（DVD 主标题判定退化为按大小猜）还是留

**第三批（收益中等，风险中）**
- `PriorityQueue` → BCL（775 行，需保稳定序）
- `ItemLruCache` → `MemoryCache`（154 行，会丢命中率统计）
- `MimeTypes` → `FileExtensionContentTypeProvider`（256 行，但 ASP.NET 表缺 `.dvr-ms`/`.wtv`/`.mk3d` 等，需逐条比对）
- 手写重试 → `Polly`（#20，顺带修 bug D）

**明确不动**
- `RSSDP`、`Mono.Nat`（换包是功能降级）
- `Emby.Server.Implementations/Sorting/` 的 27 个 `IBaseItemComparer`（**DI 反射注册**，静态 grep 全是假阳性）
- `IHttpClient` / `HttpClientManager`（805 行，是 DI 契约封装，不是 HTTP 客户端重写）
- 自有 `ILogger`（423 行实现，但 `_logger.`/`Logger.` 调用点 **783 处**，换 `Microsoft.Extensions.Logging` 是高风险噪音）
- `Emby.Server.HttpServer/Routing/RestPath.cs`（571 行手写路由模板引擎，换 ASP.NET Core `RouteTemplate` 是全架构重写）
- `SimpleInjector` 容器（`GetExports<T>()` 程序集扫描改写成本高）
- `PathExtensions.GetAttributeValue`（名字像路径工具，实为文件名 `[tmdbid=123]` 解析，是业务逻辑）

---

## 7. 已经"干净"的部分（免得下次重复怀疑）

| 类别 | 结论 |
|---|---|
| JSON | 全 `System.Text.Json`（`Serialization/JsonSerializer.cs:122,199`）；自定义 `JsonConverter` 是**必要的宽容解析**（Emby 客户端契约），不是重造轮子。手写 JSON 拼接 **0 处** |
| HTTP | `WebRequest`/`HttpWebRequest`/`WebClient`/`ServicePointManager` **0 命中**（只剩注释） |
| 加密 | `CryptographyProvider.cs:18,26,34` 是 `SHA1.Create()`/`MD5.Create()` 一行包装，正常 BCL 用法 |
| 集合 | 无手写 `ObjectPool`、无 `ConditionalWeakTable`、无 `ConcurrentDictionary` 包装类。真缺口是 **`ArrayPool` 0 处使用** |
| 日期 | Unix 时间转换 **0 处手写** |
| 并发 | 无手写线程池/定时器；`Thread.Sleep` 只有 2 处 |
| 字符串 | Base64Url **0 处手写** |

---

## 8. 第一批执行结果（已完成）

| 分支 | 改动 | 净删 | 测试 |
|---|---|---:|---|
| `refactor/ipnetwork` | `IPNetwork/` 5 文件删除；`NetworkManager` 改用 BCL + `TryParse` 防护；私网判定 CIDR 化 | **−3,184** | 112 → 145 |
| `refactor/bdinfo` | `BDInfo/` 21 文件删除；新增 156 行 IO 适配 + 91 行测试 | **−6,869** | 112 → 114 |
| `refactor/deadcode` | `EncryptionManager`、`MyHttpUtility`、`LocalizablePluginHelper` 删除；`Random.Shared` | **−591** | 112 |
| `fix(subtitles)` | 编码 provider 注册 + UTF-16 修复 | +~40 | 112 → **124** |

合并后：**0 警告 0 错误，159 个测试全过**，`/app/` 与带查询串的 API 端点实测 200。

本次总计：217,002 → **207,303 行**（−9,699），1,532 → 1,510 个 `.cs`。

### 三处被 agent 推翻的判断（记录在案，避免以后再犯）

调研报告基于静态 grep，有 3 条是假阳性，实施时被逐一推翻：

| 报告的判断 | 实际情况 | 证据 |
|---|---|---|
| 3 份 `Replace` 扩展方法全是死码 | 只有 1 份是（`Emby.Naming.Extensions/`）。`Emby.Naming/StringExtensions` 被 `SeasonPathParser.cs:60` **静态调用**；`StringHelper.Replace` 有 **12 处**静态调用 | 静态调用 `StringExtensions.Replace(...)` 不经过实例方法绑定 |
| `StreamHelper.cs:69` 是死重载 | 活的，`ProgressiveFileCopier.cs:95` 通过注入的 `_streamHelper` 字段调用 | 调用方是字段注入，不是直接方法名 |
| `MediaBrowser.Controller/StringExtensions.cs` 存在 | 该路径不存在；实际是 `MediaBrowser.Controller.Extensions.StringExtensions`，只有活着的 `RemoveDiacritics` | — |

**教训**：`扩展方法` 和 `字段注入` 这两类调用，静态 grep 按方法名搜会漏。判定死代码必须同时查 `类名.方法名(`、接口实现、字段类型。

### 其他被推翻/修正的

- `UserManager.cs:866` 的 `new Random()` 不是设备 ID 后缀，是**密码重置 PIN** —— 碰撞缺陷成立，结论不变
- BDInfo 0.8.0 的 API 一开始被误判为"被砍了"（反射只看属性），补查 **public 字段**后确认全覆盖
- `Mono.Nat` 最新版是 **3.0.4**，不是调研里猜的 10.x

### 仍然待办

- 第二批：`UTF.Unknown`（9,093 行 + 2.1MB）、`NLangDetect` 去留、`DvdLib` 去留 —— 需要产品/法务拍板
- 第三批：`PriorityQueue`（775）、`ItemLruCache`（154）、`MimeTypes`（256）、Polly
- 独立工单：无盐 SHA1 密码（C）、`MovieDbProvider` 节流竞态（D）、`FilterIpAddress` 里残留的 `StartsWith("169.")`

---

## 9. 第二批：按 Jellyfin 的决定执行（已完成）

用户拍板「参考 Jellyfin」。去他们仓库实测核对过，不是凭印象：

| 事实 | 证据 |
|---|---|
| Jellyfin 用 `UTF.Unknown 2.7.0` | `Directory.Packages.props` |
| Jellyfin 没有 `Emby.Server.Implementations/TextEncoding/` 目录 | GitHub contents API 返回 404 |
| Jellyfin 没有 DvdLib | 顶层目录列表无 `Dvd*` |
| Jellyfin 的 DVD 走 `GetPrimaryPlaylistVobFiles(path, null)` + ffprobe + 累加运行时 | `MediaBrowser.Providers/MediaInfo/FFProbeVideoInfo.cs:89-111` |

### 执行结果

| 项 | 前 | 后 | 净删 |
|---|---|---|---:|
| `UniversalDetector/`（字符集检测） | 9,093 行 / 33 文件 | `UTF.Unknown 2.8.0` | −9,093 |
| `NLangDetect/`（语种检测） | 1,938 行 + 53 个 profile（2.1MB） | 删（Jellyfin 也没有） | −1,938 行 + 2.62MB |
| `DvdLib/`（DVD IFO 解析） | 716 行 / 15 文件 | ffprobe + `.vob` 枚举 | −824 |

测试 160 → **167**（新增 UTF.Unknown 名字解析 1 例 + DVD 选集 7 例）。

### 保留了两处 Jellyfin 没有的东西（有意不跟）

1. **`TextEncodingDetect.cs`（409 行）**：Jellyfin 把 UTF-16 无 BOM 支持整个删了。我们刚修好这块并加了测试，跟进去是退步。
2. **`ITextEncoding` 接口**：Jellyfin 删了它，但本仓库有 4 个调用点，删接口是更大的重构，不在「换依赖」范围。

### 在 Jellyfin 的算法上修了一个 bug

Jellyfin 的 `GetPrimaryPlaylistVobFiles` 用文件名的**最后一个下划线段**做 title 键：

```csharp
.Select(vob => _fileSystem.GetFileNameWithoutExtension(vob).AsSpan().RightPart('_').ToString())
```

`VTS_01_1` 和 `VTS_02_1` 都得 `"1"` —— 两个不同 VTS 会撞成同一个 title，小 title 会被并进大 title 的结果。本实现改用**中间段的 VTS 号**（`VTS_01_2` → `01`），并把这个差异写成测试：

`DvdVobSelectorTests.SmallTitle_IsNotMergedIntoBigTitle_UnlikeJellyfinKeys`

顺带修掉旧实现的一个真实缺陷：它是**只按大小过滤**，所有 VOB 都小于阈值时（短片盘、高压缩）会把 `VIDEO_TS.VOB` 菜单算成正片。新实现按文件名排除 `VIDEO_TS.VOB` 与所有 `*_0.VOB`。

选集逻辑抽成了纯函数 `DvdVobSelector`，可以脱离 ffprobe 和真实光盘断言 —— 这是删掉 DvdLib 之后唯一能拿到的验证。

### 明确的已知退化

`DvdLib` 删掉后，DVD 的「主标题」判定从 IFO 精确判断退化为按体积推断。具体边界：

| 盘型 | 影响 |
|---|---|
| 单标题电影盘 | 时长仍准（首片 ffprobe + 其余累加），可能有秒级差异 |
| 每集一个 VTS 的多集盘 | 不再按 IFO 选最长 title，可能选错目标集 |
| 多集共用一个 VTS（多 PGC 共享 VOB） | **所有集被累加成一个长视频** |
| `VIDEO_TS.IFO` 缺失的盘 | 失去遍历 `VTS_0X_0.IFO` 的兜底解析 |

这是 Jellyfin 线上接受的同一套取舍。如果产品上要保 DVD 分集精度，就得把 DvdLib 拿回来。

### 顺带确认的事实

Jellyfin 的密码体系（`PasswordHash` + `ICryptoProvider.CreatePasswordHash` + PBKDF2 + 登录时惰性 rehash）**在我们的 2018 年 Emby 代码里不存在** —— 我们的 `CryptographyProvider` 只有 `GetMD5`/`ComputeSHA1`/`ComputeMD5`。修无盐 SHA1 是要移植这套体系，不是一个「换依赖」，属独立工单。

---

## 10. 第三批 + 安全（已完成）

### 10.1 密码：无盐 SHA1 → PBKDF2-SHA512（带惰性迁移）

这是全仓库唯一一个真·安全问题，不是代码卫生。

原状（`DefaultAuthenticationProvider.cs:93`）：`User.Salt` 字段存在但**从未用于加盐**，
BCrypt 分支被注释掉，所有密码都是裸 `SHA1(UTF-8)` 的十六进制。

现在：
- 版本化格式 `$PBKDF2-SHA512$<iterations>$<salt-b64>$<hash-b64>`，盐内嵌，**不改表结构**
- BCL `Rfc2898DeriveBytes.Pbkdf2`，210,000 迭代（写进串里，将来能升）
- 校验改 `CryptographicOperations.FixedTimeEquals`（原来是比较字符串）
- **登录时惰性迁移**：识别 legacy → 用明文重算 PBKDF2 并落库，用户无感
- 三条校验入口（明文密码 / 本地密码 / hashedPassword）都接了升级，只在 success 时落库

**真实服务上验证过**（不是单元测试）：`perf-user-001` 存量是 40 位 SHA1 →
用旧密码登录 `200` → 库里变成 `$PBKDF2-SHA512$210000$…` → 同密码再登 `200` → 错密码 `401`。

已知限制：只传 `hashedPassword` 的旧客户端无法迁移（SHA1 不可逆，服务端拿不到明文），
那条路径改为恒定时间校验，等用户下次提供明文时升级。

### 10.2 测试 schema 改由真实初始化器派生（挖出测试有效性大问题）

原来 `ProductionSchemaTestBase` 用「嵌入 dump + 一份**手写 ALTER 子集**」造测试 schema。
改成直接调 `PgSchemaInitializer.Initialize` 后，逐项对比发现旧 fixture 和生产**结构上差得远**：

| 项 | 旧测试 schema | 生产 |
|---|---|---|
| 分区 | 25 张普通表，`relkind=r` | 26 个分区父表 + 53 个分区子表 |
| 物化视图 | 0 | 2（`mv_library_stats`、`mv_continue_watching`） |
| 索引 | 93 | 525 |
| `display_preferences` 表 | 缺 | 有 |

**列层面完全一致** —— 这才是那些差分测试一直绿着、却没人发现结构漂移的原因。
也就是说查询引擎的 EXPLAIN/计划相关用例，此前一直在**没有分区、索引少 432 个**的库上跑。

因为 `PgSchemaInitializer` 硬编码检查 `public.media_items`（`FixItemExtradata` 同理），
测试侧改为「每用例一个独立数据库，其 `public` 就是测试 schema」，`DROP DATABASE ... WITH (FORCE)` 收尾。

### 10.3 队列与缓存

- 775 行 BlueRaja 优先队列 → BCL `PriorityQueue` 包一层 68 行的 `StablePriorityQueue`。
  坑：vendored 版用 `InsertionIndex` 保证**同优先级 FIFO**，BCL 版不稳定 →
  用 `(Priority, Sequence)` 复合键保住，5 个测试钉住。
- 154 行手写 LRU → `Microsoft.Extensions.Caching.Memory`。
  坑：MemoryCache 满容量时会**拒绝新条目**并在后台压实（实测新条目丢失、旧条目留下），
  所以写入前显式 `Compact(...)`。`GetStats()` 查证只有一处日志消费，直接删。

### 10.4 小 bug

| 项 | 改前 | 改后 |
|---|---|---|
| TMDB 节流 | `static long _lastRequestTicks` 无同步，并发下节流失效 | `Interlocked.CompareExchange` 原子预留槽位 |
| `FilterIpAddress` | `StartsWith("169.")` → 整个 169/8 被丢 | 只丢 `169.254.0.0/16`（链路本地） |
| `MakeRelativePath` | 手算相对路径，跨 scheme 原样返回 | `Path.GetRelativePath`，URL/UNC 保留旧分支 |
| `GetHostFromUrl` | `Split('/')` → userinfo/端口/IPv6 全进 HttpClient 缓存键 | `new Uri(url).Host`（超时封禁按主机共享） |
| 重复 URL 拆解 ×2 | 两份逐字节相同 | 合并到 `UrlPathParser` |

### 10.5 主动砍掉的两项

- **`MimeTypes`（256 行）**：换 `FileExtensionContentTypeProvider` 要给 `MediaBrowser.Model`
  这个契约程序集加 ASP.NET 框架引用，且它的表缺 `.dvr-ms`/`.wtv`/`.mk3d`，得逐条补。
  收益对不起这个风险，留到最后。
- **Polly**：只有 2 个调用点、约 60 行。为它引一个依赖不如把那个真实竞态修掉（已修）。
