/**
 * Serata 动效运行时 —— 与 styles/serata/tokens.css、motion.css **同源**。
 *
 * 为什么需要这个文件：
 *  CSS 变量（--s-dur-* / --s-ease-*）JS 动画库读不到，只能镜像一份；
 *  一旦两边漂移，CSS 转场和 JS 弹簧就会各说各话。所以这里：
 *   ① 把令牌值镜像成常量（改 tokens.css 必须同步改这里，probe 会兜底）；
 *   ② 提供 reduced-motion 判定（所有 JS 动画必须先问它）；
 *   ③ 提供各动效库的统一预设（motion-v 弹簧 / auto-animate FLIP / 交错进场）。
 *
 * 铁律：任何 JS 动画（motion-v / auto-animate / 手写 rAF）在动手前
 * 都必须拿到 `useReducedMotionSafe()` 的结果，并在 reduce 时**完全不位移**。
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useElementVisibility, usePreferredReducedMotion } from '@vueuse/core'

/* ============================================================
   1. 令牌镜像（与 tokens.css 一一对应）
   ============================================================ */

/** 时长（ms）—— 对应 tokens.css 的 --s-dur-*。 */
export const S_DUR = {
  instant: 80,
  fast: 140,
  base: 220,
  slow: 320,
  xslow: 480,
  sheet: 280,
  exit: 180,
} as const

/** 缓动控制点 —— 对应 tokens.css 的 --s-ease-*。 */
export const S_EASE = {
  std: [0.2, 0, 0, 1],
  exit: [0.4, 0, 1, 1],
  spring: [0.34, 1.4, 0.64, 1],
} as const

/** 同上，字符串形式（给只吃 CSS 字符串的库用，如 auto-animate）。 */
export const S_EASE_CSS = {
  std: `cubic-bezier(${S_EASE.std.join(', ')})`,
  exit: `cubic-bezier(${S_EASE.exit.join(', ')})`,
  spring: `cubic-bezier(${S_EASE.spring.join(', ')})`,
} as const

/** 交错步长（ms）与序号封顶 —— 对应 --s-stagger-step / --s-stagger-max。 */
export const S_STAGGER_STEP = 45
export const S_STAGGER_MAX = 8

/* ============================================================
   2. 减少动效判定
   ============================================================ */

/**
 * 系统是否要求减少动效。
 * 用 `as string` 比较是为了对 VueUse 的返回联合类型免疫（不同版本字面量不同）。
 */
export function useReducedMotionSafe(): ComputedRef<boolean> {
  const pref = usePreferredReducedMotion()
  return computed(() => (pref.value as string) === 'reduce')
}

/* ============================================================
   3. motion-v 预设
   ============================================================ */

export interface SpringPreset {
  type: 'spring'
  stiffness: number
  damping: number
  mass: number
}

/** 弹簧预设：数字直接对应"手感"，改之前先想清楚为什么要改。 */
export const SPRING: Record<'snappy' | 'soft' | 'bouncy', SpringPreset> = {
  /** 弹层、卡片按下回弹：偏硬、收得快 */
  snappy: { type: 'spring', stiffness: 460, damping: 36, mass: 0.85 },
  /** 内容进场：柔和，不抢戏 */
  soft: { type: 'spring', stiffness: 260, damping: 30, mass: 1 },
  /** 拖拽/手势回弹：明显回弹，但不到"蹦迪" */
  bouncy: { type: 'spring', stiffness: 380, damping: 22, mass: 1 },
}

/**
 * 上浮进场变体（配 `<motion.div :variants>` 用）。
 * reduce 时退化成"直接可见"，绝不位移。
 */
export function riseVariants(reduced: boolean) {
  if (reduced) return { hidden: { opacity: 1 }, visible: { opacity: 1 } }
  return {
    hidden: { opacity: 0, y: 14 },
    visible: { opacity: 1, y: 0 },
  }
}

/** 交错容器变体（与 riseVariants 配对；子元素用同一个 variants 即可自动错开）。 */
export function staggerContainer(reduced: boolean, step = S_STAGGER_STEP) {
  return {
    hidden: {},
    visible: {
      transition: {
        staggerChildren: reduced ? 0 : step / 1000,
        delayChildren: 0,
      },
    },
  }
}

/* ============================================================
   4. auto-animate（列表 FLIP）配置
   ============================================================ */

export interface FlipConfig {
  duration: number
  easing: string
}

/**
 * 列表增删/重排的 FLIP 配置。
 * 注意：auto-animate 默认就会在 `prefers-reduced-motion: reduce` 下自我禁用，
 * 这里再把 duration 压到 0 是**双保险**（万一某处写了 disrespectUserMode）。
 */
export function flipConfig(reduced: boolean): FlipConfig {
  return { duration: reduced ? 0 : S_DUR.base, easing: S_EASE_CSS.std }
}

/* ============================================================
   5. 滚动进场
   ============================================================ */

export interface Reveal {
  /** 绑到要观察的元素上：`<div :ref="el" />` */
  el: Ref<HTMLElement | null>
  /** 是否已经/正在可见 */
  shown: ComputedRef<boolean>
}

/**
 * 滚动进场：元素进入视口后 `shown` 变 true。
 * 默认 `once: true` —— 进场动画只放一次，来回滚动反复播是廉价感的来源。
 */
export function useReveal(options: { once?: boolean; threshold?: number } = {}): Reveal {
  const { once = true, threshold = 0.15 } = options
  const el = ref<HTMLElement | null>(null)
  const inView = useElementVisibility(el, { threshold })
  const everSeen = ref(false)

  watch(
    inView,
    (v) => {
      if (v && !everSeen.value) everSeen.value = true
    },
    { immediate: true },
  )

  return { el, shown: computed(() => (once ? everSeen.value : inView.value)) }
}
