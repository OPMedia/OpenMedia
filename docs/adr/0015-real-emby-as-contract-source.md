# ADR-0015：以真实 Emby 为契约源（mock 不得自造字段）

- **状态**：已采纳
- **日期**：2026-10-19
- **关联**：ADR-0004（以 Emby 4.8 OpenAPI 为 API 锚点）、ADR-0011（Web UI 与 Emby 对齐）、ADR-0014（前端组件基座与动效分层）

## 背景

用户的要求是：「你还是走我的 Emby 拉取真实数据下来吧，你现在这样说真的不好看，哪里有问题」。

在这之前，全部验收都跑在 MSW mock 上，而 mock 是这个仓库自己写的。
于是出现了一个隐蔽的结构性风险：**组件与 mock 互相印证，形成闭环**。
只要 mock 里的字段名和真机不一样，本地就能一直"通过"，问题只在接上真机的那一刻暴露。

本轮搭了一条真实数据链路（全部在仓库之外，不进提交）：

- `scratch/emby-proxy.mjs`：带缓存的转发代理（127.0.0.1:5175，统计口 5176），只读接口；
- `web/vite.emby.config.mjs`：**临时** vite 配置，把 `/emby` 代理到上面的代理，端口 5174；
- 服务对象：一台真实的 Emby **4.9.5.0**（ServerName「稳健115」），**22 个媒体库**。

**安全约束（用户明令）**：① 不拉视频流与视频链接 —— 代理里用正则黑名单直接 403，
实测 5 条播放/下载路径全部被拦（`/Videos/{id}/stream`、`/original.mkv`、`/PlaybackInfo`、`/Download`、`/*.mkv`）；
② 服务器地址与口令只存在于仓库外的 `scratch/emby.json`，不写入任何仓库文件、提交或探针输出。

## 事实：mock 与真机是三套契约

这一轮一共抓到 **3 个同源缺陷**，全都是"mock 自造字段"造成的：

| # | 缺陷 | 证据 |
|---|---|---|
| 1 | 剧集卡片副标题渲染出字面的 **`Sundefined:Eundefined - 如果没有面包`** | 组件读 `SeasonNumber` / `EpisodeNumber`；对真机全量扫描返回 JSON：`IndexNumber` 2 次、`ParentIndexNumber` 2 次、**`SeasonNumber` 0 次、`EpisodeNumber` 0 次**（实证某集 `IndexNumber=51` / `ParentIndexNumber=1`）。而 `types.ts` 声明的正是那两个不存在的字段，`mocks/db.ts:397` 也在造它们 —— 三方一致，所以本地永远测不出来。 |
| 2 | 首页 hero 抽到剧集时**整块没有背景图**，只剩一张竖版海报浮在底色上 | 真机剧集的 `BackdropImageTags` 是**空数组 `[]`**，大图挂在父级系列上：`ParentBackdropItemId="1332203"` + `ParentBackdropImageTags=["46af0034…"]`。而 mock 给每个条目都塞了 `BackdropImageTags: [b-id]`，"条目自己就有大图"这个假设在本地永远成立。 |
| 3 | 详情页**背景图全站不可见** | `.backdropContainer` 是 `z-index:-1`，被 `.backgroundContainer` 的 `html,.backgroundContainer{background-color:hsl(var(--background-*))}` **不透明底色**整片盖死。原版 Emby 的开关是 `.backgroundContainer.withBackdrop`（浅色 `rgba(255,255,255,.8)` / 深色 `rgba(0,0,0,.8)`）—— 规则一直在 CSS 里，只是壳子从来没加过这个类。 |

第 3 条还连带出一个**可读性事故**：详情页根节点是原版的 `darkContentContainer-item`
（`--theme-text-color-lightness:100%` → 白字，且该规则在 `@media (min-width:50em)` 内，**桌面端专属**），
它被设计成永远压在暗色 fanart 上。背景图一没，白字直接掉进浅色底：

> 实测浅色主题下标题 / 简介 / 演职人员 / 视频·音频·字幕标签的对比度 **全部是 1.09:1**（WCAG 正文要求 4.5:1）。

根因不是文字颜色，而是**背景图没显示**。

## 第二轮：首页 banner 重做时又抓到 3 个「请求参数被静默忽略」

用户对首屏的判断是：「首页的 banner 栏很丑啊，你做成这样一点都不好看」。
按"先量再改"的规矩把 hero 的数据池逐条打出来，发现丑只是表象，底下是三个**服务端静默忽略参数**的行为
——不报错、不警告、返回 200，字段全是合法的，只是**不是你要的那个集合**：

| # | 缺陷 | 证据（真机 4.9.5.0，22 库） |
|---|---|---|
| 4 | `IncludeItemTypes=Movie&Filters=IsUnplayed` **不带 `Recursive=true`** 时，Emby 直接忽略 `IncludeItemTypes`，返回**库本身** | 不带：`total=21`、`types={"CollectionFolder":12}`；带上：`total=24619`、`types={"Movie":12}`、12/12 有 Backdrop。后果是 hero 轮转池里混进了「追新剧集 / 追新电影 / 国产剧」三个**媒体库**，用户看到的"轮转图"其实是三个库的封面。 |
| 5 | `ImageTypes=Backdrop` 过滤参数**完全无效** | 带与不带该参数，返回体**逐字节相同**。所以"只要有大图的条目"必须**客户端筛**（`wideArtOf()`），不能指望服务端。 |
| 6 | `IncludeItemTypes=Series` 在真机上**结构性地不可能产出 banner** | 抽 12 个系列：`BackdropImageTags: []` 12/12、`PrimaryImageAspectRatio: 0.667` 12/12（只有竖版海报）。同理 `IncludeItemTypes=Episode&Recursive=true` 也会返回 Series/Movie，Episode 类型查询不可用。 |

顺带确认的路径陷阱：`/emby/Items/{id}`（非用户域）**404**，必须走 `/emby/Users/{uid}/Items/{id}`；
`/emby/Items/Latest` **404**，正确路径是 `/emby/Users/{uid}/Items/Latest`。

这 3 条同属一个教训：**mock 不会忽略参数**。mock 永远"你要什么给什么"，
所以"参数写错但本地全绿"这类缺陷，只有在真机才可能暴露 —— 而它比字段名错误更隐蔽，
因为返回体是合法的、类型是对的、没有一行报错。

## 决策

1. **`types.ts` 与 `mocks/db.ts` 一律对齐真机字段名**，删掉自造字段。
   mock 的职责是"复现真机的形状"，不是"造一套更方便的形状"。
   同类修复：`BackdropImageTags` 旁边补 `ParentBackdropItemId` / `ParentBackdropImageTags`。
2. **背景图取值收敛到唯一入口**：`api/endpoints.ts` 的 `backdropOf()` / `backdropUrl()`，
   内部处理"自己没有就找父级"的回退链。任何地方不得再直接读 `BackdropImageTags[0]`
   （`HomeHero`、`ItemDetailView`、`VideoOsdView` 已全部改走该入口）。
3. **恢复原版的设计意图，而不是新造配色**：详情页加 `.withBackdrop` 让背景图透出来，
   再用 `s-backdrop-dark` 把 scrim 固定成暗色渐变
   （Emby 浅色主题那条 80% 白 scrim 会让白字继续白压白）。
   渐变最低档 `.78` 在纯白底上仍有 **5.4:1**，保证任意亮度的海报上都够读。
4. **脏数据在渲染层过滤**：真机存在 `ProductionYear: 1`（实测「绝代风华苏东坡」等），
   1900 以前一律视为无效，不渲染。
5. **验证必须有一次"真机过一遍"**，并且**回看截图**——很多问题（背景图、碎图、对比度）
   只看 DOM 的"存在 + 文本 + opacity:1"是看不出来的。
6. **请求参数也要当契约测**：凡是靠服务端过滤的参数（`Recursive` / `IncludeItemTypes` /
   `Filters` / `ImageTypes`），必须**把返回体打出来核 count 与 types**，不能只看"有没有报错"。
   `useHomeHero.ts` 的数据池据此改成"宽查询 + 客户端筛"：服务端只负责排序与截断，
   "能不能当 banner"由 `wideArtOf()` 在客户端判定。

## 首页 banner 重做的定案（对应第 4~6 条）

| 决策 | 理由 |
|---|---|
| 主图 = **16:9 大图占左 63%**，右侧 35% 放"接下来"轮转缩略图（2 列 × N 行，16:9 铺满主图高度） | 用户原话：「背景图的比例不是 16:9 吗，那旁边肯定会有留白，留白放小的接下来要轮转的轮转图」。整条 banner 因此**没有空洞**：上一版是"一张 770×425 的卡片浮在奶油色空白里"。 |
| banner 高度**由 16:9 主图宽度反推**，不再用 `min(75vh, 860px)` 硬切 | 旧写法在窄屏把图压扁、在宽屏撑破首屏。令牌 `--s-hero-h` 因此**删除**（`--s-hero-min-h` 保留作短视口兜底）。 |
| 主图**只用横版大图**：`wideArtUrl()` = 自己的 Backdrop → 父级 Backdrop → 宽高比 ≈16:9 的 Primary；都没有就退品牌渐变 | 实测媒体库（CollectionFolder）的 `PrimaryImageAspectRatio` 是 **1.7778（横图）**，旧逻辑"不是 Backdrop 就当竖版"把它渲染成 2:3 的怪卡片。该分支已整段删除。 |
| 主图上的文案层用**两层叠加压暗**（横向 + 纵向），并给 `.hero-stage` 加 `isolation: isolate` | 单层纵向渐变在文案那一档只有 ~0.34 暗度，压在雪景剧照上白字读不出来。层叠上下文是必须的：不加时 `.hero-shade`（z-index 3）会跑到 `.hero-band` 的上下文里**盖住** `.hero-copy`（z-index 2），实测白字只剩 **2.3:1** —— 文案被自己的压暗层糊掉。 |
| 标题**不用 Emby 的 Logo 图**，一律渲染纯文字 | Logo 是片方提供的透明 PNG，颜色不可控：实测「梦幻西游」的 Logo 是深青色，压在深色压暗层上直接消失。纯文字走 `--s-hero-ink`，颜色可控、可量、永远读得出来。 |
| 次要 CTA / 暂停按钮用**半透明深色**填充 + 1px 浅描边，进度条轨道单独给浅色令牌 | 半透明白填充看着高级，但白字压在白色填充上实测只有 **3.0:1**（16px 正文不够 AA）。深色填充后 ≈17:1。 |
| 轮转缩略图栏**同时充当指示器**（当前项描边 + 8s 进度条），旧版底部圆点/前后翻页整组删除 | 一套导航，不摆两套。 |

量化验收（1600×900，真机数据，逐像素采样"文案背后的底色"再算对比度）：

| 区域 | 实测对比度 |
|---|---|
| 眉标 / 标题 / 主 CTA / 进度文案 | 12.6 ~ 18:1 |
| 轮转缩略图标签（12px 小字） | 6.3 ~ 16.4:1 |
| 主图与右侧栏间距 | 24px（无空洞） |
| 主图宽高比 | 1.7778（精确 16:9） |
| 移动端 390×844 横向溢出 | 0 |

## 顺带修掉的真机缺陷

| 缺陷 | 处理 |
|---|---|
| 行标题拼接成病句「最新2026十月番」 | 库名是主语、「最新」是限定语 → 后置为「2026十月番 · 最新」 |
| hero 标题是无上下文的「第 13 集」 | 剧集 hero 显示**剧名**（真机里确实有剧集名字就叫「第 13 集」），眉标补「剧名 S4:E13」 |
| 未看集数角标是裸数字 + 珊瑚色（吃 `--theme-primary-color`） | 补 `role="img"` + 「还有 N 集未看」；配色改走中性的 `--s-badge-bg/--s-badge-ink` 玻璃令牌。**不动 `--theme-primary-color` 本身** —— 进度条在用同一个变量，那是真·进度 |
| 缺海报时露出浏览器「碎图」图标 | 原来 `@error` 里写的是 `imgLoaded = true`，反而把裂图**显示**出来；改为 `imgFailed` → 隐藏 img + 品牌渐变（`--s-gradient-dusk`）与白标兜底 |
| `LoginView` 的 `backgroundContainer-withBackdrop`（连字符）匹配不到 `.backgroundContainer.withBackdrop` | 该页从未加载过背景图，所以这个类名写不写都不生效 → 删掉死类名并注明；「登录页随机 fanart」另立需求，不靠错类名假装已实现 |

## 后果

- 三条同源缺陷已修，且**从根上堵住**：mock 不再有第二套字段契约，所以本地跑绿 = 真机大概率也绿。
- 代价：以后新增 mock 数据必须照真机的字段名写。这是刻意的摩擦。
- 第二轮又证明了一件事：**mock 的第二个盲区是"它不会拒绝你"**。
  字段名错会立刻炸出来（TS 报错 / 渲染出 undefined），参数错则一路静默。
  因此凡是"服务端过滤"的参数，验收口径必须是**打印返回体的 count/types**，不是"请求成功"。
- `web/vite.emby.config.mjs` 是临时文件（必须放在 `web/` 下才能解析 bare specifier），**不进提交**。
- 真实数据下暴露的、**本轮未处理**的问题（属于设计取舍，需要产品决策，不是缺陷）：
  - 卡片副标题只有年份（`/Items/Latest` 不返回 `RunTimeTicks`，需要显式请求 `Fields`）；
  - 详情页左侧约 420px 空白（x=245→664）—— 待与真机 Emby 详情页逐像素比对后再定，不自行发明版式；
  - 跨库重复条目（同一部剧同时出现在「稳友荐剧」和「国产剧」）—— 用户明确要求**照常显示、不去重**；
  - 用户头像 `/Users/{id}/Images/Primary` 404，回退到通用图标；
  - 部分条目图片本身在服务端就取不到：`Items/1711918/Images/Primary` 404、`Items/1695809/Images/Primary` 500，
    由 `EmbyCard` 的品牌渐变兜底（不是前端缺陷，是源数据缺图）。

> 已在本轮解决、从上面这份清单里划掉的：
> 浅色主题下详情页整块暗色（ADR 决策 3 的后续，见 `ItemDetailView.vue`）；
> 首页 22 库一次性渲染 23 行 / 267 卡 / 5543 DOM 节点（改为分步行懒加载，首屏只挂 hero + 前几行）。
