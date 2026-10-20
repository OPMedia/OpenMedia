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
`--s-hero-mask`（两侧渐隐蒙版）`--s-hero-scrim`（上/下/左三层 `--s-bg` 收口）`--s-hero-copy-wash`（文案背后的局部承托）
`--s-hero-control-bg` `--s-hero-control-bg-hover` `--s-hero-control-fg` `--s-hero-control-ring`
> 这一版把图画区从「固定压暗层 + 一套固定白字」改成「图溶进页面底色」，令牌因此只剩三条几何决策 + 一组控件色。
> `--s-hero-mask` 让图自己两侧消失（用 mask 而不是盖底色 —— 盖底色会把底下的页面底色也改掉，接缝看得出来）；
> `--s-hero-scrim` 用 `--s-bg` 收口，所以**文案可以直接用 `--s-text-*` / `--s-cta-*`**，
> 不再需要 `--s-hero-ink` 那套"压在图上"的固定色（上一版正是被它拖累：浅色主题下文案区被迫是一块黑斑，与「浅色就用浅色」冲突）。
> 真正压在未渐隐剧照上的只有右下角那组圆点/暂停键，所以只有它们有一组不随主题翻转的 `--s-hero-control-*`。
>
> **遮罩浓度是用户投诉项，不是口味项（2026-10-20）**：「无论深色浅色，遮罩太浓，背后的背景图根本看不清」。
> 所以分工被拆成两层：`--s-hero-scrim` 只负责把带宽的**四条边**收进页面底色，文案对比度交给
> `--s-hero-copy-wash`（画在 `.hero-copy-body::before`，只覆盖文案块，右半张剧照保持原始亮度）。
> 改这组值必须同时守住三条**像素级**判据（脚本：`scratch/lead2/verify-final.py`）：
> 1. **图区**（x 55%~95%、y 10%~70%）与原始剧照的平均亮度差 ≤0.08、单点 ≤0.18；
> 2. **四条边**的接缝 `EXCESS = AT-EDGE − 邻域中位步长 ≤ 3`（只看边缘那一步的绝对值会把渐隐尾巴误判成台阶：
>    390 浅色实测 AT-EDGE=2.00，但邻域步长 3.36–4.72，EXCESS 是负的）；
> 3. **文字对比度**：标题 ≥3:1、其余 ≥4.5:1，深浅两主题都要。
> 另外 `--s-hero-scrim` 的**底部 0% 必须是 100% 的 `--s-bg`**（写成 `color-mix` 会留 14% 剧照，
> 实测与页面底色差 16~29，在带宽下缘形成一条横台阶）；`.hero-stage` 的底色也必须是 `--s-bg` 而不是
> `--s-img-placeholder`（两侧被 mask 溶掉后露的就是它，会与页面底色差 17.4/13.3）。加载态不受影响：
> 骨架是独立一层 `.hero-sk-art`，自己铺满整条带宽。

**字体** `--s-font` `--s-font-mono` `--s-fs-hero` `--s-fs-hero-sm` `--s-fs-display/h1/h2/h3/body/sm/xs` `--s-lh-tight/snug/body` `--s-fw-regular/medium/semibold` `--s-tracking-tight/hero`
> **字号基准是「真机 Emby 4.9.5 网页端的 computed font-size」为下限，不是口味**（用户口径：「所有字体都小了，人家 Emby 那种大小就是合适的」）。
> 实测（1600×900、已登录真机，`scratch/lead2/emby-fonts.json`）：html/body **14.424px**（Emby 把 `:root` 设成 90.15%）、
> `.sectionTitle` **21.636px**/w600、`.cardText` **14.424px**、`.cardText-secondary` 13.268px、`.emby-button` 14.424px/w600。
> 对应我们的每一档都必须 **≥** 同语义元素：`--s-fs-body`(15) `--s-fs-sm`(14) `--s-fs-xs`(13) `--s-fs-h3`(17) `--s-fs-h2`(22) `--s-fs-h1`(30) `--s-fs-display`(38)。
> `--s-fs-hero-sm`(26) 是**窄屏 banner 专用档**：文案压在图上时，靠收小字号适配带宽，而不是把 banner 撑高。

**间距** `--s-sp-1`(4px) … `--s-sp-11`(80px)、`--s-gutter`(24px)、`--s-gutter-sm`(16px)、`--s-row-gap`(40px)

**圆角** `--s-r-xs/sm/md/lg/xl/pill`

**动效** `--s-dur-instant/fast/base/slow/xslow/sheet/exit`（80/140/220/320/480/280/180ms）、`--s-ease-std/exit/spring`、`--s-transition`、`--s-stagger-step`(45ms)、`--s-stagger-max`(8)

**布局** `--s-header-h`(64) `--s-header-h-sm`(56) `--s-drawer-w`(248) `--s-hero-min-h`(460) `--s-content-max` `--s-bottom-nav-h`
> hero 高度**没有令牌**：旧的 `--s-hero-h: min(75vh, 860px)` 已删除。
> 桌面由 `HomeHero.vue` 的 `.hero-stage { height: clamp(400px, 58vh, 620px) }` 给（横幅比 16:9 宽，图 `cover` 居中裁切）；
> 窄屏（≤900px）改成**跟着宽度走**的 `clamp(224px, 60vw, 280px)` —— 文案压在图上，但**不许拿 banner 高度买文案空间**
> （用户口径：「变小适配 banner 栏的大小」「你现在这样就是浪费空间」）。
> 硬验收：390×844 / 360×640 上 stage ≤ 视口 40%，且「继续观看」行标题仍在首屏内（实测 234px/27.7%、224px/35%）。
> `aspect-ratio: 16/9` 在窄屏**装不下**这套压图文案（390 只有 201px、360 只有 184px，最少需要 ≈216px），已否决。

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

**池子口径（用户口径：「banner 放的是随机推荐的东西，而不是继续观看的图片」）**：
三层取数，顺序不可调 —— ① `Filters=IsResumable`（Limit 8）→ ② 最近播放 `SortBy=DatePlayed Desc`（Limit 24，
客户端只留**有播放痕迹**的：`LastPlayedDate || PlaybackPositionTicks>0 || Played || PlayCount>0`）→
③ 最新入库（Limit 40，**仅在 ①② 合计 <2 条时**才发这条请求）。
③ 是"随机新片"的来源：真机实测它和播放历史毫无关系，旧逻辑当补位池用，于是 6 张里 4 张变成新片推荐。
拼池时还按**图会撞**去重：解析出的横版图 key（`wideArtOf()` 的 `id+type+tag`）或 `SeriesName` 任一已占用就跳过 ——
真机实测第 3/4/5 项曾是同一条 `Items/1668375/Images/Backdrop` + 同一标题，用户会直接判定"轮播坏了"。
`playable` 与 `brand` 两条 key **各自记账**（混记会让 ① 里那条无横版图的整集文件用剧名把 ② 里同剧的集全部挡掉，池子退到 2 张）。

**交互（WCAG 2.2.2）**：8000ms 自动轮转；hover / 区域内 focus / 标签页隐藏都停；`prefers-reduced-motion` 下**初始暂停**。
暂停控件**不常驻在 banner 角上**（用户口径：「左上角为什么会有一个暂停/播放按钮，这不该出现」）：
它并进右下角那组圆点，默认 `opacity: 0` + `pointer-events: none`，只有 `:focus-visible`（键盘 Tab 到）或已暂停时才显形，
Tab 可达、带 `aria-label` / `aria-pressed` —— "能停"的能力一点没少，只是不再占画面。

**承托层与净图区（验收判据，别改坏）**：
- 承托层（`.hero-copy-body::before`）的**上沿不得高于"剧照区顶部往下 25%"** —— 窄屏实测 390×844 = 29.1%、360×640 = 25.9%。
- 代价：**窄屏标题封成一行**（桌面两行）。360×640 的剧照区 224px 装不下
  `56（25% 净图）+ 157.6（两行文案）+ 40（圆点带）= 253.6px`，收一行后 222.6 ≤ 224 刚好过线。超长标题窄屏出省略号是明确取舍。
- 净图区判据**必须用差分**：参照系 = 同一次运行把 `scrim + wash + mask` 全关（两次都隐藏文案**子元素** ——
  wash 挂在 `.hero-copy-body::before` 上，隐藏整个 `.hero-copy-body` 会连承托层一起藏掉，差分就测不到它）。
  判据区桌面 `x55–85%/y10–70%`、移动 `x25–75%/y15–25%`；**mean ≤0.08 且 p99 ≤0.25**。
  **不要**拿"与原始剧照逐像素比"当判据：1920px → 348px 是 5.5× 降采样，画面自身边缘的重采样差就能到 0.2–0.8。

**换图时延（必须保持）**：`src` 只在翻面那一刻写入的话，**点之前那张图从未被请求**，
冷缓存下一次 MISS 打真机要 1.4–3.7s，于是每次轮播都有 **3.7–6.8s 的"新标题 + 上一部剧照"** 窗口。
所以池子一确定就整池 `new Image()` 预热（不在文档里，低优先级，不抢首屏），
请求宽度按槽位算 `clamp(900, round(slotW × dpr), 1920)` 且**整个生命周期只算一次**（改了大小 URL 就变，预热缓存当场作废）。
验收量的是**用户实际看到的那张**（`opacity > 0.5 && naturalWidth > 0`），**不能看 `is-front` 类名**
—— 类名在图片加载完成之前就已切到新图，会给出"图已经换了"的假结论。

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
