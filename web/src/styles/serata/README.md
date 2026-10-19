# Serata 设计层 —— 并行开发契约（冻结接口）

> 本文件是并行开发的**唯一接口来源**。任何人不改接口，只按接口实现。
> 配色证据见 [`web/brand-assets/serata-palette.png`](../../../brand-assets/serata-palette.png)。

## 1. 三件套（已落地，禁止修改）

| 文件 | 职责 |
|---|---|
| `web/src/styles/serata/tokens.css` | 全部令牌：颜色 / 字体 / 间距 / 圆角 / 动效 / 层级 |
| `web/src/styles/serata/base.css` | 全局基元：焦点环、滚动条、骨架屏、图片淡入、行遮罩、视觉隐藏 |
| `web/src/styles/serata/emby-bridge.css` | 老页面（库页/详情/搜索/设置/后台）靠 HSL 变量整体换肤 |

加载顺序在 `web/src/main.ts`：`emby495 → login.css → tokens → base → emby-bridge`。

## 2. 令牌清单（新组件只准用这些变量）

**颜色**
`--s-bg` `--s-bg-deep` `--s-surface` `--s-raised` `--s-hover` `--s-active`
`--s-line` `--s-line-strong`
`--s-text-1` `--s-text-2` `--s-text-3`
`--s-accent` `--s-accent-hi` `--s-accent-text` `--s-accent-soft` `--s-accent-ink`
`--s-cta-bg` `--s-cta-fg` `--s-cta-bg-hover` `--s-cta2-bg` `--s-cta2-bg-hover` `--s-cta2-fg`
`--s-scrim` `--s-scrim-soft` `--s-scrim-modal` `--s-shadow-sheet` `--s-shadow-card` `--s-glow` `--s-img-placeholder`

**品牌常量（深浅通用）**
`--s-night` `--s-violet` `--s-plum` `--s-coral` `--s-coral-deep` `--s-cream` `--s-ink` `--s-ivory`
`--s-gradient-dusk`（纵向）`--s-gradient-dusk-h`（横向）

**字体** `--s-font` `--s-font-mono` `--s-fs-hero/display/h1/h2/h3/body/sm/xs` `--s-lh-tight/snug/body` `--s-fw-regular/medium/semibold` `--s-tracking-tight/hero`

**间距** `--s-sp-1`(4px) … `--s-sp-11`(80px)、`--s-gutter`(24px)、`--s-gutter-sm`(16px)、`--s-row-gap`(40px)

**圆角** `--s-r-xs/sm/md/lg/xl/pill`

**动效** `--s-dur-instant/fast/base/slow/xslow`（80/140/220/320/480ms）、`--s-ease-std/exit/spring`、`--s-transition`

**布局** `--s-header-h`(64) `--s-header-h-sm`(56) `--s-drawer-w`(248) `--s-hero-h` `--s-hero-min-h` `--s-content-max` `--s-bottom-nav-h` `--s-z-*`

**基元类** `.s-scroll` `.s-scroll-x` `.s-img`+`.is-loaded` `.s-skeleton` `.s-fade-x` `.s-sr` `.s-can-focus`

## 3. 主题

```ts
// web/src/composables/useTheme.ts（已实现）
import { useTheme } from '@/composables/useTheme'
const { mode, resolved, isDark, setMode, toggleTheme } = useTheme()
type ThemeMode = 'dark' | 'light' | 'system'  // 默认 dark
```
- `initTheme()` 已在 `main.ts` 调用；`index.html` 有首帧防闪内联脚本。
- **App.vue 必须删除旧的 `themeLink` / `html.classList` 主题管理**，改用它；后台/设置页不再强制浅色，统一跟随 `resolved`。
- 持久化键 `serata.themeMode`（`dark|light|system`）。

## 4. 组件接口

```vue
<!-- web/src/components/home/HomeHero.vue（新建，hero 负责） -->
<HomeHero :items="heroItems" :loading="heroLoading" />
<!-- props: items: BaseItemDto[]; loading?: boolean；无 emit，内部自己路由跳转 -->
```

```ts
// web/src/composables/useHomeHero.ts（新建，hero 负责）
export function useHomeHero(userId: MaybeRefOrGetter<string | undefined>): {
  items: Ref<BaseItemDto[]>
  loading: Ref<boolean>
  reload: () => Promise<void>
}
```

## 5. i18n（已冻结，**不要改 locale JSON**）

命名空间：`shell.*`（外壳）、`home.*`（首页）、`hero.*`（Hero）。已备好的关键键：

- `shell.nav/openNav/closeNav/expand/collapse/back/menu/libraries/myMedia/playlists/collections/me/searchPlaceholder/userMenu/theme/themeDark/themeLight/themeSystem/themeToggle/brandHome/accountSettings`
- `home.hero/viewAll/scrollBack/scrollForward/remaining/loading/error/empty/emptyHint/emptyAction/continueWatching/nextUp/latest/latestPrefix`
- `hero.label/slide/prev/next/pause/resume/goTo/play/resumePlay/details/empty/loading`
- `settings.theme/themeMode/themeDark/themeLight/themeSystem/themeDesc/accentFixed`
- 复用旧键：`app.home/search/favorites/settings/dashboard/manageServer`、`common.loading/retry/error`

## 6. 铁律（每条都会被验收）

1. 新组件**只消费令牌**，不写裸色值（`#xxx` / `rgb()`）；只有 `--s-*` 缺失时才可新增，且必须同时补进 `tokens.css`（由 Lead 统一改）。
2. 新组件**不用 `emby-*` 老类名**，全部 `s-` 前缀 + `<style scoped>`。
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

## 7. 自检

```bash
cd web
pnpm type-check    # vue-tsc，必须零错（报错若在别人的文件里，说明清楚即可）
```

浏览器自检由 Lead 统一做（真浏览器截图 + 深浅双模）。
