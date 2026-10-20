<script setup lang="ts">
/**
 * HomeHero —— Serata 首页首屏 banner。
 *
 * 版式（用户决策：「背景图居中两边过渡」+「文案压在图上」）：
 *
 *   ┌───────────────────────────────────────────────────────────┐
 *   │          ┌───────────────────────────────┐                │
 *   │         ╱      居中 · 整幅出血的剧照        ╲               │  ← 两侧溶进页面底色
 *   │        ╱                                     ╲              │
 *   │  ┌ 标题 ┐  ← 文案压在"左 + 下"两片渐变上，**所有断点都在图上**  │
 *   │  │ CTA  │    窄屏靠收小字号 / 36px 按钮 / 隐藏简介来适配带宽   │
 *   │  └──────┘                                · · · ··  (⏸)     │ ← 圆点 + 键盘可见的暂停键（右下）
 *   └───────────────────────────────────────────────────────────┘
 *
 * 五条硬约束：
 *
 *  ① **剧照整幅铺开、居中、两侧渐隐**，不是"一张 16:9 的卡片浮在留白里"。
 *     上一版的失败点在"框"：圆角卡片 + 右侧缩略图栏 + 硬直的矩形边缘，看起来像贴图。
 *     现在图片 `object-fit: cover; object-position: center` 铺满整条 banner，
 *     左/右两侧用 mask 溶掉（`--s-hero-mask`），上/下用 `--s-bg` 渐变收口，
 *     带宽仍与下面的行严格对齐（同一套 `--s-gutter` / `--s-content-max`）。
 *     ⚠️ **已知遗留（不是本次改动引入的，改前改后实测同值）**：带宽**右边缘**还剩一条
 *     13~17 的亮度台阶（浅色主题更明显）—— 图在右边缘已被 mask 溶到 0，露出来的是
 *     `.hero-stage` 自己的底色 `--s-img-placeholder`，而它和页面底色 `--s-bg` 不是一个色；
 *     左边不显，是因为 `--s-hero-scrim` 横向那层 88% 底色正好把占位色盖住了。
 *     证据与两种修法见 scratch/lead2/hero-ab.md，动哪一条由 Lead 定（会改到加载态观感）。
 *
 *  ② **文案在每一个断点都压在图上**：`.hero-copy` 与 `.hero-stage` 同处网格单元 1/1、
 *     `align-self: end` 贴底。窄屏**不再**把文案落到剧照下方的第二行 ——
 *     那正是"手机上文案和图是两块、看不出是一条 banner"的根因（旧版 ≤900px 分支已删）。
 *     窄屏的做法是**收小文案去适配 banner**，不是加高 banner 去装文案：
 *     标题走 `--s-fs-hero-sm`（26px）、按钮 36px / `--s-fs-sm`、隐藏简介；
 *     标题在**所有断点**都封成一行（超长走省略号，见 `.hero-title-text`），
 *     眉标（分集 · S4:E13 · 2016 · 24 分钟）已整条删除（用户口径 2026-10-20：
 *     「标题上面为什么要显示分集那些东西呢？完全没有必要」）；
 *     带宽高度跟着**宽度**走（`clamp(224px, 60vw, 280px)`，实测 ≤ 视口 40%），
 *     所以首屏永远看得见下面的「继续观看」行。
 *
 *  ③ **压字分工：全局收口 + 局部承托，不做"整条压暗"**。
 *     `--s-hero-scrim`（已减淡）只负责把带宽的顶边/底边收进页面底色；
 *     文案的对比度由 `.hero-copy-body::before` 那一层**局部**承托（`--s-hero-copy-wash`，
 *     负 inset 化开）保证 —— 图区（右半张剧照）因此保持原始亮度。
 *     这样文案可以直接跟随主题（`--s-text-*` / `--s-cta-*`），不需要再维护 `--s-hero-ink`
 *     那一整套"压在图上"的固定色：上一版正是被它拖累，浅色主题下文案区被迫是一块黑斑。
 *     用户口径（2026-10-20）：遮罩浓度太大，无论深浅，背后的图根本看不清。
 *
 *  ④ **层叠顺序写死**：图片 1 < 压字层 2 < 文案 3 < 控件 4。
 *     上一版踩过：压字层 z-index 3 高于文案 z-index 2，且画框没建层叠上下文，
 *     于是压字层把文案糊了（实测白字只剩 2.3:1）。现在 `.hero-stage` 带 `isolation: isolate`，
 *     且文案是 `.hero-stage` 的**兄弟**（同一网格单元），压字层物理上盖不到它。
 *
 *  ⑤ **主图只用横版大图**（`wideArtUrl`：自己的 Backdrop → 父级 Backdrop → 宽高比 ≈16:9 的 Primary）。
 *     实测媒体库（CollectionFolder）的 `PrimaryImageAspectRatio` 是 1.7778，是横图；
 *     旧逻辑"不是 Backdrop 就当竖版"会把它渲染成 2:3 的怪卡片，该分支已删除。
 *     拿不到就退品牌渐变（`--s-gradient-dusk`），绝不拉伸。
 *
 * 轮转 / 暂停（WCAG 2.2.2）：8000ms 自动（驻留时长，不是 --s-dur-* 令牌，理由见 ROTATE_MS）；
 *   hover / 区域内 focus / 标签页隐藏都停；prefers-reduced-motion 下**初始暂停**（铁律 4），
 *   用户显式点播放才动。
 *   **暂停控件不再常驻在 banner 角上**（用户口径：banner 上不该有一个播放按钮）：
 *   它并进右下角那一组轮转圆点，默认 `opacity: 0` + `pointer-events: none`，
 *   只有 `:focus-visible`（键盘 Tab 到）或 `userPaused`（已经暂停）时才显形。
 *   Tab 可达、有 aria-label / aria-pressed —— "可暂停"能力一点没少，只是不再占画面。
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
 * 请求尺寸按**槽位实际宽度**算，不写死 1920（Lead 口径：`clamp(900, slotW × dpr, 1920)`）。
 * 窄屏剧照区只有 358px，却去要 1920px 的图 = **5.4 倍过采样**，单张 ~245KB 纯浪费；
 * 而且图越小首次 MISS 越快 —— 预取的 6 张能更早就位。
 *
 * 宽度用 `documentElement.clientWidth - 两侧 gutter` 近似剧照区宽度：**不读 DOM 尺寸、
 * 不用 ResizeObserver**，避免"先按兜底尺寸发一次、量到真实宽度再发一次"的双请求。
 * 口径**整个组件生命周期只定一次**并缓存：否则窗口一改大小，`shotOf()` 就会算出新 URL，
 * 已经预热好的缓存当场作废，下一次切图反而多一次冷 MISS（比固定 1920 更糟）。
 */
let artWidthCache: number | null = null
function artMaxWidth(): number {
  if (artWidthCache !== null) return artWidthCache
  if (typeof document === 'undefined') return (artWidthCache = 1920)
  const vw = document.documentElement.clientWidth || 1600
  const gutter = vw <= 900 ? 16 : 24
  const slotW = Math.max(320, vw - 2 * gutter)
  const dpr = Math.min(3, (typeof window === 'undefined' ? 1 : window.devicePixelRatio) || 1)
  artWidthCache = Math.min(1920, Math.max(900, Math.round(slotW * dpr)))
  return artWidthCache
}

/**
 * 主图 shot。`art` 拿不到横版大图时**就地降级 brand**（品牌渐变 + 白标），
 * 所以调用方永远不需要再判空。
 */
function shotOf(item: BaseItemDto): Shot {
  const src = wideArtUrl(item, { maxWidth: artMaxWidth(), quality: 90 })
  return src ? { itemId: item.Id, kind: 'art', src } : { itemId: item.Id, kind: 'brand', src: '' }
}

/** 圆点的无障碍短标题：剧集显示剧名（和 heroTitle 同一套语义） */
function dotLabel(item: BaseItemDto): string {
  if (item.Type === 'Episode' && item.SeriesName) return item.SeriesName
  return item.Name ?? ''
}

// ---------------------------------------------------------------- 剧照预取（不能少）

/**
 * **为什么必须预取**（Lead 在真机首页实测，6/6 复现）：
 * 点圆点 / 自动轮播时**文案立刻换、剧照却要 3.7–6.8 秒才换**。这几秒里用户看到的
 * 是"新标题 + 上一部的剧照"—— 正好把 banner 刚修好的"图文必须对应"反过来。
 *
 * 根因：`.hero-shot` 的 src 是**翻面那一刻才写进 DOM** 的（见 prepare/reveal），
 * 所以点之前那张大图根本没被请求 —— probe-swap 实测 `newBackdropRequests=1`，
 * 即请求是点击那一刻才发的；而 `maxWidth=1920&quality=90` 的剧照走缓存代理
 * **首次 MISS 要 ~4 秒**（真机 30 张卡片图实测单张 1–8 秒，全是首次 MISS 打真机）。
 *
 * 做法：池子一确定，就把整池剧照各起一个 `new Image()`，**只填浏览器缓存、不碰 DOM**。
 * 六张并行发起（`new Image()` 不在文档里，浏览器给的是低优先级，不会抢首屏那张
 * 可见大图的带宽），一次 ~4 秒就位，正好落在第一张 8 秒的轮转窗口内；
 * 之后翻面时写 src 直接命中缓存，交叉淡入在几十毫秒内完成。
 *
 * 守卫：**拿不到横版大图的条目（kind=brand）绝不拼 URL** —— 那必然是 404 的地址。
 * `wideArtUrl()` 内部走 `wideArtOf()`，没有图就返回空串，这里再按 kind 兜一道。
 */
let warmers: HTMLImageElement[] = []

function warmPool(list: BaseItemDto[]): void {
  // 换用户 / 换池：先把上一轮的请求丢掉，别让它们继续占连接
  for (const img of warmers) img.src = ''
  warmers = []
  if (typeof window === 'undefined') return
  for (const item of list) {
    const shot = shotOf(item)
    if (shot.kind !== 'art' || !shot.src) continue
    const img = new Image()
    img.decoding = 'async'
    img.src = shot.src
    warmers.push(img)
  }
}

// 池子确定的那一刻就开跑（items 换新数组 = 池子变了）
watch(
  () => props.items,
  (list) => warmPool(list ?? []),
  { immediate: true },
)

onBeforeUnmount(() => {
  for (const img of warmers) img.src = ''
  warmers = []
})

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

/**
 * hero 大标题。
 * 剧集优先显示**剧名**：流媒体首页的 hero 位是「这部作品」的门面，
 * 显示「第 13 集」等于没有信息（而且真实站里确实有剧集的名字就叫「第 13 集」）。
 * 拿不到 SeriesName 时退回条目自己的名字。
 *
 * **永远一行**（用户口径 2026-10-20：「如果遇到长标题不要做成几行显示 这很丑
 * 直接一行过去就行了」）：`.hero-title-text` 在所有断点都是 `-webkit-line-clamp: 1`，
 * 超长走省略号；被省略掉的那部分靠 `<button title>` 悬停可读（见模板）。
 * 这里因此**不做任何截断** —— 截断交给 CSS，DOM 里留着完整标题，屏幕阅读器与
 * `title` 提示拿到的都是全名。
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

          <!-- 轮转圆点：当前项拉长成 8s 进度条（与 JS 计时器同源）。
               暂停/播放键也并进这一组（右下），**不再占 banner 角**：
               默认不可见、鼠标点不到，只有键盘 focus-visible 或"已暂停"时才显形（见 .hero-pause）。 -->
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

            <button
              ref="toggleEl"
              type="button"
              class="hero-pause"
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
        </div>

        <!-- 文案：所有断点都压在剧照上（同网格单元、更高层、align-self:end 贴底） -->
        <div ref="copyEl" class="hero-copy">
          <!-- 换片编排：容器走 motion.css 的 s-scrim 类族（旧内容先淡出 → 新内容淡入），
               子元素走 .s-stagger + --s-i 依次上浮，步长来自 --s-stagger-step（45ms）。 -->
          <Transition name="s-scrim" mode="out-in" @enter="restoreCopyFocus">
            <div :key="current.Id" class="hero-copy-body s-stagger">
              <!-- 标题：所有断点都一行（超长省略号，完整标题在 title 属性里）。
                   眉标（分集 · S4:E13 · 2016 · 24 分钟）已整条删除 —— 用户口径 2026-10-20：
                   「标题上面为什么要显示分集那些东西呢？完全没有必要」。
                   眉标原来是 --s-i: 0，删掉后整条序列前移：标题 0 → 简介 1 → 按钮 2 → 进度 3。 -->
              <h1 class="hero-title" :style="{ '--s-i': 0 }">
                <button type="button" class="hero-title-btn" :title="heroTitle" @click="openItem">
                  <span class="hero-title-text">{{ heroTitle }}</span>
                </button>
              </h1>

              <p v-if="blurb" class="hero-desc" :style="{ '--s-i': 1 }">{{ blurb }}</p>

              <div class="hero-actions" :style="{ '--s-i': 2 }">
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
              <div class="hero-progress" :class="{ 'is-off': !hasProgress }" :style="{ '--s-i': 3 }">
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
  /* 底色用**页面底色**，不用 --s-img-placeholder。
     原因：`--s-hero-mask` 把剧照在左右两侧溶到 0，右边缘露出来的是这一层的底色；
     占位色（浅 #ece5db / 深 #161a30）和页面底色 --s-bg（浅 #f7f5f2 / 深 #0a0c18）不是一个色，
     于是带宽右缘留下一条看得见的竖直台阶（实测亮度跳变 17.4 浅 / 13.3 深；
     左边不显是因为 --s-hero-scrim 横向 88% 那层正好盖住了占位色）。
     换掉它不会丢加载态占位：加载走的是另一条分支的 .hero-sk-art（骨架自己铺满整个带宽）。 */
  background: var(--s-bg);
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

/* ---------- 暂停/播放（键盘可见，不占 banner 角） ----------
   用户口径：banner 上不该常驻一个播放按钮。所以它并进右下角的圆点组，
   默认 opacity:0 + pointer-events:none —— 鼠标用户根本点不到它，
   键盘用户 Tab 到它（:focus-visible）或已经处于暂停态时才显形。
   WCAG 2.2.2 要的是"能停"，不是"必须露在外面"。
   它贴在未渐隐的剧照上，所以用 --s-hero-control-* 那组"在图上"的配色。 */
.hero-pause {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: none;
  width: 32px;
  height: 32px;
  padding: 0;
  border: 1px solid var(--s-hero-control-ring);
  border-radius: var(--s-r-pill);
  background: var(--s-hero-control-bg);
  color: var(--s-hero-control-fg);
  cursor: pointer;
  opacity: 0;
  pointer-events: none;
  transition:
    opacity var(--s-transition),
    background-color var(--s-transition),
    transform var(--s-transition);
}
/* 键盘焦点 + 已暂停：这两种情况必须看得见（否则键盘用户找不到恢复入口） */
.hero-pause:focus-visible,
.hero-pause[aria-pressed='true'] {
  opacity: 1;
  pointer-events: auto;
}
.hero-pause:hover {
  background: var(--s-hero-control-bg-hover);
}
.hero-pause:active {
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
   文案坐在 `.hero-stage` 的同一网格单元里、`align-self: end` 贴底（**所有断点**）。
   对比度靠两层配合：`--s-hero-scrim` 把带宽上下收口进页面底色，
   `.hero-copy-body::before` 在文案块背后再补一层**局部**承托（`--s-hero-copy-wash`）。
   因为是局部承托，右半张剧照保持原始亮度，文字色可以直接用主题令牌。 */
.hero-copy {
  grid-area: 1 / 1;
  z-index: 3;
  align-self: end;
  padding: var(--s-sp-8);
  pointer-events: none;
}
.hero-copy-body {
  position: relative;
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
/* 局部承托层：画在文案块背后并**负 inset 化开**，边缘不出现"一块底色"的硬边。
   inset 的左/下正好落在 .hero-copy 的 padding 边界上 = 剧照区的左边缘 / 下边缘，
   所以它只在 banner 内化开，不会糊到 banner 外面去。
   z-index:-1 不会漏到剧照下面：`.hero-copy` 是带 z-index 的网格项，自成一个层叠上下文。 */
.hero-copy-body::before {
  content: '';
  position: absolute;
  inset: -56px -72px -40px -40px;
  z-index: -1;
  /* **桌面保持 token 原样**：verify-dev 独立复测桌面净图区是干净的
     （1600×900：有文案 0.0077 / 隐藏文案 0.0208），不要去动它。
     桌面进度文案的对比度缺口由 `.hero-progress-left::before` 补
     （那一小片不落在 x55–95%/y10–70% 的净图区里）。
     眉标已整条删除，原先挂在它上面的那层 `::before`（桌面专用补丁）随之删掉。 */
  background: var(--s-hero-copy-wash);
  pointer-events: none;
  /* token 的径向渐变以 120%×150% 为界，在承托层自己的**上边和右边**还没淡到 0，
     直接铺会看出一圈矩形硬边（浅色主题下最刺眼）。实测亮度跳变
     （scratch/lead2/seam-metric.py，1600×900）：上边 28.1 / 右边 20.1，
     而左边只有 1.8 —— 因为左边落在 --s-hero-scrim 88% 底色那一层上，本来就看不出来。
     所以给上、右两条边加线性蒙版化开；左、下不裁：
     · 左边不裁 = 不削文案脚下的承托；
     · 下边**故意不裁** —— 承托层用的就是 --s-bg，它一直铺到剧照下边缘反而把
       "占位色/遮罩 vs 页面底色"那条既有的色差补平了。实测给下边加渐隐后，
       下边缘跳变从 5.2 涨到 24.1（浅色 390）/ 1.9 涨到 12.9（浅色 1600）。 */
  -webkit-mask-image: linear-gradient(90deg, black 0, black 72%, transparent 100%),
    linear-gradient(180deg, transparent 0, black 20%, black 100%);
  mask-image: linear-gradient(90deg, black 0, black 72%, transparent 100%),
    linear-gradient(180deg, transparent 0, black 20%, black 100%);
  -webkit-mask-composite: source-in;
  mask-composite: intersect;
}
/* 进度文案那一小片：**桌面和窄屏都要**（窄屏的 .hero-progress-left 没有 overflow:hidden，
   不会被裁）。它落在进度条**右端**，而 token 那条径向渐变的中心在左下角（8% 88%），
   所以这一格是全块最没被承托到的地方 —— 窄屏浅色实测最差 3.60:1，补这一片后到位。
   四边渐隐用 **px 定长**（不是百分比）：文案长短每张剧照都不同，
   百分比渐隐会让"字形是否落在实心区"随文案长度漂移。 */
.hero-progress-left {
  position: relative;
  isolation: isolate;
}
.hero-progress-left::before {
  content: '';
  position: absolute;
  inset: -6px -10px -6px -14px;
  z-index: -1;
  background: color-mix(in srgb, var(--s-bg) 62%, transparent);
  -webkit-mask-image: linear-gradient(90deg, transparent 0, black 14px, black calc(100% - 10px), transparent 100%),
    linear-gradient(180deg, transparent 0, black 6px, black calc(100% - 6px), transparent 100%);
  mask-image: linear-gradient(90deg, transparent 0, black 14px, black calc(100% - 10px), transparent 100%),
    linear-gradient(180deg, transparent 0, black 6px, black calc(100% - 6px), transparent 100%);
  -webkit-mask-composite: source-in;
  mask-composite: intersect;
  pointer-events: none;
}
@media (hover: hover) {
  .hero:hover .hero-copy-body {
    transform: translateY(-4px);
  }
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
/* 标题**永远一行**（用户口径 2026-10-20：「如果遇到长标题不要做成几行显示 这很丑
   直接一行过去就行了」）：所有断点都是 -webkit-line-clamp: 1，超长走省略号。
   被省略掉的字靠模板里 `<button title>` 的悬停提示读（DOM 里始终是完整标题）。

   `padding-bottom` 是**字形安全垫**，不是间距：line-clamp 的裁切边界是 padding box，
   而 --s-lh-tight(1.15) 的行盒比字体自带字形框（实测 1.353em）矮，不留垫子降部会被削掉。
   实测（scratch/lead3/hero/metrics.txt，`needed = (fontBoundingBoxAscent + Descent − lineHeight)/2`）：
     · 桌面 --s-fs-hero = clamp(34px, 3.4vw, 58px)：34px→3.45 / 43.5px→3.98 / 54.4px(1600)→5.22 / 58px→5.89；
       取 6px 覆盖整档。
     · 窄屏 --s-fs-hero-sm = 26px：0.101em×26 ≈ 2.6px，沿用下面 ≤900px 分支的 3px。
   `max-width: 14ch` 保持原样：实测 1600 档 14ch = 422.8px ≈ 7.8 个汉字，
   超长标题会省略号 —— 这是"一行封顶"的既定取舍（要显示更多字是另一件事，见交接说明）。 */
.hero-title-text {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 1;
  overflow: hidden;
  max-width: 14ch;
  padding-bottom: 6px;
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
  /* **不是 --s-text-3**：那是"最低强调色"，拿来当"压在剧照上的正文级信息"不够。
     实测（6 张剧照 × 深/浅 × 桌面/手机 = 24 次）text-3 只有 6 次达到 4.5:1，最差 1.17:1
     —— 而且最差的全部落在 390×844，只测桌面会漏掉（桌面 12 次里挂 7 次，最差 3.01）。
     它落在进度条**右端**，而承托层的径向渐变中心在左下角（8% 88%），
     所以这一格是全块最没被承托到的地方 —— 承托层补强见上面 `.hero-copy-body::before`。 */
  color: var(--s-text-2);
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

/* ---------- 窄屏：文案**仍然**压在图上，只把整套排版收小 ----------
   旧版在这里把 .hero-copy 落到 grid-area 2/1（剧照下方的第二行），
   于是手机上"图"和"字"变成两块，看起来不像一条 banner。该分支已删除。 */
@media (max-width: 900px) {
  .hero {
    padding: max(var(--s-header-h-sm), var(--s-sp-5)) 0 var(--s-sp-7);
  }
  .hero-inner {
    padding: 0 var(--s-gutter-sm);
  }
  .hero-stage {
    /* 窄屏高度**跟着宽度走**，不跟着视口高度走。
       用户口径：「变小适配 banner 栏的大小」—— 不是把 banner 撑高去装文案。
       预算账（实测，见 scratch/lead2/hero-after/geom2.json）：
         · 这套压在图上的文案最少要 ≈183px（眉标 18 + 标题一行 33 + 按钮 36 + 进度 15.6
           + 三个 8px 间距 + 上 16 / 下 40 内边距）；
         · 另外承托层上沿必须落在剧照区往下 25% 以下（Lead 硬要求：banner 上方 ≥25%
           必须能看见原始剧照），等于还要再让出 0.25×H 的净图区；
         · 16:9 在 390 只有 201px、在 360 只有 184px —— 扣掉 25% 净图区（360 是 46px）
           只剩 138px，装不下 183px 的文案，所以不能回 aspect-ratio；
         · 取 clamp(224px, 60vw, 280px)：360→224（视口 640 的 35%）、390→234（844 的 28%）、
           900→280（700 的 40%），全部落在「≤ 视口 40%」的硬线以内。 */
    height: clamp(224px, 60vw, 280px);
  }
  .hero-copy {
    /* 底边 40px（= --s-sp-8）：文案块底边离剧照区底边 40px；
       「圆点 + 暂停键」那组贴在剧照区底边上（dots 的 bottom 收到 --s-sp-2 = 8px，
       圆点 28px 高 → 净空 4px），不会被文案最后一行（续播进度条）压住。
       为什么从 48 收到 40：文案块是**底对齐**的，底边内边距每多 1px 文案块就整体上移 1px，
       而承托层的上沿（= 文案块顶部）必须落在「剧照区顶部往下 25%」以下 —— 见下一条。 */
    padding: var(--s-sp-4) var(--s-sp-4) var(--s-sp-8);
  }
  .hero-copy-body {
    max-width: none;
    gap: var(--s-sp-2);
  }
  /* 承托层收窄：窄屏文案块本来就贴着剧照边，负 inset 再大就溢出 banner 了。
     右边界收在 .hero-stage 的右边缘上（和桌面同口径），免得蒙版外侧又留一条边。
     **下边界必须等于 .hero-copy 的 padding-bottom（--s-sp-8）**，让承托层的下边缘正好压在
     剧照区的下边缘上：那里 --s-hero-scrim 的底部渐变已经把画面收口到底色，接缝看不见。
     差 8px 都会露馅 —— 实测下边缘亮度跳变 27.7（浅色）/ 6.9（深色），
     A/B 关掉承托层后降到 3.0 / 0.07（scratch/lead2/hero-after/wash-ab.json）。 */
  .hero-copy-body::before {
    /* **上沿从 -40px 收到 0**（= 文案块顶部）。这是 Lead 的硬要求：承托层最上沿不得高于
       「剧照区顶部往下 25%」，即 banner 上方 ≥25% 必须能看见原始剧照。
       原来的 -40px 让上沿落到 4.5%（360×640）/ 8.5%（390×844）——verify-dev 独立复测
       x5–95%/y5–25% 那条带平均亮度差 0.1967，几乎整块被洗白。
       密度**一点没降**（顶上照样 0.72，加 token 那条径向渐变还更浓），只收面积。
       顶部羽化用 **3px 定长**：文案块第一个子元素现在是标题，它的字形顶离文案块顶
       只有 ≈1px（26px 字号、行盒 29.9px、字形框 35.2px → 上下各溢出 2.6px），
       羽化一长，字形顶部就吃不到承托，4.5:1 会掉。 */
    inset: 0 -16px calc(-1 * var(--s-sp-8)) -16px;
    background: linear-gradient(
        180deg,
        color-mix(in srgb, var(--s-bg) 72%, transparent) 0,
        color-mix(in srgb, var(--s-bg) 66%, transparent) 30%,
        color-mix(in srgb, var(--s-bg) 60%, transparent) 100%
      ),
      var(--s-hero-copy-wash);
    -webkit-mask-image: linear-gradient(90deg, black 0, black 72%, transparent 100%),
      linear-gradient(180deg, transparent 0, black 3px, black 100%);
    mask-image: linear-gradient(90deg, black 0, black 72%, transparent 100%),
      linear-gradient(180deg, transparent 0, black 3px, black 100%);
  }
  /* 标题收一档（--s-fs-hero-sm = 26px，≥24px 的读得清下限）；单行封顶的
     line-clamp / overflow / 字形安全垫都在上面的基础规则里，这里只改字号与宽度上限。
     为什么窄屏必须是 1 行（不是口味，是算出来的）：
     文案块是底对齐的，标题每多一行 → 文案块整体上移一行高（≈31px）。
     实测 390×844 那张两行标题的剧照（虽然我是不完美恶女…）跑到 y=86 = 剧照区
     往下 **12.8%**，而 Lead 的硬要求是「承托层最上沿不得高于 25%」—— 文案压在
     25% 以上的裸剧照上，只能二选一：要么违反净图区（实测那条带平均亮度差会被顶到
     0.1967 那一档），要么文字对比度掉到 4.5:1 以下。**两行标题在最窄档装不下**：
     360×640 剧照区只有 224px，而下方要占
       上方净图区 56px（= 224 × 25%）
     + 两行文案 ≈157.6px（眉标 18 + 两行标题 63 + 按钮 36 + 进度 15.6 + 三个 8px 间距）
     + 右下角圆点那一带 40px（28px 圆点 + 8px 底距 + 4px 净空）
     = 253.6px > 224px，**数学上就不可能**（差 29.6px）。
     收成一行后文案块 126.6px：56 + 126.6 + 40 = 222.6 ≤ 224，**刚好过线**（实测承托层
     上沿落在 25.9%）。一行封顶后文案块高度还与标题长短无关，承托层上沿的位置才稳定。
     注：上面的 157.6/126.6 是**含眉标 18px** 的旧预算（当时眉标还在）。
     眉标删除后窄屏文案块再矮 18+8=26px，净图区只会更宽松 —— 预算不变保守成立。
     代价：长标题在窄屏会出省略号（如「虽然我是不完美恶女 ～雏…」）—— 现在**桌面同样**
     单行省略（见基础规则），不再有"桌面两行"这一档。 */
  .hero-title-text {
    max-width: 100%;
    font-size: var(--s-fs-hero-sm);
    /* 字形安全垫收窄：26px 字号下实测需要 (35.2−29.9)/2 ≈ 2.6px，取 3px */
    padding-bottom: 3px;
  }
  /* 两行简介会把文案块顶出 banner —— 窄屏直接不给它位置 */
  .hero-desc {
    display: none;
  }
  .hero-actions {
    gap: var(--s-sp-2);
    padding-top: 0;
  }
  .hero-cta {
    height: 36px;
    padding: 0 14px 0 12px;
    gap: 8px;
    font-size: var(--s-fs-sm);
  }
  .hero-cta-icon {
    width: 15px;
    height: 15px;
  }
  .hero-progress {
    gap: var(--s-sp-2);
  }
  .hero-progress-left {
    line-height: 1.2;
  }
  .hero-dots {
    right: var(--s-sp-4);
    /* 从 --s-sp-3 收到 --s-sp-2：文案块底边内边距同时从 48 收到 40，
       进度条整体下移 8px，圆点不跟着下移就会被压住（28px 圆点 → 净空 4px）。 */
    bottom: var(--s-sp-2);
  }
  .hero-pause {
    width: 28px;
    height: 28px;
  }
  .hero-pause svg {
    width: 14px;
    height: 14px;
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
