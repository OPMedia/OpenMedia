# ADR-0014：前端组件基座与动效分层

- **状态**：已采纳
- **日期**：2026-10-19
- **关联**：ADR-0011（Web UI 与 Emby 对齐）、ADR-0013（设置页只保留生效项）

## 背景

用户的要求是：「用了 Vite 就要用组件库，比如 shadcn 那种，要好看、兼容性强、动效丰富的动效库」。

先做事实核查，再动手：

| 说法 | 事实 |
|---|---|
| 「shadcn 是 Vue 的组件库」 | **shadcn/ui 是 React 专用**。Vue 侧对应物是 shadcn-vue。 |
| 「shadcn-vue 是一个库」 | 社区原话：*"quite literally just pre-styled Reka UI with tailwindcss"*。它的下层就是 **Reka UI**（原 Radix Vue）。 |
| 「Vite 决定组件库选型」 | Vite 是打包器，不约束组件库。任何 Vue 组件库都能跑在 Vite 上。 |

关键约束在**设计层**：`web/src/styles/serata/` 是一套冻结的 CSS 令牌体系——
`tokens.css` 定义全部颜色/间距/动效/层级，`:root{font-size:90.15%}` 决定了**单位必须用 px**，
老页面靠 `emby-bridge.css` 覆盖 Emby 的 HSL 变量换肤，另有 17 条铁律（见契约 README）。
在这个体系上再引一层 Tailwind，等于给同一个界面装两套样式真源：令牌说一套、原子类说一套，
两边漂移是迟早的事，而**漂移的代价全部落在「深浅双主题一致性」上**。

与此同时，审计手搓弹层时查到了四条**真实缺陷**（不是审美问题）：

| 缺陷 | 证据 |
|---|---|
| `EmbySelect` 外部点击**永不关闭**：`@click.stop="onDocClick"` 挂在 `rootEl` 上，而判断条件是 `!rootEl.contains(e.target)` —— 监听器在 rootEl 上，`e.target` 恒在 rootEl 内，条件永远为假，且没有任何 document 级监听 | `EmbySelect.vue:73` + `:49-51` |
| `ItemMenuSheet` 弹层硬编码 `rgb(40,40,43)` / `rgba(255,255,255,.87)` → **浅色模式下是一块黑方块** | `ItemMenuSheet.vue:109-110` |
| `EmbySelect` 弹层同样硬编码 `rgb(40,40,43)` | `EmbySelect.vue:122` |
| 全部弹层用 `v-if` 硬弹，**全库 `<Transition>` 0 命中**；无 Esc 关闭、无焦点陷阱、无读屏隔离、无滚动锁定 | grep `<Transition` / `<Teleport` |

另外 `base.css` 里有一套 `::view-transition-*` 规则，但全库 grep `startViewTransition` 是 **0 命中**——
写了从来没被触发过的死代码。`api/toast.ts` 是 19 行单例：`.toast` 自身没有 `position`，
定位靠的是 `.toast-group` 包裹层而旧代码没建，于是 toast 被 append 到 body 流尾、常常落在视口外，
且全局只有一个 div，后一条覆盖前一条。

## 决策

### 1. 组件基座选 Reka UI，**不引 Tailwind、不引 shadcn-vue**

要的是 shadcn 的**能力**（可访问性原语 + `data-state` 动效状态机），不是它的**皮**。

- Reka UI 就是 shadcn-vue 的下层。选它 = 零样式冲突，令牌体系继续做唯一真源。
- 这个选择是**可增量演进**的：如果将来真要上 Tailwind 皮，那是在 Reka UI 之上叠加，
  而不是把今天的决定推倒重来。**先做不会错的那一半。**
- 落地形态：`web/src/components/ui/SSheet.vue` —— 管行为（焦点陷阱、Esc、点外关闭、滚动锁定、
  `aria-modal`、焦点归还、进出动效），不管长相（内容由插槽给，宿主继续用 emby495 的 `actionSheet*` 类名）。
  这样既拿到 a11y，又保住 1:1 还原。

### 2. 动效分三层，各司其职，**不要用错工具**

| 场景 | 工具 | 理由 |
|---|---|---|
| 弹层/toast 进出、悬停微状态、骨架屏 | **CSS**（`motion.css`） | 声明式、零 JS，`<Transition>` / reka 的 `data-state` 直接驱动 |
| 滚动进场、指针驱动的悬停编排、布局过渡 | **motion-v**（Framer Motion 的 Vue 移植） | 需要弹簧物理与手势，CSS 表达不了 |
| 列表增删/重排（侧栏、设置项、媒体库网格） | **@formkit/auto-animate**（2KB） | 一个指令搞定 FLIP；手写要一堆测量代码 |

### 3. 动效令牌唯一真源：`tokens.css` → `motion.css` → `lib/motion.ts`

- **定义**在 `tokens.css`（`--s-dur-*` / `--s-ease-*` / `--s-stagger-*`）。
- **类名与关键帧**在 `motion.css`（Vue `<Transition>` 类族 + reka `data-state` 动画 + 交错进场）。
- **JS 镜像**在 `lib/motion.ts`：CSS 变量 JS 读不到，只能镜像一份给动画库用。
- **组件里不许再写** `@keyframes`、裸 `ms`、裸 `cubic-bezier`（铁律 14）。

### 4. reduced-motion 要**双保险**，JS 侧没有兜底

- CSS 侧：`tokens.css` 在 `reduce` 下把全部 `--s-dur-*` 压到 1ms；`motion.css` 末尾再一刀切掉 `animation`。
  两层都需要——只压时长的话，1ms 的关键帧仍会闪一下。
- **JS 侧（motion-v / auto-animate）没有兜底**，所以铁律 16 规定：动手前必须过 `useReducedMotionSafe()`，
  `reduce` 时**完全不位移**（不是「快一点」，是不动）。

### 5. 退出必须比进场快

`--s-dur-exit`(180ms) < `--s-dur-sheet`(280ms)。进场用 `--s-ease-spring`，退出用 `--s-ease-exit`。
让人等的只有进场，退场不该让人等。

### 6. 删掉死代码，让 View Transitions 真正被调用

`base.css` 里从未被触发的 `::view-transition-*` 块删除。
View Transitions 唯一真正的用武之地是**主题切换**：整页几十个 HSL 变量同时翻转，
硬切会闪，而两套主题直接对叠又会糊成中间色——所以 `useTheme.ts` 现在用 `startViewTransition`
包住这次 DOM 变更，并在 `motion.css` 里让老快照保持不透明、新快照淡入。
不支持该 API 或用户要求减少动效时，直接切，不做快照。

### 7. 弹层统一走 `SSheet`，禁止手搓

凡是「浮在页面上方、需要 Esc / 点外关闭 / 焦点管理」的东西，一律走 `SSheet`（铁律 13）。
手搓的 `v-if` 遮罩会丢掉焦点陷阱、读屏隔离和滚动锁定。

## 后果

**正面**

- 深浅双主题只有一套真源，弹层底色随主题走（旧实现浅色下是黑方块）。
- 三个手搓弹层的交互缺陷（外部点击关不掉、无 Esc、无焦点归还）一次性消除。
- 键盘可达性与读屏语义由 Reka UI 保证，不用逐组件手写。
- 动效词汇表统一（`s-pop` / `s-rise` / `s-up` / `s-page` / `.s-anim-*`），
  任何人加新动效都是「取类名」而不是「再写一遍关键帧」。

**代价与风险**

- 新增 4 个运行时依赖：`reka-ui`、`motion-v`、`@vueuse/core`、`@formkit/auto-animate`。
  `motion-v` 的 peer 依赖 `@vueuse/core` 必须显式安装，不能靠传递依赖。
- `reka-ui` 的 `DialogContent` 必须配 `DialogTitle`（Radix 血统要求），缺失会报警。
  `SSheet.vue` 已在没有 `title` prop 时渲染 `VisuallyHidden` 兜底标题——
  **不要绕过 SSheet 直接用 `DialogContent`**。
- reka 的 `Presence` 靠 `animationend` 判断卸载时机，所以它的状态动画**必须用 `animation` 而不是 `transition`**。
  这条约束写进了 `motion.css` 的注释，改的时候别改成 transition。
- 依赖升级可能静默改变导出。`tools/probe-libs.mjs` 把我们实际依赖的 41 个导出钉成断言，
  升级后先跑它。

**未覆盖**

- 生产构建下的首屏绘制、真实视频 OSD 可读性、真实后端错误路径，不在本轮验收范围。
- `startViewTransition` 的浏览器支持面（Chromium 111+ / Firefox 139+ / Safari 18+）没有做降级动画，
  只做「支持就淡入、不支持就硬切」。
- 卡片 → 详情页的**共享元素**转场（`view-transition-name`）没有做：它需要把
  `startViewTransition` 接到路由导航上，而 vue-router 没有能包住「DOM 更新」这一步的钩子。
  路由转场因此走 Vue `<Transition name="s-page">`。这是**有意识的取舍**，不是遗漏。

## 验收结果（2026-10-19，Lead 真浏览器实测，深/浅双主题）

### 验收阶段才发现、并已修掉的五个真缺陷

**① toast 完全没有被样式化（`SToastList.vue` 的 `<style scoped>` 整片空转）**

- 证据：`OL.s-toast__viewport` 与 `LI.s-toast` 的 `attributeNames` 里**没有 `data-v-*`**（它们父级那个 div 有）。
  Vue 把作用域标记打在组件根元素上，而 reka 的 `Primitive` 把 `class` 转发到了真正渲染的 `<ol>`/`<li>` 上。
- 后果（实测）：`position: static`、宽 **1400px**、`background-color: rgba(0,0,0,0)`、躺在文档流顶部——一条透明通栏裸列表。
- **为什么之前没被发现**：断言 `count=1 / id=reka-toast-1 / 文本一致 / opacity=1` **全部通过**。
  全局的 `.s-anim-up` 照常生效，**动画恰好把"完全没被样式化"伪装成了"一切正常"**。
- 修：改为非 scoped 全局样式（`.s-toast*` 全项目唯一，且 toast 被 teleport 到 `<body>`，逻辑上在所有 scoped 子树之外）。
- 复验：`fixed` / `z-index: 1200` / 宽 380 / 右下各 20 / 底色深 `rgb(26,31,59)`、浅 `rgb(242,235,226)`；语义条 info·success·error 三色互异。
- **对比：`SSheet.vue` 用 `scoped` 是有效的**（`DialogContent`/`DialogOverlay` 能拿到 `data-v-*`）。这条不能一刀切。

**② `LibraryView.vue` 遗留硬编码在浅色模式下的可用性缺陷**

- 来源：上一轮 1:1 还原时把 Emby **深色主题**的配色抄死了（`git show HEAD` 可证：非本轮引入）。
- `.pager .cur { color: rgba(255,255,255,.6) }` —— 白字 60% 压在 `#F7F5F2` 浅底上**完全看不见**。
- `.filterChip-on { background: var(--theme-primary-color); color: #fff }` —— 珊瑚 + 白字 = **3.01:1，未达 AA**。
- 修：全部改走令牌（`--s-raised` / `--s-line` / `--s-text-1` / `--s-text-2` / `--s-hover` / `--s-accent` + `--s-accent-ink`），版式不动。
- 实测：选中芯片 **4.68:1（浅）/ 5.42:1（深）**；未选中 **13.5:1 / 13.21:1**。
- `.pager` 在 mock 数据下只有一页 → 不渲染，**运行时无法验证**，只能源码核对。已在报告中如实标注。

**③ `EmbySelect` 的下拉面板会顶出视口（`left` 与 `max-width` 互不知情）**

- 结构：面板的 `left` 由 `EmbySelect` 用 `getBoundingClientRect()` 取自触发器左沿（inline style），
  而宽度上限是 `SSheet` 里**固定的 `max-width: 92vw`**。两者互不知情，于是「靠右的触发器 + 较宽的选项文字」会溢出。
- **为什么危险**：面板是 `position: fixed`，溢出时**不产生横向滚动条**，直接被静默裁掉——用户看不到选项右侧内容，且没有任何提示。
- 真实音频/字幕轨名普遍很长（`English (SDH) - Signs & Songs - Forced Narrative…`），这不是理论问题。
- 实测（280px 视口机制探针，280 低于现实最小机宽 320，仅用于区分新旧代码）：

  | | 旧代码 | 修后 |
  |---|---|---|
  | `max-width` | `257.6px`（固定 92vw） | `220.969px` = `280 − 51.03 − 8` |
  | 面板右边缘 | **296 → 溢出 16px** | **274 → 余 6px** |

- 修（`EmbySelect.vue` 的 `popupStyle`）：`left` 在右侧放不下时先左移，再把 `maxWidth` 夹到剩余空间——
  让「面板必在视口内」成为**构造上的不变式**，而不是依赖数据恰好不长。
- 回归：390px 下自然宽度 `x=55 / w=168·243·160` 与修前**逐像素一致**（1:1 还原未受影响）；
  注入超长标签后 `w=329 / right=384 < 390`。`box-sizing: content-box`，故实际外宽 = `maxWidth + 2px` 边框，`GAP=8` 足以吸收。

**④ 首页行入场动效**整体失效**：`:viewport` 这个 prop 根本不存在（本轮最严重、最难发现的一条）**

- 现象：行在**完全没进视口（ratio≈0）时就播完**入场动效，用户永远看不到；滚出视口会被动画回
  `opacity:0` + `translateY(14px)`，滚回来再重播一次（`once` 也失效）。DOM 上还残留一个丑陋的
  `viewport="[object Object]"` 属性。
- 根因（源码级）：`motion-v@2.8.0` **只声明了 `inViewOptions`**
  （`node_modules/motion-v/dist/es/components/motion/props.mjs:110`），whileInView 特性读的也是
  `state.options.inViewOptions`（`.../features/gestures/in-view/index.mjs:20`）。
  写 `:viewport="…"` 时它是**未声明 prop** → 整个对象落进 `$attrs` 渲染成 DOM 属性，
  `inViewOptions` 是 `undefined` → IO 退回默认 `threshold: 0`，`once` 拿不到。
- 修：`:viewport` → `:in-view-options`。已在 `HomeView.vue` 模板里钉上两处 file:line 证据防回退。
- **为什么三个实现者 + Lead 都没抓到**：动效"看起来播了"（全局类照常生效、`opacity` 最终也是 1），
  只有**测 IO 阈值 + 测触发瞬间的可见比例 + 测滚回是否重播**才能证伪。断言「元素存在 / 最终不透明」是抓不到的。
- **顺带修掉一个更隐蔽的 UX 问题**：即使 prop 名对了，`amount: 0.15` 也不够——
  实测「卡片顶部相对整行的高度」：桌面 1440px 下 49/289 = 0.169、63/356 = 0.176；
  移动 390px 下 49/256 = 0.191、63/301 = 0.208。`0.15` **恰好卡在所有阈值之下**，
  于是触发时屏内只有 41px 的标题、卡片可见比例实测为 **0**，交错动效照样在视口外播完。
  取 **0.3**（比最大需求 0.208 高约 44%），触发时卡片露出 15~44px。
- 约束：framer 的 `amount` 只吃数字或 `'some'`/`'all'`，**不支持像素值**
  （`framer-motion@14.3.0/dist/es/render/dom/viewport/index.mjs:37`：
  `threshold: typeof amount === "number" ? amount : thresholds[amount]`），
  所以写不成「露出 60px」这种与行高无关的形式——**行高改了要重新核这个数**。

**⑤ 非模态弹层关闭后焦点丢失（`modal=false` 的 `EmbySelect`）**

- 实测：点面板外空白关闭后 `activeElement` 掉到 `body.mainAnimatedPages`；
  用 `HTMLElement.prototype.focus` 插桩证明**应用层从未调用过 `focus()`**（调用记录为空数组）。
  同一轮里 Esc 路径、以及其余 6 个 `modal=true` 弹层的点外路径**都**正常归还。
- 根因：reka 只在**模态**分支挂 `FocusScope`，焦点归还也是 FocusScope 的职责；非模态分支没有。
- 修（`SSheet.vue`）：打开时记录 `document.activeElement`（`EmbySelect` 是先 `facadeEl.focus()`
  再 `open=true`，所以拿到的是触发器），关闭后延迟 200ms（> `--s-dur-exit` 180ms，等退出动效走完），
  **仅在焦点已无处安放**（`activeElement` 为 `null`/`body`）时归还；`modal=true` 一律不插手，避免和 FocusScope 打架。
  顺手修掉文件头那句「关闭后焦点归还」——它对 `modal=false` 是假话。

### 通过项（真浏览器，深浅各一遍）

| 项 | 实测 |
|---|---|
| 路由转场 | `/`→`/search` 观测到 `s-fade-out main 0.14s` + `s-page-in 0.32s`；opacity 0→0.77→0.98、translateY 8px→0 |
| 弹层（排序/过滤器/设置/条目菜单/添加用户/新增媒体库） | `role=dialog`、`z=1000`、`s-pop-in 0.22s`、遮罩随主题、滚动锁、Esc 关、点外/点遮罩关。**注意 `EmbySelect` 是 `modal=false`：无遮罩、不锁滚动（`overflow: hidden auto`），关闭方式是「点页面空白」，不是「点遮罩」** |
| 排序面板几何 | `y=58, w=137` 与旧实现一致；**`x` 随视口居中而变**（`inset:0 + margin:auto` 是视口居中）：1440px 视口下 `x=651`，1600px 下 `x=731`。旧实现代码已不在工作区，`x` **无法再核对**；只有 `y/w` 是可比的 |
| 条目菜单几何 | `x=8, y=115, w=322` —— 与旧 `margin: 8em 0 0 8px` + `width:320px` 一致 |
| SSheet 可访问名 | `aria-labelledby` → 「用户菜单」/「更多」/「添加用户」/「新增媒体库」（证明 `VisuallyHidden` 标题生效） |
| Esc 后焦点归还 | 回到触发按钮（用户菜单 → `.s-avatar`） |
| 遮罩底色 | 深 `rgba(6,7,14,0.6)`；浅 **`rgba(23,26,46,0.32)`**（`tokens.css` 按主题定义，不是同一个值） |
| Hero CTA 对比度 | **深 16.3:1 / 浅 16.3:1**（同一对色值按主题反转，对比度是对称量、两个方向必然相等）。主 CTA 是奶白胶囊 + 墨字 ⇄ 墨胶囊 + 奶白字，**不占珊瑚** |
| Hero 文字令牌翻转 | 深 `#FFF8F1`/`#C6BFD4`/`#8E88A4` → 浅 `#171A2E`/`#4A4560`/`#6B6480` |
| 全流程 console | `errors: []`（含 `[Vue warn]` 计数 0） |
| 生产构建 | `pnpm build` exit 0，912ms |

**移动端 390×844 补充验收（task-8 只写了"移动端用 bottom 变体"，但全仓 12 处弹层实际都用 `pop`/`pop-start`/`center`——所以必须实测是否可用）**

| 项 | 实测 |
|---|---|
| 全站横向滚动 | `scrollWidth == clientWidth == 390`，**零溢出**（首页/库页/条目页/后台） |
| Hero 高度 | `633px` / 844 = **0.75** ✅ |
| 底栏 | `y=783, h=61, z=80, position:fixed`，5 项 |
| 抽屉 | `x=0, y=56, w=248, h=788, z=60`，遮罩 `z=40`；`y=56` = 顶栏下沿（铁律 22 生效） |
| toast | 视口 `w=366, x=12`（`@media max-width:600px` 的 `inset-inline:12px` 生效），底部间距 20 |
| 排序面板 | `x=126, w=137` → **水平居中**（`pages` 的 `margin-inline:auto` 在移动端同样成立） |
| 筛选面板 | `x=15, w=361, y=67, h=711`；`right=375 ≤ 390`、`bottom=777 ≤ 844`，内容不溢出故无需滚动 |
| 条目菜单 | `x=8, w=322, right=330 ≤ 390` |
| EmbySelect | `left` 跟随触发器、`minWidth`=触发器宽、`maxWidth` 随剩余空间收缩；Esc 关且**焦点归还原触发器** |

> `variant="bottom"` 未被使用：移动端这些面板（筛选 361×711、条目菜单 322×702）已近乎满屏，
> 居中与底部锚定在观感上差异很小，且居中版在平板/桌面复用同一套样式。**判为可接受的实现选择**，不是偏离。
> 若日后要为移动端做底部锚定，改 `SSheet` 的 `variant` 一处即可（调用方零改动）。

### 被实测推翻的四个怀疑（记录以防后人重复怀疑）

1. 浅色 Hero 标题看着"白字压白底"（截图发灰）→ 实测 `rgb(23,26,46)` + `opacity: 1`。
   那张截图**抓在 Hero 换片的交错动画中途**（同批采样里 `s-sr` 已显示「第 2 项，共 6 项」）。**不是缺陷。**
2. 主 CTA 疑似踩"珊瑚 + 白字 3.01:1" → 实测是按主题反转的药丸按钮（见上表），**根本没用到珊瑚**。
3. "抽屉层级低于顶栏"疑似 bug → 判为**有意为之**（见铁律 22）。
4. `pnpm type-check` 冒出的 2 条 `LibraryView layout="vertical-*"` 报错曾被误判为实现者用错 motion-v API；
   实为**上一轮 DOM dump 遗留的死属性**被 motion-v 的全局 `HTMLAttributes` 扩增暴露（见铁律 18）。
   教训：**报错行号漂移说明文件正在被改，先查 `git diff` 再下结论。**

### 本轮仍未覆盖

- `.pager` 的运行时表现（mock 数据只有一页，不渲染）。源码级替代证据：`.pager button` → `var(--s-raised)`、
  `.pager .cur` → `var(--s-text-2)`（CSSOM 遍历读出），但**没有运行时证据**，不要当成已验证。
- 生产构建下的**首屏闪白**：`pnpm build` 已通过，但没有在真实生产构建上测首帧。
- **触摸/真机移动端**：窄视口只用 `Emulation.setDeviceMetricsOverride` 模拟，未派发真实触摸事件。
- 旧实现排序面板的 `x` 坐标：旧代码已不在工作区，**无法再核对**（`y/w` 可比，`x` 不可比）。
- `EmbySelect` 定位仍为 `getBoundingClientRect` 手算（未走 Floating UI），父级若做 `transform` 动画弹层不跟随（旧实现同问题，未恶化）。
  已加的是「宽度夹紧 + 贴边左移」，不是完整的碰撞翻转（flip）。
- 焦点环在"刚按过键盘再鼠标点开菜单"时会出现在首个菜单项上——这是 FocusScope 自动聚焦 + Chromium 的
  `:focus-visible` 模态启发式，**属正确 a11y 行为**；纯鼠标路径实测无环。已决定**不为像素级 1:1 削弱焦点陷阱**。

### 独立验收（257 条断言，`web/tools/probe-motion.mjs`）

本轮的验收由一个**不写产品代码**的独立角色完成，交付 `probe-motion.mjs`（257 条断言）+ 逐条报告 + 23 张截图。
**最终结果：257 条断言 / 257 通过 / 0 失败，`exit 0`。**

过程（重要，别只看结论）：
1. 独立验收首轮：**250 通过 / 7 失败**，抓出缺陷 ④⑤；
2. Lead 修复后复跑：**256 / 1**，唯一失败项是验收者自己的断言把修复前的 bug 值（IO threshold `0`）写死成了期望值
   （`probe-motion.mjs` 里 `filter(t => t.indexOf('0|') === 0)` 与 `filter(x => x.th === "0")`）；
3. 验收者在改这条断言时**进程崩溃**（开局第一步），因此**最后这条断言由 Lead 修改**——
   记录在此以免误解为"验收者全权闭环"。
   为免变成自己给自己放水，改法是**收紧而非放宽**：不再按具体数值匹配，改为按「rootMargin 为空的那唯一一个观察器」
   定位，要求 `threshold >= 0.2`，即**同时拒绝 `0` 和 `0.15` 两个历史 bug 值**；
   断言标题也从「`:viewport="{ once: true, amount: 0.15 }"` 生效」改为描述真正的不变式。
   真正的因果证据仍是独立断言 `reveal.cardsVisibleAtStart`（触发瞬间卡片可见比例 > 0），这条不受本次改动影响。

这次独立验收的价值在于**它推翻了 Lead 自己的三个数值断言**，记录在此以免后人引用错：

| Lead 原先的断言 | 实测 | 说明 |
|---|---|---|
| Hero CTA 深 18.5:1 / 浅 14.2:1 | **16.3:1 / 16.3:1** | 对比度是**对称量**，同一对色值两个方向必然相等——原值在数学上就不可能成立 |
| 排序面板 `x=651` 与旧实现逐像素一致 | 1440px 下 `x=651`、1600px 下 `x=731` | 新实现是 `inset:0 + margin:auto` 的**视口居中**，`x` 随视口变；旧代码已不在，无法核对 |
| 浅色遮罩 `rgba(6,7,14,.6)` | 浅色是 `rgba(23,26,46,0.32)` | `rgba(6,7,14,.6)` 是**深色**的值，`tokens.css` 按主题分别定义 |

另有一条**任务书自身的错误**：任务描述里写「`EmbySelect` 与 `ItemMenuSheet` 都是 `modal=false`、都没有遮罩」，
实测**只有 `EmbySelect` 是**；`ItemMenuSheet` 有遮罩且点遮罩能关。按原文写断言会把 `ItemMenuSheet` 测反。

还有一条实现者自述被**改判**：`rows` 说「`viewport.amount: 0.15` 未严格生效，行刚露头就开播」——
定性不对，实际是**整个 prop 未被消费**（见缺陷 ④），且后果比他描述的更严重（外加滚出回退与重播）。

> 方法论结论：**「元素存在 + 有文本 + 最终 opacity=1」这类断言抓不到本轮的 4 个真缺陷**。
> 有效的断言形态是「测量机制本身」——IO 阈值、触发瞬间的可见比例、滚回是否重播、
> 非模态弹层关闭后的 `activeElement`、`focus()` 的插桩调用记录。
