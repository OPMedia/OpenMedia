<script setup lang="ts">
/**
 * HomeHero —— Serata 首页首屏 banner。
 *
 * 版式（用户决策：「背景图居中两边过渡」）：
 *
 *   ┌───────────────────────────────────────────────────────────┐
 *   │          ┌───────────────────────────────┐                │
 *   │         ╱      居中 · 整幅出血的剧照        ╲               │  ← 两侧溶进页面底色
 *   │        ╱                                     ╲              │
 *   │  ┌ 眉标 ┐                                                     │
 *   │  │ 标题 │   ← 文案压在"左 + 下"两片 --s-bg 渐变上            │
 *   │  │ CTA  │                                    · · · ··        │ ← 轮转圆点（右下）
 *   │  └──────┘                                                    │
 *   └───────────────────────────────────────────────────────────┘
 *
 * 四条硬约束：
 *
 *  ① **剧照整幅铺开、居中、两侧渐隐**，不是"一张 16:9 的卡片浮在留白里"。
 *     上一版的失败点在"框"：圆角卡片 + 右侧缩略图栏 + 硬直的矩形边缘，看起来像贴图。
 *     现在图片 `object-fit: cover; object-position: center` 铺满整条 banner，
 *     左/右两侧用 mask 溶掉（`--s-hero-mask`），上/下用 `--s-bg` 渐变收口，
 *     四周没有一条硬边。带宽仍与下面的行严格对齐（同一套 `--s-gutter` / `--s-content-max`）。
 *
 *  ② **压字层用 `--s-bg` 而不是固定深色**。这是这一版最大的结构简化：
 *     文案坐在"图片溶进页面底色"的那一片上，那片底色**就是页面底色**，
 *     所以文字色可以直接跟随主题（`--s-text-*` / `--s-cta-*`），
 *     不需要再维护 `--s-hero-ink` 那一整套"压在图上"的固定色。
 *     上一版正是被那套固定色拖累：浅色主题下文案区被迫是一块黑斑，与"浅色就用浅色"直接冲突。
 *
 *  ③ **层叠顺序写死**：图片 1 < 压字层 2 < 文案 3 < 控件 4。
 *     上一版踩过：压字层 z-index 3 高于文案 z-index 2，且画框没建层叠上下文，
 *     于是压字层把文案糊了（实测白字只剩 2.3:1）。现在 `.hero-stage` 带 `isolation: isolate`，
 *     且文案是 `.hero-stage` 的**兄弟**（同一网格单元），压字层物理上盖不到它。
 *
 *  ④ **主图只用横版大图**（`wideArtUrl`：自己的 Backdrop → 父级 Backdrop → 宽高比 ≈16:9 的 Primary）。
 *     实测媒体库（CollectionFolder）的 `PrimaryImageAspectRatio` 是 1.7778，是横图；
 *     旧逻辑"不是 Backdrop 就当竖版"会把它渲染成 2:3 的怪卡片，该分支已删除。
 *     拿不到就退品牌渐变（`--s-gradient-dusk`），绝不拉伸。
 *
 * 轮转：8000ms 自动（驻留时长，不是 --s-dur-* 令牌，理由见 ROTATE_MS）；hover / 区域内 focus /
 *       标签页隐藏都停；prefers-reduced-motion 下**初始暂停**（铁律 4），用户显式点播放才动。
 *
 * 换片编排：容器走 motion.css 的 s-scrim 类族（旧内容先淡出 → 新内容淡入），子元素走
 *           .s-stagger + --s-i 依次上浮（45ms 步长）。组件内只剩圆点进度条一个关键帧。
 * 手势：桌面悬停内容轻上浮 + 背景图轻推近；横向拖拽跟手位移、松手 spring 回弹；← → 与滑动切换。
 * 键盘：暂停 / 上一张 / 下一张 / 圆点全部 Tab 可到、有 aria-label、Enter/Space 触发。
 *
 * 只消费 styles/serata 的 --s-* 令牌，无裸色值、无 emby-* 类名。
 */
import { computed, onBeforeUnmount, onMounted, ref, watch, type Ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { BaseItemDto } from '@/api/types'
import { wideArtUrl } from '@/api/endpoints'
import markWhite from '@/assets/brand/mark-white.svg'
import { useReducedMotionSafe } from '@/lib/motion'

/**
 * 自动轮转驻留时长。**故意不走 --s-dur-***：令牌的语义是"转场时长"（最长 480ms），
 * 这是"停留时长"（让人读完一屏信息），不是一个类；WCAG 2.2.2 也要求自动轮转 ≥5s。
 * 它同时是 JS 计时器与圆点进度条 CSS 动画的**唯一真源**（模板里下发 --hero-rotate）。
 */
const ROTATE_MS = 8000
const SWIPE_MIN_PX = 40
/** 拖拽跟手上限（px）：再远也不跟了，避免"把整条 banner 拖出去"的错觉 */
const DRAG_LIMIT_PX = 72
/** 入画兜底：图片既不 load 也不 error 时也要换（避免卡在旧帧） */
const REVEAL_FALLBACK_MS = 1500
const TICKS_PER_MINUTE = 600_000_000

const props = withDefaults(defineProps<{ items: BaseItemDto[]; loading?: boolean }>(), { loading: false })

const router = useRouter()
const { t } = useI18n()

// ---------------------------------------------------------------- 图片降级链

type ShotKind = 'art' | 'brand'

interface Shot {
  itemId: string
  kind: ShotKind
  src: string
}

/**
 * 主图 shot。`art` 拿不到横版大图时**就地降级 brand**（品牌渐变 + 白标），
 * 所以调用方永远不需要再判空。
 */
function shotOf(item: BaseItemDto): Shot {
  const src = wideArtUrl(item, { maxWidth: 1920, quality: 90 })
  return src ? { itemId: item.Id, kind: 'art', src } : { itemId: item.Id, kind: 'brand', src: '' }
}

/** 圆点的无障碍短标题：剧集显示剧名（和 heroTitle 同一套语义） */
function dotLabel(item: BaseItemDto): string {
  if (item.Type === 'Episode' && item.SeriesName) return item.SeriesName
  return item.Name ?? ''
}

// ---------------------------------------------------------------- 当前条目

const active = ref(0)
const current = computed<BaseItemDto | undefined>(() => props.items[active.value])

/** 只有 1 项时圆点没有意义（自己指向自己），整组收掉 */
const hasMultiple = computed(() => props.items.length >= 2)

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
  const labelKey: string | undefined = TYPE_LABEL_KEY[item.Type]
  const parts: string[] = [labelKey ? t(labelKey) : item.Type]
  // 剧集补上「S4:E13」定位。**不拼 SeriesName**：剧集的标题已经是剧名了，
  // 眉标再带一遍会变成「分集 · 梦幻西游 S4:E13 · 梦幻西游」这种复读。
  if (item.Type === 'Episode') {
    const code = episodeCode(item)
    if (code) parts.push(code)
  }
  // 脏数据兜底：真实站有 ProductionYear=1（见 EmbyCard 同名处理）
  if (item.ProductionYear && item.ProductionYear >= 1900) parts.push(String(item.ProductionYear))
  if (item.RunTimeTicks) parts.push(t('item.runtime', { minutes: Math.round(item.RunTimeTicks / TICKS_PER_MINUTE) }))
  if (item.OfficialRating) parts.push(item.OfficialRating)
  return parts.join(' · ')
})

/** `S4:E13`；季/集号缺任一就返回空串，绝不拼出 undefined（真实字段是 ParentIndexNumber/IndexNumber） */
function episodeCode(item: BaseItemDto): string {
  const s = item.ParentIndexNumber
  const e = item.IndexNumber
  return s != null && e != null ? `S${s}:E${String(e).padStart(2, '0')}` : ''
}

/**
 * hero 大标题。
 * 剧集优先显示**剧名**：流媒体首页的 hero 位是「这部作品」的门面，
 * 显示「第 13 集」等于没有信息（而且真实站里确实有剧集的名字就叫「第 13 集」）。
 * 拿不到 SeriesName 时退回条目自己的名字。
 *
 * **不用 Emby 的 Logo 图**（`ImageTags.Logo`）：Logo 是透明 PNG，颜色由片方决定，
 * 实测「梦幻西游」的 Logo 是深青色，压在图上直接消失；换成别的片子又会反过来。
 * 标题一律纯文字，颜色走主题令牌，颜色可控、可量、永远读得出来，
 * 也省掉一次图片请求和一个布局抖动源。
 */
const heroTitle = computed(() => {
  const item = current.value
  if (!item) return ''
  if (item.Type === 'Episode' && item.SeriesName) return item.SeriesName
  return item.Name ?? ''
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

/** 每个槽自己的条目（降级时要知道给谁重算） */
const slotItem: Record<0 | 1, BaseItemDto | null> = { 0: null, 1: null }

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
  const next = shotOf(item)
  const prev = slotRef(slot).value
  const painted = prev !== null && prev.src === next.src && loadedRef(slot).value
  slotItem[slot] = item
  slotRef(slot).value = next
  // 品牌渐变是本地绘制，没有网络加载，直接算已就绪
  if (next.kind === 'brand') loadedRef(slot).value = true
  else if (!painted) loadedRef(slot).value = false
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

function onShotLoad(slot: 0 | 1): void {
  loadedRef(slot).value = true
  settle(slot)
}
function onShotError(slot: 0 | 1): void {
  // 图挂了就退品牌渐变，绝不留一个浏览器裂图图标
  slotRef(slot).value = { itemId: slotItem[slot]?.Id ?? '', kind: 'brand', src: '' }
  loadedRef(slot).value = true
  settle(slot)
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
    const frontShot = front.value === 0 ? slotA.value : slotB.value
    if (frontShot?.itemId === next.Id) return
    reveal(next)
  },
  { immediate: true },
)

// ---------------------------------------------------------------- 轮转 / 暂停

const hovering = ref(false)
const focusInside = ref(false)
const tabHidden = ref(false)
const userPaused = ref(false)
/** 铁律 16：任何 JS 位移（拖拽跟手）动手前先问它；reduce 时位移必须完全消失 */
const reduced = useReducedMotionSafe()
const rootEl = ref<HTMLElement | null>(null)
const toggleEl = ref<HTMLButtonElement | null>(null)

const canRotate = computed(() => !props.loading && props.items.length > 1)
/**
 * 暂停条件：hover / 区域内 focus / 标签页隐藏 / 显式暂停。
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

// 换片编排交给 motion.css：容器走 s-scrim 类族（旧内容先淡出 → 新内容淡入），
// 子元素走 .s-stagger + --s-i 依次上浮（45ms 步长）。组件内不再自建关键帧（铁律 14）。
// 代价是文案节点按 current.Id 重建：换片前焦点若在文案里，@enter 时立刻还给同位置元素，
// 否则键盘用户会被丢到 body（铁律 4）。
const copyEl = ref<HTMLElement | null>(null)
const FOCUSABLE = 'button, a[href], [tabindex]:not([tabindex="-1"])'
let copyFocusIndex = -1

function captureCopyFocus(): void {
  const el = document.activeElement
  if (!(el instanceof HTMLElement) || !copyEl.value?.contains(el)) {
    copyFocusIndex = -1
    return
  }
  copyFocusIndex = [...copyEl.value.querySelectorAll<HTMLElement>(FOCUSABLE)].indexOf(el)
}
function restoreCopyFocus(): void {
  const idx = copyFocusIndex
  copyFocusIndex = -1
  if (idx < 0) return
  copyEl.value?.querySelectorAll<HTMLElement>(FOCUSABLE)[idx]?.focus({ preventScroll: true })
}
// flush 默认 'pre'：DOM 换掉之前抓，抓到的还是旧节点
watch(active, () => captureCopyFocus())

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
  // 暂停按钮自己的 focus 不算「区域内 focus」，否则点了继续播放永远起不来
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
/** 拖拽跟手位移（px）：只写 transform，松手回弹 */
const dragDx = ref(0)
const dragging = ref(false)

function resetDrag(): void {
  dragging.value = false
  dragDx.value = 0
}
function onPointerDown(event: PointerEvent): void {
  if (event.pointerType === 'mouse' && event.button !== 0) return
  swipeTracking = true
  swipeStartX = event.clientX
  swipeStartY = event.clientY
}
function onPointerMove(event: PointerEvent): void {
  // reduce 下完全不给位移（铁律 16：不是"快一点"，是不动）
  if (!swipeTracking || reduced.value) return
  const dx = event.clientX - swipeStartX
  const dy = event.clientY - swipeStartY
  // 纵向意图（页面滚动）不抢：横向位移不够大就不跟手
  if (Math.abs(dx) < 6 || Math.abs(dx) <= Math.abs(dy)) return
  dragging.value = true
  dragDx.value = Math.max(-DRAG_LIMIT_PX, Math.min(DRAG_LIMIT_PX, dx * 0.5))
}
function onPointerUp(event: PointerEvent): void {
  if (!swipeTracking) return
  swipeTracking = false
  const dx = event.clientX - swipeStartX
  const dy = event.clientY - swipeStartY
  resetDrag()
  if (Math.abs(dx) < SWIPE_MIN_PX || Math.abs(dx) <= Math.abs(dy)) return
  // 滑动手势不该顺带触发 banner 点击跳详情
  suppressClickUntil = performance.now() + 400
  step(dx < 0 ? 1 : -1)
}
function onPointerCancel(): void {
  swipeTracking = false
  resetDrag()
}

function onVisibilityChange(): void {
  tabHidden.value = document.hidden
}

const announcement = computed(() =>
  current.value ? t('hero.slide', { n: active.value + 1, total: props.items.length }) : '',
)

watch(reduced, (on) => {
  // 系统偏好切到减少动效 → 落到暂停位；切回来不强行恢复（由用户自己点）
  if (on) userPaused.value = true
})

onMounted(() => {
  // 铁律 4：reduce 下**初始暂停**（不自动播放），但保留用户显式点播放的能力
  userPaused.value = reduced.value
  tabHidden.value = document.hidden
  document.addEventListener('visibilitychange', onVisibilityChange)
})

onBeforeUnmount(() => {
  stopTimer()
  clearPendingTimer()
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
    <div class="hero-inner">
      <!-- 骨架：与真实版式同一个网格（图片格子 + 文案格子），CLS≈0 -->
      <div v-if="loading" class="hero-band" aria-busy="true">
        <div class="hero-stage">
          <div class="s-skeleton hero-sk-art"></div>
        </div>
        <div class="hero-copy hero-copy--sk">
          <div class="s-skeleton hero-sk hero-sk--eyebrow"></div>
          <div class="s-skeleton hero-sk hero-sk--title"></div>
          <div class="s-skeleton hero-sk hero-sk--line"></div>
          <div class="s-skeleton hero-sk hero-sk--cta"></div>
        </div>
        <span class="s-sr">{{ t('hero.loading') }}</span>
      </div>

      <!-- 空态：极简占位，不报错 -->
      <p v-else-if="!current" class="hero-empty">{{ t('hero.empty') }}</p>

      <div v-else class="hero-band">
        <!-- 剧照区：整幅铺开、居中、两侧渐隐。自成层叠上下文（见脚本头注释 ③） -->
        <div
          class="hero-stage"
          @pointerdown="onPointerDown"
          @pointermove="onPointerMove"
          @pointerup="onPointerUp"
          @pointercancel="onPointerCancel"
        >
          <button
            type="button"
            class="hero-hit"
            :class="{ 'is-dragging': dragging }"
            :style="dragDx ? { transform: `translateX(${dragDx}px)` } : undefined"
            :aria-label="t('hero.details')"
            @click="openItem"
          >
            <!-- 渐隐蒙版挂在这一层：mask 不跟着图片的 1.03→1 微缩一起缩放，边缘才不会抖 -->
            <span class="hero-art" aria-hidden="true">
              <template v-for="s in slots" :key="s.slot">
                <img
                  v-if="s.shot && s.shot.kind === 'art'"
                  class="hero-shot"
                  :class="{ 'is-front': front === s.slot, 'is-loaded': s.loaded }"
                  :src="s.shot.src"
                  alt=""
                  draggable="false"
                  decoding="async"
                  @load="onShotLoad(s.slot)"
                  @error="onShotError(s.slot)"
                />
                <span v-else-if="s.shot" class="hero-brand" :class="{ 'is-front': front === s.slot }">
                  <img class="hero-brand-mark" :src="markWhite" alt="" draggable="false" />
                </span>
              </template>
            </span>
          </button>

          <div class="hero-scrim" aria-hidden="true"></div>

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

          <!-- 轮转圆点：当前项拉长成 8s 进度条（与 JS 计时器同源） -->
          <div
            v-if="hasMultiple"
            class="hero-dots"
            role="group"
            :aria-label="t('hero.navLabel')"
            :style="{ '--hero-rotate': `${ROTATE_MS}ms` }"
          >
            <button
              v-for="(item, i) in items"
              :key="item.Id"
              type="button"
              class="hero-dot"
              :class="{ 'is-active': i === active }"
              :aria-label="t('hero.dot', { n: i + 1, name: dotLabel(item) })"
              :aria-current="i === active ? 'true' : undefined"
              @click="goTo(i)"
            >
              <span
                v-if="i === active"
                :key="active"
                class="hero-dot-fill"
                :style="{ animationPlayState: running ? 'running' : 'paused' }"
              ></span>
            </button>
          </div>
        </div>

        <!-- 文案：桌面压在剧照左下（同网格单元、更高层），窄屏落到剧照下方 -->
        <div ref="copyEl" class="hero-copy">
          <!-- 换片编排：容器走 motion.css 的 s-scrim 类族（旧内容先淡出 → 新内容淡入），
               子元素走 .s-stagger + --s-i 依次上浮，步长来自 --s-stagger-step（45ms）。 -->
          <Transition name="s-scrim" mode="out-in" @enter="restoreCopyFocus">
            <div :key="current.Id" class="hero-copy-body s-stagger">
              <p class="hero-eyebrow" :style="{ '--s-i': 0 }">{{ metaLine }}</p>

              <h1 class="hero-title" :style="{ '--s-i': 1 }">
                <button type="button" class="hero-title-btn" :title="heroTitle" @click="openItem">
                  <span class="hero-title-text">{{ heroTitle }}</span>
                </button>
              </h1>

              <p v-if="blurb" class="hero-desc" :style="{ '--s-i': 2 }">{{ blurb }}</p>

              <div class="hero-actions" :style="{ '--s-i': 3 }">
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
              <div class="hero-progress" :class="{ 'is-off': !hasProgress }" :style="{ '--s-i': 4 }">
                <div class="hero-progress-track">
                  <div class="hero-progress-fill" :style="{ width: `${progressPct}%` }"></div>
                </div>
                <span class="hero-progress-left">{{ t('home.remaining', { minutes: remainingMinutes }) }}</span>
              </div>
            </div>
          </Transition>
        </div>
      </div>
    </div>

    <p class="s-sr" aria-live="polite">{{ announcement }}</p>
  </section>
</template>

<style scoped>
.hero {
  position: relative;
  /* 顶栏 fixed 且不透明：上内边距至少让开一个顶栏高度，短视口/横屏不会压住内容 */
  padding: max(var(--s-header-h), var(--s-sp-6)) 0 var(--s-sp-9);
  background: var(--s-bg);
  isolation: isolate;
}
.hero-inner {
  max-width: var(--s-content-max);
  margin: 0 auto;
  padding: 0 var(--s-gutter);
}

/* ---------- 版式：一个网格单元，图片与文案叠在一起 ----------
   这样文案是 `.hero-stage` 的**兄弟**而不是子节点：
   `.hero-stage` 有自己的 `overflow: hidden` 与层叠上下文，
   文案放外面就物理上不可能被压字层盖住（上一版正是在这里翻的车）。 */
.hero-band {
  position: relative;
  display: grid;
  grid-template-columns: minmax(0, 1fr);
}
.hero-stage {
  grid-area: 1 / 1;
  position: relative;
  width: 100%;
  /* 高度给一个舒适的带宽：太矮会变成一条横幅，太高会把下面的行挤出首屏 */
  height: clamp(400px, 58vh, 620px);
  overflow: hidden;
  background: var(--s-img-placeholder);
  /* 横向滑动自己处理，纵向滚动留给页面 */
  touch-action: pan-y;
  /* **必须**：层叠上下文。否则里面的 .hero-scrim（z-index 2）会跑到
     .hero-band 的上下文里，与 .hero-copy 的 z-index 直接比大小。 */
  isolation: isolate;
}
.hero-sk-art {
  position: absolute;
  inset: 0;
  border-radius: 0;
}

.hero-hit {
  position: absolute;
  inset: 0;
  z-index: 1;
  display: block;
  width: 100%;
  height: 100%;
  padding: 0;
  border: 0;
  background: none;
  cursor: pointer;
  /* 拖拽跟手位移：松手回弹走 spring；跟手过程中关掉过渡，否则手感拖泥带水 */
  transition: transform var(--s-dur-base) var(--s-ease-spring);
}
.hero-hit.is-dragging {
  transition: none;
}

/* ---------- 剧照 + 两侧渐隐 ----------
   蒙版挂在这一层而不是 <img> 上：<img> 每次换片会做 1.03→1 的微缩，
   蒙版跟着缩放会让两边的渐隐在换片时"呼吸"一下。 */
.hero-art {
  position: absolute;
  inset: 0;
  display: block;
  mask-image: var(--s-hero-mask);
}
.hero-shot {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  /* 居中铺满。横幅比 16:9 宽，cover 会裁掉上下，构图重心留在中间 */
  object-fit: cover;
  object-position: center;
  opacity: 0;
  transform: scale(1.03);
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
@media (hover: hover) {
  /* 悬停时背景图轻推近：只动 transform，复用同一套 --s-dur-xslow 运动语言 */
  .hero:hover .hero-shot.is-front.is-loaded {
    transform: scale(1.04);
  }
}
.hero-brand {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--s-gradient-dusk);
  opacity: 0;
  transform: scale(1.03);
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

/* 压字层：上 + 下 + 左三层 --s-bg 渐变。文案的底色因此就是页面底色，
   文字色可以直接跟随主题，不需要另一套"压在图上"的固定色。 */
.hero-scrim {
  position: absolute;
  inset: 0;
  z-index: 2;
  pointer-events: none;
  background: var(--s-hero-scrim);
}

.hero-toggle {
  position: absolute;
  top: var(--s-sp-5);
  right: var(--s-sp-5);
  z-index: 4;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  padding: 0;
  border: 1px solid var(--s-hero-control-ring);
  border-radius: var(--s-r-pill);
  /* 按钮压在剧照上（那块没有 --s-bg 兜底），所以走"在图上"专用的控件令牌 */
  background: var(--s-hero-control-bg);
  color: var(--s-hero-control-fg);
  cursor: pointer;
  transition:
    background-color var(--s-transition),
    transform var(--s-transition);
}
.hero-toggle:hover {
  background: var(--s-hero-control-bg-hover);
}
.hero-toggle:active {
  transform: scale(0.94);
}

/* ---------- 轮转圆点（右下） ---------- */
.hero-dots {
  position: absolute;
  right: var(--s-sp-8);
  bottom: var(--s-sp-8);
  z-index: 4;
  display: flex;
  align-items: center;
  gap: var(--s-sp-2);
}
.hero-dot {
  position: relative;
  width: 8px;
  height: 8px;
  padding: 0;
  border: 0;
  border-radius: var(--s-r-pill);
  background: color-mix(in srgb, var(--s-text-3) 45%, transparent);
  cursor: pointer;
  overflow: hidden;
  transition:
    width var(--s-dur-base) var(--s-ease-spring),
    background-color var(--s-transition);
}
.hero-dot:hover {
  background: color-mix(in srgb, var(--s-text-3) 75%, transparent);
}
/* 当前项拉长成一小段进度条；轨道底色留白，让渐变填充看得清 */
.hero-dot.is-active {
  width: 40px;
  background: color-mix(in srgb, var(--s-text-3) 26%, transparent);
}
.hero-dot-fill {
  display: block;
  width: 100%;
  height: 100%;
  background: var(--s-gradient-dusk-h);
  transform-origin: left center;
  transform: scaleX(0);
  animation: hero-dot-fill var(--hero-rotate, var(--s-dur-xslow)) linear forwards;
}
@keyframes hero-dot-fill {
  from {
    transform: scaleX(0);
  }
  to {
    transform: scaleX(1);
  }
}

/* ---------- 文案 ----------
   文案坐在 `.hero-stage` 的同一网格单元里、`align-self: end` 贴底，
   它的底色是 `--s-hero-scrim` 收口出来的页面底色，所以文字/按钮全部用主题令牌。 */
.hero-copy {
  grid-area: 1 / 1;
  z-index: 3;
  align-self: end;
  padding: var(--s-sp-8);
  pointer-events: none;
}
.hero-copy-body {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--s-sp-3);
  width: 100%;
  /* 文案列收在左半区：左侧渐隐最浓，越往外越依赖剧照本身 */
  max-width: 560px;
  /* 悬停上浮只动 transform（铁律 3）；时长/缓动走令牌。
     pointer-events 收回：外层是为了让 padding 区不吃点击，内容自己要能点。 */
  pointer-events: auto;
  transition: transform var(--s-dur-base) var(--s-ease-spring);
}
@media (hover: hover) {
  .hero:hover .hero-copy-body {
    transform: translateY(-4px);
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
  max-width: 46ch;
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

/* ---------- 空态 ---------- */
.hero-empty {
  margin: 0;
  min-height: var(--s-hero-min-h);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: var(--s-fs-body);
  color: var(--s-text-3);
}

/* ---------- 骨架 ---------- */
.hero-copy--sk {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--s-sp-3);
  pointer-events: none;
}
.hero-sk {
  border-radius: var(--s-r-sm);
}
.hero-sk--eyebrow {
  width: 180px;
  height: 18px;
}
.hero-sk--title {
  width: min(70%, 360px);
  height: 48px;
}
.hero-sk--line {
  width: min(88%, 460px);
  height: 16px;
}
.hero-sk--cta {
  width: 200px;
  height: 44px;
  margin-top: 4px;
  border-radius: var(--s-r-pill);
}

/* ---------- 窄屏：剧照 16:9 站上面，文案落到它下方 ---------- */
@media (max-width: 900px) {
  .hero {
    padding: max(var(--s-header-h-sm), var(--s-sp-5)) 0 var(--s-sp-7);
  }
  .hero-inner {
    padding: 0 var(--s-gutter-sm);
  }
  .hero-band {
    grid-template-rows: auto auto;
  }
  .hero-stage {
    grid-area: 1 / 1;
    /* 窄屏上不要裁掉构图：直接按 16:9 让图自己长高 */
    height: auto;
    aspect-ratio: 16 / 9;
  }
  /* 文案离开剧照 → 落到第二行，横向对齐剧照左边缘 */
  .hero-copy {
    grid-area: 2 / 1;
    align-self: start;
    padding: var(--s-sp-5) 0 0;
    pointer-events: auto;
  }
  .hero-copy-body {
    max-width: none;
  }
  .hero-title-text {
    max-width: 18ch;
  }
  /* 文案不在图上，压字层只负责让右下角圆点读得出来 */
  .hero-scrim {
    background: linear-gradient(
      0deg,
      var(--s-bg) 0%,
      color-mix(in srgb, var(--s-bg) 62%, transparent) 24%,
      transparent 46%
    );
  }
  .hero-dots {
    right: var(--s-sp-4);
    bottom: var(--s-sp-3);
  }
  .hero-toggle {
    top: var(--s-sp-3);
    right: var(--s-sp-3);
  }
}

@media (prefers-reduced-motion: reduce) {
  /* 铁律 16 的 CSS 侧兜底：motion.css 已切掉 animation，这里再收掉悬停位移与背景图放大 */
  .hero:hover .hero-copy-body,
  .hero:hover .hero-shot.is-front.is-loaded,
  .hero-dot {
    transform: none;
  }
}
</style>
