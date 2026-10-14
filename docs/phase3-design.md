# OpenMedia 三期设计：跟 Emby 的差距路线图

> 依据：docs/api-gap-map.md（44 缺失端点盘点）、docs/test-report.md §6/§8（83 FAIL 分类 + 复测建议）、docs/phase2-design.md（二期口径延续）
> 代码证据核验日期：2026-10-14（Architect 实读 LiveTv/Providers/WebDashboard/插件加载链）
> 前置：二期 T1 健壮性、T2 性能基线、音乐/剧集 fixture 轨道（D2/D3 进行中）——三期轨道多数在 fixture 就绪后启动

## 0. 差距总览（五个维度）

| 维度 | 现状 | 与 Emby 4.x 的差距 | 三期动作 |
|------|------|---------------------|---------|
| API 面 | 63.9%（责任口径 87%）；B1–B5 已对齐 DTO/参数/核心端点 | 剩 B6 插件端点 25 个 + 转码链路 bug（二期修） | P3-C |
| 功能域 | 电影全链路通（含转码）；音乐/剧集 fixture 轨道进行中；LiveTv 域 API 在但无数据源 | LiveTv 无免费 EPG 源=死域；音乐/剧集元数据未验证 | P3-A / P3-B |
| 元数据 | 仅 nfo 本地元数据验证过；网络 provider（TMDb/TVDB/MusicBrainz…）全部未验证 | Emby 核心体验（海报/简介/演员自动来）缺失 | P3-A |
| Web UI | 3.5.3 老 jQuery dashboard（bower_components/legacy 目录） | Emby 4.x 是全新 React web，用户可见差距最大 | P3-D spike |
| 生态 | 插件框架代码在，**0 插件、从未验证**；官方客户端兼容目标 D7 未实测 | 插件生态为零；4.1 客户端直连未验证 | P3-C / P3-D |

**优先级判断**（按用户可见价值）：元数据 provider（A）≈ Web UI（D）> LiveTv（B）> 插件/B6（C）。A 和 D 是用户"打开就能看见"的差距；B 是功能域从死到活；C 是生态基础。

---

## 1. 差距盘点（代码证据）

### 1.1 B6 插件端点（25 个，口径说明）

api-gap-map §4.2 全量盘点 **25 个**：user_usage_stats 19 + Reports 4 + ThumbnailSet 1 + SMTP Test 1。test-report 的"22 个"是矩阵 SKIP 登记数（user_usage_stats 17 + Reports 4 + ThumbnailSet 1，SMTP 与 2 个 user_usage_stats 端点未入矩阵）——**以 25 为准，矩阵是子集**。

这些端点在 Emby 里全部由插件提供（Playback Reporting / Emby Reports / RokuMetadata / SMTP 通知），官方客户端不调用，仅 dashboard 插件页使用。

### 1.2 LiveTv 现状

- **TunerHosts 已有**：M3UTunerHost（免费，本地文件/URL）、HdHomerun（硬件）——`Emby.Server.Implementations/LiveTv/TunerHosts/`
- **Listings 只有 SchedulesDirect**（`Listings/SchedulesDirect.cs`）——付费订阅服务，需账号，无 fixture 可测 → **guide 永远空，LiveTv 域在 UI 上是死的**
- **XmlTv 解析库存在但零接线**：`src/Emby.XmlTv`（完整解析器）被 `Emby.Server.Implementations.csproj` 引用，但全解决方案**没有任何 .cs 使用它**——死引用，正好是三期要接的线
- **EmbyTV 录制引擎完整**：DirectRecorder/EncodedRecorder/TimerManager/SeriesTimerManager
- API 端点：B3 已补 3 个 GET；二期 T1.2 正在修 8 个健壮性端点

### 1.3 元数据 provider 现状（全部未验证）

| 域 | Provider（代码位置 MediaBrowser.Providers/） | 状态 |
|----|---------------------------------------------|------|
| 电影 | MovieDb（TMDb v3，**API key 硬编码** `MovieDbProvider.cs:171`）、Fanart、Omdb | 代码在，从未跑过真实拉取 |
| 剧集 | TheMovieDb（6 个 provider）、**TheTVDB（v3 API，外部已停服，大概率死）**、FanArt、MissingEpisode | 同上；TVDB 需处置 |
| 音乐 | MusicBrainz、AudioDb、FanArt | 同上 |
| 本地 | LocalMetadata + XbmcMetadata（nfo） | ✅ 已验证（一期/二期） |

关键背景：一期 BUG-16 修复（22fcd01242）前**元数据保存整个事务回滚**——网络 provider 即使拉到也存不进去。现在保存链路通了，provider 验证才第一次有意义。预期首跑暴露：老 API 端点变更、TLS/UA 被拒、图片下载路径 bug（同 ffmpeg 首跑炸三个 bug 的模式，登记不算失败）。

### 1.4 Web UI 现状

`MediaBrowser.WebDashboard/dashboard-ui/` = Emby 3.5.3 老 jQuery UI（bower_components、legacy/、85 个顶层条目）。Emby 4.x 的 web 是全新应用（MediaBrowser/Emby.Web，4.1.x tag 有开源构建产物）。api-gap-map 的 **D7 目标本来就是"官方 Emby 客户端 4.1.x 零适配直连"**——同一逻辑适用于官方 web：API 对齐做到位，4.1 web 理论上能连。

### 1.5 插件框架现状

`ApplicationHost.cs:1400` LoadPlugin → `:1833` GetPluginAssemblies（从 ProgramData/plugins 扫 DLL + 从 resources/plugins 复制预装）→ 硬编码插件名单（:1925 TvMazeProvider/Vimeo/Twitch…）。**框架代码完整但 0 插件装过、.NET 10 下从未验证**——这是比 25 个端点更基础的差距。

---

## 2. 方案决策

### 2.1 B6：插件框架优先（方案 C：框架验证 → 内置插件 → 长尾跟随）

- **方案 A 全进核心**：25 端点 × 中难度占核心开发量，且违背 Emby 架构语义（这些本来是插件），核心 API 面膨胀。否决。
- **方案 B 纯等外部插件**：老 3.5.3 插件生态已死，没有现成 DLL 可装，等于永远不做。否决。
- **方案 C（选定）**：
  1. **C1 先验证插件框架本身**（1 天 spike）：写一个最小测试插件（一个 service + 一个页面），编译 → 放 resources/plugins → 启动加载 → 端点 200。**这是三期的生态基石**——框架不通，B6 没有载体，fallback 才是核心实现
  2. **C2 Playback Reporting 做成内置插件**（user_usage_stats 19 端点）：数据源核心已有（activity log / sessions / user_data 播放记录），做插件不污染核心，同时当框架的实战验证
  3. **C3 Reports/ThumbnailSet/SMTP 跟随**：C2 完成后按用户需求拍板，默认继续暂缓（404 现状可接受）
- 判定点：C1 失败（框架在 .NET 10 跑不通且 1 天内修不好）→ B6 降级回"核心实现 user_usage_stats 最小集（user_list/user_activity/HourlyReport 3 个）"，其余维持 404，四期再议

### 2.2 LiveTv：XmlTv + M3U 免费全链路

- SchedulesDirect 付费且无 fixture，**不做**；HdHomerun 无硬件，**不做**
- 路线：**新建 XmlTvListingsProvider**（复用现成 `src/Emby.XmlTv` 解析库，接线 `ILiveTvListingsProvider`）+ 现成 M3UTunerHost = IPTV 通用组合（Emby 官方同样支持）
- 全链路验收：M3U+XMLTV fixture → /LiveTv/Channels 非空 → guide 有 program → 频道可播放 → timer 录制出文件
- 这是把 LiveTv 域从"API 在但永远空"变成可用功能的最短路径

### 2.3 元数据：逐 provider 验证与修复

- TMDb 优先（电影+剧集主源，v3 API 仍在服务，key 已硬编码）；音乐三件套（MusicBrainz/AudioDb/FanArt）跟随 D2 音频 fixture；TVDB v3 预处置：**默认禁用**（外部 API 已停），TMDb 剧集链路顶上，不花时间修死源
- 每个 provider 一份验证记录（成功/失败/根因/处置），修一个 commit 一个
- 图片下载链路（ProviderManager 远程图片入库）单独验证——海报是用户最直观的元数据体验

### 2.4 Web UI：Emby.Web 4.1 接入 spike（只出结论不承诺）

- 半天~1 天：取 Emby.Web 4.1.x 构建产物 → 替换 dashboard-ui（保留回滚）→ 连本地服务器 → 逐页面记录可用/白屏/API 缺口
- 产出决策报告：**换新 web 的 API 缺口清单 + 工作量估计**，给四期拍板；spike 期间老 UI 保留随时回滚
- license 与来源合规由 Chief 核实后才能转正式轨道（spike 本地试验无风险）
- 不选"修老 UI"：老 UI 补丁是往 8 年前技术上投资，方向错误；不选"自建"：工作量否决

---

## 3. 任务清单（三开发并行 + 条件轨道）

| 任务 | 内容 | 依赖 | 工作量 | 验收 |
|------|------|------|--------|------|
| **P3-A1** | TMDb 电影链路验证修复（Movie 无 nfo → 拉取 Overview/Poster/Year 入库） | — | 1d | Movie1 删 nfo 重扫后 TMDb 元数据+海报入库；验证记录入 docs |
| **P3-A2** | TMDb 剧集链路（Series/Season/Episode） | D3 剧集 fixture | 1d | 剧集 fixture 无 nfo 拉取全季元数据 |
| **P3-A3** | 音乐 provider（MusicBrainz/AudioDb/FanArt） | D2 音频 fixture | 1d | 音频 fixture 拉专辑/艺人元数据 |
| **P3-A4** | TVDB 禁用处置 + Omdb 验证 | — | 0.5d | TVDB 默认关闭不报错；Omdb 出验证记录 |
| **P3-B1** | XmlTvListingsProvider 新建（接 src/Emby.XmlTv） | — | 1.5d | XMLTV 文件 → guide program 数据入库 |
| **P3-B2** | M3U+XMLTV 全链路 fixture | P3-B1 | 1d | 频道列表非空 → guide 有数据 → 频道可播放 |
| **P3-B3** | 录制链路验证（timer → EmbyTV 出文件） | P3-B2 | 1d | 单次 timer 录制出媒体文件可播放 |
| **P3-C1** | 插件框架验证 spike（最小插件加载） | — | 1d | 日志见插件加载；测试插件端点 200；失败出 fallback 决策 |
| **P3-C2** | Playback Reporting 内置插件（user_usage_stats 19） | P3-C1 | 2d | /user_usage_stats/user_list 等 200；dashboard 插件页可见 |
| **P3-C3** | Reports/ThumbnailSet/SMTP 处置 | P3-C2 | 0.5d | 用户拍板记录：实现或维持 404 |
| **P3-D** | Emby.Web 4.1 接入 spike | — | 1d | 决策报告：页面可用性清单 + API 缺口清单 + 回滚验证 |
| **P3-E** | 性能优化实施（按优化候选清单） | 二期 T2 基线产出 | 视清单 | 每项优化前后对比数字；回归不劣化 |

**依赖图**：P3-A2←D3 fixture；P3-A3←D2 fixture；P3-B1→B2→B3；P3-C1→C2→C3；P3-E←T2。A/B/C/D 四轨道相互独立全并行。

**分工建议**：Developer（二期 T1 完）→ P3-A；Developer-2（音乐完）→ P3-B；Developer-3（剧集完）→ P3-C；P3-D 给先空出的开发（半天可插入）；Tester 每轨道验收 + 负路径用例入 test-plan；Reviewer 按总闸门。

**总闸门**：三套件回归不劣化（场景 ≥ 22/24、矩阵 ≥ 当前基线、契约 ≥ 16/2）+ 各轨道验收全过 + 每修一个 bug/功能一个 commit。

---

## 4. 风险登记

| 风险 | 应对 |
|------|------|
| R1 网络 provider 首跑出新 bug（同 ffmpeg 模式） | 预期内，登记 fail-classified 新分类，不算轨道失败 |
| R2 外部 API 已死（TVDB v3、可能 AudioDb/Fanart） | 逐个验证记录处置；死源禁用不修；TMDb 主源顶上 |
| R3 插件框架 .NET 10 跑不通 | C1 判定点 1 天为限，fallback 核心最小集 |
| R4 Emby.Web license 不合规 | spike 仅本地试验；正式接入前 Chief 核实 |
| R5 元数据拉取污染共享条目库 | fixture 条目用独立虚拟文件夹；验证前后 TotalRecordCount 对账 |
| R6 三期轨道与二期收尾抢人 | 依赖图已错开：A2/A3 等 fixture，E 等基线，不空转 |
| R7 XMLTV 大文件解析慢/内存 | fixture 控制规模；真实 IPTV 源留四期压测 |

---

## 5. 与二期/四期的衔接

- 二期收尾（T1 健壮性 + T2 基线 + 音乐/剧集 fixture）= 三期的启动条件，无空窗
- 三期产出决定四期方向：P3-D 结论 → Web UI 大轨道 or 继续补丁；P3-E 基线 → 性能优化批次；P3-C 结论 → 插件生态是否展开
