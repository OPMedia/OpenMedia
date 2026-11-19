# 与 Jellyfin 的功能差距（可排期版）

**基线**：我们 = `openapi.json` 版本 4.1.1.0（Emby 3.5.3 血统，2018 fork 点）；
上游 = `jellyfin/jellyfin` **v12.4（2026-10-26）**。

**版本时间线**：`10.0 (2019-01)` → `10.8 (2023)` → `10.9 (2024-05)` → `10.10 (2024-10)`
→ `10.11 (2025-10)` → **`12.0 (2026-09)`** → `12.4`。

**结论先行**：差距不是一个功能清单，是 **8 年 / 12 个大版本**。但真正卡住的不是功能数量，
而是三件事：**身份兼容、几块纯空洞、以及部署正确性**。与此同时我们有 Jellyfin 已经丢掉的资产
（`/emby` 前缀、DLNA、LiveTV 在核心里、通知框架）。

> 注意：`12.3` 只有 7 个 PR、`12.4` 只有 1 个（.NET 补丁）。**真正的功能面在 12.0**，
> 排期目标应锁 12.0 的能力集，不要去追 12.x 的小版本。

---

## 一、三个必须你先拍板的岔路口

### 决策 A：对外报什么版本号？

这是**唯一的 0 号阻塞点**，不决定，后面所有客户端相关工作都白做。

Jellyfin 官方客户端（Android / Android TV / Swiftfin / Roku / Tizen / webOS）在「添加服务器」
阶段就会做两项校验：

| 校验 | JF 客户端要求 | 我们实测 |
|---|---|---|
| 产品名 | `productName == "Jellyfin Server"` | **字段根本不存在**（`PublicSystemInfo` 只有 LocalAddress/ServerName/Version/OperatingSystem/Id） |
| 版本 | `>= 12.0.0.0` | `3.5.3.0` |

→ **客户端在连接到我们之前就放弃了**，连密码框都不会出现。

两条路：

| | 报 Jellyfin 12.x 语义 | 继续报 Emby 4.x |
|---|---|---|
| 收益 | 白拿 JF 全生态客户端（9 个官方客户端 + SDK） | 保住 Emby 老客户端（JF 12 已删 `/emby` 前缀，我们**还支持**） |
| 代价 | 引入 JF 生态兼容义务（他们每次大版本动路由我们要跟） | 客户端只能自己写，`web/` 现有 14k 行不够（无 LiveTV/无转码配置） |

**我们其实有独特位置**：`/emby/*`、`/mediabrowser/*`、裸根路径**三个前缀同时支持**
（`ServiceRoutingMiddleware.cs:120-160`）。Jellyfin 12 把前两个删了。所以「同时报两种身份」
技术上可能，但需要一个明确的策略而不是随手改字段。

### 决策 B：`web/` 是终点还是过渡？

现在 `web/` 是 Vue 3 + Vite，46 个文件 / 约 14k 行，页面有 Home / ItemDetail / Library /
Login / Search / Person / VideoOsd + 4 个 Settings + 6 个 admin 页。
**缺**：LiveTV、转码/HW 加速配置、插件管理、SyncPlay。

- 目标是「不写客户端」→ 决策 A 必须选 JF 兼容，并补下面第二节的路由垫片。
- 目标是「自研替代」→ 决策 A 可以继续 Emby 身份，但要接受 `web/` 还得涨 2-3 倍。

### 决策 C：插件系统重启，还是先做内置实现？

插件轨道当年被明确取消（`Updates/InstallationManager.cs:163-172` 的 `GetAvailablePackages`
恒返回空列表）。重启的**真实代价**：

| 切片 | 内容 | 体量 |
|---|---|---|
| S1 | dll 加载 + `AssemblyLoadContext` 隔离 + `meta.json` + 启停 | M（2-3 周） |
| S2 | 官方目录 + 安装/更新 + targetAbi 匹配 | M |
| S3 | **插件能注册进 DI** | **L（真正的大头）** |

我们用的是 **SimpleInjector**，插件要注册自己的服务必须先于第一次解析发生（容器首解析即封印），
即两阶段 bootstrap；而 `GetExportTypes<T>()` 是「任何实现 `T` 的具体类都自动成为导出」
（`ApplicationHost.cs:654-663`）—— 插件放错一个类就能顶替 `IMediaSourceProvider`。

**我的建议：先不重启**。理由是时机不对：`MediaBrowser.Controller` 还在收敛期，
现在开 dll 门等于把内部接口冻结成公共 ABI。更便宜的路是先做**内置插件**（编译进主程序、
用 `IPlugin` 做模块化，零隔离成本、零 ABI 承诺），把契约跑熟。

---

## 二、按域结论

### 2.1 播放与转码：差距最大的一域

| 能力 | Jellyfin | 我们 | 体量 |
|---|---|---|---|
| HDR/DoVi 色调映射 | 7 条管线、9 种算法 | **完全没有**（配置层 0 字段） | XL（只做 CPU 版可压到 M） |
| 硬件加速 | 6 家厂商 | **出货 ffmpeg 只有 vdpau**，没有 NVENC/QSV/VAAPI/AMF | XL |
| HEVC/AV1 输出 | 有 | **坏路径**：`hevc` → `-c:v hevc`（不是合法编码器名） | M |
| Trickplay 缩略图 | 14 文件 + DB 表 | **完全没有**（`BifService.cs` 直接抛） | L |
| 转码节流 | 有 | **配置项在、实现不在** | S |
| HLS 分片清理 | 有 | 无（临时目录只增不减） | S |
| ASS 字体保真 | 提取内嵌字体注入 `fontsdir=` | **强行改字体**：`force_style='FontName=Droid Sans Fallback'` | M |
| PGS/VOBSUB 图形字幕 | 提取 + `sub2video` | 无 | M |
| 编码器能力矩阵 | 6 类探测（filter/hwaccel/bsf/版本） | 只有硬编码 32 decoder + 26 encoder | M |

**前置条件 0**：`Dockerfile:49` 锁的 `ffmpeg-7.1.1` 下载 URL 已 **404**，构建本身就是坏的；
且该静态包只有 `vdpau`，没有任何 GPU 编码器。不做这一步，上面所有 HWA/HDR 都无法落地。

### 2.2 元数据：两个源已经死了

| 项 | 实测结论 |
|---|---|
| **AudioDB** | 我们的 key 返回 **404**（对照：Jellyfin 的 `195003` 返回 200）→ 专辑/艺人元数据 + 全部封面失效。且 `AudioDbArtistProvider.cs:42` 的 `GetSearchResults` 是**空实现** |
| **TVDB v1 名称搜索** | 4 个查询全部返回空 `<Data></Data>` → 按名字搜剧已死 |
| **TVDB 增量更新** | `TvdbPrescanTask` 拿到 `<Time>0</Time>` → 定时任务永远空转、白烧 CPU |
| TVDB 按 ID 拉数据 | 仍然可用 |

缺失的源（Jellyfin 有、我们 0 行）：TVmaze、AniList、Kitsu、AniSearch、Cover Art Archive、
LrcLib、IMVDb、OpenLibrary、OPDS、ListenBrainz。

其他缺口：
- **歌词子系统完全没有**（Jellyfin 核心内置 LRC/TXT 解析 + LrcLib 在线）
- **3 位以上集号不支持**（`\d{1,3}` vs JF 的 `[0-9]{2,4}`）→ 千集长篇番识别失败
- 剧集多版本不支持（我们只有电影级 `PrimaryVersionId`）
- 搜索：单一实现 vs Jellyfin 的可插拔 + SQL 打分 + **转写名匹配**（对中文/日文命中影响明显）

**我们该删的**：游戏/游戏系统（10+ 文件）。Jellyfin 全树 `grep -ic game` = **0**。

### 2.3 平台功能：三块真空洞，但也有一块我们是领先的

**完全空白**：

| 项 | 影响 | 体量 |
|---|---|---|
| **全量备份/恢复** | 自建用户丢了就是全部重来；我们**还在频繁改 schema**，每次升级都是赌博 | M（PG 有 `pg_dump`，可压到 S/M） |
| **通知传输** | 框架 917 行全在，但**零个 `INotificationService` 实现** → `POST /Notifications/Admin` 静默丢弃 | **S**（写一个类就通） |
| **Quick Connect** | 电视/机顶盒输长密码是真实痛点 | **S** |

**我们是领先的**（别当差距排）：
- **DLNA**：在核心里且完整（`Emby.Dlna/` 90 文件 + `RSSDP/`）；Jellyfin 已移出核心改成插件
- **LiveTV/DVR**：调谐器、EPG、录制、系列录制全部对等（`LiveTv/EmbyTV/` 11411 行）
- **通知框架**：比 Jellyfin 核心里的残渣更完整

**安全级问题**：`KestrelRequest.cs:122,139` **无条件信任** `X-Forwarded-For` / `X-Forwarded-Proto`
→ 局域网免登录判定、家长时段、远程码率上限**全部可伪造**。Jellyfin 有 `KnownProxies` 白名单。
同时**没有 base URL 子路径部署**（Jellyfin 有 `NetworkConfiguration.BaseUrl`），
自建 100% 走反代，现在只能靠 nginx `sub_filter` 脏补丁。

### 2.4 API 与客户端兼容

路径层（参数名归一化后）：**共享 203 个路由模板；Jellyfin 独有 90；我们独有 150**。

JF 客户端必用、我们缺（全部实测 404/400）：

```
GET  /UserViews                 404    ← 首页轮播
GET  /UserItems/Resume          404    ← "继续观看"
GET  /Users/Me                  400    ← 取当前用户
GET  /Items/{id}                404    ← 详情页（我们只有 DELETE，实测 204）
POST /UserPlayedItems/{id}      缺     ← 标记已看
POST /UserFavoriteItems/{id}    缺     ← 收藏
     QuickConnect (4 条)        缺
     SyncPlay (22 条)           缺
```

**关键**：这些**全是薄封装** —— 底层 `/Users/{userId}/Views`、`/Users/{userId}/Items/Resume`、
`/Users/{userId}/PlayedItems/{id}` 都已经实现好了，只是换一层 URL 皮。

**两个小动作能让 JF SDK 白拿**：
1. 给 `openapi.json` 加 `components.securitySchemes`（JF 规范里现成的，照抄）—— 现在**没有**，
   导致任何代码生成器都产不出带鉴权的客户端。
2. 补 `PublicSystemInfo.ProductName` + `StartupWizardCompleted`。

### 2.5 运维债（不性感但要命）

| # | 债 | 现状 | 体量 |
|---|---|---|---|
| 1 | 无健康检查 | 全仓 `AddHealthChecks/MapHealthChecks` 0 命中 | **0.5 天** |
| 2 | 无指标 | 无 prometheus；线上慢/转码堆积全靠猜 | **1 天** |
| 3 | 无优雅停机 | 无 `ApplicationStopping`；`docker stop` = SIGKILL | **1-2 天** |
| 4 | 无版本化迁移 | 靠「`media_items` 存在吗」做门卫 + 23 处手写 ALTER；无版本号、无回滚 | 中-大 |
| 5 | 无结构化日志 | 自研纯文本；排障靠 grep | 小-中 |

---

## 三、立刻可修的一行级 bug（不用排期，建议本周）

| 位置 | 问题 |
|---|---|
| `DynamicHlsService.cs:843` | `Equals(text5,"m4s") \|\| Equals(text5,"m4s")` —— 第二个应为 `"mp4"`。上一行 `:830` 是正确写法，这是笔误 → **fMP4 HLS 实际断裂** |
| `EncodingHelper.cs:121` | `hevc`/`av1` 输出直接 `ToLower()` 返回 `"hevc"`，不是合法 ffmpeg 编码器名 → 报错而非回退 |
| `EncodingHelper.cs:1737-1741` | AMF 的 `mpeg2video` 分支检查 `hevc_amf` 却返回 `mpeg2_mmal` |
| `AudioDbArtistProvider.cs:31,42` | key 失效（404）+ `GetSearchResults` 空实现 |
| `KestrelRequest.cs:122,139` | 无条件信任 `X-Forwarded-For`（安全） |

---

## 四、建议排期

**P0（本周，一天内）**：5 个一行级 bug + AudioDB key。

**P1（两周内，每件都零前置依赖）**：
1. **反代可信代理白名单 + base URL**（S+M）—— 安全洞 + 自建必用
2. **Jellyfin 路由垫片 10 条**（S）—— 决定 A 选 JF 兼容时唯一必须做的事
3. **通知 Webhook 实现**（S）—— 框架全在，缺一个类
4. **运维三件套**（S）—— `/health` + Prometheus + 优雅停机

**P2（一个月内）**：
5. **换 ffmpeg 发行版 + 编码器能力矩阵** —— 所有 HWA/HDR 的地基，同时修好 404 的构建
6. **CPU 版 HDR10/HLG 色调映射**（复用 5 的产物，XL 砍成 M）
7. **TVDB 迁 v4** —— 修掉「看起来在工作、实际空转」的定时任务
8. **全量备份/恢复**（PG 版）

**P3（季度级）**：Trickplay、歌词子系统、插件系统（若决策 C 选重启）、命名解析三连。

**明确不做**：SyncPlay（22 端点 + 实时组播，独占一季）、OIDC/LDAP/SAML（企业级）、
漫画/电子书、拼音搜索（差异化而非补齐）、游戏支持（应删）。

---

## 五、调研方法与可信度

| 数据 | 来源 |
|---|---|
| Jellyfin 代码/结构 | `gh api` + `raw.githubusercontent.com`（master @ 2026-11-18） |
| Jellyfin 版本面 | release notes 全文（v12.0–v12.4） |
| Jellyfin API | `api.jellyfin.org/openapi/jellyfin-openapi-stable.json`（12.4.0） |
| 插件清单 | `repo.jellyfin.org/files/plugin/manifest.json`（实测 36 个） |
| **外部端点存活** | **本机 curl 实测**：TVDB v1 ×6 变体、AudioDB ×3 key、TMDb ×2、Fanart、OMDb、MusicBrainz |
| **我们的运行时行为** | **对本地实例实测**：`/System/Info/Public` 字段、`GET /Items/{id}`、`/UserViews`、`/UserItems/Resume`、`/Users/Me` |
| 我们的代码 | `文件:行` 引用 |

**未确认**（不编）：TVDB v1 空返回是永久下线还是临时故障；SAML 是否有成熟社区插件；
我们的 license 是 GPL-2.0 only 还是 or later（决定能否引入 GPL-3.0 的 Jellyfin 官方插件代码）。
