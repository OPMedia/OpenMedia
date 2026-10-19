<script setup lang="ts">
/**
 * HomeHero —— Serata 首页首屏轮转（约 3/4 视口）。
 *
 * 左栏：眉标 / Logo（回退大标题）/ 两行简介 / 主次 CTA / 进度条。
 * 右栏：16:9 轮转画框（无 Backdrop 时降级 2:3 竖版，绝不拉伸），右侧探出下一张，
 *       双槽 <img> 叠放做 480ms 交叉淡入 + 1.02 → 1 缩放缓动。
 *
 * 轮转：8000ms 自动；hover / 画框内 focus / 标签页隐藏 / prefers-reduced-motion
 *       四种情况都停；右上角显式暂停按钮（WCAG 2.2.2）；← → 切换；指针滑动 >40px 切换。
 *
 * 只消费 styles/serata 的 --s-* 令牌，无裸色值、无 emby-* 类名。
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch, type Ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { BaseItemDto } from '@/api/types'
import { imageUrl } from '@/api/endpoints'
import { useTheme } from '@/composables/useTheme'
import markWhite from '@/assets/brand/mark-white.svg'

const ROTATE_MS = 8000
const SWIPE_MIN_PX = 40
/** 入画兜底：图片既不 load 也不 error 时也要换（避免卡在旧帧） */
const REVEAL_FALLBACK_MS = 1500
const TICKS_PER_MINUTE = 600_000_000

const props = withDefaults(defineProps<{ items: BaseItemDto[]; loading?: boolean }>(), { loading: false })

const router = useRouter()
const { t } = useI18n()
// 浅色底上放「糊过的深色剧照」会变成一团脏灰，故贴片光晕只在深色模式铺
const { isDark } = useTheme()

// ---------------------------------------------------------------- 图片降级链

type ShotKind = 'backdrop' | 'primary' | 'thumb' | 'brand'

interface Shot {
  itemId: string
  kind: ShotKind
  src: string
}

const IMAGE_TYPE: Record<Exclude<ShotKind, 'brand'>, string> = {
  backdrop: 'Backdrop',
  primary: 'Primary',
  thumb: 'Thumb',
}

/** 该条目是否真的有这类图（tags 为准；tags 撒谎时还有 @error 兜底） */
function hasImage(item: BaseItemDto, type: 'Primary' | 'Logo' | 'Thumb' | 'Backdrop'): boolean {
  if (type === 'Backdrop') {
    return (item.BackdropImageTags?.length ?? 0) > 0 || !!item.ImageTags?.Backdrop
  }
  return !!item.ImageTags?.[type]
}

/** 回退链：Backdrop → Primary → Thumb → 品牌渐变 */
function imageKindChain(item: BaseItemDto): ShotKind[] {
  const chain: ShotKind[] = []
  if (hasImage(item, 'Backdrop')) chain.push('backdrop')
  if (hasImage(item, 'Primary')) chain.push('primary')
  if (hasImage(item, 'Thumb')) chain.push('thumb')
  chain.push('brand')
  return chain
}

function shotOf(item: BaseItemDto, kind: ShotKind): Shot {
  if (kind === 'brand') return { itemId: item.Id, kind, src: '' }
  const type = IMAGE_TYPE[kind]
  const tag = kind === 'backdrop'
    ? item.BackdropImageTags?.[0] ?? item.ImageTags?.Backdrop
    : item.ImageTags?.[type]
  // 背景图走 1280 宽，竖版海报走 720，避免首屏拖大图
  const src = imageUrl(item.Id, type, tag, { maxWidth: kind === 'backdrop' ? 1280 : 720, quality: 90 })
  return { itemId: item.Id, kind, src }
}

// ---------------------------------------------------------------- 当前条目

const active = ref(0)
const current = computed<BaseItemDto | undefined>(() => props.items[active.value])

watch(
  () => props.items.length,
  (total) => {
    if (active.value > total - 1) active.value = 0
  },
)

/** 眉标类型走 i18n（search.type* 已备好，不改 locale）；未知类型回退 Emby 原值 */
const TYPE_LABEL_KEY: Record<string, string> = {
  Movie: 'search.typeMovie',
  Series: 'search.typeSeries',
  Episode: 'search.typeEpisode',
}

const metaLine = computed(() => {
  const item = current.value
  if (!item) return ''
  const labelKey = TYPE_LABEL_KEY[item.Type]
  const parts: string[] = [labelKey ? t(labelKey) : item.Type]
  if (item.ProductionYear) parts.push(String(item.ProductionYear))
  if (item.RunTimeTicks) parts.push(t('item.runtime', { minutes: Math.round(item.RunTimeTicks / TICKS_PER_MINUTE) }))
  if (item.OfficialRating) parts.push(item.OfficialRating)
  return parts.join(' · ')
})

const logoUrl = computed(() => {
  const item = current.value
  if (!item || !hasImage(item, 'Logo')) return ''
  return imageUrl(item.Id, 'Logo', item.ImageTags?.Logo, { maxWidth: 480, quality: 90 })
})

const blurb = computed(() => {
  const item = current.value
  if (!item) return ''
  return item.Overview || item.Taglines?.[0] || ''
})

const progressPct = computed(() => {
  const item = current.value
  const pos = item?.UserData?.PlaybackPositionTicks ?? 0
  if (!item?.RunTimeTicks || pos <= 0) return 0
  return Math.min(100, (pos / item.RunTimeTicks) * 100)
})

const hasProgress = computed(() => progressPct.value > 0)

const remainingMinutes = computed(() => {
  const item = current.value
  if (!item?.RunTimeTicks) return 0
  const pos = item.UserData?.PlaybackPositionTicks ?? 0
  return Math.max(0, Math.ceil((item.RunTimeTicks - pos) / TICKS_PER_MINUTE))
})

// ---------------------------------------------------------------- 双槽交叉淡入

const slotA = ref<Shot | null>(null)
const slotB = ref<Shot | null>(null)
const loadedA = ref(false)
const loadedB = ref(false)
const front = ref<0 | 1>(0)
const pending = ref<0 | 1 | null>(null)

/** 每个槽自己的降级进度（不放进响应式：只有渲染结果需要响应） */
const slotState: Record<0 | 1, { chain: ShotKind[]; step: number; item: BaseItemDto | null }> = {
  0: { chain: [], step: 0, item: null },
  1: { chain: [], step: 0, item: null },
}

let pendingTimer: ReturnType<typeof setTimeout> | undefined

function slotRef(slot: 0 | 1): Ref<Shot | null> {
  return slot === 0 ? slotA : slotB
}
function loadedRef(slot: 0 | 1): Ref<boolean> {
  return slot === 0 ? loadedA : loadedB
}
function clearPendingTimer(): void {
  if (pendingTimer) {
    clearTimeout(pendingTimer)
    pendingTimer = undefined
  }
}
/**
 * 写背面槽。返回 true =「这张图本来就画在这个槽里」：
 * src 没变浏览器不会再发 load，干等 load 只能等到 1.5s 兜底才翻面
 * （A→B→A 这种来回切是最常见操作，必须立刻翻）。
 */
function prepare(slot: 0 | 1, item: BaseItemDto): boolean {
  const chain = imageKindChain(item)
  const next = shotOf(item, chain[0] ?? 'brand')
  const prev = slotRef(slot).value
  const painted = prev !== null && prev.src === next.src && loadedRef(slot).value
  slotState[slot] = { chain, step: 0, item }
  slotRef(slot).value = next
  if (!painted) loadedRef(slot).value = false
  return painted
}
function settle(slot: 0 | 1): void {
  if (pending.value !== slot) return
  pending.value = null
  clearPendingTimer()
  front.value = slot
}
/** 换到新条目：写背面槽 → 等它 load（或降到品牌渐变）→ 翻面，旧帧留在底下淡出 */
function reveal(item: BaseItemDto): void {
  const target: 0 | 1 = front.value === 0 ? 1 : 0
  pending.value = target
  const painted = prepare(target, item)
  if (painted || slotRef(target).value?.kind === 'brand') {
    settle(target)
    return
  }
  clearPendingTimer()
  pendingTimer = setTimeout(() => settle(target), REVEAL_FALLBACK_MS)
}

const slots = computed(() => [
  { slot: 0 as const, shot: slotA.value, loaded: loadedA.value },
  { slot: 1 as const, shot: slotB.value, loaded: loadedB.value },
])

const frontShot = computed<Shot | null>(() => (front.value === 0 ? slotA.value : slotB.value))

/** 16:9 只在真有 Backdrop 时用；退到竖版 Primary 就降级 2:3，绝不拉伸 */
const isPortrait = computed(() => frontShot.value !== null && frontShot.value.kind !== 'backdrop')

function onShotLoad(slot: 0 | 1): void {
  loadedRef(slot).value = true
  settle(slot)
}
function onShotError(slot: 0 | 1): void {
  const state = slotState[slot]
  if (!state.item) return
  state.step = Math.min(state.step + 1, state.chain.length - 1)
  const kind = state.chain[state.step] ?? 'brand'
  slotRef(slot).value = shotOf(state.item, kind)
  loadedRef(slot).value = kind === 'brand'
  if (kind === 'brand') settle(slot)
}

watch(
  current,
  (next) => {
    if (!next) {
      slotA.value = null
      slotB.value = null
      pending.value = null
      clearPendingTimer()
      return
    }
    if (!slotA.value && !slotB.value) {
      prepare(0, next)
      front.value = 0
      return
    }
    if (frontShot.value?.itemId === next.Id) return
    reveal(next)
  },
  { immediate: true },
)

// 右侧「下一张探出」：只在下一张也有 Backdrop 时才露
const peekSrc = computed(() => {
  const total = props.items.length
  if (total < 2 || isPortrait.value) return ''
  const next = props.items[(active.value + 1) % total]
  if (!next || !hasImage(next, 'Backdrop')) return ''
  return imageUrl(next.Id, 'Backdrop', next.BackdropImageTags?.[0], { maxWidth: 640, quality: 80 })
})

// 环境光晕：当前条目的极小图（32px，几百字节）+ 暮紫铺底
const glowUrl = computed(() => {
  const item = current.value
  if (!item || !hasImage(item, 'Backdrop')) return ''
  return imageUrl(item.Id, 'Backdrop', item.BackdropImageTags?.[0], { maxWidth: 32 })
})

// ---------------------------------------------------------------- 轮转 / 暂停

const hovering = ref(false)
const focusInside = ref(false)
const tabHidden = ref(false)
const userPaused = ref(false)
const rootEl = ref<HTMLElement | null>(null)
const toggleEl = ref<HTMLButtonElement | null>(null)

const canRotate = computed(() => !props.loading && props.items.length > 1)
/**
 * 暂停条件：hover / 画框内 focus / 标签页隐藏 / 显式暂停。
 * `prefers-reduced-motion` 不是闸门而是**初始暂停位**（见 onMounted）：系统开启减少动效时
 * 不自动轮转，但用户显式点「继续轮播」即可开启——WCAG 2.2.2 管的是"自动"播放，
 * 用户主动触发的不算自动。
 */
const running = computed(
  () =>
    canRotate.value &&
    !userPaused.value &&
    !hovering.value &&
    !focusInside.value &&
    !tabHidden.value,
)

let timer: ReturnType<typeof setTimeout> | undefined
let elapsed = 0
let startedAt = 0

function stopTimer(): void {
  if (timer) {
    clearTimeout(timer)
    timer = undefined
  }
}
function startTimer(): void {
  stopTimer()
  startedAt = performance.now()
  timer = setTimeout(() => {
    timer = undefined
    elapsed = 0
    step(1)
  }, Math.max(160, ROTATE_MS - elapsed))
}
function pauseTimer(): void {
  if (!timer) return
  stopTimer()
  elapsed = Math.min(ROTATE_MS, elapsed + (performance.now() - startedAt))
}

watch(
  running,
  (on) => {
    if (on) startTimer()
    else pauseTimer()
  },
  { immediate: true },
)

function step(delta: number): void {
  const total = props.items.length
  if (total < 2) return
  active.value = (active.value + delta + total) % total
  elapsed = 0
  if (running.value) startTimer()
}
function goTo(index: number): void {
  if (index === active.value || index < 0 || index >= props.items.length) return
  active.value = index
  elapsed = 0
  if (running.value) startTimer()
}
function togglePause(): void {
  // 只翻显式暂停位；hover/focus/隐藏 是瞬时态，不写进按钮语义
  userPaused.value = !userPaused.value
}

// 文字错峰入场：先摘掉 is-entering 让四行归零，下一帧再加回 → 动画重播（不重建节点，焦点不丢）
const entering = ref(true)
watch(active, async () => {
  entering.value = false
  await nextTick()
  entering.value = true
})

// ---------------------------------------------------------------- 交互

function openItem(): void {
  if (performance.now() < suppressClickUntil) return
  const item = current.value
  if (item) router.push({ name: 'item', params: { id: item.Id } })
}
function playItem(): void {
  const item = current.value
  if (!item) return
  // 与 EmbyCard / ItemDetailView 一致：只带 id，播放页自己读 PlaybackPositionTicks 续播
  router.push({ name: 'play', params: { id: item.Id } })
}

function onPointerEnter(event: PointerEvent): void {
  if (event.pointerType === 'touch') return
  hovering.value = true
}
function onPointerLeave(event: PointerEvent): void {
  if (event.pointerType === 'touch') return
  hovering.value = false
}
function onRootFocusIn(event: FocusEvent): void {
  // 暂停按钮自己的 focus 不算「画框内 focus」，否则点了继续播放永远起不来
  focusInside.value = event.target !== toggleEl.value
}
function onRootFocusOut(event: FocusEvent): void {
  const next = event.relatedTarget instanceof Node ? event.relatedTarget : null
  if (next && rootEl.value?.contains(next)) focusInside.value = next !== toggleEl.value
  else focusInside.value = false
}

let swipeStartX = 0
let swipeStartY = 0
let swipeTracking = false
let suppressClickUntil = 0

function onPointerDown(event: PointerEvent): void {
  if (event.pointerType === 'mouse' && event.button !== 0) return
  swipeTracking = true
  swipeStartX = event.clientX
  swipeStartY = event.clientY
}
function onPointerUp(event: PointerEvent): void {
  if (!swipeTracking) return
  swipeTracking = false
  const dx = event.clientX - swipeStartX
  const dy = event.clientY - swipeStartY
  if (Math.abs(dx) < SWIPE_MIN_PX || Math.abs(dx) <= Math.abs(dy)) return
  // 滑动手势不该顺带触发画框点击跳详情
  suppressClickUntil = performance.now() + 400
  step(dx < 0 ? 1 : -1)
}
function onPointerCancel(): void {
  swipeTracking = false
}

function onVisibilityChange(): void {
  tabHidden.value = document.hidden
}

const announcement = computed(() =>
  current.value ? t('hero.slide', { n: active.value + 1, total: props.items.length }) : '',
)

let reduceQuery: MediaQueryList | undefined
function onReduceChange(event: MediaQueryListEvent): void {
  // 系统偏好变化时落到暂停位：开启减少动效→停下；关闭→不强行恢复（由用户自己点）
  if (event.matches) userPaused.value = true
}

onMounted(() => {
  reduceQuery = window.matchMedia('(prefers-reduced-motion: reduce)')
  // reduced-motion 下不自动播放：初始即暂停，但保留用户显式开启的能力
  userPaused.value = reduceQuery.matches
  reduceQuery.addEventListener('change', onReduceChange)
  tabHidden.value = document.hidden
  document.addEventListener('visibilitychange', onVisibilityChange)
})

onBeforeUnmount(() => {
  stopTimer()
  clearPendingTimer()
  reduceQuery?.removeEventListener('change', onReduceChange)
  document.removeEventListener('visibilitychange', onVisibilityChange)
})
</script>

<template>
  <section
    ref="rootEl"
    class="hero"
    role="region"
    tabindex="0"
    :aria-label="t('hero.label')"
    @keydown.left.prevent="step(-1)"
    @keydown.right.prevent="step(1)"
    @focusin="onRootFocusIn"
    @focusout="onRootFocusOut"
    @pointerenter="onPointerEnter"
    @pointerleave="onPointerLeave"
  >
    <div class="hero-glow" aria-hidden="true">
      <div v-if="isDark && glowUrl" class="hero-glow-img" :style="{ backgroundImage: `url(${glowUrl})` }"></div>
    </div>

    <!-- 骨架：尺寸与真实版式一致，CLS≈0 -->
    <div v-if="loading" class="hero-inner" aria-busy="true">
      <div class="hero-grid">
        <div class="hero-copy">
          <div class="s-skeleton hero-sk hero-sk--eyebrow"></div>
          <div class="s-skeleton hero-sk hero-sk--title"></div>
          <div class="hero-sk-desc">
            <div class="s-skeleton hero-sk hero-sk--line"></div>
            <div class="s-skeleton hero-sk hero-sk--line hero-sk--short"></div>
          </div>
          <div class="s-skeleton hero-sk hero-sk--cta"></div>
          <div class="s-skeleton hero-sk hero-sk--progress"></div>
        </div>
        <div class="hero-visual">
          <div class="hero-frame-wrap">
            <div class="s-skeleton hero-frame hero-frame-sk"></div>
          </div>
        </div>
      </div>
      <span class="s-sr">{{ t('hero.loading') }}</span>
    </div>

    <!-- 空态：极简占位，不报错 -->
    <div v-else-if="!current" class="hero-inner">
      <p class="hero-empty">{{ t('hero.empty') }}</p>
    </div>

    <div v-else class="hero-inner">
      <div class="hero-grid">
        <div class="hero-copy" :class="{ 'is-entering': entering }">
          <p class="hero-line hero-eyebrow" :style="{ '--i': 0 }">{{ metaLine }}</p>

          <h1 class="hero-line hero-title" :style="{ '--i': 1 }">
            <button type="button" class="hero-title-btn" :title="current.Name" @click="openItem">
              <img v-if="logoUrl" class="hero-logo" :src="logoUrl" :alt="current.Name" draggable="false" />
              <span v-else class="hero-title-text">{{ current.Name }}</span>
            </button>
          </h1>

          <p v-if="blurb" class="hero-line hero-desc" :style="{ '--i': 2 }">{{ blurb }}</p>

          <div class="hero-line hero-actions" :style="{ '--i': 3 }">
            <button type="button" class="hero-cta hero-cta--primary" @click="playItem">
              <svg class="hero-cta-icon" viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" focusable="false">
                <path d="M8 5.2v13.6L19 12z" fill="currentColor" />
              </svg>
              <span>{{ hasProgress ? t('hero.resumePlay') : t('hero.play') }}</span>
            </button>
            <button type="button" class="hero-cta hero-cta--secondary" @click="openItem">
              <span>{{ t('hero.details') }}</span>
            </button>
          </div>

          <!-- 无播放进度时只占位不显示：文案列高度恒定，换张不抖动 -->
          <div class="hero-progress" :class="{ 'is-off': !hasProgress }">
            <div class="hero-progress-track">
              <div class="hero-progress-fill" :style="{ width: `${progressPct}%` }"></div>
            </div>
            <span class="hero-progress-left">{{ t('home.remaining', { minutes: remainingMinutes }) }}</span>
          </div>
        </div>

        <div class="hero-visual">
          <div class="hero-frame-wrap">
            <div
              class="hero-frame"
              :class="{ 'is-portrait': isPortrait }"
              @pointerdown="onPointerDown"
              @pointerup="onPointerUp"
              @pointercancel="onPointerCancel"
            >
              <button type="button" class="hero-hit" :aria-label="t('hero.details')" @click="openItem">
                <template v-for="s in slots" :key="s.slot">
                  <img
                    v-if="s.shot && s.shot.kind !== 'brand'"
                    class="hero-shot"
                    :class="{ 'is-front': front === s.slot, 'is-loaded': s.loaded }"
                    :src="s.shot.src"
                    alt=""
                    draggable="false"
                    decoding="async"
                    @load="onShotLoad(s.slot)"
                    @error="onShotError(s.slot)"
                  />
                  <div v-else-if="s.shot" class="hero-brand" :class="{ 'is-front': front === s.slot }">
                    <img class="hero-brand-mark" :src="markWhite" alt="" draggable="false" />
                  </div>
                </template>
              </button>

              <div class="hero-frame-shade" aria-hidden="true"></div>

              <button
                ref="toggleEl"
                type="button"
                class="hero-toggle"
                :aria-label="userPaused ? t('hero.resume') : t('hero.pause')"
                :aria-pressed="userPaused"
                @click="togglePause"
              >
                <svg v-if="userPaused" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false">
                  <path d="M8 5.2v13.6L19 12z" fill="currentColor" />
                </svg>
                <svg v-else viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false">
                  <rect x="7" y="5" width="3.4" height="14" rx="1" fill="currentColor" />
                  <rect x="13.6" y="5" width="3.4" height="14" rx="1" fill="currentColor" />
                </svg>
              </button>
            </div>

            <div v-if="peekSrc" class="hero-peek" aria-hidden="true">
              <img class="hero-peek-img" :src="peekSrc" alt="" draggable="false" />
            </div>
          </div>
        </div>
      </div>
    </div>

    <div class="hero-fade" aria-hidden="true"></div>

    <!-- 底部指示器：当前段用 8s 线性动画表现剩余时间，跟随暂停态 -->
    <div v-if="!loading && items.length" class="hero-footer">
      <div class="hero-dots">
        <button
          v-for="(dot, i) in items"
          :key="dot.Id"
          type="button"
          class="hero-dot"
          :aria-label="t('hero.goTo', { n: i + 1 })"
          :aria-current="i === active ? 'true' : undefined"
          @click="goTo(i)"
        >
          <span class="hero-dot-rail">
            <span
              v-if="i === active"
              :key="active"
              class="hero-dot-fill"
              :style="{ animationPlayState: running ? 'running' : 'paused' }"
            ></span>
          </span>
        </button>
      </div>
    </div>

    <p class="s-sr" aria-live="polite">{{ announcement }}</p>
  </section>
</template>

<style scoped>
.hero {
  /* 画框高度上限：首屏高度减去上下内边距，保证画框永远不撑破首屏 */
  --hero-frame-h: max(200px, calc(var(--s-hero-h) - 168px));
  position: relative;
  height: var(--s-hero-h);
  min-height: var(--s-hero-min-h);
  overflow: hidden;
  background: var(--s-bg);
  isolation: isolate;
}

/* ---------- 环境光晕（暮紫 + 当前条目的 32px 极小图） ---------- */
.hero-glow {
  position: absolute;
  inset: 0 0 0 34%;
  z-index: 0;
  pointer-events: none;
  background: var(--s-glow);
  filter: blur(40px);
}
.hero-glow-img {
  position: absolute;
  inset: 8% 4% 12% 12%;
  background-position: center;
  background-size: cover;
  filter: blur(40px) saturate(1.4);
  opacity: 0.35;
}

/* ---------- 版心 ---------- */
.hero-inner {
  position: relative;
  z-index: 1;
  height: 100%;
  max-width: var(--s-content-max);
  margin: 0 auto;
  /* 顶栏 fixed 且不透明：上内边距至少让开一个顶栏高度（短视口/横屏不会压住内容） */
  padding: max(var(--s-header-h), var(--s-sp-9)) var(--s-gutter) 76px;
  box-sizing: border-box;
}
.hero-grid {
  display: grid;
  grid-template-columns: minmax(0, 0.92fr) minmax(0, 1.08fr);
  gap: var(--s-sp-9);
  align-items: center;
  height: 100%;
}

/* ---------- 左栏 ---------- */
.hero-copy {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--s-sp-4);
  min-width: 0;
}
.hero-line {
  opacity: 0;
}
.hero-copy.is-entering .hero-line {
  animation: hero-rise var(--s-dur-base) var(--s-ease-std) both;
  animation-delay: calc(var(--i, 0) * 60ms);
}
@keyframes hero-rise {
  from {
    opacity: 0;
    transform: translateY(8px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}

.hero-eyebrow {
  margin: 0;
  font-size: var(--s-fs-sm);
  color: var(--s-text-3);
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.hero-title {
  margin: 0;
  max-width: 100%;
  line-height: var(--s-lh-tight);
}
.hero-title-btn {
  display: block;
  max-width: 100%;
  padding: 0;
  border: 0;
  background: none;
  color: inherit;
  font: inherit;
  text-align: left;
  cursor: pointer;
}
.hero-logo {
  display: block;
  height: 96px;
  width: auto;
  max-width: 100%;
  object-fit: contain;
  object-position: left;
}
.hero-title-text {
  display: block;
  max-width: 14ch;
  font-size: var(--s-fs-hero);
  font-weight: var(--s-fw-semibold);
  letter-spacing: var(--s-tracking-hero);
  color: var(--s-text-1);
}

.hero-desc {
  margin: 0;
  max-width: 52ch;
  font-size: var(--s-fs-body);
  line-height: var(--s-lh-snug);
  color: var(--s-text-2);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-sp-3);
  padding-top: var(--s-sp-1);
}
.hero-cta {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  height: 44px;
  padding: 0 22px 0 18px;
  border: 0;
  border-radius: var(--s-r-pill);
  font-family: var(--s-font);
  font-size: var(--s-fs-h3);
  font-weight: var(--s-fw-medium);
  cursor: pointer;
  transition:
    background-color var(--s-transition),
    transform var(--s-transition);
}
.hero-cta--primary {
  background: var(--s-cta-bg);
  color: var(--s-cta-fg);
}
.hero-cta--primary:hover {
  background: var(--s-cta-bg-hover);
  transform: translateY(-1px);
}
.hero-cta--secondary {
  background: var(--s-cta2-bg);
  color: var(--s-cta2-fg);
}
.hero-cta--secondary:hover {
  background: var(--s-cta2-bg-hover);
  transform: translateY(-1px);
}
.hero-cta:active {
  transform: scale(0.98);
}
.hero-cta-icon {
  flex: none;
}
.hero-progress.is-off {
  visibility: hidden;
}
.hero-progress {
  display: flex;
  align-items: center;
  gap: var(--s-sp-3);
  width: 100%;
  max-width: 420px;
}
.hero-progress-track {
  flex: 1;
  height: 3px;
  border-radius: var(--s-r-pill);
  background: var(--s-line);
  overflow: hidden;
}
.hero-progress-fill {
  height: 100%;
  border-radius: var(--s-r-pill);
  background: var(--s-gradient-dusk-h);
}
.hero-progress-left {
  flex: none;
  font-size: var(--s-fs-xs);
  color: var(--s-text-3);
}

/* ---------- 右栏画框 ---------- */
.hero-visual {
  display: flex;
  flex-direction: column;
  justify-content: center;
  min-width: 0;
}
.hero-frame-wrap {
  position: relative;
  width: calc(100% - 40px);
  margin-left: auto;
}
.hero-frame {
  position: relative;
  /* 由高度上限反推宽度：16:9 永远成立，绝不被 max-height 压扁变形 */
  width: min(100%, calc(var(--hero-frame-h) * 1.7778));
  aspect-ratio: 16 / 9;
  margin-left: auto;
  border: 1px solid var(--s-line);
  border-radius: var(--s-r-xl);
  overflow: hidden;
  background: var(--s-img-placeholder);
  box-shadow: var(--s-shadow-card);
  /* 横向滑动自己处理，纵向滚动留给页面 */
  touch-action: pan-y;
}
/* 没有 Backdrop、只有竖版 Primary → 降级 2:3 画框 + 右侧留白（图片始终 cover，不拉伸） */
.hero-frame.is-portrait {
  width: min(100%, calc(var(--hero-frame-h) * 0.6667));
  margin-left: 0;
  aspect-ratio: 2 / 3;
}
.hero-frame-sk {
  border-radius: var(--s-r-xl);
}

.hero-hit {
  position: absolute;
  inset: 0;
  display: block;
  width: 100%;
  height: 100%;
  padding: 0;
  border: 0;
  background: none;
  cursor: pointer;
}
.hero-shot {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
  opacity: 0;
  transform: scale(1.02);
  transition:
    opacity var(--s-dur-xslow) var(--s-ease-std),
    transform var(--s-dur-xslow) var(--s-ease-std);
}
.hero-shot.is-front {
  z-index: 2;
}
.hero-shot.is-front.is-loaded {
  opacity: 1;
  transform: scale(1);
}
.hero-brand {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--s-gradient-dusk);
  opacity: 0;
  transform: scale(1.02);
  transition:
    opacity var(--s-dur-xslow) var(--s-ease-std),
    transform var(--s-dur-xslow) var(--s-ease-std);
}
.hero-brand.is-front {
  opacity: 1;
  transform: scale(1);
}
.hero-brand-mark {
  width: 72px;
  height: 72px;
}

.hero-frame-shade {
  position: absolute;
  inset: auto 0 0 0;
  z-index: 3;
  height: 48%;
  pointer-events: none;
  background: linear-gradient(0deg, var(--s-scrim-soft), transparent);
}

.hero-toggle {
  position: absolute;
  top: var(--s-sp-3);
  right: var(--s-sp-3);
  z-index: 4;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  padding: 0;
  border: 1px solid var(--s-line);
  border-radius: var(--s-r-pill);
  background: var(--s-cta2-bg);
  color: var(--s-text-1);
  cursor: pointer;
  transition:
    background-color var(--s-transition),
    transform var(--s-transition);
}
.hero-toggle:hover {
  background: var(--s-cta2-bg-hover);
}
.hero-toggle:active {
  transform: scale(0.94);
}

.hero-peek {
  position: absolute;
  top: 0;
  bottom: 0;
  left: 100%;
  width: 32px;
  margin-left: 8px;
  border-radius: var(--s-r-xl) 0 0 var(--s-r-xl);
  overflow: hidden;
  opacity: 0.55;
  pointer-events: none;
}
.hero-peek-img {
  height: 100%;
  width: auto;
  max-width: none;
  object-fit: cover;
  object-position: left center;
}

/* ---------- 底部渐隐 + 指示器 ---------- */
.hero-fade {
  position: absolute;
  inset: auto 0 0 0;
  z-index: 2;
  height: 120px;
  pointer-events: none;
  background: linear-gradient(180deg, transparent, var(--s-bg));
}
.hero-footer {
  position: absolute;
  inset: auto 0 var(--s-sp-6) 0;
  z-index: 3;
}
.hero-dots {
  display: flex;
  gap: 6px;
  max-width: var(--s-content-max);
  margin: 0 auto;
  padding: 0 var(--s-gutter);
  box-sizing: border-box;
}
.hero-dot {
  flex: 1;
  height: 18px;
  padding: 0;
  border: 0;
  background: none;
  cursor: pointer;
  display: flex;
  align-items: center;
}
.hero-dot-rail {
  display: block;
  width: 100%;
  height: 2px;
  border-radius: var(--s-r-pill);
  background: var(--s-line);
  overflow: hidden;
}
.hero-dot-fill {
  display: block;
  width: 100%;
  height: 100%;
  border-radius: var(--s-r-pill);
  background: var(--s-accent);
  transform-origin: left center;
  transform: scaleX(0);
  animation: hero-dot-fill 8s linear forwards;
}
@keyframes hero-dot-fill {
  from {
    transform: scaleX(0);
  }
  to {
    transform: scaleX(1);
  }
}

/* ---------- 空态 ---------- */
.hero-empty {
  margin: 0;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: var(--s-fs-body);
  color: var(--s-text-3);
}

/* ---------- 骨架 ---------- */
.hero-sk {
  border-radius: var(--s-r-sm);
}
/* 骨架逐行对齐真实左栏（实测 1440×900：18+53+38+48+16 + 4×16 gap = 237px），CLS≈0 */
.hero-sk--eyebrow {
  width: 180px;
  height: 18px;
}
.hero-sk--title {
  width: min(70%, 420px);
  height: 53px;
}
.hero-sk-desc {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: min(88%, 520px);
}
.hero-sk--line {
  width: 100%;
  height: 16px;
}
.hero-sk--short {
  width: 72%;
}
.hero-sk--cta {
  width: 220px;
  height: 44px;
  margin-top: 4px;
  border-radius: var(--s-r-pill);
}
.hero-sk--progress {
  width: min(60%, 420px);
  height: 16px;
}

/* ---------- 窄屏：单栏，画框移到文案下方 —— 首屏必须有主图 ---------- */
@media (max-width: 900px) {
  .hero-inner {
    padding: max(var(--s-header-h-sm), var(--s-sp-7)) var(--s-gutter-sm) 64px;
  }
  .hero-grid {
    grid-template-columns: minmax(0, 1fr);
    grid-template-rows: auto auto;
    align-content: center;
    gap: var(--s-sp-6);
  }
  .hero-frame-wrap {
    width: 100%;
    margin-left: 0;
  }
  .hero-frame {
    /* 16:9、满宽、最多 40vh：文案下方仍稳稳落在首屏内 */
    width: 100%;
    max-height: 40vh;
    margin-left: 0;
  }
  /* 只有竖版图时收成一张 2:3 小卡，同样在首屏内 */
  .hero-frame.is-portrait {
    width: auto;
    height: 40vh;
    aspect-ratio: 2 / 3;
    margin: 0 auto;
  }
  .hero-peek {
    display: none;
  }
  .hero-logo {
    height: 72px;
  }
  .hero-title-text {
    max-width: 18ch;
  }
  .hero-dots {
    padding: 0 var(--s-gutter-sm);
  }
}

@media (prefers-reduced-motion: reduce) {
  .hero-copy.is-entering .hero-line {
    animation-delay: 0ms;
  }
}
</style>
