# Serata 设计层 —— 并行开发契约（冻结接口）

> 本文件是并行开发的**唯一接口来源**。任何人不改接口，只按接口实现。
> 配色证据见 [`web/brand-assets/serata-palette.png`](../../../brand-assets/serata-palette.png)。

## 1. 四件套（已落地，禁止修改）

| 文件 | 职责 |
|---|---|
| `web/src/styles/serata/tokens.css` | 全部令牌：颜色 / 字体 / 间距 / 圆角 / 动效 / 层级 |
| `web/src/styles/serata/base.css` | 全局基元：焦点环、滚动条、骨架屏、图片淡入、行遮罩、视觉隐藏 |
| `web/src/styles/serata/motion.css` | **动效原语**：关键帧 + reka `data-state` 动画 + Vue `<Transition>` 类族 + 交错进场 |
| `web/src/styles/serata/emby-bridge.css` | 老页面（库页/详情/搜索/设置/后台）靠 HSL 变量整体换肤 |

加载顺序在 `web/src/main.ts`：`emby495 → login.css → tokens → base → motion → emby-bridge`。

## 2. 令牌清单（新组件只准用这些变量）

**颜色**
`--s-bg` `--s-bg-deep` `--s-surface` `--s-raised` `--s-hover` `--s-active`
`--s-line` `--s-line-strong`
`--s-text-1` `--s-text-2` `--s-text-3`
`--s-accent` `--s-accent-hi` `--s-accent-text` `--s-accent-soft` `--s-accent-ink`
`--s-ok` `--s-danger`（语义状态色，只做指示条/图标，不做大面积填充）
`--s-cta-bg` `--s-cta-fg` `--s-cta-bg-hover` `--s-cta2-bg` `--s-cta2-bg-hover` `--s-cta2-fg`
`--s-scrim` `--s-scrim-soft` `--s-scrim-modal` `--s-shadow-sheet` `--s-shadow-card` `--s-glow` `--s-img-placeholder` `--s-skeleton-hi`

**品牌常量（深浅通用）**
`--s-night` `--s-violet` `--s-plum` `--s-coral` `--s-coral-deep` `--s-cream` `--s-ink` `--s-ivory`
`--s-gradient-dusk`（纵向）`--s-gradient-dusk-h`（横向）

**图画区（hero）—— 「背景图居中、两侧过渡」，只放在 `:root`、不在主题块里覆盖**
`--s-hero-mask`（两侧渐隐蒙版）`--s-hero-scrim`（上/下/左三层 `--s-bg` 收口）
`--s-hero-control-bg` `--s-hero-control-bg-hover` `--s-hero-control-fg` `--s-hero-control-ring`
> 这一版把图画区从「固定压暗层 + 一套固定白字」改成「图溶进页面底色」，令牌因此只剩三条几何决策 + 一组控件色。
> `--s-hero-mask` 让图自己两侧消失（用 mask 而不是盖底色 —— 盖底色会把底下的页面底色也改掉，接缝看得出来）；
> `--s-hero-scrim` 用 `--s-bg` 收口，所以**文案可以直接用 `--s-text-*` / `--s-cta-*`**，
> 不再需要 `--s-hero-ink` 那套"压在图上"的固定色（上一版正是被它拖累：浅色主题下文案区被迫是一块黑斑，与「浅色就用浅色」冲突）。
> 唯一真的压在未渐隐剧照上的是暂停按钮，所以只有它有一组不随主题翻转的 `--s-hero-control-*`。

**字体** `--s-font` `--s-font-mono` `--s-fs-hero/display/h1/h2/h3/body/sm/xs` `--s-lh-tight/snug/body` `--s-fw-regular/medium/semibold` `--s-tracking-tight/hero`

**间距** `--s-sp-1`(4px) … `--s-sp-11`(80px)、`--s-gutter`(24px)、`--s-gutter-sm`(16px)、`--s-row-gap`(40px)

**圆角** `--s-r-xs/sm/md/lg/xl/pill`

**动效** `--s-dur-instant/fast/base/slow/xslow/sheet/exit`（80/140/220/320/480/280/180ms）、`--s-ease-std/exit/spring`、`--s-transition`、`--s-stagger-step`(45ms)、`--s-stagger-max`(8)

**布局** `--s-header-h`(64) `--s-header-h-sm`(56) `--s-drawer-w`(248) `--s-hero-min-h`(460) `--s-content-max` `--s-bottom-nav-h`
> hero 高度**没有令牌**：旧的 `--s-hero-h: min(75vh, 860px)` 已删除。
> 桌面由 `HomeHero.vue` 的 `.hero-stage { height: clamp(400px, 58vh, 620px) }` 给（横幅比 16:9 宽，图 `cover` 居中裁切）；
> 窄屏（≤900px）改回 `aspect-ratio: 16/9` 让剧照不被裁构图，文案落到图下方。

**层级** `--s-z-backdrop`(40) `--s-z-drawer`(60) `--s-z-header`(70) `--s-z-nav`(80) `--s-z-sheet`(1000) `--s-z-toast`(1200)
> 弹层带（1000/1200）与壳层（≤80）之间留空档，避免 header/drawer 和弹层抢层级。旧实现里各写各的硬编码 999/1000，已收敛。

**基元类（base.css）** `.s-scroll` `.s-scroll-x` `.s-img`+`.is-loaded` `.s-skeleton` `.s-fade-x` `.s-sr` `.s-can-focus`

**动效类（motion.css）**
- reka `data-state` 动画：`.s-anim-scrim` `.s-anim-pop` `.s-anim-up` `.s-anim-end`
- Vue `<Transition>` 类族：`s-scrim` `s-pop` `s-rise` `s-up` `s-page`
- 交错进场：`.s-stagger`（容器）+ 子元素 `--s-i` 序号
- 交互微状态：`.s-press`（按下缩放）`.s-lift`（悬停上浮）

## 3. 主题

```ts
// web/src/composables/useTheme.ts（已实现）
import { useTheme } from '@/composables/useTheme'
const { mode, resolved, isDark, setMode, toggleTheme } = useTheme()
type ThemeMode = 'dark' | 'light' | 'system'  // 默认 dark
```
- `initTheme()` 已在 `main.ts` 调用；`index.html` 有首帧防闪内联脚本。
- 持久化键 `serata.themeMode`（`dark|light|system`）。

## 4. 组件接口

### 4.1 `components/ui/SSheet.vue`（弹层基元，reka-ui Dialog 驱动）

```vue
<SSheet
  :open="open"
  variant="pop"           <!-- pop | pop-start | center | bottom | end -->
  :modal="true"           <!-- true=焦点陷阱+读屏隔离；false=轻量浮层（下拉选择） -->
  :dismissible="true"     <!-- 点遮罩关闭 -->
  :close-on-escape="true"
  :title="t('shell.userMenu')"   <!-- 只做可访问名，视觉上不出现 -->
  :panel-class="'my-panel'"
  :content-style="{ top: '40px' }"
  @close="open = false"
>
  <div>…内容…</div>
</SSheet>
```
负责：焦点陷阱、Esc、点外关闭、滚动锁定、`aria-modal`、焦点归还、进出动效。
不负责：具体版式 —— 内容由默认插槽给，宿主可以继续用 emby495 的 `actionSheet*` 类名。

> **`title` / `description` 只做可访问名，永远 `VisuallyHidden`，不渲染可见标题。**
> 因为已迁移的弹层各自都带可见标题区（`actionSheetTitle` / `formDialogHeaderTitle` /
> `actionsheetItemPreviewText-main`），SSheet 再画一行就是贴边角的重复标题。
> 面板**没有 padding**也是同理——长相归插槽。需要可见标题就把 `<h2>` 写进插槽。
> `title` 请传该弹层**自己的**语义（如「外观」），不要传通用的"对话框"。

### 4.2 `components/ui/SToastHost.vue`（全局 toast 宿主）

在 `App.vue` 根节点挂一次，**所有路由可见**（含登录/播放页）。
发 toast 用 `@/api/toast`：

```ts
import { showToast, showSuccess, showError, closeToast } from '@/api/toast'
showToast('已保存')                          // 兼容旧签名
showSuccess('扫描已开始', { description: '…' })
showError(t('admin.actionFailed'))
```
`showToast(text, { status, description, duration, id })`；同 `id` 重复调用会**就地更新并重置计时器**。

### 4.3 HomeHero / useHomeHero（已实现）

```vue
<HomeHero :items="heroItems" :loading="heroLoading" />
<!-- props: items: BaseItemDto[]; loading?: boolean；无 emit，内部自己路由跳转 -->
```

```ts
export function useHomeHero(userId: MaybeRefOrGetter<string | undefined>): {
  items: Ref<BaseItemDto[]>
  loading: Ref<boolean>
  reload: () => Promise<void>
}
```

### 4.4 动效运行时 `lib/motion.ts`

```ts
import { S_DUR, S_EASE, S_EASE_CSS, SPRING, useReducedMotionSafe, riseVariants,
         staggerContainer, flipConfig, useReveal } from '@/lib/motion'
```
- `S_DUR` / `S_EASE` 是 `tokens.css` 的 JS 镜像（CSS 变量 JS 读不到，只能镜像）。
- `useReducedMotionSafe()` —— **任何 JS 动画动手前必须先问它**。
- `SPRING.snappy / soft / bouncy` —— motion-v 弹簧预设。
- `riseVariants(reduced)` + `staggerContainer(reduced)` —— motion-v 进场变体。
- `flipConfig(reduced)` —— auto-animate（列表 FLIP）配置。
- `useReveal({ once, threshold })` —— 滚动进场，返回 `{ el, shown }`。

## 5. 三层动效分工（不要用错工具）

| 场景 | 工具 | 理由 |
|---|---|---|
| 弹层/toast 的进出、悬停微状态、骨架屏 | **CSS**（`motion.css`） | 声明式、零 JS、`<Transition>`/`data-state` 直接驱动 |
| 滚动进场、指针驱动的悬停编排、布局/共享元素过渡 | **motion-v** | 需要弹簧物理与手势，CSS 表达不了 |
| 列表增删/重排（侧栏、设置项、媒体库网格） | **@formkit/auto-animate** | 2KB 一个指令搞定 FLIP，手写要一堆测量 |

## 6. i18n

命名空间：`shell.*`（外壳）、`home.*`（首页）、`hero.*`（Hero）、`ui.*`（通用组件）、`common.*`（通用词）。

- `ui.dialog/close/dismiss/notifications/more` —— **弹层基元专用，locale JSON 由 Lead 独占维护**
- `shell.nav/openNav/closeNav/expand/collapse/back/menu/libraries/myMedia/playlists/collections/me/searchPlaceholder/userMenu/theme/themeDark/themeLight/themeSystem/themeToggle/brandHome/accountSettings`
- `home.hero/viewAll/scrollBack/scrollForward/remaining/loading/error/empty/emptyHint/emptyAction/continueWatching/nextUp/latest/latestPrefix`
- `hero.label/slide/prev/next/pause/resume/goTo/play/resumePlay/details/empty/loading/navLabel/dot`
- `settings.theme/themeMode/themeDark/themeLight/themeSystem/themeDesc/accentFixed`
- 复用旧键：`app.home/search/favorites/settings/dashboard/manageServer`、`common.loading/retry/error`

> **`web/src/i18n/locales/*.json` 由 Lead 独占修改**（两份必须严格等键，改一处漏一处就是线上缺词）。

## 7. 铁律（每条都会被验收）

1. 新组件**只消费令牌**，不写裸色值（`#xxx` / `rgb()`）；只有 `--s-*` 缺失时才可新增，且必须同时补进 `tokens.css`（由 Lead 统一改）。
2. 新组件**不用 `emby-*` 老类名**，全部 `s-` 前缀 + `<style scoped>`。
   > 边界澄清（2026-10-19 审计后补）：这条管的是**新建**的组件。像 `EmbySelect.vue` 这种
   > 上一轮为了 1:1 还原 emby495 而建的组件，本来就复用 `emby-select-facade / -sizer / -wrapper /
   > -selectedName` 这批既有类名（`assets/emby495` 的 CSS 里也有对应定义），继续用不算违规；
   > **但任何人都不得再发明新的 `emby-*` 类名**。新写的结构一律 `s-` 或语义化自有类名。
3. 动效只允许 `transform` / `opacity` / `filter`；禁止 `transition: all`、禁止动画 `width/height/top/left`。
4. 所有交互可达：键盘可操作、`:focus-visible` 可见、`aria-label` 齐全、`prefers-reduced-motion: reduce` 时**初始暂停**（不自动播放，但允许用户显式开启——WCAG 2.2.2 约束的是自动播放）。
5. 文案一律 `t('...')`，不写死中文/英文。
6. 珊瑚（`--s-accent`）单屏 ≤5 处**不同用途**；同一用途在列表里重复（每张卡的进度条、每个未看徽标、每个当前项指示）按一处计。渐变只用 `--s-gradient-dusk(-h)`。
7. 不改任何 `web/src/assets/emby495/**` 与 `emby495-overrides.css`。
8. 单位用 `px`（`:root` 是 90.15%，用 `rem` 会得到 14.42px 的怪基准）。
9. **主题类归 `useTheme()` 独占**：任何页面/组件都不准整体赋值 `document.documentElement.className`（会抹掉 `theme-dark` / `withHeaderTabs` 等）。页面只能 `classList.add/remove` 自己那份结构性类。
10. 需要固定深色语境的局部区域（如播放页 OSD），在**自己的根节点**上用局部令牌覆盖 + 显式 `color` 实现，不要动 `<html>` 的主题类；注意老浅色主题在 `.skinBody` 上写的是**具体颜色**而非变量，只覆盖 HSL 三件套不生效。
11. **焦点环要双保险**：老主题 `.button-link` / `.listItem` / `.emby-button` 一族带 `outline:0!important`，只画 outline 会被静默吃掉（实测首页 42 个 Tab 停靠点里 17 个中招）。`base.css` 的 `:focus-visible` 必须同时落 `outline`（带 `!important`）和 `box-shadow` 双层环。**鼠标点击不触发 `:focus-visible`**，所以键盘验证只能靠 Tab 逐站走，不能靠点几下看看。
12. **首页 Hero 是 `homeSection1..7` 里的一项**，位置必须跟着槽位走（`rows` 里用 `isHero` 占位行参与排序）；`emby-bridge.css` 里老页面的写死珊瑚一律用 `var(--s-accent)`（浅色档是更深的 `#D9604C`）。
13. **弹层不许再手搓**：凡是"浮在页面上方、需要 Esc/点外关闭/焦点管理"的东西，一律走 `SSheet`（reka-ui）。手搓的 `v-if` 遮罩会丢掉焦点陷阱、读屏隔离和滚动锁定——旧 `SheetMenu`/`ItemMenuSheet`/`EmbySelect` 三处就是这么丢的。
14. **动效一律从 `motion.css` 取类名**，组件里不许再写 `@keyframes`、裸 `ms`、裸 `cubic-bezier`。时长/缓动只认 `--s-dur-*` / `--s-ease-*`。
15. **退出必须比进场快**：进场用 `--s-ease-spring`，退出用 `--s-ease-exit` + `--s-dur-exit`。让人等的只有进场，不是退场。
16. **JS 动画（motion-v / auto-animate）动手前必须过 `useReducedMotionSafe()`**；`reduce` 时**完全不位移**（不是"快一点"，是不动）。CSS 侧的兜底在 `motion.css` 末尾，但 JS 侧没有兜底，漏了就是真的在动。
17. **弹层颜色不许硬编码**：旧 `ItemMenuSheet`（`rgb(40,40,43)`）与 `EmbySelect` 弹层（`rgb(40,40,43)` + `rgba(255,255,255,.87)`）在浅色模式下是一块黑方块。一律用 `--s-raised` / `--s-text-1`。
18. **原生元素上不许写与 Motion 同名的裸属性**（`layout` / `initial` / `animate` / `exit` / `variants` / `transition` / `whileHover` / `whileTap` / `whileInView` / `whileFocus` / `whileDrag` / `drag`）。原因：`motion-v` 在 `node_modules/motion-v/dist/es/types/instance.d.ts:2-4` 里 `declare module '@vue/runtime-dom'` 扩增了 `HTMLAttributes`，全仓原生元素的这些属性都会被按 Motion 的 prop 类型检查。从真实站 DOM dump 抄结构时尤其容易踩——需要保留语义就写成 `data-*`（本项目 `LibraryView` 的 `layout="vertical"` 已改为 `data-virtualscrolllayout`）。
19. **`scoped` 样式不一定落得到 reka 渲染的元素上——必须实测，不能假设。**
    Vue 把 `data-v-*` 打在**组件根元素**上，但 reka 的 `Primitive` 会转发属性：`ToastViewport` / `ToastRoot` 的 `class` 落到了 `<ol>` / `<li>`，而 `data-v-*` 落在了它们**父级那个 div** 上。后果是 `<style scoped>` 编译出的 `.s-toast__viewport[data-v-xxx]` **整片空转**，toast 退化成文档流顶部一条 1400px 宽的透明裸列表。
    **对比：`SSheet.vue` 用 `scoped` 是有效的**（`DialogContent` / `DialogOverlay` 能拿到 `data-v-*`），所以这条**不能一刀切**，也不能照抄。判断方法只有一条：**量 computed style**。
    `SToastList.vue` 因此改为非 scoped 全局样式（`.s-toast*` 全项目唯一，且 toast 被 teleport 到 `<body>`，逻辑上在所有 scoped 子树之外）。
20. **"元素存在 + 有文本 + `opacity: 1`" 不是可用性证据。**
    这是本轮最贵的教训：toast 的断言 `count=1 / id=reka-toast-1 / 文本一致 / opacity=1` **全部通过**，而它当时完全没有样式——`position: static`、宽 1400px、`background-color: rgba(0,0,0,0)`。全局的 `.s-anim-up` 照常生效，**动画恰好把"完全没被样式化"伪装成了"一切正常"**。
    凡验"浮层 / 弹层 / toast / 抽屉"，一律断言 computed style 四件套：`position`、`z-index`、**尺寸 + 相对视口的落位**、`background-color`（不透明且主题色正确）。
21. **`LoginView.vue` 是 4 个无条件根**（`div + header + main + div`），是全仓唯一多根组件（`node tools/audit-roots.mjs .` 可复核）。
    所以 App.vue 的**裸分支（login / play）刻意不套 `<Transition>`**——多根会让 `<Transition>` 告警且**转场静默失效**（不报错、只是不生效）。这是有意识的取舍，**不是待修的 bug**，也不要为了让它过而去改 `LoginView.vue`（那 4 个根靠父级布局撑起来，包一层 `div` 会改坏登录页版式）。
22. **`--s-z-drawer`(60) < `--s-z-header`(70) < `--s-z-nav`(80)、遮罩 40 是有意为之。**
    移动端顶栏常驻，抽屉从顶栏下沿展开（iOS/Android 主流做法）；抽屉若盖住顶栏，用户会同时失去"我在哪（品牌）"和"怎么关（✕）"两个锚点。由此推出三条约束：覆盖式抽屉 `top` = 顶栏下沿 / 头部 44px **不重复顶栏品牌**（顶栏在页顶是透明的，重复会叠出两个 Serata——实测过的真 bug）/ 底栏高于抽屉故抽屉要 `padding-bottom` 让位。**谁想"修"这个层级必须先说服 Lead**。

## 8. 自检

```bash
cd web
node tools/probe-libs.mjs   # 动效/组件栈导出探针（版本升级后先跑这个）
pnpm type-check             # vue-tsc，必须零错
pnpm build                  # 生产构建（含 type-check）
```

浏览器自检由 Lead 统一做（真浏览器截图 + 深浅双模 + Tab 逐站走焦点环）。
