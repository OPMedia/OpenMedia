/**
 * 主题（深浅双模）—— 单一事实来源。
 *
 * 为什么不用 pinia：主题必须在首屏绘制前就落到 <html> 上，
 * 而 store 要等 app.use(pinia) 之后才可用；这里用模块级单例 + initTheme()，
 * 由 main.ts 在挂载前调用（index.html 里还有一段内联脚本兜住首次绘制前的闪白）。
 *
 * 三档：dark（默认，品牌是"夜晚"）/ light / system。
 * 切换时做两件事：
 *   1. 给 <html> 挂 theme-dark / theme-light（新设计层 tokens 靠它取值）；
 *   2. 按需注入 emby495 的浅色主题 CSS（老页面靠它换肤，深色主题是静态加载的）。
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

  // 移动端浏览器地址栏跟随主题
  ensureMetaThemeColor().content = theme === 'dark' ? '#0A0C18' : '#F7F5F2'
  root.dataset.serataTheme = theme
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

  watch(resolved, apply, { immediate: false })
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
