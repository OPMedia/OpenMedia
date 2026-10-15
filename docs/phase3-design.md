# OpenMedia 三期设计：跟 Emby 的差距路线图（v2，去 Emby 化版）

> 依据：docs/api-gap-map.md（44 缺失端点盘点）、docs/test-report.md §6/§8、docs/phase2-design.md（二期口径延续）
> 代码证据核验日期：2026-10-14（Architect 实读 LiveTv/Providers/WebDashboard/Security/EntryPoints 全链）
> **v2 变更（用户决策 10-14 22:2x）**：①插件系统不做——原 P3-C 插件框架轨道整体删除，B6 25 端点维持 404 跳过（矩阵按"用户决策跳过"记账）；②新增**去 Emby 化轨道**（会员/云耦合/遥测清除）。

## 0. 差距总览（五个维度）

| 维度 | 现状 | 与 Emby 4.x 的差距 | 三期动作 |
|------|------|---------------------|---------|
| API 面 | 63.9%（责任口径 87%）；B1–B5 已对齐 | 剩 B6 插件端点 25 个 | **用户拍板暂缓**（不做插件系统，维持 404） |
| 功能域 | 电影全链路通（含转码）；音乐/剧集 fixture 轨道进行中；LiveTv 域 API 在但无数据源 | LiveTv 无免费 EPG 源=死域；**DVR/系列录制被 Premiere 门禁静默阻断**（新发现，见 §1.2） | P3-B + P3-C2 |
| 元数据 | 仅 nfo 本地元数据验证过；网络 provider 全部未验证 | Emby 核心体验（海报/简介/演员自动来）缺失 | P3-A |
| Web UI | 3.5.3 老 jQuery dashboard | Emby 4.x 全新 React web；**老 UI 内嵌 Premiere 推销位**（侧栏菜单/supporterkey 页/iap 组件） | P3-D spike + P3-C3 |
| 商业耦合 | mb3admin 遥测/会员校验、emby.media RSS/下载链接散布 8 处 | 去 Emby 化（用户点名：会员、云同步之类） | P3-C |

**优先级**（按用户点名 + 可见价值）：去 Emby 化（C）≥ 元数据（A）> LiveTv（B）> Web UI spike（D）。

---

## 1. 差距盘点（代码证据）

### 1.1 B6 插件端点（用户已拍板：不做）

api-gap-map §4.2 全量 25 个（user_usage_stats 19 + Reports 4 + ThumbnailSet 1 + SMTP 1；矩阵 SKIP 的 22 是子集）。**用户决策：插件系统可能得逆向 Emby，先不做**——B6 维持 404，矩阵按"用户决策跳过"记账，不进通过率分母。插件加载框架代码（ApplicationHost.cs:1400/:1833）保留现状不动（死代码无害），四期若做插件系统再启。

### 1.2 LiveTv 现状（含新发现：DVR 门禁真阻断）

- **TunerHosts 已有**：M3UTunerHost（免费）、HdHomerun（硬件）——`Emby.Server.Implementations/LiveTv/TunerHosts/`
- **Listings 只有 SchedulesDirect**（付费订阅，无 fixture 可测）→ guide 永远空，LiveTv 域是死的
- **XmlTv provider 已存在**（v2 误判更正）：`src/Emby.Server.MediaEncoding/Emby.Server.Implementations.LiveTv.Listings/XmlTvProvider.cs`（C01 反编译恢复，IListingsProvider 实现），经 GetExports DI 自动发现，Type="xmltv" 直接可用。v2 曾断言"零接线死引用需新建 provider 1.5d"——**扫描范围失误**（只查了根目录 Emby.Server.Implementations/，漏了 src/Emby.Server.MediaEncoding/），实际 B 轨道零代码改动完成
- **web UI 的 XmlTv 配置页已存在**（`dashboard-ui/components/tvproviders/xmltv.js`）——服务端 provider 与 UI 均现成
- ⚠️ **新发现——DVR 门禁是真阻断**：
  - `LiveTvManager.cs:2193` CreateSeriesTimer：`GetRegistrationInfo("seriesrecordings")` → `!IsValid` 时 **log + return**——系列录制静默失败
  - `EmbyTV.cs:1193`：`GetRegistrationInfo("dvr")` → `!IsValid` 时 **log + OnTimerOutOfDate + return**——DVR 录制静默失败
  - 链路：GetRegistrationInfo → PluginSecurityManager → mb3admin.com 校验（外部已死）→ IsValid=false → **P3-B3 录制验证必被此门禁挡死**。去 Emby 化（P3-C2）是 B3 的硬前置

### 1.3 元数据 provider 现状（全部未验证）

| 域 | Provider（MediaBrowser.Providers/） | 状态 |
|----|--------------------------------------|------|
| 电影 | MovieDb（TMDb v3，key 硬编码 `MovieDbProvider.cs:171`）、Fanart、Omdb | 代码在，从未跑过真实拉取 |
| 剧集 | TheMovieDb（6 个 provider）、TheTVDB（v3 外部已停服，大概率死）、MissingEpisode | 同上 |
| 音乐 | MusicBrainz、AudioDb、FanArt | 同上 |
| 本地 | LocalMetadata + XbmcMetadata（nfo） | ✅ 已验证 |

背景：BUG-16 修复（22fcd01242）前元数据保存整个事务回滚，网络 provider 拉到也存不进。现在保存链路通了，provider 验证才第一次有意义。预期首跑暴露老 API 变更/UA 拒绝/图片下载 bug（同 ffmpeg 首跑模式，登记不算失败）。

### 1.4 Web UI 现状

`dashboard-ui/` = 3.5.3 老 jQuery UI。**内嵌 Emby 商业耦合**（详见 §2.5 清单）：侧栏 "Emby Premiere" 菜单项（librarymenu.js）、supporterkey.html 密钥页、iap.js 购买组件、registrationservices.js（bower emby-webcomponents）、XmlTv 页 premiere 提示、emby.media 下载/新闻链接、sync 菜单残留。Emby 4.x web（MediaBrowser/Emby.Web 4.1.x 开源构建产物）是换新路线——**注意其 emby-webcomponents 同样带 premiere/iap 耦合，换新后去 Emby 化要重做一遍**，spike 结论必须计入该成本。

### 1.5 去 Emby 化全量盘点（服务器 + web UI，实测 grep 取证）

**打 mb3admin.com 的点（6 处）**：

| 位置 | 内容 | 性质 |
|------|------|------|
| `Security/PluginSecurityManager.cs:26,27` | 会员校验/注册 URL（IsSupporter/SupporterKey/GetRegistrationStatus/LicenseFile） | 会员体系核心 |
| `EntryPoints/UsageReporter.cs:20`（wired by `UsageEntryPoint.cs:88,116`） | 服务器/客户端使用遥测上报 | 纯遥测 |
| `Updates/InstallationManager.cs:181,216` | 插件包目录两个 URL | 插件系统（已不做） |
| `Api/StartupWizardService.cs:78` | 安装向导完成时上报安装信息 | 纯遥测 |
| `Api/PluginService.cs` 4 端点 | GET/POST /Plugins/SecurityInfo、GET /Plugins/RegistrationRecords/{Name}、POST /Appstore/Register | 会员 API 面 |
| `LiveTv/LiveTvManager.cs:2193` + `EmbyTV/EmbyTV.cs:1193` | DVR/系列录制 Premiere 门禁（真阻断，见 §1.2） | 功能门禁 |

**打 emby.media 的点**：`News/NewsEntryPoint.cs:86`（博客 RSS → /ProductNews → web 首页新闻区块）、`dashboardpage.js` 手动更新下载链接、DLNA `DefaultProfile.cs:21` ModelUrl/ManufacturerUrl（协议字段）、`EmbyTV.cs:283` provider info URL、`Notifications.cs:141` 文案、2 处代码注释。**云同步/Connect**：DLL 迁移期已删，代码零残留（实测 grep 零命中），仅 web UI 有 sync 菜单残留（lnkSyncToOtherDevices/mySyncPage，服务器无对应能力时 UI 自动隐藏）。

---

## 2. 方案决策

### 2.1 B6：用户拍板不做插件系统

维持 404 跳过；插件框架代码保留不动；矩阵记账口径改"用户决策跳过"。四期若启动插件系统，需先逆向 Emby 插件 API（用户原话），另立设计。

### 2.2 去 Emby 化：删除 + 桩化双轨（原则：UI 会调的桩化，纯外连的删除，门禁的解除）

| # | 项 | 处置 | 理由 |
|---|----|------|------|
| 1 | PluginSecurityManager（GetRegistrationInfo/IsSupporter） | **桩化：恒返回 IsValid=true**（单点） | DVR/themes 等所有门禁走这里；删 ISecurityManager 接口动静大，桩化一行=全功能解锁，语义正确 |
| 2 | PluginService 4 端点 | SecurityInfo GET 桩化 `{IsMBSupporter:true}`；POST 桩化 204 no-op；RegistrationRecords 桩化 `IsRegistered:true`；Appstore/Register **删除** | 老 web UI getPluginSecurityInfo 调用链有 catch 兜底（false→themes 禁用），桩化让 UI 全绿且不炸 |
| 3 | UsageEntryPoint + UsageReporter | **删除**（EntryPoint 摘除 + 类删） | 纯遥测无 UI 依赖 |
| 4 | StartupWizardService:78 安装上报 | **删除调用** | 纯遥测 |
| 5 | NewsEntryPoint RSS 拉取 | **桩化：GetProductNews 返回空 QueryResult**（删定时拉取） | web 首页新闻区块调 /ProductNews；空列表 UI 自然隐藏不报错 |
| 6 | InstallationManager 包目录远程 URL | **桩化：不打 mb3admin，返回空/仅已装** | /Packages 端点在用（矩阵 200 + web 插件页）；插件系统不做，目录无意义但端点要活 |
| 7 | LiveTvManager:2193 系列录制门禁 | **删门禁**（registration check + return 整块） | mb3admin 死→IsValid=false→系列录制静默失败，这是真功能 bug |
| 8 | EmbyTV:1193 DVR 门禁 | **删门禁**（同上） | 同上；P3-B3 硬前置 |
| 9 | SystemInfo.HardwareAccelerationRequiresPremiere | 不动（B1 已补，恒 false） | 无商业语义 |
| 10 | DLNA ModelUrl/ManufacturerUrl、EmbyTV.cs:283、Notifications 文案、注释 | **白名单保留**（登记进文档） | 协议字段/文案/注释，非功能耦合，改动无收益 |
| 11 | web：librarymenu.js "Emby Premiere" 菜单项 | **删除菜单项** | 用户可见推销位 |
| 12 | web：supporterkey.html + supporterkeypage.js + supporter 图片 | **删除**（菜单删了无入口） | 会员密钥页 |
| 13 | web：iap.js | **桩化**（getSubscriptionOptions 空列表、beginPurchase no-op） | xmltv 等页面依赖 registrationServices 链 |
| 14 | web：registrationservices.js（bower emby-webcomponents） | **桩化：所有 feature 恒 unlocked** | themes/xmltv 多处调用，删了报 require 错 |
| 15 | web：xmltv.js `.lnkPremiere` + XmlTvPremiere 文案 | **删提示元素** | 推销位 |
| 16 | web：dashboardpage.js emby.media/download 链接 | 改 GitHub release 链接或删（低优先） | 死链 |
| 17 | web：sync 菜单残留（lnkSyncToOtherDevices 等） | **删菜单**（服务器 sync 已删） | 死链接 UI |

**PremiereDate（首播日期）是正常元数据字段，与会员无关，严禁误伤**（LiveTvManager 里大量 IsPremiere/PremiereDate 是节目元数据）。

### 2.3 LiveTv：XmlTv + M3U 免费全链路（✅ 已完成，零代码改动）

- SchedulesDirect 付费无 fixture 不做；HdHomerun 无硬件不做
- ~~新建 XmlTvListingsProvider~~（v2 误判：provider 已存在于 Emby.Server.MediaEncoding，见 §1.2 更正）——实际执行：纯 fixture+配置（`tests/setup-livetv.ps1`：guide.xml 2 频道×2 节目 + channels.m3u 指向自身 Movie1 静态流 + 幂等 provider/tuner 注册），commit `daa1dd269f`
- 全链路验收实测全过：/LiveTv/Channels 2 频道、guide 真实节目、频道 PlaybackInfo 200、DVR timer 录制出真实 mpegts 文件可回放（门禁由 P3-C2 `7b773d9ca4` 先行解除）

### 2.4 元数据：逐 provider 验证与修复

TMDb 优先（电影+剧集主源，v3 API 在服务，key 已硬编码）；音乐三件套跟随 D2 音频 fixture；**TVDB v3 默认禁用**（外部停服，不修死源，TMDb 顶上）；每 provider 一份验证记录，修一个 commit 一个；图片下载链路（ProviderManager 远程图入库）单独验证。

### 2.5 Web UI：Emby.Web 4.1 接入 spike（⛔ 已否决，license 依据）

**否决结论（2026-10-15，Chief 核实）**：Emby 的 web 仓库（emby-web-defaultskin / emby-webcomponents）公开源码但**没有 LICENSE 文件**——默认全版权保留，二次分发属灰色地带；且 4.1 前端自带 premiere/iap 耦合，与去 Emby 化方向直接冲突。**UI 升级以后单独立项**（改造 3.5.3 皮肤或自研前端），不碰 Emby 4.1 资产。spike 未执行，P3-D 任务取消。

---

## 3. 任务清单（三开发并行 + 条件轨道）

| 任务 | 内容 | 依赖 | 工作量 | 验收 |
|------|------|------|--------|------|
| **P3-C1** | 服务器遥测清除：UsageEntryPoint/UsageReporter 删、StartupWizardService:78 删、NewsEntryPoint 桩化空新闻、InstallationManager 包目录桩化 | — | 1d | 断网启动零外连报错；/Packages 200；/ProductNews 200 空列表 |
| **P3-C2** | 会员门禁桩化与解除：PluginSecurityManager 恒 IsValid=true、PluginService 4 端点处置、DVR×2 门禁删除 | — | 1d | SecurityInfo 返回 IsMBSupporter=true；CreateSeriesTimer 不再 return；EmbyTV 录制不再 OnTimerOutOfDate |
| **P3-C3** | web UI 去 Emby 化：菜单/supporterkey 页删、iap+registrationservices 桩、xmltv 提示删、sync 残留删 | — | 1d | 侧栏无 Premiere 项；supporterkey 404；XmlTv 页无 premiere 提示；themes 可用 |
| **P3-C4** | 全量复扫 + 白名单登记 + 验收报告 | C1–C3 | 0.5d | grep mb3admin 零命中；emby.media 仅白名单；三套件回归不劣化 |
| **P3-A1** | TMDb 电影链路验证修复（无 nfo → 拉取入库） | — | 1d | Movie1 删 nfo 重扫后 TMDb 元数据+海报入库；验证记录入 docs |
| **P3-A2** | TMDb 剧集链路（Series/Season/Episode） | D3 剧集 fixture | 1d | 剧集 fixture 无 nfo 拉全季元数据 |
| **P3-A3** | 音乐 provider（MusicBrainz/AudioDb/FanArt） | D2 音频 fixture | 1d | 音频 fixture 拉专辑/艺人元数据 |
| **P3-A4** | TVDB 禁用处置 + Omdb 验证 | — | 0.5d | TVDB 默认关闭不报错；Omdb 出验证记录 |
| **P3-B1** | XmlTvListingsProvider 新建（接 src/Emby.XmlTv） | — | 1.5d | XMLTV 文件 → guide program 入库 |
| **P3-B2** | M3U+XMLTV 全链路 fixture | P3-B1 | 1d | 频道列表非空 → guide 有数据 → 频道可播放 |
| **P3-B3** | 录制链路验证（timer → EmbyTV 出文件） | **P3-B2 + P3-C2** | 1d | 单次 timer 录制出媒体文件可播放 |
| **P3-D** | Emby.Web 4.1 接入 spike（含去 Emby 化成本评估） | — | 1d | 决策报告：页面可用性 + API 缺口 + premiere 耦合重做成本 + 回滚验证 |
| **P3-E** | 性能优化实施（按优化候选清单） | 二期 T2 基线 | 视清单 | 每项前后对比数字；回归不劣化 |

**依赖图**：C2→B3（DVR 门禁）；B1→B2→B3；A2←D3 fixture；A3←D2 fixture；E←T2 基线。A/B/C/D 四轨道相互独立全并行。

**分工建议**：最先空出的开发 → P3-C（用户点名，最高优先）；Developer（二期 T1 完）→ P3-A；Developer-2（音乐完）→ P3-B；P3-D 给再下一个空出的（半天可插入）；Tester 每轨道验收 + 负路径用例入 test-plan；Reviewer 总闸门。

**总闸门**：三套件回归不劣化（场景 ≥ 22/24、矩阵 ≥ 当前基线、契约 ≥ 16/2）+ 各轨道验收全过 + 修一个 commit 一个 + **去 Emby 化复扫零命中**。

---

## 4. 风险登记

| 风险 | 应对 |
|------|------|
| R1 网络 provider 首跑出新 bug（同 ffmpeg 模式） | 预期内，登记 fail-classified 新分类，不算轨道失败 |
| R2 外部 API 已死（TVDB v3、可能 AudioDb/Fanart） | 逐个验证记录处置；死源禁用不修；TMDb 主源顶上 |
| R3 桩化 SecurityInfo 后 web themes 全解锁 | 语义即"全功能解锁"，符合去商业化目标，非风险 |
| R4 DVR 门禁删除后录制无门槛 | 符合目标；B3 验证录制链路真实可用 |
| R5 Emby.Web license 不合规 | spike 仅本地试验；正式接入前 Chief 核实 |
| R6 元数据拉取污染共享条目库 | fixture 条目独立虚拟文件夹；前后 TotalRecordCount 对账 |
| R7 三期轨道与二期收尾抢人 | 依赖图错开：A2/A3 等 fixture、E 等基线、C 独立可先行 |
| R8 XMLTV 大文件解析慢/内存 | fixture 控制规模；真实 IPTV 源留四期压测 |
| R9 去Emby化误伤 PremiereDate 元数据字段 | §2.2 已标注严禁误伤；Reviewer 对照白名单审查 |

---

## 5. 与二期/四期的衔接

- 二期收尾（T1 健壮性 + T2 基线 + 音乐/剧集 fixture）= 三期启动条件，无空窗
- 三期产出决定四期方向：~~P3-D 结论~~（D 已否决，UI 单独立项）；P3-E 基线 → 性能优化 P1–P7 转四期正式轨道（Developer 进行中）；插件系统 → 用户拍板"先逆向再议"，四期另立设计

---

## 6. 三期终态（v3 修订，2026-10-15）

| 轨道 | 状态 | 交付 |
|------|------|------|
| P3-A 元数据 | ✅ 完成 | A1/A4（`6ee30b8071`：TMDb 电影链路验证 + TVDB 死源禁用 + Omdb 图片源修复）、A2/A3（`ec82eb4deb`：TMDb 剧集 + 音乐链路验证）；产出 docs/provider-validation.md |
| P3-B LiveTv | ✅ 完成（零代码改动，v2 误判见 §1.2 更正） | `daa1dd269f`：XmlTv+M3U 全链路（频道/guide/播放/DVR 录制出文件），fixture tests/setup-livetv.ps1 |
| P3-C 去Emby化 | ✅ 完成 | C1/C2（`7b773d9ca4` 注册门禁恒有效+外连清零、`de50f35f5d` ProductNews 空列表）、C3+死代码（`a72a8ff29e` -367 行、`1026604761` web UI 清理，19 处 emby.media 外链清零） |
| P3-D Web UI spike | ⛔ 否决 | license 无 LICENSE 文件 + premiere 耦合冲突（§2.5）；UI 现代化单独立项 |
| P3-E 性能 | ➡️ 转四期 | 基线已出（docs/perf-baseline.md，Tester `2f2e73f6cc`）；P1–P7 优化候选转四期轨道实施 |

**v2→v3 修订记录**：①XmlTv"零接线"误判更正（provider 在 Emby.Server.MediaEncoding，B 轨道实际零代码）；②D 否决及 license 依据入档；③各轨道终态对账。**经验教训**：断言"全解决方案零使用"前必须扫全部路由/服务承载程序集（src/ 与根目录两套布局都要扫）——v2 的扫描只覆盖了根目录。
