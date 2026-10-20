<script setup lang="ts">
/**
 * 应用外壳（Serata）—— 极简顶栏 + 抽屉侧栏 + 移动端底栏。
 *
 * 设计层契约（web/src/styles/serata/README.md）：
 * - 只用 --s-* 令牌 + s- 前缀类名 + <style scoped>，不写裸色值；
 * - 动效只碰 transform / opacity / filter，不用 transition: all；
 * - 主题唯一事实来源是 useTheme()：<html> 的 theme-dark/theme-light 与老页面浅色 CSS 都由它注入，
 *   本文件只负责「结构类」（noScrollY / withFullDrawer / withHeaderTabs / withDialogOpen），
 *   因为这些类驱动的是 emby495 老页面（库页/详情/搜索/设置/后台）的版式，不动它们的盒模型。
 *
 * 关键实现：
 * - 抽屉：≥60em 且 docked 语境（库页 / 管理设置页）常驻；其余为覆盖式 translateX(-100% → 0)。
 *   关闭态用 visibility:hidden + inert 同时移出 tab 序列与无障碍树（靠 CSS 判断视口，不用 JS 探测宽度）。
 * - 选中竖条：单个绝对定位元素，只改 transform: translateY()（FLIP 思路），不做 top/left 动画。
 *   顶栏 tabs 的激活墨条同构（translate(x, y)），折叠区用 grid-template-rows 0fr → 1fr。
 * - 页面转场：<RouterView v-slot> + <Transition name="s-page" mode="out-in">，
 *   类族由 styles/serata/motion.css 提供（本文件不写 @keyframes / 裸 ms）。
 * - 层级：抽屉 --s-z-drawer(60)、遮罩 --s-z-backdrop(40)、顶栏 --s-z-header(70)、
 *   底栏 --s-z-nav(80)，弹层带 --s-z-sheet(1000) / toast --s-z-toast(1200) 在壳层之上。
 * - 顶栏：滚动 8px 后加玻璃底；向下滚隐藏、向上滚显现，只动 transform。
 */
import { RouterView, useRoute, useRouter } from 'vue-router'
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch, watchEffect } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { getUserViews, userImageUrl } from '@/api/endpoints'
import type { BaseItemDto } from '@/api/types'
import lockupDark from '@/assets/brand/lockup-on-dark.svg'
import lockupLight from '@/assets/brand/lockup-on-light.svg'
import SheetMenu from '@/components/SheetMenu.vue'
import SToastHost from '@/components/ui/SToastHost.vue'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { flipConfig, useReducedMotionSafe } from '@/lib/motion'
import { useTheme } from '@/composables/useTheme'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
const { t } = useI18n()

/**
 * 主题：唯一事实来源。旧版在这里用 themeLink 注入浅色 CSS + 手写 <html> class，
 * 已全部删除——浅色 CSS 的注入/移除与 <html> 类名由 useTheme().initTheme() 统一负责，
 * 后台/设置页也不再强制浅色，统一跟随 resolved。
 * setMode 由设置页（SettingsDisplayView）写入，这里保留解构以维持单一入口。
 */
const { resolved, isDark, setMode, toggleTheme } = useTheme()

const brandLogo = computed(() => (isDark.value ? lockupDark : lockupLight))
/** 顶栏主题按钮的提示 = 点下去会切到哪一档（用 resolved 而不是 mode） */
const themeTargetLabel = computed(() => (resolved.value === 'dark' ? t('shell.themeLight') : t('shell.themeDark')))

const isLoginRoute = computed(() => router.currentRoute.value.name === 'login')
const views = ref<BaseItemDto[]>([])
const isHome = computed(() => router.currentRoute.value.name === 'home')
const isSearch = computed(() => router.currentRoute.value.name === 'search')
const isItem = computed(() => router.currentRoute.value.name === 'item')
/** 播放页：全屏 OSD，body 直下 overlay（无壳 —— 无 drawer/header） */
const isPlay = computed(() => router.currentRoute.value.name === 'play')
/**
 * 管理/设置语境：按 path 判断，不按 route name ——
 * 这一批页面的 route name 五花八门（dashboard-* / settings-*），但页面根节点都带
 * page-withFullDrawer（老 CSS 在 ≥60em 给左侧留 248px），漏判会让这些页左边空一条沟。
 */
const isAdminContext = computed(() => {
  const path = router.currentRoute.value.path
  return path === '/dashboard' || path.startsWith('/dashboard/') || path.startsWith('/settings/')
})
/** 库页：老页面 page-withFullDrawer 语境（docked 侧栏） */
const hasFullDrawer = computed(() => router.currentRoute.value.name === 'library')
/**
 * docked 语境 = 页面根节点带 page-withFullDrawer 的那些页：库页 / 搜索 / 设置 / 后台。
 * 这些页在 ≥60em 被老 CSS 固定左偏移，侧栏必须常驻，否则左边空一条；
 * 首页（HomeView 已换成全出血的 .s-home）、人物页（无 page-withFullDrawer）与**详情页**走覆盖式抽屉。
 *
 * 详情页（/item/:id，电影/剧集/分集同一个路由）曾经也在 docked 名单里，是照抄 Emby 的保真行为。
 * 产品决策明确覆盖它 —— 用户原话：
 *   「虽然 Emby 是常驻的 但是我觉得 你都进入详情页和集详情页了 侧边栏应该收起的 不然没有沉浸感」
 * 所以 `name === 'item'` 已从这里摘除：进详情页侧栏收起，改由顶栏汉堡打开覆盖式抽屉。
 * 同时生效的还有 ItemDetailView 侧（三个 <main> 去掉 page-withFullDrawer / page-withDockedDrawer）——
 * 两处必须同时落地，否则会出现「侧栏不常驻、但页面仍给侧栏留了 248px」的空沟。
 * 连带效果（都是期望内的）：html 不再挂 withFullDrawer、抽屉关闭时 inert、
 * 顶栏 `.s-header.is-docked .s-header__menu{display:none}` 失效 → 汉堡在详情页变得可见。
 */
const isDocked = computed(() => {
  const name = String(router.currentRoute.value.name ?? '')
  return hasFullDrawer.value || name === 'search' || isAdminContext.value
})

/**
 * 顶栏 tabs：**只有首页**那对「首页 / 收藏」。
 *
 * 库页的 10 个老式 Emby tab（库名/推荐/预告片/收藏/合集/类型/标签/工作室/分集/文件夹）
 * 已整条删除：真机 1600×900 下它和右侧的搜索/主题/头像挤在同一行，视觉极乱。
 * 删掉之后库名由 pageTitle 直接补进顶栏标题（见下），库内分节仍可用 ?tab= 深链进入
 * （LibraryView.vue 的 tab 分支保留，不删）。
 * 详情/搜索/人物/设置/后台同样不放 tabs，顶栏只留返回 + 标题/logo。
 */
const headerTabs = computed(() => {
  const current = router.currentRoute.value
  if (current.name !== 'home') return []
  const favoritesActive = current.query.tab === 'favorites'
  return [
    { key: 'app.home', label: t('app.home'), active: !favoritesActive, route: '/' },
    { key: 'app.favorites', label: t('app.favorites'), active: favoritesActive, route: '/?tab=favorites' },
  ]
})

const currentLib = computed(() => {
  if (router.currentRoute.value.name !== 'library') return null
  const id = router.currentRoute.value.params.id
  return views.value.find((v) => v.Id === id) ?? null
})

/** 顶栏标题：搜索页 / 管理语境页名 / 当前库名；其余用品牌 logo */
const pageTitle = computed(() => {
  if (isSearch.value) return t('search.title')
  if (isAdminContext.value) return adminPageTitle.value
  // 库页：顶栏已没有 tab 条（headerTabs 对 library 返回 []），库名就是标题。
  // 曾经这里靠「第 0 个 tab 就是库名」来判重，tabs 一删标题就空了 —— 直接吃 currentLib.Name。
  // views 列表尚未加载完（currentLib 为 null）时仍回落到品牌 logo，不写半截标题。
  if (currentLib.value) return currentLib.value.Name ?? t('shell.libraries')
  return ''
})

/** 管理/设置语境标题 = 当前侧栏项名（真实站管理页 header 显示页名，如"用户"/"媒体库"） */
const adminPageTitle = computed(() => {
  const path = router.currentRoute.value.path
  for (const s of adminSections.value) {
    for (const it of s.items) {
      if (it.route && it.route === path) return it.label
    }
  }
  return t('dash.serverDashboard')
})

// 管理侧栏（真实站 dump:emby495-dashboard.md 的分组结构）
// 已移除「元数据管理器」（路由已删）、「Emby Premiere」（路由已删），
// 以及「Serata Web 设置」组（播放/通用/键盘和遥控器 三条 route 为 null 的死按钮——
// 同名能力已由下面「<用户> 首选」组的 播放/配置 覆盖）。
// 本轮再砍 8 条（用户点名不需要）：电视直播 / 转码 / 神医助手 / Backup & Restore /
// Diagnostic Options / Open Subtitles / Subtitle Options / User Sessions ——
// 侧栏项、路由、插件详情视图一并删除，不留死链（这些页的路由与视图文件已全库 0 引用）。
const adminSections = computed(() => [
  {
    title: `${auth.user?.Name ?? ''} ${t('dash.secPrefs')}`,
    items: [
      { label: t('dash.prefDisplay'), route: '/settings/display' },
      { label: t('dash.prefHome'), route: '/settings/homescreen' },
      { label: t('dash.prefPlayback'), route: '/settings/playback' },
      { label: t('dash.prefSubtitles'), route: '/settings/subtitles' },
      { label: t('dash.prefProfile'), route: '/settings/general' },
    ],
  },
  {
    title: t('dash.secServer'),
    items: [
      { label: t('dash.serverDashboard'), route: '/dashboard' },
      { label: t('dash.serverGeneral'), route: '/dashboard/general' },
      { label: t('dash.serverUsers'), route: '/dashboard/users' },
      { label: t('dash.serverLibraries'), route: '/dashboard/libraries' },
      { label: t('dash.serverNetwork'), route: '/dashboard/network' },
      { label: t('dash.serverDatabase'), route: '/dashboard/database' },
      { label: t('dash.serverSync'), route: '/dashboard/convert' },
      { label: t('dash.serverTasks'), route: '/dashboard/tasks' },
      { label: t('dash.serverLogs'), route: '/dashboard/logs' },
    ],
  },
  {
    title: t('dash.secDevices'),
    items: [
      { label: t('dash.devDevices'), route: '/dashboard/devices' },
    ],
  },
  {
    title: t('dash.secAdvanced'),
    items: [
      { label: t('dash.advPlugins'), route: '/dashboard/plugins' },
      { label: t('dash.advApiKeys'), route: '/dashboard/apikeys' },
    ],
  },
])

/**
 * 真正渲染的侧栏分组：**没有 route 的条目一律不渲染**。
 * 历史坑：曾经有 3 条 `route: null` 的条目（播放/通用/键盘和遥控器），按钮有 hover 有焦点，
 * 点下去什么都不发生——比缺失更伤。这里做兜底，任何人再往数据里塞 null 也不会变成死按钮。
 */
const visibleAdminSections = computed(() =>
  adminSections.value
    .map((s) => ({ ...s, items: s.items.filter((i) => Boolean(i.route)) }))
    .filter((s) => s.items.length > 0),
)

// ---------- 抽屉（覆盖式 / docked 共用同一份状态） ----------
const drawerEl = ref<HTMLElement | null>(null)
const menuBtnEl = ref<HTMLButtonElement | null>(null)
const overlayDrawerOpen = ref(false)
/** 关闭且非 docked 时整个抽屉 inert（docked 常驻，不 inert） */
const drawerInert = computed(() => !isDocked.value && !overlayDrawerOpen.value)

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

function focusFirstInDrawer(): void {
  const root = drawerEl.value
  if (!root) return
  const target = root.querySelector<HTMLElement>('.s-drawer__head button, .s-brand')
  target?.focus()
}

function openDrawer(): void {
  overlayDrawerOpen.value = true
  void nextTick(focusFirstInDrawer)
}

function closeDrawer(): void {
  if (!overlayDrawerOpen.value) return
  overlayDrawerOpen.value = false
  // 关闭后焦点回到汉堡按钮
  menuBtnEl.value?.focus()
}

function toggleDrawer(): void {
  if (overlayDrawerOpen.value) closeDrawer()
  else openDrawer()
}

/** Tab 焦点陷阱（只在覆盖式打开时生效；inert 已经排除折叠区里的项） */
function onDrawerKeydown(e: KeyboardEvent): void {
  if (e.key !== 'Tab' || !overlayDrawerOpen.value) return
  const root = drawerEl.value
  if (!root) return
  const list = Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE))
  const first = list[0]
  const last = list[list.length - 1]
  if (!first || !last) return
  const active = document.activeElement
  if (!e.shiftKey && active === last) {
    e.preventDefault()
    first.focus()
  } else if (e.shiftKey && (active === first || !root.contains(active))) {
    e.preventDefault()
    last.focus()
  }
}

function onWindowKeydown(e: KeyboardEvent): void {
  if (e.key === 'Escape' && overlayDrawerOpen.value) closeDrawer()
}

// ---------- 分组折叠 ----------
const userLibsOpen = ref(true)
const adminCollapsed = ref<Record<string, boolean>>({})

function isAdminSectionOpen(title: string): boolean {
  return !adminCollapsed.value[title]
}

function toggleAdminSection(title: string): void {
  adminCollapsed.value = { ...adminCollapsed.value, [title]: !adminCollapsed.value[title] }
}

function toggleAllGroups(): void {
  userLibsOpen.value = !userLibsOpen.value
}

// ---------- 列表 FLIP（auto-animate）----------
/**
 * 侧栏库列表 / 管理分组 / 底部导航的增删重排走 FLIP。
 * 铁律 16：JS 动画动手前必须先过 useReducedMotionSafe()——reduce 时 duration 直接压到 0，
 * 不位移（auto-animate 自己也会忽略 reduced motion，这里是双保险）。
 * 指令只在 mounted 读一次 options，所以这里取当次快照即可。
 */
const flipAnimOptions = flipConfig(useReducedMotionSafe().value)

// ---------- 选中项竖条（FLIP：只动 transform） ----------
const activeKey = computed(() => {
  const current = router.currentRoute.value
  const name = String(current.name ?? '')
  if (name === 'library') return `lib:${current.params.id}`
  if (name === 'search') return 'search'
  if (name === 'home') return 'home'
  if (isAdminContext.value) return `admin:${current.path}`
  return ''
})

const navContentEl = ref<HTMLElement | null>(null)
const indicatorTop = ref(0)
const indicatorHeight = ref(0)
const indicatorVisible = ref(false)
const indicatorReady = ref(false)

const indicatorStyle = computed<Record<string, string>>(() => ({
  transform: `translateY(${indicatorTop.value}px)`,
  height: `${indicatorHeight.value}px`,
  ...(indicatorReady.value ? {} : { transition: 'none' }),
}))

/**
 * 量出当前选中行在滚动内容里的位置。
 * getBoundingClientRect 差值天然抵消滚动与抽屉位移，嵌套折叠组也不用管 offsetParent。
 */
async function syncIndicator(): Promise<void> {
  await nextTick()
  updateNavFades()
  // 顶栏墨条与侧栏竖条在同一批时机（挂载/路由/视图列表/折叠/开抽屉/改窗口）重算
  syncTabInk()
  const content = navContentEl.value
  if (!content) {
    indicatorVisible.value = false
    return
  }
  const row = content.querySelector<HTMLElement>('.s-nav__row.is-active')
  // 选中行在收起的分组里：竖条隐藏（等展开后再出现）
  if (!row || row.closest('.s-collapse:not(.is-open)')) {
    indicatorVisible.value = false
    return
  }
  const base = content.getBoundingClientRect()
  const rect = row.getBoundingClientRect()
  indicatorTop.value = rect.top - base.top
  indicatorHeight.value = rect.height
  indicatorVisible.value = true
  if (!indicatorReady.value) {
    requestAnimationFrame(() => {
      indicatorReady.value = true
    })
  }
}

// ---------- 顶栏 tabs 激活墨条（FLIP：只动 transform，横向滚动时跟着 tab 走） ----------
const tabsEl = ref<HTMLElement | null>(null)
const tabInkX = ref(0)
const tabInkY = ref(0)
const tabInkVisible = ref(false)
/** 与旧 ::after 下划线同宽（16px） */
const TAB_INK_W = 16

/**
 * 墨条与侧栏竖条同构：单个绝对定位元素，切换 tab 时只改 transform。
 * 横向滚动条容器里用「视口差值 + scrollLeft」换算成内容坐标，滚动时墨条不会被落下。
 */
function syncTabInk(): void {
  const nav = tabsEl.value
  if (!nav) {
    tabInkVisible.value = false
    return
  }
  const navRect = nav.getBoundingClientRect()
  const active = nav.querySelector<HTMLElement>('.s-tab.is-active')
  // 移动端 tabs 是 display:none（量不到宽度），这里不写死视口判断，量不到就隐藏
  if (!active || navRect.width === 0) {
    tabInkVisible.value = false
    return
  }
  const rect = active.getBoundingClientRect()
  tabInkX.value = rect.left - navRect.left + nav.scrollLeft + rect.width / 2 - TAB_INK_W / 2
  // 墨条底边离 tab 底边 3px（与旧 ::after 视觉一致）
  tabInkY.value = rect.bottom - navRect.bottom - 5
  tabInkVisible.value = true
}

const tabInkStyle = computed<Record<string, string>>(() => ({
  transform: `translate(${tabInkX.value}px, ${tabInkY.value}px)`,
}))

// ---------- 抽屉长列表：只有真的还能滚，才显示那一侧的渐隐遮罩 ----------
const navEl = ref<HTMLElement | null>(null)
const navFadeTop = ref(false)
const navFadeBottom = ref(false)

function updateNavFades(): void {
  const el = navEl.value
  if (!el) return
  navFadeTop.value = el.scrollTop > 2
  navFadeBottom.value = el.scrollTop + el.clientHeight < el.scrollHeight - 2
}

// ---------- 顶栏滚动：8px 后玻璃底、下滚隐藏 / 上滚显现 ----------
const headerElevated = ref(false)
const headerHidden = ref(false)
let lastScrollTop = 0

/**
 * 壳用 noScrollY 锁住了 window 滚动，真正的滚动容器是老页面的 .scrollY / .s-scroll。
 * 滚动事件不冒泡但会在捕获阶段经过 document，所以用捕获监听统一接住。
 */
function onAnyScroll(e: Event): void {
  const target = e.target
  // 抽屉内部滚动：只影响抽屉自己的渐隐遮罩，不影响顶栏
  if (target instanceof Node && drawerEl.value?.contains(target)) {
    updateNavFades()
    return
  }
  const y = target instanceof HTMLElement ? target.scrollTop : window.scrollY || 0
  headerElevated.value = y > 8
  if (y <= 8 || overlayDrawerOpen.value) {
    headerHidden.value = false
  } else {
    const dy = y - lastScrollTop
    if (dy > 6) headerHidden.value = true
    else if (dy < -6) headerHidden.value = false
  }
  lastScrollTop = y
}

function resetHeader(): void {
  headerHidden.value = false
  headerElevated.value = false
  lastScrollTop = 0
}

// ---------- 用户菜单（真实站 headerUserButton → actionSheet） ----------
const userMenuOpen = ref(false)
const appVersionText = 'Serata Web 0.1.0'
const userMenuItems = computed(() => [
  { id: 'settings', icon: '\uE8B8', label: t('app.appSettings'), secondaryText: appVersionText },
  { id: 'dashboard', icon: 'dashboard', label: t('app.manageServer') },
  { id: 'switch', icon: '\uE7FD', label: t('app.switchUser') },
  { id: 'logout', icon: 'exit_to_app', label: t('app.signOut') },
])

const avatarError = ref(false)
// userImageUrl 在没有 PrimaryImageTag 时返回空串：本服务器账号没有头像，
// 无条件拼 URL 会在**每一个页面**打一条恒定 404 的 /Users/{id}/Images/Primary。
// 空串时下面的 <img v-if="avatarSrc && !avatarError"> 直接不渲染，走既有的 svg 占位兜底。
const avatarSrc = computed(() => (auth.user ? userImageUrl(auth.user.Id, auth.user.PrimaryImageTag) : ''))

function onUserMenuPick(item: { id?: string }): void {
  userMenuOpen.value = false
  if (item.id === 'settings') void router.push('/settings/display')
  else if (item.id === 'dashboard') void router.push('/dashboard')
  else if (item.id === 'switch' || item.id === 'logout') {
    auth.logout()
    void router.push('/login')
  }
}

// ---------- 移动端底栏选中态 ----------
const bottomActive = computed(() => {
  const current = router.currentRoute.value
  const name = current.name
  if (name === 'library') return 'library'
  if (name === 'search') return 'search'
  if (name === 'home') return current.query.tab === 'favorites' ? 'favorites' : 'home'
  return ''
})

// ---------- 小工具 ----------
function goHome(): void {
  if (overlayDrawerOpen.value) overlayDrawerOpen.value = false
  void router.push('/')
}

function openLibrary(id: string): void {
  void router.push({ name: 'library', params: { id } })
}

async function loadViews(): Promise<void> {
  if (!auth.user) return
  try {
    views.value = (await getUserViews(auth.user.Id)).Items ?? []
  } catch {
    /* 侧栏库列表失败不阻塞 */
  }
}

/**
 * 登录态一变就（重）拉库列表 —— 「侧栏 · 我的媒体整段空白」的根因修复。
 * App.vue 是根组件，onMounted 只在**整页加载**时跑一次：从登录页进来的那次挂载，
 * auth.user 还是 null，loadViews() 直接 return，之后再没人叫它 ——
 * 于是整个会话里 views 恒为空，「我的媒体」展开后一行都没有（只有 F5 才恢复）。
 * immediate 同时替掉原来 onMounted 里那次调用；登出时清空，避免残留上一个账号的库。
 */
watch(
  () => auth.user?.Id,
  (id) => {
    if (id) void loadViews()
    else views.value = []
  },
  { immediate: true },
)

// ---------- 结构类：主题类归 useTheme，这里只碰版式类 ----------
watchEffect(() => {
  const el = document.documentElement
  const bare = isLoginRoute.value || isPlay.value
  el.classList.toggle('noScrollY', true)
  el.classList.toggle('withFullDrawer', !bare && isDocked.value)
  el.classList.toggle('withHeaderTabs', !bare && headerTabs.value.length > 0)
  el.classList.toggle('withDialogOpen', !bare && overlayDrawerOpen.value)
  document.body.className = 'mainAnimatedPages skinBody'
})

// 导航后自动收起覆盖式抽屉（真实站行为），并把焦点交还汉堡按钮
watch(
  () => route.fullPath,
  () => {
    if (overlayDrawerOpen.value) {
      overlayDrawerOpen.value = false
      void nextTick(() => menuBtnEl.value?.focus())
    }
    resetHeader()
    void syncIndicator()
  },
)

watch(activeKey, () => {
  void syncIndicator()
})
watch(views, () => {
  void syncIndicator()
})
watch([userLibsOpen, adminCollapsed], () => {
  void syncIndicator()
})
watch(overlayDrawerOpen, (open) => {
  if (open) void syncIndicator()
})
watch(
  () => auth.user?.Id,
  () => {
    avatarError.value = false
  },
)

function onResize(): void {
  void syncIndicator()
  updateNavFades()
}

onMounted(() => {
  void syncIndicator()
  document.addEventListener('scroll', onAnyScroll, { capture: true, passive: true })
  window.addEventListener('keydown', onWindowKeydown)
  window.addEventListener('resize', onResize)
})

onBeforeUnmount(() => {
  document.removeEventListener('scroll', onAnyScroll, { capture: true })
  window.removeEventListener('keydown', onWindowKeydown)
  window.removeEventListener('resize', onResize)
})
</script>

<template>
  <template v-if="isLoginRoute || isPlay">
    <!--
      登录/播放页：全屏无壳（播放页 = body 直下 overlay）。
      刻意**不套** <Transition>：LoginView.vue 的模板是 4 个无条件根（div/header/main/div），
      <Transition> 只接受单根子节点，套上去会告警 "expects exactly one child" 并静默失效。
      播放页（VideoOsdView）虽然单根，但为它单独分一条分支不值得，登录页本来也不该有转场。
      视图文件不在本任务写作用域内，所以在这里分流（完整根节点审计见交付说明）。
    -->
    <RouterView />
  </template>
  <template v-else>
    <!-- body 级背景层（详情页会把 backdrop 图写到这里，z-index:-1，保留不动） -->
    <div class="backdropContainer"></div>
    <!-- 背景大图为什么以前看不见：
         `.backdropContainer` 是 `z-index:-1`，而 `.backgroundContainer` 吃的是
         `html,.backgroundContainer{background-color:hsl(var(--background-*))}` 这条**不透明**底色，
         于是背景图被整片盖死。原版 Emby 的开关是 `.backgroundContainer.withBackdrop`
         （浅色 rgba(255,255,255,.8) / 深色 rgba(0,0,0,.8)）—— 规则一直在 CSS 里，
         只是我们的壳子从来没加过这个类。
         `s-backdrop-dark` 再补一刀：详情页根节点是 `darkContentContainer-item`（白字），
         浅色主题那条 80% 白色 scrim 会让白字继续白压白，所以这两页在**两种主题下都必须暗**。
         注：这不是自创配色，是把原版「详情页 = 白字压暗色 fanart」的设计意图恢复回来。 -->
    <div
      class="backgroundContainer"
      :class="{ 'backgroundContainer-withdockeddrawer': isDocked, withBackdrop: isItem, 's-backdrop-dark': isItem }"
    ></div>

    <!-- 覆盖式抽屉遮罩（docked 语境在 ≥60em 由 CSS 隐藏）；
         进出场走 motion.css 的 s-scrim 类族（进场 --s-dur-base，退场 --s-dur-exit 更快） -->
    <Transition name="s-scrim">
      <div
        v-if="overlayDrawerOpen"
        class="s-backdrop"
        :class="{ 'is-docked': isDocked }"
        @click="closeDrawer"
        aria-hidden="true"
      ></div>
    </Transition>

    <!-- 侧栏：库页/管理页 docked 常驻；其余覆盖式（<60em 一律覆盖式） -->
    <aside
      id="s-nav-drawer"
      ref="drawerEl"
      class="s-drawer"
      :class="{ 'is-open': overlayDrawerOpen, 'is-docked': isDocked }"
      :role="isDocked ? 'navigation' : 'dialog'"
      :aria-modal="!isDocked && overlayDrawerOpen ? 'true' : undefined"
      :aria-label="t('shell.nav')"
      :inert="drawerInert || undefined"
      @keydown="onDrawerKeydown"
    >
      <div class="s-drawer__head">
        <a
          class="s-brand"
          href="/"
          :title="t('shell.brandHome')"
          :aria-label="t('shell.brandHome')"
          @click.prevent="goHome"
        >
          <img class="s-brand__img" :src="brandLogo" alt="Serata" />
        </a>
        <button
          type="button"
          class="s-iconbtn s-drawer__close"
          :aria-label="t('shell.closeNav')"
          @click="closeDrawer"
        >
          <svg class="s-ico" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg>
        </button>
      </div>

      <!--
        抽屉里**不留任何搜索入口**（用户 2026-10-20 第二轮口径："搜索栏不需要两个 侧边栏那个删掉"）。
        演进史：抽屉搜索输入框 → 抽屉「搜索」导航行 → 全删。
        为什么上一轮留下的「搜索」行也得走：≥60em 时抽屉在搜索页是常驻 docked，
        侧栏那条「搜索」行会和页面自己的输入框同屏 —— 一屏两个搜索入口。
        现在搜索入口只有两处，且互斥、每屏最多一个：
          · 桌面（>768px）：顶栏放大镜（搜索页自身除外，那一页的输入框就是入口）
          · 移动（≤768px）：底部导航的「搜索」tab（顶栏放大镜此时隐藏）
        打字统一发生在搜索页自己的输入框（SearchView 的 #searchPageInput，带搜索历史）。
      -->

      <div class="s-drawer__navwrap">
        <nav ref="navEl" class="s-nav s-scroll" :aria-label="t('shell.nav')">
          <div ref="navContentEl" class="s-nav__content">
            <span
              class="s-nav__indicator"
              :class="{ 'is-visible': indicatorVisible }"
              :style="indicatorStyle"
              aria-hidden="true"
            ></span>

            <template v-if="!isAdminContext">
              <!-- 展开/收起所有分组 -->
              <button type="button" class="s-nav__row s-nav__row--ghost" @click="toggleAllGroups">
                <span class="s-nav__label">{{ userLibsOpen ? t('shell.collapse') : t('shell.expand') }}</span>
                <svg class="s-nav__chev" :class="{ 'is-open': userLibsOpen }" viewBox="0 0 24 24" aria-hidden="true">
                  <path d="m6 9 6 6 6-6" />
                </svg>
              </button>
              <button
                type="button"
                class="s-nav__row"
                :class="{ 'is-active': activeKey === 'home' }"
                @click="router.push('/')"
              >
                <span class="s-nav__label">{{ t('app.home') }}</span>
              </button>
              <div class="s-nav__group">
                <button
                  type="button"
                  class="s-nav__grouphead"
                  :aria-expanded="userLibsOpen"
                  aria-controls="s-group-userlibs"
                  @click="userLibsOpen = !userLibsOpen"
                >
                  <span class="s-nav__grouphead-label">{{ t('library.myMediaSection') }}</span>
                  <svg class="s-nav__chev" :class="{ 'is-open': userLibsOpen }" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="m6 9 6 6 6-6" />
                  </svg>
                </button>
                <div id="s-group-userlibs" class="s-collapse" :class="{ 'is-open': userLibsOpen }">
                  <div class="s-collapse__inner">
                    <div
                      class="s-collapse__content"
                      :inert="!userLibsOpen || undefined"
                      v-auto-animate="flipAnimOptions"
                    >
                      <!-- 库行：只留文字，无图标（行高与对齐由 .s-nav__row 统一） -->
                      <button
                        v-for="lib in views"
                        :key="lib.Id"
                        type="button"
                        class="s-nav__row s-nav__row--lib"
                        :class="{ 'is-active': activeKey === `lib:${lib.Id}` }"
                        :title="lib.Name"
                        @click="openLibrary(lib.Id)"
                      >
                        <span class="s-nav__label">{{ lib.Name }}</span>
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            </template>

            <!-- 管理侧栏（dashboard / 用户设置语境） -->
            <template v-else>
              <div v-for="(section, si) in visibleAdminSections" :key="section.title" class="s-nav__group">
                <button
                  type="button"
                  class="s-nav__grouphead"
                  :aria-expanded="isAdminSectionOpen(section.title)"
                  :aria-controls="`s-group-admin-${si}`"
                  @click="toggleAdminSection(section.title)"
                >
                  <span class="s-nav__grouphead-label">{{ section.title }}</span>
                  <svg
                    class="s-nav__chev"
                    :class="{ 'is-open': isAdminSectionOpen(section.title) }"
                    viewBox="0 0 24 24"
                    aria-hidden="true"
                  >
                    <path d="m6 9 6 6 6-6" />
                  </svg>
                </button>
                <div
                  :id="`s-group-admin-${si}`"
                  class="s-collapse"
                  :class="{ 'is-open': isAdminSectionOpen(section.title) }"
                >
                  <div class="s-collapse__inner">
                    <div
                      class="s-collapse__content"
                      :inert="!isAdminSectionOpen(section.title) || undefined"
                      v-auto-animate="flipAnimOptions"
                    >
                      <button
                        v-for="item in section.items"
                        :key="item.label"
                        type="button"
                        class="s-nav__row"
                        :class="{ 'is-active': route.path === item.route }"
                        :title="item.label"
                        @click="item.route && router.push(item.route)"
                      >
                        <span class="s-nav__label">{{ item.label }}</span>
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            </template>
          </div>
        </nav>
        <!-- 长列表上下渐隐（只在真的还能滚的一侧出现；pointer-events: none 不挡点击） -->
        <div
          class="s-drawer__fade s-drawer__fade--top"
          :class="{ 'is-visible': navFadeTop }"
          aria-hidden="true"
        ></div>
        <div
          class="s-drawer__fade s-drawer__fade--bottom"
          :class="{ 'is-visible': navFadeBottom }"
          aria-hidden="true"
        ></div>
      </div>
    </aside>

    <!-- 顶栏：左（返回/首页/菜单/标题或 logo）· 中（库页 tabs）· 右（主题/用户） -->
    <header
      class="s-header"
      :class="{ 'is-elevated': headerElevated, 'is-hidden': headerHidden, 'is-docked': isDocked }"
    >
      <div class="s-header__inner">
        <div class="s-header__left">
          <button
            v-if="isSearch || isItem"
            type="button"
            class="s-iconbtn"
            :aria-label="t('shell.back')"
            @click="router.back()"
          >
            <svg class="s-ico" viewBox="0 0 24 24" aria-hidden="true"><path d="m15 5-7 7 7 7" /></svg>
          </button>
          <button
            v-if="!isHome && !isAdminContext"
            type="button"
            class="s-iconbtn"
            :aria-label="t('app.home')"
            @click="router.push('/')"
          >
            <svg class="s-ico" viewBox="0 0 24 24" aria-hidden="true">
              <path d="M4 11.5 12 4l8 7.5" />
              <path d="M6.5 10.8V20h11v-9.2" />
            </svg>
          </button>
          <button
            ref="menuBtnEl"
            type="button"
            class="s-iconbtn s-header__menu"
            :aria-label="overlayDrawerOpen ? t('shell.closeNav') : t('shell.menu')"
            :aria-expanded="overlayDrawerOpen"
            aria-controls="s-nav-drawer"
            @click="toggleDrawer"
          >
            <svg class="s-ico" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16" /></svg>
          </button>
          <h1 v-if="pageTitle" class="s-header__title">{{ pageTitle }}</h1>
          <a
            v-else
            class="s-brand s-brand--header"
            href="/"
            :title="t('shell.brandHome')"
            :aria-label="t('shell.brandHome')"
            @click.prevent="goHome"
          >
            <img class="s-brand__img" :src="brandLogo" alt="Serata" />
          </a>
        </div>

        <nav
          v-if="!isAdminContext && headerTabs.length"
          ref="tabsEl"
          class="s-header__tabs s-scroll-x"
          :aria-label="t('shell.libraries')"
        >
          <button
            v-for="tab in headerTabs"
            :key="tab.key"
            type="button"
            class="s-tab"
            :class="{ 'is-active': tab.active }"
            :aria-current="tab.active ? 'page' : undefined"
            @click="tab.route && router.push(tab.route)"
          >{{ tab.label }}</button>
          <!-- 激活墨条：整条 tabs 共用一条，切换时只动 transform（不再每个 tab 各画一个 ::after） -->
          <span
            class="s-tabs__ink"
            :class="{ 'is-visible': tabInkVisible }"
            :style="tabInkStyle"
            aria-hidden="true"
          ></span>
        </nav>

        <div class="s-header__right">
          <!--
            顶栏放大镜（用户 2026-10-20 第二轮定调："搜索栏不需要两个 侧边栏那个删掉"）：
            侧栏那条「搜索」行已删，桌面端搜索入口就是这一个放大镜。
            两条自我约束，保证任何一屏都只有一个搜索入口：
              · 搜索页自身不显示（那一页的 #searchPageInput 就是入口，
                否则"放大镜 + 输入框"又是两个）；
              · ≤768px 不显示（移动端底栏已有「搜索」tab，见 .s-iconbtn--search 的媒体查询）。
          -->
          <button
            v-if="!isSearch"
            type="button"
            class="s-iconbtn s-iconbtn--search"
            :aria-label="t('app.search')"
            @click="router.push('/search')"
          >
            <svg class="s-ico" viewBox="0 0 24 24" aria-hidden="true">
              <circle cx="11" cy="11" r="6.5" />
              <path d="m16 16 4 4" />
            </svg>
          </button>
          <button
            type="button"
            class="s-iconbtn"
            :aria-label="t('shell.themeToggle')"
            :title="themeTargetLabel"
            @click="toggleTheme()"
          >
            <svg v-if="resolved === 'dark'" class="s-ico" viewBox="0 0 24 24" aria-hidden="true">
              <circle cx="12" cy="12" r="4" />
              <path d="M12 2.5v2M12 19.5v2M4.5 4.6 6 6M18 18l1.5 1.5M2.5 12h2M19.5 12h2M4.5 19.4 6 18M18 6l1.5-1.5" />
            </svg>
            <svg v-else class="s-ico" viewBox="0 0 24 24" aria-hidden="true">
              <path d="M20 14.5A8.5 8.5 0 1 1 9.5 4a6.8 6.8 0 0 0 10.5 10.5Z" />
            </svg>
          </button>
          <button
            type="button"
            class="s-avatar"
            :aria-label="t('shell.userMenu')"
            :title="auth.user?.Name"
            @click="userMenuOpen = true"
          >
            <img v-if="avatarSrc && !avatarError" class="s-avatar__img" :src="avatarSrc" alt="" @error="avatarError = true" />
            <svg v-else class="s-ico" viewBox="0 0 24 24" aria-hidden="true">
              <circle cx="12" cy="8.5" r="3.5" />
              <path d="M4.5 20a7.5 7.5 0 0 1 15 0" />
            </svg>
          </button>
        </div>
      </div>
    </header>

    <!-- 用户菜单（headerUserButton → actionSheet：应用设置/管理 Server/更改用户/注销退出） -->
    <SheetMenu
      :open="userMenuOpen"
      :preview-icon="'\uE7FD'"
      :preview-title="auth.user?.Name ?? ''"
      :preview-secondary="appVersionText"
      :items="userMenuItems"
      @close="userMenuOpen = false"
      @pick="onUserMenuPick"
    />

    <!-- 路由转场：s-page 类族在 motion.css（进场淡入+上浮 --s-dur-slow，退场 --s-dur-fast 更快） -->
    <RouterView v-slot="{ Component, route }">
      <Transition name="s-page" mode="out-in">
        <component :is="Component" :key="route.path" />
      </Transition>
    </RouterView>

    <!-- 移动端底部导航（桌面端 CSS 隐藏，不用 JS 判断视口）；
         进场走 motion.css 的 s-up 类族（底部弹层语义），列表 FLIP 走 auto-animate -->
    <Transition name="s-up" appear>
      <nav
        class="s-bottom-nav"
        role="navigation"
        :aria-label="t('shell.nav')"
        v-auto-animate="flipAnimOptions"
      >
        <button
          type="button"
          class="s-bottom-nav__item"
          :class="{ 'is-active': bottomActive === 'home' }"
          :aria-current="bottomActive === 'home' ? 'page' : undefined"
          @click="router.push('/')"
        >
          <svg class="s-ico" viewBox="0 0 24 24" aria-hidden="true">
            <path d="M4 11.5 12 4l8 7.5" />
            <path d="M6.5 10.8V20h11v-9.2" />
          </svg>
          <span class="s-bottom-nav__label">{{ t('app.home') }}</span>
        </button>
        <button
          type="button"
          class="s-bottom-nav__item"
          :class="{ 'is-active': bottomActive === 'library' }"
          :aria-current="bottomActive === 'library' ? 'page' : undefined"
          @click="openDrawer"
        >
          <svg class="s-ico" viewBox="0 0 24 24" aria-hidden="true">
            <rect x="3.5" y="4.5" width="7" height="15" rx="1.6" />
            <rect x="13.5" y="4.5" width="7" height="15" rx="1.6" />
          </svg>
          <span class="s-bottom-nav__label">{{ t('shell.libraries') }}</span>
        </button>
        <button
          type="button"
          class="s-bottom-nav__item"
          :class="{ 'is-active': bottomActive === 'search' }"
          :aria-current="bottomActive === 'search' ? 'page' : undefined"
          @click="router.push('/search')"
        >
          <svg class="s-ico" viewBox="0 0 24 24" aria-hidden="true">
            <circle cx="11" cy="11" r="6.5" />
            <path d="m16 16 4 4" />
          </svg>
          <span class="s-bottom-nav__label">{{ t('app.search') }}</span>
        </button>
        <button
          type="button"
          class="s-bottom-nav__item"
          :class="{ 'is-active': bottomActive === 'favorites' }"
          :aria-current="bottomActive === 'favorites' ? 'page' : undefined"
          @click="router.push('/?tab=favorites')"
        >
          <svg class="s-ico" viewBox="0 0 24 24" aria-hidden="true">
            <path d="M12 20s-7.5-4.6-7.5-9.6A4.4 4.4 0 0 1 12 7.4a4.4 4.4 0 0 1 7.5 3c0 5-7.5 9.6-7.5 9.6Z" />
          </svg>
          <span class="s-bottom-nav__label">{{ t('app.favorites') }}</span>
        </button>
        <button
          type="button"
          class="s-bottom-nav__item"
          :class="{ 'is-active': userMenuOpen }"
          :aria-current="userMenuOpen ? 'page' : undefined"
          @click="userMenuOpen = true"
        >
          <svg class="s-ico" viewBox="0 0 24 24" aria-hidden="true">
            <circle cx="12" cy="8.5" r="3.5" />
            <path d="M4.5 20a7.5 7.5 0 0 1 15 0" />
          </svg>
          <span class="s-bottom-nav__label">{{ t('shell.me') }}</span>
        </button>
      </nav>
    </Transition>
  </template>

  <!-- 全局 toast 宿主：挂在壳之外（第三个根节点），登录页/播放页也能弹 -->
  <SToastHost />
</template>

<style scoped>
/* ============================================================
   图标基元：内联 SVG 一律走 currentColor 描边（不再消费 md-icon 字体）
   ============================================================ */
.s-ico {
  width: 20px;
  height: 20px;
  flex: none;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.7;
  stroke-linecap: round;
  stroke-linejoin: round;
}

/* ============================================================
   顶栏
   ============================================================ */
.s-header {
  position: fixed;
  top: 0;
  inset-inline: 0;
  z-index: var(--s-z-header);
  height: calc(var(--s-header-h) + env(safe-area-inset-top, 0px));
  padding-top: env(safe-area-inset-top, 0px);
  box-sizing: border-box;
  transform: translateY(0);
  transition:
    transform var(--s-dur-base) var(--s-ease-std),
    background-color var(--s-dur-base) var(--s-ease-std);
  will-change: transform;
}

/* 滚动 8px 后的玻璃底 */
.s-header.is-elevated {
  background: color-mix(in srgb, var(--s-bg) 82%, transparent);
  backdrop-filter: blur(18px);
}

/* 向下滚隐藏 / 向上滚显现：只动 transform */
.s-header.is-hidden {
  transform: translateY(-100%);
}

/* docked 语境：顶栏从侧栏右侧开始（宽度靠 margin 收缩，不写 left 动画） */
@media all and (min-width: 60em) {
  .s-header.is-docked {
    margin-inline-start: var(--s-drawer-w);
  }
}

.s-header__inner {
  display: flex;
  align-items: center;
  gap: var(--s-sp-2);
  height: 100%;
  padding: 0 var(--s-gutter-sm);
}

.s-header__left {
  display: flex;
  align-items: center;
  gap: var(--s-sp-1);
  min-width: 0;
  flex: 0 1 auto;
}

.s-header__right {
  display: flex;
  align-items: center;
  gap: var(--s-sp-1);
  margin-inline-start: auto;
  flex: none;
}

.s-header__title {
  margin: 0;
  padding-inline-start: var(--s-sp-2);
  font-family: var(--s-font);
  font-size: var(--s-fs-h3);
  font-weight: var(--s-fw-semibold);
  letter-spacing: var(--s-tracking-tight);
  color: var(--s-text-1);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* 桌面端 docked 时汉堡无意义（侧栏常驻），由 CSS 隐藏 */
@media all and (min-width: 60em) {
  .s-header.is-docked .s-header__menu {
    display: none;
  }
}

/* ============================================================
   通用按钮 / 头像 / 品牌
   ============================================================ */
.s-iconbtn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  padding: 0;
  border: 0;
  border-radius: var(--s-r-sm);
  background: transparent;
  color: var(--s-text-2);
  font-family: var(--s-font);
  cursor: pointer;
  transition:
    color var(--s-dur-fast) var(--s-ease-std),
    background-color var(--s-dur-fast) var(--s-ease-std);
}

.s-iconbtn:hover {
  color: var(--s-text-1);
  background: var(--s-hover);
}

.s-avatar {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 0;
  border: 0;
  border-radius: var(--s-r-pill);
  overflow: hidden;
  background: var(--s-raised);
  color: var(--s-text-2);
  cursor: pointer;
  transition: transform var(--s-dur-fast) var(--s-ease-std);
}

.s-avatar:hover {
  transform: scale(1.06);
}

.s-avatar__img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.s-brand {
  display: inline-flex;
  align-items: center;
  min-width: 0;
  text-decoration: none;
}

.s-brand__img {
  display: block;
  width: auto;
  height: 24px;
}

/* 抽屉里的 lockup 稍大一点（头部 64px，有空间） */
.s-drawer .s-brand__img {
  height: 28px;
}

.s-brand--header {
  padding-inline-start: var(--s-sp-1);
}

/* ============================================================
   顶栏 tabs（横向可滚，激活项珊瑚下划线）
   ============================================================ */
.s-header__tabs {
  position: relative;
  display: flex;
  align-items: center;
  gap: var(--s-sp-1);
  flex: 1 1 auto;
  min-width: 0;
  margin-inline: var(--s-sp-3);
}

.s-tab {
  position: relative;
  flex: none;
  height: 32px;
  padding: 0 var(--s-sp-3);
  border: 0;
  border-radius: var(--s-r-sm);
  background: transparent;
  color: var(--s-text-3);
  font-family: var(--s-font);
  /*
    字号/字重 = 真机 Emby 4.9.5 顶栏 tab 实测值（.emby-tab-button：
    font-size 16.1549px / font-weight 600 / line-height 24.2323px），
    与 .skinHeader .emby-button 同值。此前这里是 --s-fs-sm(14px)/--s-fw-medium(500)，
    用户连续两轮反馈"顶栏字体太小"。
    这里是**唯一**按真机实测值写死的字号（tokens.css 无对应令牌，且 tokens 不在本文件写域）；
    五档视口实测不换行/不横向溢出/相邻 tab 不相交，横向挤压时优先缩 padding，不回退字号。
  */
  font-size: 16.1549px;
  font-weight: 600;
  white-space: nowrap;
  cursor: pointer;
  transition:
    color var(--s-dur-fast) var(--s-ease-std),
    background-color var(--s-dur-fast) var(--s-ease-std);
}

.s-tab:hover {
  color: var(--s-text-1);
  background: var(--s-hover);
}

.s-tab.is-active {
  color: var(--s-text-1);
}

/* 激活墨条：整条 tabs 共用一条，切换 tab 时用 transform 滑过去（与侧栏竖条同构 FLIP）。
   只动 transform / opacity，不动 left/width。 */
.s-tabs__ink {
  position: absolute;
  left: 0;
  bottom: 0;
  width: 16px;
  height: 2px;
  border-radius: var(--s-r-pill);
  background: var(--s-accent);
  opacity: 0;
  pointer-events: none;
  transition:
    transform var(--s-dur-base) var(--s-ease-std),
    opacity var(--s-dur-fast) var(--s-ease-std);
}

.s-tabs__ink.is-visible {
  opacity: 1;
}

/* ============================================================
   抽屉
   ============================================================ */
/* 遮罩：只在抽屉真打开时由 <Transition name="s-scrim"> 挂载，
   进出场全部走 motion.css 的 s-scrim 类族（进场 --s-dur-base / 退场 --s-dur-exit） */
.s-backdrop {
  position: fixed;
  inset: 0;
  z-index: var(--s-z-backdrop);
  background: var(--s-scrim-soft);
}

/* docked 语境不需要遮罩（移动端仍需要，所以只在 ≥60em 关掉） */
@media all and (min-width: 60em) {
  .s-backdrop.is-docked {
    display: none;
  }
}

.s-drawer {
  position: fixed;
  /* 覆盖式默认：抽屉层级(--s-z-drawer 60) 低于顶栏(--s-z-header 70)，
     所以从顶栏下沿开始，不做"盖住顶栏"的假象；常驻侧栏在 ≥60em 由 .is-docked 回到 top:0 */
  top: calc(var(--s-header-h) + env(safe-area-inset-top, 0px));
  bottom: 0;
  left: 0;
  /* 壳层阶梯：遮罩 40 < 抽屉 60 < 顶栏 70 < 底栏 80；弹层带(1000)/toast(1200) 在壳层之上。
     抽屉不能落在弹层带，否则用户菜单/toast 会被它盖住。 */
  z-index: var(--s-z-drawer);
  display: flex;
  flex-direction: column;
  width: var(--s-drawer-w);
  box-sizing: border-box;
  background: var(--s-surface);
  border-inline-end: 1px solid var(--s-line);
  /* 关闭态：位移出场 + visibility 兜底（后者把整棵子树移出 tab 序列与无障碍树）。
     退场必须比进场快（铁律 15）：进场 --s-dur-slow(320)，退场 --s-dur-exit(180)。 */
  transform: translateX(-100%);
  visibility: hidden;
  transition:
    transform var(--s-dur-exit) var(--s-ease-exit),
    visibility 0s linear var(--s-dur-exit);
  will-change: transform;
}

.s-drawer.is-open {
  transform: translateX(0);
  visibility: visible;
  transition:
    transform var(--s-dur-slow) var(--s-ease-std),
    visibility 0s;
}

@media all and (min-width: 60em) {
  .s-drawer.is-docked {
    top: 0;
    transform: translateX(0);
    visibility: visible;
    transition: none;
  }
}

@media all and (max-width: 59.9375em) {
  /* <60em 一律覆盖式（即使 JS 判成 docked，实际也是覆盖式），顶栏降为 sm 高度 */
  .s-drawer {
    top: calc(var(--s-header-h-sm) + env(safe-area-inset-top, 0px));
  }
}

.s-drawer__head {
  display: flex;
  align-items: center;
  gap: var(--s-sp-2);
  flex: none;
  /* 覆盖式默认：顶栏就在抽屉上方，头部只留关闭按钮（不重复顶栏的品牌），高度紧凑 */
  height: 44px;
  padding: 0 var(--s-sp-4);
  box-sizing: border-box;
}

/* 覆盖式抽屉不重复顶栏品牌（顶栏在页顶是透明的，重复会叠出两个 Serata） */
.s-drawer .s-drawer__head .s-brand {
  display: none;
}

/* 常驻侧栏：老页面左偏移已让开 248px，头部恢复品牌 + 与顶栏等高 */
@media all and (min-width: 60em) {
  .s-drawer.is-docked .s-drawer__head {
    height: calc(var(--s-header-h) + env(safe-area-inset-top, 0px));
    padding: env(safe-area-inset-top, 0px) var(--s-sp-4) 0;
  }

  .s-drawer.is-docked .s-drawer__head .s-brand {
    display: inline-flex;
  }
}

.s-drawer__close {
  margin-inline-start: auto;
}

/* docked 常驻时不需要关闭按钮（≥60em 才有 docked） */
@media all and (min-width: 60em) {
  .s-drawer.is-docked .s-drawer__close {
    display: none;
  }
}

/* 抽屉搜索输入框（.s-drawer__search / .s-drawer__searchicon / .s-input）已整块删除：
   搜索入口只剩导航（抽屉「搜索」行 + 移动底栏「搜索」tab），打字在搜索页进行。 */

.s-drawer__navwrap {
  position: relative;
  display: flex;
  flex: 1 1 auto;
  min-height: 0;
}

.s-nav {
  flex: 1 1 auto;
  overflow-y: auto;
  overscroll-behavior: contain;
  padding: var(--s-sp-1) var(--s-sp-3) var(--s-sp-6);
}

.s-nav__content {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

/* 长列表上下渐隐遮罩（只盖视觉，不挡点击；只在还能继续滚的一侧出现） */
.s-drawer__fade {
  position: absolute;
  left: 0;
  right: 0;
  height: var(--s-sp-5);
  pointer-events: none;
  opacity: 0;
  transition: opacity var(--s-dur-fast) var(--s-ease-std);
}

.s-drawer__fade.is-visible {
  opacity: 1;
}

.s-drawer__fade--top {
  top: 0;
  background: linear-gradient(to bottom, var(--s-surface), transparent);
}

.s-drawer__fade--bottom {
  bottom: 0;
  background: linear-gradient(to top, var(--s-surface), transparent);
}

/* ---------- 行条 ---------- */
.s-nav__row {
  position: relative;
  display: flex;
  align-items: center;
  gap: var(--s-sp-2);
  width: 100%;
  height: 40px;
  padding: 0 var(--s-sp-3);
  border: 0;
  border-radius: var(--s-r-sm);
  background: transparent;
  color: var(--s-text-2);
  font-family: var(--s-font);
  font-size: var(--s-fs-body);
  font-weight: var(--s-fw-medium);
  text-align: start;
  cursor: pointer;
  transition:
    background-color var(--s-dur-fast) var(--s-ease-std),
    color var(--s-dur-fast) var(--s-ease-std);
}

.s-nav__row:hover {
  background: var(--s-hover);
  color: var(--s-text-1);
}

.s-nav__row.is-active {
  color: var(--s-text-1);
}

.s-nav__label {
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

/* 「展开 / 收起」这条不对应任何路由，弱化一档 */
.s-nav__row--ghost {
  height: 32px;
  color: var(--s-text-3);
  font-size: var(--s-fs-xs);
}

.s-nav__row--ghost .s-nav__chev {
  margin-inline-start: auto;
}

/* ---------- 选中竖条：只动 transform ---------- */
.s-nav__indicator {
  position: absolute;
  top: 0;
  left: 0;
  width: 3px;
  border-radius: var(--s-r-pill);
  background: var(--s-accent);
  opacity: 0;
  transform: translateY(0);
  pointer-events: none;
  transition:
    transform var(--s-dur-base) var(--s-ease-std),
    opacity var(--s-dur-fast) var(--s-ease-std);
  will-change: transform;
}

.s-nav__indicator.is-visible {
  opacity: 1;
}

/* ---------- 分组 ---------- */
.s-nav__group {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.s-nav__grouphead {
  display: flex;
  align-items: center;
  gap: var(--s-sp-2);
  width: 100%;
  height: 36px;
  margin-top: var(--s-sp-3);
  padding: 0 var(--s-sp-3);
  border: 0;
  background: transparent;
  color: var(--s-text-3);
  font-family: var(--s-font);
  font-size: var(--s-fs-xs);
  font-weight: var(--s-fw-semibold);
  letter-spacing: 0.04em;
  text-align: start;
  cursor: pointer;
  transition: color var(--s-dur-fast) var(--s-ease-std);
}

.s-nav__grouphead:hover {
  color: var(--s-text-1);
}

.s-nav__grouphead-label {
  flex: 1 1 auto;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.s-nav__chev {
  width: 16px;
  height: 16px;
  flex: none;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.8;
  stroke-linecap: round;
  stroke-linejoin: round;
  transform: rotate(-90deg);
  transition: transform var(--s-dur-base) var(--s-ease-std);
}

.s-nav__chev.is-open {
  transform: rotate(0deg);
}

/* ---------- 折叠：grid-template-rows 0fr → 1fr（不动画 height） ---------- */
.s-collapse {
  display: grid;
  grid-template-rows: 0fr;
  transition: grid-template-rows var(--s-dur-base) var(--s-ease-std);
}

.s-collapse.is-open {
  grid-template-rows: 1fr;
}

.s-collapse__inner {
  overflow: hidden;
  min-height: 0;
}

.s-collapse__content {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

/* ============================================================
   移动端底部导航
   ============================================================ */
.s-bottom-nav {
  display: none;
}

@media (max-width: 768px) {
  .s-bottom-nav {
    display: flex;
    position: fixed;
    left: 0;
    right: 0;
    bottom: 0;
    z-index: var(--s-z-nav);
    height: var(--s-bottom-nav-h);
    padding-bottom: env(safe-area-inset-bottom, 0px);
    box-sizing: content-box;
    background: var(--s-surface);
    border-top: 1px solid var(--s-line);
  }

  .s-bottom-nav__item {
    display: flex;
    flex: 1 1 0;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 3px;
    min-width: 0;
    padding: 0;
    border: 0;
    background: transparent;
    color: var(--s-text-3);
    font-family: var(--s-font);
    font-size: var(--s-fs-xs);
    cursor: pointer;
    transition: color var(--s-dur-fast) var(--s-ease-std);
  }

  .s-bottom-nav__item.is-active {
    color: var(--s-text-1);
  }

  .s-bottom-nav__item.is-active .s-ico {
    color: var(--s-accent);
  }

  .s-bottom-nav__label {
    line-height: 1;
  }

  /* 移动端顶栏降为 sm 高度 */
  .s-header {
    height: calc(var(--s-header-h-sm) + env(safe-area-inset-top, 0px));
  }

  .s-header__tabs {
    display: none;
  }

  /* 移动端顶栏不放搜索：底栏已有「搜索」tab，两个入口正是用户反复投诉的点。
     顶栏放大镜只在桌面出现；搜索页自身的那个由模板里的 v-if="!isSearch" 关掉。 */
  .s-iconbtn--search {
    display: none;
  }

  /* 移动端抽屉：顶边对齐 sm 顶栏（头部 44px 只留关闭按钮，品牌由顶栏负责），
     底栏(--s-z-nav 80) 在层级上高于抽屉(--s-z-drawer 60)，底部让出底栏高度 */
  .s-drawer {
    padding-bottom: calc(var(--s-bottom-nav-h) + env(safe-area-inset-bottom, 0px));
  }

  /* 覆盖式抽屉打开时（= withDialogOpen，见 watchEffect 里的结构类），底栏必须让位。
     为什么：底栏 --s-z-nav(80) > 抽屉 --s-z-drawer(60)，实测抽屉展开时底栏仍浮在模态之上，
     elementFromPoint 命中它的 svg（真的可点）。于是抽屉里的「搜索」行与底栏的「搜索」tab
     同时可点 —— 又变成两个搜索入口（用户口径 2026-10-20：搜索入口只许有一个）。
     而且抽屉此时是 aria-modal + 焦点陷阱 + 滚动锁的模态，被它遮住的内容本就不该还能交互。
     底栏让出的 --s-bottom-nav-h 由抽屉的 padding-bottom 同步收回，抽屉内容补满到屏幕底。 */
  html.withDialogOpen .s-bottom-nav {
    display: none;
  }
  html.withDialogOpen .s-drawer {
    padding-bottom: 0;
  }
}
</style>

<style>
/* ============================================================
   全局对齐（非 scoped，刻意为之）：
   emby495 老页面把「顶栏高度」和「docked 侧栏宽度」写成两个常量：
     · --header-height 基础值 ≈ 79.7px，而 .withHeaderTabs[.withFullDrawer]:root
       在 <2016px 时把它抬到 9.104em / calc(7.904em + max(1.2em, safe-area)) ≈ 131px
       （老外壳是「标题行 + 独立 tabs 行」两行头，新外壳是 64px 单行头 + 行内 tabs）；
     · .page-withFullDrawer left 16.5em ≈ 238px（新侧栏是 248px）。
   这里用**更高特异性**把这两个偏移量校正到设计层令牌（所以选择器要显式压过
   .withHeaderTabs.withFullDrawer:root 那一档），只改位置，不碰字号、配色、结构。
   ============================================================ */
html:root,
html:root.withFullDrawer,
html:root.withHeaderTabs,
html:root.withHeaderTabs.withFullDrawer,
html:root.withheadernowplaying.withHeaderTabs.withFullDrawer {
  --header-height: calc(var(--s-header-h) + env(safe-area-inset-top, 0px));
}

@media all and (min-width: 60em) {
  html .page-withFullDrawer,
  html .page-withFullDrawer .viewContent-fixed {
    left: var(--s-drawer-w);
    inset-inline-start: var(--s-drawer-w);
  }
}

/* 移动端：内容给底部导航让位（#app 主区域 + 直接子级的 page 都抬起来） */
@media (max-width: 768px) {
  html:root,
  html:root.withFullDrawer,
  html:root.withHeaderTabs,
  html:root.withHeaderTabs.withFullDrawer,
  html:root.withheadernowplaying.withHeaderTabs.withFullDrawer {
    --header-height: calc(var(--s-header-h-sm) + env(safe-area-inset-top, 0px));
  }

  #app {
    padding-bottom: calc(var(--s-bottom-nav-h) + env(safe-area-inset-bottom, 0px));
  }

  /* 老页面（position: absolute 撑满视口）直接抬底边 */
  #app > .page {
    bottom: calc(var(--s-bottom-nav-h) + env(safe-area-inset-bottom, 0px));
  }

  /* 新式页面（HomeView 的 .s-home 这类走文档流的全出血页）用 padding 让位；
     这是外壳与页面之间的约定：底部导航高度由外壳预留，页面不用自己再算。 */
  #app > .s-home {
    padding-bottom: calc(var(--s-bottom-nav-h) + env(safe-area-inset-bottom, 0px) + var(--s-sp-6));
  }
}
</style>
