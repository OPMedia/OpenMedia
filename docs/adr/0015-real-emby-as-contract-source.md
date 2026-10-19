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
- `web/vite.emby.config.mjs` 是临时文件（必须放在 `web/` 下才能解析 bare specifier），**不进提交**。
- 真实数据下暴露的、**本轮未处理**的问题（属于设计取舍，需要产品决策，不是缺陷）：
  - 浅色主题下详情页是一块暗色（原版 Emby 的设计即如此），与浅色外壳（奶油色侧栏）并置；
  - 首页 22 个库 → 23 行 / 267 张卡 / 5543 个 DOM 节点 / scrollHeight 9789，全部一次性渲染；
  - 卡片副标题只有年份（`/Items/Latest` 不返回 `RunTimeTicks`，需要显式请求 `Fields`）；
  - 详情页左侧约 420px 空白（x=245→664）；
  - 跨库重复条目（同一部剧同时出现在「稳友荐剧」和「国产剧」），只有继续观看/接下来播放做了去重；
  - 用户头像 `/Users/{id}/Images/Primary` 404，回退到通用图标。
