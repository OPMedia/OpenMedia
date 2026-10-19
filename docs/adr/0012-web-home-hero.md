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
