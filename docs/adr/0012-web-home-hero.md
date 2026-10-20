# Web 首页改造为流媒体形态，并建立独立设计层（Serata 视觉）

Emby 网页端的布局与交互对齐已推进到"可验证"阶段（ADR-0011 的前置条件满足），
现在执行**对齐之后的自主改进**：把首页从"库入口集合"改造为流媒体平台形态，
并以 `serata-icons` 图标包为锚点重建整套视觉，不再沿用 Emby 的品牌色与主题体系。

## Decision

1. **侧栏**：媒体库行**只显示名字**，去掉行首图标；移除"元数据管理器"入口
   （用户侧栏本就没有，管理后台的 `advMetadata` 项与 `/dashboard/metadatamanager`
   路由一并删除）；选中态用左侧 3px 强调轨 + FLIP 平滑位移；分组折叠用
   `grid-template-rows: 0fr → 1fr`（不动画 height）；抽屉 280ms + 焦点陷阱 + Esc。
2. **首屏 Hero**：占视口约 3/4（`min(75vh, 860px)`，下限 460px）；左信息区
   （类型·年份·时长·分级 / 标题优先用 Logo 图 / 两行简介 / 主次 CTA / 进度）
   + 右侧 16:9 轮转画框（不拉伸；只有竖版图时降级 2:3）；8s 自动轮转，
   hover/focus/标签页隐藏时暂停，**显式暂停按钮**（WCAG 2.2.2），
   ←/→ 与滑动切换，`prefers-reduced-motion` 下不自动播放。
3. **首页顺序**：Hero → 继续观看 → 接下来播放（与继续观看去重）→ 每库一行最新；
   删除与库入口重复的"我的媒体"行。
4. **顶栏减法**：移除"获取 Emby Premiere"、设置齿轮、投屏占位；右侧收敛为
   搜索 + 主题切换 + 用户菜单。管理后台的 Premiere 入口与路由一并移除。
5. **移动端**：≤768px 改用底部 5 项导航（首页/媒体库/搜索/收藏/我的），
   抽屉保留用于媒体库与账户。
6. **设计层**：新建 `web/src/styles/serata/`（`tokens` / `base` / `emby-bridge`），
   新组件只用 `--s-*` 令牌与 `s-` 前缀类名，不再碰 `emby-*` 类名；
   **老页面（库页/详情/搜索/设置/后台）不重写**，靠 6 组 HSL 变量整体换肤。
7. **深浅双模**：`dark`（默认）/ `light` / `system` 三档，`useTheme()` 为唯一
   事实来源，`localStorage['serata.themeMode']` 持久化，`index.html` 内联脚本
   兜住首帧防闪；后台不再强制浅色，统一跟随用户选择。
8. **配色锚点**：`#1B1F3B → #46356F → #E0705A` 的黄昏色阶；珊瑚退居强调位
   （单屏 ≤5 处），主 CTA 走"纸与墨"——因为珊瑚上放奶白实测只有 3.01:1，
   不满足 AA，而墨字压在珊瑚上是 5.42:1。
9. **i18n**：外壳与首页的中文硬编码全部收进 `shell.*` / `home.*` / `hero.*`；
   同时把产品文案里的 "Emby Server / Emby Web" 改为 "Serata Server"。
10. **PWA**：补齐 `manifest.webmanifest`（图标包里的 maskable 方版终于用上），
    `index.html` 增加 manifest 与 `theme-color`（深浅两值动态更新）。

## Considered Options

- **A. 逐页重写为新设计层**：视觉最统一，但 20+ 个已经和 Emby 对齐好的页面
  全部推倒，返工量巨大，且对齐成果白扔。
- **B. 只改首页、其余保持 Emby 视觉**：改动最小，但两周后就会出现"首页像
  Netflix、库页像 Emby 4.9.5"的两套皮，产品感断裂。
- **C.（采纳）新页面走新层 + 老页面靠变量换肤**：Emby 的主题是完全 HSL 变量
  驱动的（`--background-*` / `--theme-primary-color-*` / `--theme-accent-text-color-*`），
  覆盖 6 组变量就能让老页面整体落到同一色阶上，不碰一行压缩 CSS。
  成本最低、返工最少，且**两者可以随时收敛**（未来逐页迁移到新层）。

## Consequences

- `web/src/styles/serata/README.md` 成为并行开发的冻结契约：令牌清单、动效规范、
  组件接口、i18n 键、铁律（只动 transform/opacity/filter、不写裸色值、单位用 px）。
- 对比度是硬约束而非口味：深色底奶白 18.49:1、珊瑚 6.15:1；浅色底墨 15.77:1、
  珊瑚仅 3.37:1（只准做填充/轨道，文字必须用 `#A83E2C` 的 5.69:1 档）。
- 老页面的视觉改动**集中在 `emby-bridge.css` 一处**，出问题回滚只需删三个 import。
- 后续新增页面一律走 `--s-*` 令牌；`emby495` 目录视为只读遗产，只允许整体删除、
  不允许逐条修改。

## 落地后追加的约束（验收阶段发现，已写进 `serata/README.md` 铁律 9/10）

1. **主题类有且只有一个所有者**。原 `DashboardView` 与 `VideoOsdView` 在 `onMounted`
   里整体赋值 `document.documentElement.className = [... 'theme-light' | 'theme-dark' ...]`，
   后果是：深色模式进后台整页翻白（且把 `useTheme` 注入的 `theme-dark` 一起抹掉，
   退出后不还原）；浅色模式播片把整站切成深色。现在主题类只由 `useTheme()` 维护，
   页面最多 `classList.add/remove` 自己那份结构性类。
2. **局部固定深色语境要在自己的根节点上做**。播放页 OSD 需要永远是深色（视频画面 +
   控制条），做法是给根节点挂 `.s-osd` 并覆盖局部令牌，而不是改 `<html>`。
   额外坑：老浅色主题在 `.skinBody` 上用的是**具体颜色**而非 HSL 变量，所以只覆盖
   `--theme-text-color-*` 不够，`.s-osd` 必须再落一个显式 `color`，否则控件继承到墨色、
   在暗画面上完全不可读（实测从 `rgba(24,27,47,.87)` 变为 `rgb(255,248,241)`）。
3. **`prefers-reduced-motion` 是初始暂停位，不是硬闸门**。系统开启减少动效时首页
   Hero 初始不自动轮转，但用户显式点「继续轮播」可以开启——WCAG 2.2.2 约束的是
   "自动"播放，用户主动触发不算自动。
4. **模态遮罩需要独立令牌**。`--s-scrim` 在浅色主题下是"提亮"用的（Hero 底部渐隐），
   复用到弹层会让页面看起来只是变淡而非被压暗；因此新增 `--s-scrim-modal`
   （深色 `rgba(6,7,14,.6)` / 浅色 `rgba(23,26,46,.32)`）。
5. **i18n 与换肤要一并对齐**：`library.tags` / `library.folders` 之前缺失、库页 tabs
   是借用 `library.filter` / `library.view` 占位；`dash.supportTeam` 等处残留
   "Emby"字样已全部改为 Serata。库页第 0 个 tab 是**库名本身**，运行时解析，不用静态键。
6. **老 CSS 的 `outline:0!important` 会静默吃掉新层焦点环**。`.button-link` / `.listItem`
   / `.emby-button` 一族在老主题里写了 `outline:0!important`，新层 `:focus-visible` 画的
   outline 在混了这些老类名的元素上完全不显示（实测首页 42 个 Tab 停靠点里 17 个中招，
   而且鼠标自测发现不了——鼠标点击不触发 `:focus-visible`）。现在 `:focus-visible` 同时
   落 `outline`（带 `!important`）**和**一圈 `box-shadow` 双层环：老 CSS 完全不碰
   `box-shadow`，所以任何混类名的元素都不会再丢焦点指示。
7. **Hero 是槽位里的一项，不是钉在顶部的一块**。`homeSection1..7` 必须能决定 Hero 出现在
   第几位：实现方式是让 `rows` 里多一条 `isHero` 占位行参与排序，模板遇到它渲染
   `<HomeHero>`；`.s-home-inner:has(> .hero:first-child){padding-top:0}` 吃掉父级上留白，
   `.s-home-inner > .hero{width:100vw;margin-inline:calc(50% - 50vw)}` 让它在超过
   `--s-content-max` 的大屏上仍然通栏（实测 1920 宽下 hero 1920×810 = 75vh 且无横向滚动条）。
8. **首帧底色要写在 `index.html` 内联 `<style>` 里**。dev 下 CSS 由 JS 注入，深色 reload
   会先出 2 帧纯白画布；`html.theme-dark{background:#0a0c18}` / `html.theme-light{...}`
   放在防闪脚本旁边，画布颜色早于样式包解析，生产同样受益。
9. **docked 侧栏不是 dialog**。常驻侧栏如果保留 `role="dialog"`，每个老页面都会向屏幕
   阅读器报一个"对话框"且 `aria-modal` 为空；现在按 `isDocked` 切成 `navigation`。
   另外：浅色主题的珊瑚是深一档的 `#D9604C`，老页面里写死的珊瑚一律改用 `var(--s-accent)`，
   否则同屏出现两种珊瑚（卡片进度条就是一处）。

## 修订 2026-10-20（用户实测反馈驱动，六条）

用户口径（原话）：「banner 栏的左上角为什么会有一个暂停/播放按钮，这不该出现」；
「文案还有继续观看和详情要放在图片上，变小适配 banner 栏的大小，手机和电脑都是」；
「遮罩浓度太大了，无论深色还是浅色，背后的背景图根本看不清」；
「所有字体都小了，你看看 Emby 的字体大小」；
「banner 放的是随机推荐的东西，而不是继续观看的图片」；
「你拉的真实数据不够覆盖我们会看到的页面」。

1. **暂停按钮不再常驻在 banner 角上**。Decision 2 当初为 WCAG 2.2.2 加的是"显式暂停按钮"，
   但它压在剧照左上角，用户第一眼就判定"这里不该有按钮"。现在它并进右下角那组轮转圆点，
   默认 `opacity: 0` + `pointer-events: none`，只有 `:focus-visible`（键盘 Tab 到）或已暂停时才显形，
   `aria-label` / `aria-pressed` / Tab 可达全部保留 —— WCAG 2.2.2 要的是"能停"，不是"必须露在外面"。
2. **文案在所有断点都压在图上**。原 Decision 2 的"左侧信息区 + 右侧 16:9 画框"在窄屏退化成
   "图一行、文案一行"，用户看到的是"两块东西"而不是一条 banner。现在 `.hero-copy` 与 `.hero-stage`
   同处网格单元 1/1、`align-self: end`，窄屏靠**收小字号**适配（`--s-fs-hero-sm` 26px、按钮 36px、
   隐藏两行简介），**不靠加高 banner**：`aspect-ratio: 16/9` 在 390 只有 201px、360 只有 184px，
   装不下最少 ≈216px 的压图文案；改用跟着宽度走的 `clamp(224px, 60vw, 280px)`，
   硬约束是 390×844 / 360×640 上 stage ≤ 视口 40% 且「继续观看」行仍在首屏。
3. **压字分工：全局收口 + 局部承托**。原实现靠一层 0.96/0.84 的 `--s-hero-scrim` 把整条 banner
   压暗来保证对比度，用户判定"背后的图根本看不清"。现在遮罩只负责把四条边收进页面底色，
   文案对比度交给 `--s-hero-copy-wash`（只画在 `.hero-copy-body` 背后），右半张剧照保持原始亮度。
   验收改成像素级三条判据（图区平均亮度差 ≤0.08、四条边 `EXCESS ≤3`、文字对比度 ≥4.5:1）。
   两个必须记住的接缝陷阱：`.hero-stage` 底色要用 `--s-bg`（不是 `--s-img-placeholder`，
   两侧被 mask 溶掉后露的就是它，实测差 17.4/13.3）；遮罩底部 0% 必须是 100% 的 `--s-bg`
   （写成 `color-mix` 会留 14% 剧照，实测差 16~29 的一条横台阶）。
4. **字号基准从"口味"改成"真机 Emby 的 computed font-size 为下限"**。实测真机 4.9.5 网页端
   （`:root` 90.15%）：body 14.424px、`.sectionTitle` 21.636px/w600、`.cardText` 14.424px、
   `.cardText-secondary` 13.268px、`.emby-button` 14.424px/w600。我们上一版对应 14/19/14/13/16 ——
   行标题小 12%。现在每一档都 ≥ 同语义元素：15/22/15/14/17，正文与副标题各抬一档。
5. **Hero 池子只装"用户真的接着看过的东西"**。① `Filters=IsResumable`（Limit 8）→
   ② 最近播放 `SortBy=DatePlayed Desc`（Limit 24，客户端只留有播放痕迹的）→
   ③ 最新入库**仅在 ①② 合计 <2 条时**兜底。旧逻辑把 ③ 当补位池，真机实测 6 张里 4 张变成
   与播放历史无关的新片（龙珠超/尼娜与萝扎/玩具总动员5/舞女大盗）。另按"图会撞"去重：
   解析出的横版图 key 或 `SeriesName` 任一已占用就跳过（实测曾有 3/6 个圆点是同一张父级剧照 + 同一标题）。
6. **`/Items/{id}/Images/Primary` 不能无条件请求**（真机无 Primary 时返回 **500**，不是 404）。
   13739 条真实条目里 1931 条没有 Primary，其中 1777 条有父级剧照、1857 条有系列海报可用；
   详情页「一个屋檐下」100 集全部没有 Primary，改前进一次页面打出 99×500+1×502、129 张卡只有
   29 张出图（22.5%）。现在卡片图源收敛到 `endpoints.cardArtOf/cardArtUrl`：**先看 tag，再拼 URL**，
   回退链 = 自己的 Primary →（横版卡）自己/父级 Backdrop → 系列海报 → 空串（走品牌渐变占位，不发请求）。

## 修订 2026-10-20（二 · 验收阶段实测追加）

上一节的六条是"按用户口径改"，本节是"改完自己量出的事实与判据"，全部有实测数字支撑。

1. **"遮罩太浓"的判据不能用"与原始剧照逐像素比"**。剧照从 1920px 缩到窄屏的 348px 是 5.5× 降采样，
   画面自身高对比边缘的重采样差就能到 0.2–0.8 —— 移动档**连"关掉文案与承托层"都过不了**，
   桌面 1280×800 的均值极干净（0.0074/0.0217）却卡在单点 0.1902。正确比法是**差分**：
   参照系 = 同一次运行把 `scrim + wash + mask` 全部关掉（两次都隐藏文案子元素，
   注意 wash 挂在 `.hero-copy-body::before` 上，隐藏整个 `.hero-copy-body` 会把承托层一起藏掉、差分测不到它）。
   判据区桌面 `x55–85%/y10–70%`、移动 `x25–75%/y15–25%`（移动档只有这条带还有净图）；
   **通过条件 mean ≤0.08 且 p99 ≤0.25**（单点阈值换成 p99：一个边缘像素不该判死，一整片洗白必须判死）。
   改后 8/8 运行通过（最紧 390×844-light p99 0.2269）。参照系自检：层全关 vs 原始剧照 mean 0.0026–0.0046。
2. **承托层上沿不得高于"剧照区顶部往下 25%"**（窄屏硬约束）。代价是**窄屏标题封成一行**（桌面仍两行）：
   360×640 的剧照区只有 224px，两行标题需要 `56（25% 净图）+ 157.6（两行文案）+ 40（圆点带）= 253.6px`，
   **数学上装不下**；收一行后 222.6 ≤ 224 刚好过线。这是"用密度换空间"——承托层密度一点没降（顶上仍是 0.72），
   只收面积，顶部羽化故意只留 3px（眉标字形顶离文案块顶只有 ≈3px）。超长标题在窄屏出省略号，是明确接受的取舍。
3. **换图时延是独立缺陷：`src` 只在翻面那一刻写入 → 点之前那张图从未被请求**（实测每次点击新增
   `/Images/Backdrop` 请求数恒为 1）。冷缓存下一次 MISS 打真机要 **1.4–3.7s**（117KB/245KB 实测），
   于是每次轮播/点圆点都有 **3.7–6.8s** 的"新标题 + 上一部剧照"窗口 —— 恰好把第 5 条修好的"图文对应"又反过来了。
   修法两条，都只动 `<script>`：① 池子一确定就整池 `new Image()` 预热（不在文档里，浏览器给低优先级，
   实测首图仍是六张里最快的 3256ms，池子 5s 内就位 < 8s 轮转窗口）；② 请求宽度按槽位算
   `clamp(900, round(slotW × dpr), 1920)`（窄屏 stage 只有 358px 却要 1920px 的图，5.4 倍过采样）。
   **口径在整个组件生命周期只算一次并缓存** —— 否则窗口一改大小 URL 就变，已预热的缓存当场作废。
   改后 5/5 ≤800ms（582–754ms，其中 ~330ms 是 CDP 点击下发开销，页内真实链路 260–353ms）、点击新增请求 0、6/6 HTTP 200。
   **验收这条必须量"用户实际看到的那张"**（`opacity > 0.5 && naturalWidth > 0`），
   **不能看 `is-front` 类名** —— 类名在图片加载完成之前就已切到新图，会得出"图已经换了"的假结论（两边都这么被骗过一次）。
4. **卡片图源与 padder 的两个真机陷阱**：
   - `cardPadder-horiz` / `cardPadder-portrait-horiz` **在 Emby 的 CSS 里根本不存在**
     （vendor `modules__cardbuilder__card.css` 只有 `backdrop/square/portrait/fourThree/banner`；
     参考 `cardbuilder.js` 也只拼 `cardPadder-{shape}`）。实测 `.cardPadder-portrait-horiz` = **165×1px**，
     图容器塌成一条线 —— 库列表「集」页签的图从来没显示过。一律改用真实存在的 `cardPadder-backdrop`(16:9)，
     `portrait-horiz` 降级为 `backdrop` 的别名（padder 与请求尺寸都要跟着走 16:9）。
   - `/library/{id}?tab=episodes` 必须带 **`Filters=IsNotFolder`**：真机在库级查询下**忽略
     `IncludeItemTypes=Episode`**（日番 1464 Series → 带 Filters 后 50068 Episode）。另：**真机完全忽略 `Fields` 查询参数**。
5. **长标题会把卡片撑破**：vendor `.cardTextActionButton` 是 `width:auto` + `text-overflow:ellipsis` 但**没有 `overflow`**，
   而 `.cardText` 是 `white-space:nowrap; overflow:hidden` → 子按钮长到 353.7px 撑出父盒，桌面实测 31 对重叠、48/60 张卡的父盒溢出。
   修法一条：`.cardText > .cardTextActionButton { max-width:100%; overflow:hidden }`（48/60 → **0/60**，重叠 31 → **0**）。
6. **后台设置页调的是一个规格里不存在的端点**：`/System/Configuration/Fields` 在 `openapi.json` 里 **0 命中**，
   真端点是 `/System/Configuration/{Key}`；本机只认 `branding / metadata / livetv / encoding / subtitles / dlna / sync / cinemaMode`
   这 8 个 Key（`general / transcoding / network / database / convert` 等一律 500）。所以 6 个设置页原本全空。
   现在 `Promise.allSettled` 降级 + 页面诚实提示（端点 → 状态码 → 真实条数），不再静默吞异常。
   同类：`/Auth/Keys` 条目**没有 `Name`**（真机是 `AppName` / `AccessToken`，`Id` 是 number），`TotalRecordCount` 恒 0。
7. **虚拟库的库级递归不下沉**（已记账的服务端限制，不修）：`稳友荐剧 2000000002` / `2026十月番 2000000007`
   的 `Recursive=true` 与 `false` 返回同一批 52/70 个 Series，而**剧集确实存在**
   （《一斩苍穹》14 集、《FX战士久留美》3 集，`/Shows/{id}/Episodes` 正常）。客户端唯一解法是逐 Series 拉再聚合
   （52~100 条额外请求），**明确不做**；「集」页签退化为列 Series（优于空白）。

## 验收基线与"以后怎么复查"

改完之后**不要**用"看起来对不对"验收，用下面这些可复现的锚点（都实测过，数字见 `.tmp/verify2/VERIFY-REPORT.md`）：

| 锚点 | 值 / 判据 |
|---|---|
| hero 剧照尺寸 | `maxWidth = clamp(900, round(slotW × dpr), 1920)`，1600×900 @dpr1 → **1552**、390×844 @dpr2 → **900** |
| 预取 | 池子就绪后静置 3s，6 个 `/Images/Backdrop` **必须全部已请求**；点圆点每次**新增请求 = 0** |
| 换图时延 | 点击 → 可见剧照（`opacity>0.5 && naturalWidth>0`）变更 ≤ **800ms**（实测 212–293ms 净） |
| 净图区 | 差分 mean ≤0.08 且 p99 ≤0.25（参照系 = 同运行关掉 scrim + wash + mask） |
| 接缝 | 四边 `EXCESS(noImg=0) ≤ 3`，且必须有 `noScrim` 对照组（否则"没接缝"可能只是"没遮罩"） |
| 对比度 | 标题 ≥3.0、其余 ≥4.5（WCAG AA），≥6 张剧照 × 深浅两主题 |
| 字号 | 每一档 ≥ 真机 Emby 同语义元素的 computed font-size（卡片主 15>14.424、行标题 22>21.636…） |
| `HomeHero.vue` 的 `<style>` 段 | 冻结版 sha256 **`D0EB3E89C04BA3CE…`（内层全文，646 行）** —— 本次只改了 `<script>`，改 CSS 前先比这个 |

两处**明确保留、不改**的行为，写在这里免得后人重复讨论：
- `GET /System/Configuration/Fields` 是真机**服务端 500 空 body**（`openapi.json` 里也没有这个端点），
  客户端**保留发起 + 诚实降级 + 页面提示**（端点 → 状态码 → 真实条数），不静默吞。
  它会在后台设置页的失败请求里出现 6 条 —— 这是**上游缺陷的可见化**，不装护栏是为了让修服务器的人看得见。
- 真机存在 `/Items/1677500` 这类"元数据 404 但图片端点 200"的数据不一致，属服务器侧脏数据，前端不做特判。

## 修订 2026-10-20（三 · 用户第二轮实测反馈，七条）

> **前提（这一轮最重要的认知纠偏）**：本项目是**前端**工程（`OpenMedia/web`），**后端是我们自己写的**
> （`MediaBrowser.Api/`、`Emby.Server.Implementations/` 等，按 ADR-0001 往 Go 重写），API 契约锚定 Emby 4.8/4.9 OpenAPI。
> `https://tv.wenjian.de` 上的真实 Emby **只是"真数据夹具"**。
> 上一轮我拿夹具 `/Users/{uid}/Suggestions` 的 **8–12 秒**延迟去改架构（双源、缓存、超时降级），
> 被用户当场纠正：**夹具的慢不代表我们后端慢，不许把夹具延迟写进设计决策**。这一轮所有设计只依据契约（`SuggestionsService.cs`），
> 不依据夹具性能。

1. **banner 标题永远一行**（「如果遇到长标题不要做成几行显示 这很丑 直接一行过去就行了」）
   → `.hero-title-text` 在**所有断点** `-webkit-line-clamp: 1`（此前桌面是 2）。
   `padding-bottom` 是**字形安全垫**不是间距：line-clamp 的裁切边界是 padding box，
   而 `--s-lh-tight`(1.15) 比字形框(1.353em)矮，不留垫子降部会被削 —— 桌面 6px、窄屏 3px。
   实测 5 档 `getClientRects().length = 1`、高/行高 1.10~1.12；34 字超长标题 5 档全部截断且仍 1 行。
2. **删掉标题上方的眉标**（「标题上面为什么要显示分集那些东西呢？完全没有必要」）
   → `.hero-eyebrow` + `metaLine` + 它的两处 `::before` 承托补丁**整条删除**（骨架态里的眉标占位条也删）；
   文案块首个子元素变成 `.hero-title`，`--s-i` 序列改为 标题0 / 简介1 / 按钮2 / 进度3。
   删完文案块矮 26.1px → 承托层上沿由 29.1%/25.9% 升到 40.0%/37.3%，净图区只多不少。
3. **banner 池只许是"随机推荐"**（「我反复说了 Banner栏只显示随机推荐的条目…Emby也有这个api」）
   —— 这是对**第一轮第 5 条的纠偏**：当时把"放随机推荐"读成了"别放继续观看"，实现成
   `IsResumable ∪ 最近播放 ∪ 最新入库`，方向正好相反（记为一次**需求误读**，不是执行失误）。
   → 唯一来源 `GET /Users/{uid}/Suggestions?MediaType=Video&Type=Movie,Series&Limit=6&EnableTotalRecordCount=false&Fields=…`。
   **契约依据是本仓后端自己的实现，不是猜夹具**：`MediaBrowser.Api/SuggestionsService.cs` =
   `OrderBy=ItemSortBy.Random` + `IncludeItemTypes=Type` + `MediaTypes=MediaType` + `IsVirtualItem=false` + `Recursive=true`
   —— 即"服务端随机挑 N 个影片/剧集"。`IsResumable` / `SortBy=DatePlayed` / `Items/Latest` 三层连同 `hasPlaybackTrace()` 一并删除。
   客户端只留**防御性归一化**（夹具 4.9.5 不认 `Type`：`Limit=6` 会回 4 Episode + 1 Folder + 1 Series）：
   `Movie/Series` 直接用；`Episode` 且有 `SeriesId + ParentBackdropImageTags[0]` 时映射成**它所属的剧**
   （标题 `SeriesName`、跳转 `SeriesId`、图 `/Items/{SeriesId}/Images/Backdrop?tag=父级tag`，并清掉那一集的
   `ImageTags/UserData/ParentBackdrop*` 防张冠李戴）；`Folder/Season/其它` 丢弃；
   去重按 Id + 横版图 key + 系列 key；不足 6 条只补一次 `Limit=24`，不为凑数放宽过滤。
   实测：两次独立会话池子不同、与 `IsResumable`（14 条）剧名交集 **0**、圆点 6、主按钮"播放"。
4. **收藏页首个标题压在顶栏底下**（「第一个标题和顶栏的间隔太小看着很冲突」）
   → 根因不是"间距小"而是**负间距**：`.s-home-inner` 的 `padding-top` 只写 `--s-sp-9`(48px)，
   而顶栏 `fixed` 64px；首页靠 hero 自己让位（`:has(> .hero:first-child){padding-top:0}`），
   收藏页 `heroSlotActive=false` 没有兜底 → 实测首标题 top=48 / 顶栏 bottom=64 = **-16px**（移动端 24-56 = **-32px**）。
   改成 `calc(var(--header-height) + var(--s-sp-6))`（`--header-height` 已含 `env(safe-area-inset-top)`，
   ≤768px 自动切 `--s-header-h-sm`），并**删掉 ≤640px 里写死的 `padding-top: var(--s-sp-6)`**（否则移动端重新压回去）。
   实测 5 档间距恒 **24px**、横向溢出 0；首页 hero 在首位时 `padding-top:0` 与 `heroShot.top=64/56` 未变。
5. **顶栏字号小**（「顶栏的字体大小太小了 我在上一轮就已经提到了 你还是没改」）
   → 真机 Emby 4.9.5 实测 `.emby-tab-button` = **16.1549px / 600**（`.skinHeader .emby-button` 同值），
   我们 `.s-tab` 只有 **14px / 500** → 逐值对齐 16.1549px/600。
   实测 1600/1280/1024 可见 tab=2、无重叠、无裁切、横向溢出 0；≤768px 顶栏 tab 本就 `display:none`（移动端设计如此）。
   附：抽屉 `.s-nav__label` 15px/500 已 ≥ 真机 `.navMenuOption` 14.424px/500，**不动**。
6. **设置页砍掉不需要的 8 条**（点名：转码 / 电视直播 / 神医助手 / Backup & Restore / Diagnostic Options / Open Subtitles / Subtitle Options / User Sessions）
   → `App.vue` 的 `adminSections` 删这 8 条、`router/index.ts` 删对应 8 条路由、
   `AdminPluginView.vue`（6 条插件路由的共用存根）**整个删除**、`AdminSettingsView.vue` 保留但清掉 `transcoding/livetv` 死分支。
   实测侧栏 25 → **17 条**、17 条逐个点击 0 死链、8 个 route 字符串在 `src/` 全域 0 命中；
   顺带删掉 3 个随之变成死键的 i18n 键（`shell.searchPlaceholder` / `dash.serverLiveTv` / `dash.serverTranscode`，zh/en 同步，371 键对齐）。
   **还剩 17 条**（供继续砍）：显示 / 主页面 / 播放 / 字幕 / 配置 ｜ 控制台 / 通用 / 用户 / 媒体库 / 网络 / 数据库 / 转换 / 计划任务 / 日志 ｜ 设备 ｜ 插件 / API 密钥。
7. **搜索只有一个入口，且搜索页要是"能看历史的简单搜索页"**
   （「搜索栏为什么还是有两个 而且两个搜索栏指向的搜索页都是不对的 搜索页就是搜索页 简简单单的一个搜索页 可以看到搜索历史的搜索页」）
   → 改前实测：桌面可见输入框 1 个（抽屉输入框）、移动 **0 个**；入口有"顶栏放大镜 + 抽屉输入框 + 抽屉搜索行 + 移动底栏 tab"四个。
   删掉**顶栏放大镜**与**抽屉搜索输入框**（模板 / `drawerSearch` ref / 两个处理函数 / `clearTimeout` /
   `.s-drawer__search*` / `.s-input*` 样式全清），保留"抽屉搜索行 + 移动底栏 tab"这一套导航语义。
   `SearchView.vue` 重做：顶部**可见**输入框（进入自动聚焦、输入即搜 debounce 300ms、回车立即搜）、
   `localStorage['serata.searchHistory']`（最多 10、最近在前、去重、规范化）、单条删除 + 清空、
   无查询词只显示历史（**删掉原「推荐」列表与 `Items/Latest` 请求**）、`?q=` 回填。
   实测：**每个状态可点搜索入口恰好 1 个**（桌面抽屉关 0 → 靠汉堡展开；抽屉开 1；移动抽屉关 1 = 底栏；
   移动抽屉开 1 = 抽屉行，底栏被 `html.withDialogOpen .s-bottom-nav{display:none}` 让位 —— 底栏 `--s-z-nav`(80) > 抽屉(60)，
   改前它浮在模态之上、`elementFromPoint` 真的能点到，等于又一个"两个搜索"）；
   搜索页输入框可见 640/364px、聚焦正确、无查询词标题 `["搜索","搜索历史"]`、`Items/Latest` 请求 0、
   输入"沙丘"只发 1 次 `SearchTerm=沙丘`、历史落盘 `["沙丘"]`。
   **一个明确取舍**：桌面首页（覆盖式抽屉默认关闭）搜索要"汉堡 → 搜索"两步；因为只要给顶栏加回放大镜，
   在"库页 / 搜索页 / 设置页"这种 docked 抽屉语境里就会同时出现"放大镜 + 抽屉搜索行"两个入口，又回到用户投诉的状态。

### 第三轮验收锚点（覆盖上面 1–7）

| 锚点 | 判据（实测值） |
|---|---|
| 标题行数 | 5 档 `getClientRects().length = 1` 且 `高/行高 = 1.10~1.12`；34 字标题仍 1 行 |
| 眉标 | `.hero-eyebrow` 计数 **0**，`.hero-copy-body` 首个子元素 = `.hero-title` |
| banner 池来源 | banner 只发 `/Suggestions`（`Limit=6`，补足时 `Limit=24`）；资源表里的 `IsResumable`/`SortBy=DatePlayed`/`Items/Latest` 全部来自 HomeView 的"继续观看 / 最新"**行**（`Limit=20` / `Limit=12`），与 banner 无关 —— 判据要按**请求签名**比，不能按 URL 里出现过的字符串比 |
| 池子随机性 | 两次独立会话（不同 DeviceId → 不同 api_key，绕开夹具代理按 URL 缓存）池子不同；与 `IsResumable` 剧名交集 **0** |
| 收藏页间距 | 5 档 `首标题 top − 顶栏 bottom` = **24px**（改前 -16 / 移动 -32） |
| 顶栏 tab | `.s-tab` computed `16.1549px / 600`，无重叠、无裁切、横向溢出 0 |
| 设置侧栏 | 可见条目 **17**，用户点名 8 条 0 命中，8 个 route 在 `src/` 0 引用 |
| 搜索入口 | 每个视口 × 抽屉开/关 = **1 个可点**（`elementFromPoint` 判定）；顶栏放大镜 0、抽屉搜索输入框 0 |
| 搜索页 | 输入框可见且聚焦；无查询词只显示"搜索历史"；`Items/Latest` 请求 0；`serata.searchHistory` 落盘 |

第三轮冻结哈希（`Get-FileHash -Algorithm SHA256`，改前先比这个）：

| 文件 | sha256 |
|---|---|
| `web/src/components/home/HomeHero.vue` | `FFB17471057C831F37D0BCC0B4C37330A04D6313DEFEBBA54C5428939E97EC9D` |
| `web/src/composables/useHomeHero.ts` | `F4186D212AB3107BCFD0DEB2E43FA18FC486C47E29CA266BA4477015D42AF586` |
| `web/src/App.vue` | `7C22509084E26438B21BD7A86D7920FCA75EA25B3406F7CAE3610093DE29DAA0` |
| `web/src/views/SearchView.vue` | `6DD06B5B3D66885C89D679026D3FC993A88BF48F8FAB78B3D1C0A193EE5B4F07` |
| `web/src/views/HomeView.vue` | `86A5067C53CCB61C897CBB3BE55BE6C5E158731E04B2ABF7E89050305EB8138F` |
| `web/src/views/admin/AdminSettingsView.vue` | `48AF866250301FAAB24FE8DB7E49057D007431F8A4BD6BA2124C71770BA95E06` |
| `web/src/router/index.ts` | `CB71720529B7320EB44D9C0C9BA40179A50395BB3E45B65B92A7A6996499C773` |
| `web/src/i18n/locales/zh-CN.json` | `EFB095F22068C0AA683DE5647A551C7CBB023EA4D88CC1AD5350D954F2992350` |
| `web/src/i18n/locales/en.json` | `4DAFE111D6955CEB5E5ECE71FB58EE8DC274A32C7B2358DBC62583691D0EF41C` |

> 上一节里 `HomeHero.vue` 的 `<style>` 冻结哈希 `D0EB3E89…` 已被本轮 ①② 作废，
> 改 CSS 前请用本节的整文件哈希。

## 修订 2026-10-20（四 · 用户第三轮实测反馈，三条）

原话：

1. 「搜索栏不需要两个 侧边栏那个删掉」
2. 「另外侧边栏里面的我的媒体库要显示媒体库呀 你现在都不显示的」
3. 「第二个搜索页选中搜索框之后为什么会有方框框着 看着好丑不需要那个方框」

### 1. 搜索入口：侧栏那条删掉，桌面改回顶栏放大镜

上一轮（修订三·第 7 条）把顶栏放大镜删了、只留"抽屉搜索行 + 移动底栏 tab"。
这一轮实测发现那个方案的真正问题：**≥60em 时抽屉在搜索页是常驻 docked 的**，
于是搜索页同屏出现"侧栏「搜索」行 + 页面输入框"——还是一屏两个搜索入口。

现在的口径（每屏最多一个，互斥）：

| 语境 | 唯一入口 | 其余 |
|---|---|---|
| 桌面 >768px，非搜索页 | 顶栏放大镜 `.s-iconbtn--search` | 侧栏无搜索行、无输入框 |
| 桌面 >768px，搜索页 | 页面自己的 `#searchPageInput` | 放大镜由 `v-if="!isSearch"` 关掉 |
| 移动 ≤768px | 底栏「搜索」tab | 放大镜 `display:none`（底栏已覆盖） |

改动：删掉抽屉里那条「搜索」导航行（模板 + 注释）；顶栏放大镜恢复，但加两条约束
（`v-if="!isSearch"` + ≤768px 隐藏）。`activeKey` 里 `'search'` 分支保留（只影响竖条是否显示）。

### 2. 侧栏「我的媒体」整段空白 —— 真 bug，根因是挂载时机

改前**可复现**：清空 `localStorage` → `/login` → 填真机账号提交 → 回到首页 → 开抽屉：
`libRows=0 / libRowsInDom=0 / collapseHeight=0`，侧栏只剩「收起 · 首页」，
分组头「我的媒体」展开后一行都没有。而服务器侧完全正常：
`GET /Users/{uid}/Views` 返回 22 个 `CollectionFolder`（稳友荐剧/稳友荐影/2026十月番/…）。

根因：`App.vue` 是根组件，`onMounted` 只在**整页加载**时跑一次。从登录页进来的那次挂载
`auth.user` 还是 `null`，`loadViews()` 第一行 `if (!auth.user) return` 直接退出，
之后**再没有任何东西叫它** → 整个会话 `views` 恒空。只有 F5（这时 localStorage 里已有 token/user，
挂载瞬间就非空）才会恢复 —— 所以"有时有、有时没有"。

修复：把 `loadViews()` 从 `onMounted` 挪进 `watch(() => auth.user?.Id, …, { immediate: true })`，
登录态一变就（重）拉，登出时清空（`views.value = []`），顺带 `?? []` 兜住接口返回无 `Items` 的情况。

> 过程教训（写下来免得再犯）：上一轮的验收脚本是"先注入 localStorage 再导航"，
> 天生绕过了登录流程 —— 结构性地看不见这类"挂载时机"缺陷。
> 本轮验收改成**走真实登录表单**（填 `#txtManualName` / `#txtManualPassword` + 点 `.paperSubmit`），
> 才把这个 bug 复现出来。

### 3. 搜索页输入框的"方框"：不是 focus ring，是常驻边框

实测（CDP `CSS.getMatchedStylesForNode` + 聚焦/失焦计算样式对比）：
输入框本体 `outline: none 0px`、四边框 0、无阴影；容器 `.searchInputContainer`
**聚焦前后计算样式零差异**。那个"方框"是容器上常驻的
`border: 1px solid var(--s-line)` + `background: var(--s-raised)` + `border-radius: var(--s-r-md)`。

修复：拆掉盒子（`border: 0; background: transparent; border-radius: 0`），
只留一条 `border-bottom: 1px solid var(--s-line)` 当"这里能打字"的提示；
`padding` 归零让底线宽度等于文字宽度；补 `align-self: flex-start`
—— 因为父级 `.searchFieldsInner` 是居中列，没有边框后那根线会悬在中间、
和下方左对齐的「搜索历史」对不上（≤768px 容器本就占满宽度，这条不改移动端）。

### 第四轮验收锚点

| 锚点 | 判据（实测值） |
|---|---|
| 侧栏搜索入口 | `.s-nav__row` 里「搜索」= **0 个**（DOM 里也没有） |
| 每屏搜索入口 | 1600/1280/1024 × 首页抽屉开/关 = **1**（放大镜）；同三档搜索页 = **0 入口 + 1 可见输入框**；768/390/360 × 首页 = **1**（底栏 tab）、搜索页 = **1**（底栏 tab）+ 1 输入框 |
| 我的媒体 | 真实登录表单流程后侧栏库行 = **22**，与 `/Users/{uid}/Views` 的 22 个库**逐个一致**；点「稳友荐剧」→ `/library/2000000002`，顶栏标题「稳友荐剧」 |
| 库行跳转 | 抽屉打开态真实鼠标点击 → 1s 内跳 `/library/<id>`，抽屉自动收起 |
| 方框 | 容器上/右/左 border-width **0**、下 **1px**、圆角 **0**、底色 `rgba(0,0,0,0)`、阴影 none；输入框 outline none / 四边 0；**聚焦前后完全一致** |
| 放大镜 | 桌面点击 → `/search`，输入框可见且自动聚焦，顶栏放大镜计数归 0 |

### 第四轮冻结哈希

| 文件 | sha256 |
|---|---|
| `web/src/App.vue` | `B40C2FE9B83E33D07AD1045220F1205E85B90DFA0D4EDEBAE02FD4058BF717A8` |
| `web/src/views/SearchView.vue` | `D6CF0E6885643CC6213D5D891A7E947BB3B077EC2813412A17F697509559549D` |

> 其余 7 个文件（`HomeHero.vue` / `useHomeHero.ts` / `HomeView.vue` / `AdminSettingsView.vue` /
> `router/index.ts` / `zh-CN.json` / `en.json`）本轮**一个字节没动**，哈希与上一节相同。

## 修订 2026-10-20（五 · 用户第四轮实测反馈，五条）

原话：

1. 「优化搜索页 搜索出来的候选结果和结果都要用卡片式 不要用条式 很难看 标题过长就省略」
2. 「我们现在没有多选 卡片左上角的多选按钮不需要了」
3. 「媒体库右上角的两个 一个过滤器 一个设置 这两个都没做好」
4. 「为什么每个卡片悬浮鼠标之后显示出来的按钮的右半都会有黑色的描线 看起来很丑啊 还会让人觉得自己的观看进度到那了 误导人了」
5. 「卡片的右下角的更多按钮 点击之后弹出的列表不是按照位置出现的 这个弹出的列表 在卡片的任意位置右键也会在右键卡片的位置弹出继续优化吧」

### 1. 搜索页：行式列表整体换成卡片网格

改前 `/search?q=沙` 是 40 条 `listItem` 行（每条宽 1000+px、高 ~105px），行内是「小缩略图 + 标题 + 类型/年份 + ⋯」。
改成与库页同一套 `EmbyCard`（`shape="portrait"`，2:3 海报），容器 `.itemsContainer.vertical-wrap.padded-bottom-page.searchResultsGrid`。

同时删掉的死代码：行标记、行内那个 `⋯` 按钮、`rowArt()`、`ICON_MORE`、`itemTypeName()`、`go()`、`useRouter/router`、`cardArtUrl` 引入。

标题省略号：`.s-card-info .cardTextActionButton` 上
`display:inline-block; inline-size:100%; max-inline-size:100%; overflow:hidden; white-space:nowrap; text-overflow:ellipsis`。
实测最长标题「从球场到商场：沙奎尔·奥尼尔的品牌经营路」：`scrollWidth 277 / clientWidth 135`，
仍单行省略、卡宽不变（156.3px）。

> 顺带作废：`search.typeMovie` / `search.typeSeries` / `search.typeEpisode` 三个 key 随 `itemTypeName()`
> 一起失去全部消费者（`src/**` 全量 grep：除两份 locale 的定义处外 0 引用）。
> 本轮**故意不动 locale 文件**，留待下次清理。

### 2. 多选入口整体下线

`.chkItemSelectContainer` label（卡片左上角那个圆勾）、`item-multiselected` 类、
`multiSelected` / `toggleMultiSelect`、以及 `open()` 里的多选分支全部删除；
`web/src/stores/multiselect.ts`（37 行，全库唯一消费者就是这里）**整个文件删除**。

`open()` 现在恒定 = 跳转，不存在"先勾选不跳转"的中间态。实测库页
`.chkItemSelectContainer` / 卡内 checkbox / `.item-multiselected` 合计 **53 → 0**；
点卡片左上角仍进详情（`/item/1310342`）。

### 3. 过滤器 / 设置两个弹层：四个独立缺陷

改前实测：设置面板 **509.4×179.2**，内容区被压到只剩 **24.2px** 可见（滑条/下拉几乎看不见）；
过滤器内容 328/328 完全不可滚，6 个控件压在**绝对定位的 footer** 底下。逐条根因：

| # | 现象 | 根因 | 修法 |
|---|---|---|---|
| a | 面板高度失控 | 中心变体是 `inset:0; margin:auto`，我最初按"内容自适应"写成 `height:auto` → 等于撑满包含块（720px） | `:content-style` 传 `height:fit-content` + `max-height:min(84vh,720px)` + **显式 `minWidth:0`** |
| b | 内容区不可滚 | footer 绝对定位、内容区没有 `min-height:0`，flex 子项默认 `min-height:auto` 拒绝收缩 | `.formDialogFooter{position:static}`、`.formDialogContent{flex:1 1 auto; min-height:0; overflow-y:auto}` |
| c | 单选/开关是原生控件 | 从未 token 化 | `appearance:none` + 18×18 + `--s-accent`；开关做两态 |
| d | 「图像大小」滑条拖了没反应 | vendor `.emby-slider` 让它 `opacity:0`（主因），而且**根本没有消费者** | 恢复可见 + 把 `--user-cards-size-adjust` 绑到 imageSize |

`minWidth:0` 不是可选项：vendor `.formDialog{min-width:clamp(20%,60ch,50%)}` ≈ 432px 会赢过内容宽度。

「图像大小」三档实测卡宽 **138.9 / 156.3 / 178.6px**（7 / 8 / 9 列），
`--user-cards-size-adjust` = `1 / 0 / -1`；`serata.library.viewMode` 与 `serata.library.imageSize`
落 localStorage，刷新后仍在（视图=列表 → 53 行；imageSize=1 保持）。

筛选态按钮用 **inset box-shadow** 而不是 `outline` —— `.emby-button{outline:0!important}` 会把 outline 吃掉。

### 4. 悬浮按钮的"黑色描线"：UA 默认边框，不是我们的样式

根因：这批悬浮按钮**没有 `emby-button` 类**，于是吃浏览器 UA 的默认 `border: 2px outset`。
outset 会把右下两条边画深 → 看起来就像一条"看到一半的进度弧"。实测库页 403 个可见按钮里 **212 个**中招。

修法：`.s-card .cardOverlayButton-fab { border: 0 }`（带根因注释）。
改后库页 + 首页 + 搜索页可见按钮带 `outset/inset/ridge/groove/double` 的 = **0**。
按钮盒子 34.3 → **30.3px**（正好等于 vendor `.emby-button` 的几何），底色 `rgba(29,35,69,.7)` 与行高不变。

### 5. 更多菜单：锚到触发点，而不是固定在左上角

改前用 `SSheet variant="pop-start"`，面板被钉在 `left:8px / top:115px`，与触发点无关：
按钮在 (1493,843) 时面板离它 **1163.1px**；右键点离面板 **315.7px**。

修法（`ItemMenuSheet.vue` 新增 `anchor` prop + `panelStyle` 计算）：
`PANEL_W=320`、`ANCHOR_GAP=8`、`VIEWPORT_EDGE=8`、`MIN_SPACE_BELOW=240`。
下方放不下就向上翻（`bottom = vh - aTop + 8`），右边放不下就向左展开（`left = aRight - PANEL_W`）。
锚点来源三处：按钮 `getBoundingClientRect()`、右键 `e.clientX/clientY`、触屏长按点。

实测间隙：首卡按钮 **8.4 → 7.7px**、底部卡向上翻 **8.0px**、右键点 **7.7px**、
右边缘向左展开 7.2px、长按 8px；900×600 与 700×500 也在视口内。

> 决策留档：桌面右键**保留**（用户抱怨的是位置，不是存在；说「不需要了」时才删）；
> 移动端长按保留 —— `.cardOverlayContainer` 在 `@media not all and (pointer:fine)` 下是 `display:none`，
> 长按是触屏唯一的菜单路径。

### 附带修掉：`.viewScroller` / `.scrollFrameY` 全项目 0 定义

搜索页卡片化后暴露出一个**行式列表时代就存在**的缺陷：`/search` 完全不能纵向滚动
（1600×900 下内容 1676px 只露 900px）。审计后确认是单根因：
这两个类在**全项目 CSS 里 0 定义**，能滚的页面都是同一元素上另挂了 vendor 真类 `scrollY` / `overflowYScroll`。
19 处带 `scrollFrameY` 的元素里，缺真类的**恰好 3 处，且都是 `<main>`**：
`ItemDetailView.vue:492`、`admin/AdminUsersView.vue:71`、`SearchView.vue:195`。

修法（`emby-bridge.css`，1 条规则 + 注释）：

```css
.scrollFrameY:not(.scrollY):not(.overflowYScroll),
.viewScroller:not(.scrollY):not(.overflowYScroll) { overflow-y: auto; overflow-x: hidden; min-height: 0 }
```

`:not()` 是安全阀——已有真滚动容器的元素一个都匹配不到，**双滚动条在结构上不可能发生**。
逐页 diff（同脚本 A/B，改前用 `!important` 中和新规则）：`/search` — → **776**、`/item/319159` — → **700**、
`/dashboard/users` — → **50197** 三页 FAIL→PASS；其余 7 页（`/`、`/library/2000000002`、`/person`、
`/settings/display`、`/dashboard`、`/dashboard/tasks`、`/login`）滚动元素与 `scrollTop max` **逐字相同**。
`SearchView.vue` 里 task-10 的临时 `scrollY` 已删，交回全局。

`overflow-x` 写 `hidden` 而不是 `clip`：一个轴为 `auto` 时规范强制把另一轴 `clip` 算成 `hidden`
（实测 `getComputedStyle(main).overflowX` 回报 `"hidden"`），写 `clip` 是假承诺。

### 第五轮验收锚点

| 锚点 | 判据（实测值） |
|---|---|
| 搜索页卡片化 | `.listItem` = **0**（改前 40）；`.s-card` = **40**；卡宽 **156.3×268.5**（2:3）；`scrollWidth == clientWidth` |
| 标题省略号 | `white-space:nowrap` / `text-overflow:ellipsis` / `overflow:hidden`；最长标题 `sw 277 / cw 135` 仍单行 |
| 多选入口 | `.chkItemSelectContainer` + 卡内 checkbox + `.item-multiselected` = **0**；点卡片左上角 → `/item/1310342` |
| 过滤器弹层 | **562×676.5** @(519,111.8)；内容控件 **41/41** 可见、零尺寸 0、被 footer 覆盖 **0**；内容区底 703.3 ≤ footer 顶 703.3 |
| 过滤数据 | 分组 = 播放状态 / 收藏 / **类型(30 chips)** / 年份；单选 `appearance:none` **18×18**；勾选后发出 `Genres=惊悚`，`aria-pressed=true` + `.btnFilter-on` |
| 设置弹层 | **422×298.2** @(589,300.9)；控件 **4/4** 可见、覆盖 0；内容区底 514.2 ≤ footer 顶 514.2；下拉 1 + 滑条 1 |
| 图像大小 | 三档卡宽 **138.9 / 156.3 / 178.6**（去重 3） |
| 持久化 | 刷新后 `serata.library.viewMode=list` + `imageSize=1` 都在，列表渲染 **53 行** |
| UA 黑边 | 库页 403 个可见按钮带 `outset/inset/ridge/groove/double` = **0**（改前 212）；首页 32 个 = 0；搜索页 0 个 = 0 |
| 悬浮按钮几何 | 四个按钮 `border-width 0px / border-style none / 圆角 50% / 30.3px` |
| 菜单定位 | 首卡间隙 **7.7px**、底部卡向上翻 **8.0px**、右键点 **7.7px**；面板均在视口内 |
| 滚动修复 | 10 条路由真实滚轮审计：改后 **0/10 失败**；7 页 `scrollTop max` 逐字不变；`/item` 滚轮 0→**700**（=max）、搜索页 →**776**；Tab 70 次「焦点落在视口外」**0 次**；浅色主题两页均正常 |

### 第五轮冻结哈希

| 文件 | sha256 |
|---|---|
| `web/src/components/EmbyCard.vue` | `95389AA9FB474B37BEE070E5C7E597596344B99AB916210E13A29F6DB5576900` |
| `web/src/components/ItemMenuSheet.vue` | `353B25EB273C1EEDE48D7B0B61845C1C5D7C480CECE1DA9225C1991FD0E6FCAE` |
| `web/src/views/SearchView.vue` | `7906766573DCBB7ED6215DDC6919AC87E11AD01ECAEDE9E18FE43DCF7A259544` |
| `web/src/views/LibraryView.vue` | `3C17EDABD68F936FAEAE06116BDA6C76400A6B9012D8D790080A9D09DBD7C112` |
| `web/src/api/endpoints.ts` | `7E4813646C33265FD6D25D7F259B2C0AAB144D9B782A0AD50A01E6E5E2F3E4B4` |
| `web/src/styles/serata/emby-bridge.css` | `D2B0636D71D276BD523202E267BDBBC4C17A5DFB9DC5883B224B504A89EB4907` |
| `web/src/stores/multiselect.ts` | **已删除**（37 行） |

`pnpm type-check` exit 0；`pnpm build` exit 0（897ms）。

### 本轮暴露的两件事，必须留档

**一、`getGenres` 在这台夹具上是"假成功"，不是我们的 bug。**
`/Genres?UserId={uid}&ParentId={lib}` 返回 **200 但 Items 为空**，`/Genres?ParentId={lib}` 返回 **64 条**，
`/Genres?UserId=deadbeef&ParentId` 返回 **500**。已让 `endpoints.ts` 在"带 UserId 为空"时去掉 UserId 重试
（A/B 数据写在代码注释里）。更严重的是**夹具整台忽略 `Genres`**：直连 `tv.wenjian.de` 实测
`Genres=惊悚` → 仍 53 条、`Genres=绝对不存在的类型XYZ` → 仍 53 条、`TotalRecordCount` 恒 **53**
（而 `Limit=5`→5、`StartIndex=50`→3、`SortBy=ProductionYear`→顺序变化、`Filters=IsUnplayed`→53→**50** 都正常）。
⇒ **筛选结果集的变化在夹具上无法证明**，前端只证明了"参数正确发出"。
最终验收必须在自有后端 `MediaBrowser.Api` 上重做一遍（可复跑 `scratch/lead5/probe-genre-ab-run.mjs`、`probe-param-ab.mjs`）。

**二、验收探针自己错了 4 次，全部由我方自查抓出并纠正。**
① 把 footer **自己的按钮**算进"内容控件"，于是 `lastControlBottom > footer.top` 必然成立 → 假 FAIL
（改成 `footer.contains()` 排除整棵子树后：41/41 可见、0 覆盖）；
② 写死 `.scrollSlider` 当滚动容器，而库页真正滚的是内层 `div.scrollFrameY` → 点击落空 → 假 FAIL；
③ 按"上一档卡宽"断言持久化，但视图切到列表后根本没有 `.s-card` → 假 FAIL（改成断言 localStorage + 列表行数）；
④ 详情页滚轮连发不给事件间隔 → `scrollTop` 恒 0 → 假 FAIL（补 120ms 间隔后 4 个落点全部 0→960=max）。
**度量错了，结论就会假** —— 这条比这一轮的 5 个修复更值得记住。

### 第五轮遗留风险（未验证 / 已知缺陷，不许含糊）

1. **首次点击卡片偶发不跳转（1 次观测，8 次受控复测无法复现）**：`probe-diag2.mjs` 抓到一次完整
   `pointerdown/mousedown/pointerup/click` 都落在 `.cardOverlayContainer`、`defaultPrevented=false`，
   但 `history.pushState` **一次都没被调用**（即 `open()` 没走到 `router.push`）；紧接第二次点击即正常。
   随后 `probe-diag3.mjs` 在"卡片出现后稳 0.6s / 3.5s"两档各 4 次全部 PASS（8/8）。
   仅出现在**冷启动后第一次点击 + 探针连续猛击**的语境，**未在真实设备上确认**，本轮不盲改。
2. **移动端逐页滚动审计缺失**：10 条路由的滚动审计全在 1600×900；详情页移动端未测
   （搜索页在 390 单独验过：40 张卡、无横向滚动）。
3. **`/dashboard/users` 一次渲染 835 行（51097px）**：能滚到底，滚动性能未测。
4. **`overflow-x:hidden` 让内容压在纵向滚动条内沿 15px**（与全站一致），后续往这几页塞跨边界元素需注意。
5. `ItemMenuSheet` **不跟随窗口 resize** 重算位置（改窗口大小后面板可能偏出）。
6. RTL 未测；<336px 视口未测；真实手指长按未测（本机 `Input.dispatchTouchEvent` 45s 超时）。
7. 库页仍可能同时出现「0 个条目」与「加载中…」；移动端 `/search` 输入框旁仍有底栏「搜索」tab。
8. `HomeView` 的边框普查**没有改前基线数字**（只有改后 0）。
9. 遗留死 i18n key：`search.typeMovie` / `search.typeSeries` / `search.typeEpisode`（本轮故意不删）。
10. `web/tools/probe-multi-body.mjs`、`probe-content-card.mjs`、`probe-home3.mjs`、`probe-home4.mjs`、
    `probe-card-hover.mjs` 仍在断言**已删除的多选 DOM**（无 npm script 引用，未清理）。

