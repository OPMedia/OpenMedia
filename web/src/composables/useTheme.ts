/**
 * 主题（深浅双模）—— 单一事实来源。
 *
 * 为什么不用 pinia：主题必须在首屏绘制前就落到 <html> 上，
 * 而 store 要等 app.use(pinia) 之后才可用；这里用模块级单例 + initTheme()，
 * 由 main.ts 在挂载前调用（index.html 里还有一段内联脚本兜住首次绘制前的闪白）。
 *
 * 三档：dark（默认，品牌是"夜晚"）/ light / system。
 * 切换时做三件事：
 *   1. 给 <html> 挂 theme-dark / theme-light（新设计层 tokens 靠它取值）；
 *   2. 按需注入 emby495 的浅色主题 CSS（老页面靠它换肤，深色主题是静态加载的）；
 *   3. 用 View Transition 包住这次 DOM 变更，整页几十个 HSL 变量不会硬闪（见 withViewTransition）。
 */
import { computed, ref, watch } from 'vue'

export type ThemeMode = 'dark' | 'light' | 'system'
export type ResolvedTheme = 'dark' | 'light'

const LS_KEY = 'serata.themeMode'
const LEGACY_LIGHT_STYLE_ID = 'serata-legacy-light'

// 浅色主题 CSS 以文本形式内联注入：避免 <link> 在 dev/prod 下路径与 MIME 的坑
import lightThemeCss from '@/assets/emby495/modules__themes__light__theme.css?inline'
import lightThemeNontvCss from '@/assets/emby495/modules__themes__light__theme_nontv.css?inline'

const VALID: ThemeMode[] = ['dark', 'light', 'system']

function readStored(): ThemeMode {
  try {
    const v = localStorage.getItem(LS_KEY) as ThemeMode | null
    return v && VALID.includes(v) ? v : 'dark'
  } catch {
    return 'dark'
  }
}

const mode = ref<ThemeMode>(readStored())
const systemPrefersDark = ref(
  typeof window !== 'undefined' && window.matchMedia
    ? window.matchMedia('(prefers-color-scheme: dark)').matches
    : true,
)

const resolved = computed<ResolvedTheme>(() =>
  mode.value === 'system' ? (systemPrefersDark.value ? 'dark' : 'light') : mode.value,
)
const isDark = computed(() => resolved.value === 'dark')

function ensureMetaThemeColor(): HTMLMetaElement {
  let el = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')
  if (!el) {
    el = document.createElement('meta')
    el.name = 'theme-color'
    document.head.appendChild(el)
  }
  return el
}

function ensureLegacyLightStyle(): void {
  if (document.getElementById(LEGACY_LIGHT_STYLE_ID)) return
  const style = document.createElement('style')
  style.id = LEGACY_LIGHT_STYLE_ID
  style.textContent = `${lightThemeCss}\n${lightThemeNontvCss}`
  document.head.appendChild(style)
}

function removeLegacyLightStyle(): void {
  document.getElementById(LEGACY_LIGHT_STYLE_ID)?.remove()
}

function apply(): void {
  if (typeof document === 'undefined') return
  const theme = resolved.value
  const root = document.documentElement
  root.classList.toggle('theme-dark', theme === 'dark')
  root.classList.toggle('theme-light', theme === 'light')

  if (theme === 'light') ensureLegacyLightStyle()
  else removeLegacyLightStyle()

  // 移动端浏览器地址栏跟随主题。
  // ⚠️ 这里**必须是字面量**：<meta name="theme-color"> 只接受具体颜色，不解析 var()。
  //    两个值分别等于深色 --s-bg (#0A0C18) 与浅色画布 (#F7F5F2)。
  //    这是全项目唯一被批准的裸色值（另一个是 index.html 里的首帧防闪内联脚本），
  //    改 tokens 里的 --s-bg / 浅色画布时必须同步改这里 + index.html。
  ensureMetaThemeColor().content = theme === 'dark' ? '#0A0C18' : '#F7F5F2'
  root.dataset.serataTheme = theme
}

/** View Transitions 的最小类型（lib.dom 尚未覆盖到所有 TS 版本） */
type ViewTransitionCapableDocument = Document & {
  startViewTransition?: (callback: () => void) => { finished: Promise<void> }
}

/**
 * 用 View Transition 包住一次 DOM 变更 —— 目前只给主题切换用。
 *
 * 为什么主题切换值得一次页面快照：整页几十个 HSL 变量同时翻转，硬切会"闪"；
 * 而两套主题直接对叠又会糊成中间色，所以 motion.css 里让老快照保持不透明、新快照淡入。
 *
 * 三个前置条件不满足就**直接切**（不做快照，不做动画）：
 *   ① 浏览器不支持 startViewTransition；② 用户要求减少动效；③ 已经有一个转场在跑（会抛异常）。
 */
function withViewTransition(mutate: () => void): void {
  if (typeof document === 'undefined') return
  const doc = document as ViewTransitionCapableDocument
  const reduced =
    typeof window !== 'undefined' && window.matchMedia
      ? window.matchMedia('(prefers-reduced-motion: reduce)').matches
      : false

  if (reduced || typeof doc.startViewTransition !== 'function') {
    mutate()
    return
  }

  try {
    doc.startViewTransition(mutate)
  } catch {
    // 并发转场/快照失败：退化成硬切，绝不能因为动效把主题切换本身搞坏
    mutate()
  }
}

let mediaQuery: MediaQueryList | null = null
let mediaHandler: ((e: MediaQueryListEvent) => void) | null = null

/** 在 main.ts 里、mount 之前调用一次 */
export function initTheme(): void {
  apply()

  if (typeof window !== 'undefined' && window.matchMedia) {
    mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
    mediaHandler = (e) => {
      systemPrefersDark.value = e.matches
    }
    mediaQuery.addEventListener('change', mediaHandler)
  }

  // 首帧那次 apply() 已在上面同步跑完（首屏绝不允许有转场）；
  // 之后的切换（用户点主题按钮 / 系统主题变化）才用 View Transition 包一层。
  watch(resolved, () => withViewTransition(apply), { immediate: false })
}

function setMode(next: ThemeMode): void {
  mode.value = next
  try {
    localStorage.setItem(LS_KEY, next)
  } catch {
    /* 隐私模式忽略 */
  }
}

/** 深/浅两档之间直接切换（system 时切到当前的反面） */
function toggleTheme(): void {
  setMode(resolved.value === 'dark' ? 'light' : 'dark')
}

export function useTheme() {
  return { mode, resolved, isDark, setMode, toggleTheme }
}
