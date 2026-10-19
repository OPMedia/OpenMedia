<script setup lang="ts">
/**
 * 首页 —— Serata 流媒体首页（重写，替换原 1:1 Emby home 版式）。
 *
 * 区块顺序 = localStorage['serata.homescreenSettings'] 的 7 个槽位 homeSection1..7
 * （与 SettingsHomescreenView 写入的结构一一对应；值为 '' 即空槽，跳过）：
 *   hero（首屏轮转）/ resume（继续观看）/ nextup（接下来播放）/ latest（每个媒体库一行「最新」）
 *   / mymedia / mymediasmall（库入口行）/ recordings / resumeaudio
 * 未配置该键 → 默认序：hero, resume, nextup, latest, '', '', ''。
 *
 * 去重规则：nextup 行按 Id 剔除已在 resume 行出现的条目（同一条目不在两行同时出现）；
 * 去重后为空 → 整行不渲染。
 *
 * 非 happy path：加载中每行 6 张骨架卡（尺寸对齐 EmbyCard，CLS≈0）；任一关键请求失败 → 页面级
 * 错误块 + 重试；所有行皆空 → 空态引导（品牌 mark + 去媒体库扫描）。绝不静默吞错。
 *
 * 铁律：只用 --s-* 令牌、px 单位、动效只用 transform/opacity/filter、文案一律 t()。
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { motion } from 'motion-v'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import type { BaseItemDto } from '@/api/types'
import { getFavorites, getItems, getLatestItems, getNextUp, getUserViews } from '@/api/endpoints'
import { useAuthStore } from '@/stores/auth'
import EmbyCard from '@/components/EmbyCard.vue'
import HomeHero from '@/components/home/HomeHero.vue'
import { useHomeHero } from '@/composables/useHomeHero'
import {
  S_DUR,
  S_EASE_CSS,
  flipConfig,
  riseVariants,
  staggerContainer,
  useReducedMotionSafe,
} from '@/lib/motion'
import markGradient from '@/assets/brand/mark-gradient.svg'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const { t } = useI18n()

/** 卡片取数必须带 Fields，否则拿不到图片比例/播放进度（卡片图与进度条都依赖它） */
const CARD_FIELDS = 'Overview,ImageTags,BackdropImageTags,PrimaryImageAspectRatio,RunTimeTicks,UserData'
/** 每个媒体库「最新」取 12 条 */
const LATEST_PER_LIB = 12
/** 骨架卡数量（与真实行的可见卡数对齐） */
const SKELETON_CARDS = 6

const LS_KEY = 'serata.homescreenSettings'
/** 未配置时的默认序：首槽 hero = 开箱即流媒体首页形态 */
const DEFAULT_SLOTS = ['hero', 'resume', 'nextup', 'latest', '', '', '']
/** 支持的槽位取值（与设置页选项表一一对应） */
const KNOWN_SLOTS = new Set(['hero', 'resume', 'nextup', 'latest', 'mymedia', 'mymediasmall', 'recordings', 'resumeaudio'])

/** 读 7 个槽位；未配置/损坏 → 回落默认序（不让一个坏 JSON 把首页打空） */
function readSlots(): string[] {
  try {
    const raw = localStorage.getItem(LS_KEY)
    if (!raw) return [...DEFAULT_SLOTS]
    const saved = JSON.parse(raw) as Record<string, unknown>
    return DEFAULT_SLOTS.map((fallback, i) => {
      const v = saved[`homeSection${i + 1}`]
      return typeof v === 'string' ? v : fallback
    })
  } catch {
    return [...DEFAULT_SLOTS]
  }
}

const slots = ref<string[]>(readSlots())
/** 收藏 tab（/?tab=favorites，外壳的「收藏」入口）仍由本视图承载 */
const isFavoritesTab = computed(() => route.query.tab === 'favorites')

const loading = ref(true)
/** true = 关键请求失败（页面级错误块 + 重试）；与「空」严格区分 */
const failed = ref(false)
const views = ref<BaseItemDto[]>([])
const resumeItems = ref<BaseItemDto[]>([])
const nextUpItems = ref<BaseItemDto[]>([])
const latestRows = ref<{ lib: BaseItemDto; items: BaseItemDto[] }[]>([])
const favMovies = ref<BaseItemDto[]>([])
const favSeries = ref<BaseItemDto[]>([])

// Hero（接口冻结：见 styles/serata/README.md §4）。README 的 props 是值类型，
// 故解构出 ref 再交给模板（顶层 ref 才会自动解包；hero.items 形式会把 Ref 传进 props）。
const { items: heroItems, loading: heroLoading, reload: heroReload } = useHomeHero(
  computed(() => auth.user?.Id),
)

/** nextup 与 resume 按 Id 去重后的数据 */
const nextUpDeduped = computed(() => {
  const seen = new Set(resumeItems.value.map((i) => i.Id))
  return nextUpItems.value.filter((i) => !seen.has(i.Id))
})

type Shape = 'portrait' | 'backdrop'
type Size = 'md' | 'sm'

interface HomeRow {
  key: string
  title: string
  shape: Shape
  size: Size
  items: BaseItemDto[]
  /** 「查看全部」→ 该库页 */
  libId?: string
  /** 「查看全部」→ 该库页的分节列表 */
  sectionId?: 'resume' | 'nextup'
  /** 「查看全部」→ 该库页的收藏 tab */
  favOf?: 'Movie' | 'Series'
  /** 槽位占位：这一项是首屏 Hero，不是普通行（模板据此渲染 <HomeHero>） */
  isHero?: boolean
}

/** Hero 槽位是否被启用（收藏 tab 里不启用） */
const heroSlotActive = computed(() => !isFavoritesTab.value && slots.value.includes('hero'))

/** Hero 槽位是否真的能出内容（加载中也算，出骨架） */
const heroRenderable = computed(
  () => heroSlotActive.value && (heroLoading.value || heroItems.value.length > 0),
)

/** 按槽位顺序（而不是写死顺序）组装行；hero 以占位行参与排序 */
const rows = computed<HomeRow[]>(() => {
  if (isFavoritesTab.value) {
    const favRows: HomeRow[] = []
    if (favMovies.value.length) {
      favRows.push({ key: 'fav-movies', title: t('home.favMovies'), shape: 'portrait', size: 'md', items: favMovies.value, favOf: 'Movie' })
    }
    if (favSeries.value.length) {
      favRows.push({ key: 'fav-series', title: t('home.favSeries'), shape: 'portrait', size: 'md', items: favSeries.value, favOf: 'Series' })
    }
    return favRows
  }

  const out: HomeRow[] = []
  for (const [i, slot] of slots.value.entries()) {
    if (!KNOWN_SLOTS.has(slot) || slot === '') continue

    // hero 也是槽位里的一项：用占位行参与排序，位置真的跟着设置页的槽位走
    if (slot === 'hero') {
      if (heroRenderable.value) {
        out.push({ key: `hero-${i}`, title: '', shape: 'backdrop', size: 'md', items: [], isHero: true })
      }
      continue
    }

    if (slot === 'resume') {
      if (resumeItems.value.length) {
        out.push({ key: `resume-${i}`, title: t('home.continueWatching'), shape: 'backdrop', size: 'md', items: resumeItems.value, sectionId: 'resume' })
      }
    } else if (slot === 'nextup') {
      if (nextUpDeduped.value.length) {
        out.push({ key: `nextup-${i}`, title: t('home.nextUp'), shape: 'backdrop', size: 'md', items: nextUpDeduped.value, sectionId: 'nextup' })
      }
    } else if (slot === 'latest') {
      for (const { lib, items } of latestRows.value) {
        if (!items.length) continue
        // 原来是 t('home.latestPrefix') + lib.Name，即「最新」+ 库名直接拼接 →
        // 「最新2026十月番」「最新国产剧」。真实服务器有 22 个库，这一串读起来是病句，
        // 而且「最新2026十月番」会把年份粘在中文里造成歧义。
        // 库名才是行的主语，「最新」是限定语 → 后置，用 · 分隔：「2026十月番 · 最新」。
        out.push({ key: `latest-${lib.Id}`, title: `${lib.Name} · ${t('home.latestPrefix').trim()}`, shape: 'portrait', size: 'md', items, libId: lib.Id })
      }
    } else if (slot === 'mymedia' || slot === 'mymediasmall') {
      if (views.value.length) {
        out.push({ key: `${slot}-${i}`, title: t('home.myMedia'), shape: 'backdrop', size: slot === 'mymediasmall' ? 'sm' : 'md', items: views.value })
      }
    }
    // recordings / resumeaudio：本项目无 Live TV 与音频续播端点（endpoints.ts 无对应函数），
    // 槽位保持可用但渲染空行 —— 不造假数据。
  }
  return out
})

/** 加载中的骨架行：与即将出现的真实行同形状同尺寸（latest 加载期按 1 行占位；hero 同样占位） */
const skeletonRows = computed<{ key: string; shape: Shape; size: Size; isHero?: boolean }[]>(() => {
  if (isFavoritesTab.value) {
    return [
      { key: 'sk-fav-movies', shape: 'portrait', size: 'md' },
      { key: 'sk-fav-series', shape: 'portrait', size: 'md' },
    ]
  }
  const out: { key: string; shape: Shape; size: Size; isHero?: boolean }[] = []
  for (const [i, slot] of slots.value.entries()) {
    if (!KNOWN_SLOTS.has(slot) || slot === '') continue
    if (slot === 'hero') {
      if (heroSlotActive.value) out.push({ key: `sk-hero-${i}`, shape: 'backdrop', size: 'md', isHero: true })
      continue
    }
    if (slot === 'latest') out.push({ key: `sk-latest-${i}`, shape: 'portrait', size: 'md' })
    else if (slot === 'resume' || slot === 'nextup') out.push({ key: `sk-${slot}-${i}`, shape: 'backdrop', size: 'md' })
    else if (slot === 'mymedia' || slot === 'mymediasmall') {
      out.push({ key: `sk-${slot}-${i}`, shape: 'backdrop', size: slot === 'mymediasmall' ? 'sm' : 'md' })
    }
  }
  return out
})

/** 全空：没有真实行、hero 也没东西可放（hero 占位行不算行） */
const pageEmpty = computed(
  () =>
    !loading.value &&
    !failed.value &&
    rows.value.every((r) => r.isHero) &&
    !(heroSlotActive.value && (heroLoading.value || heroItems.value.length > 0)),
)

async function load() {
  const user = auth.user
  if (!user) return
  // 每次进入都重读槽位：设置页改完返回首页立即生效
  slots.value = readSlots()
  loading.value = true
  failed.value = false
  try {
    const id = user.Id
    const viewList = (await getUserViews(id)).Items
    views.value = viewList

    if (isFavoritesTab.value) {
      const [fm, fs] = await Promise.all([getFavorites(id, 'Movie'), getFavorites(id, 'Series')])
      favMovies.value = fm.Items
      favSeries.value = fs.Items
    } else {
      const need = new Set(slots.value)
      const [cw, nu, latest] = await Promise.all([
        need.has('resume')
          ? getItems(id, {
              // 继续观看：Filters=IsResumable + SortBy=DatePlayed 倒序
              Filters: 'IsResumable', SortBy: 'DatePlayed', SortOrder: 'Descending',
              IncludeItemTypes: 'Movie,Episode', Recursive: true, Limit: 20, Fields: CARD_FIELDS,
            })
          : Promise.resolve(null),
        need.has('nextup') ? getNextUp(id, { Limit: 20 }) : Promise.resolve(null),
        need.has('latest')
          ? Promise.all(viewList.map(async (lib) => ({
              lib,
              items: await getLatestItems(id, { Limit: LATEST_PER_LIB, ParentId: lib.Id }),
            })))
          : Promise.resolve(null),
      ])
      resumeItems.value = cw?.Items ?? []
      nextUpItems.value = nu?.Items ?? []
      latestRows.value = latest ?? []
      favMovies.value = []
      favSeries.value = []
    }
  } catch (e) {
    // 失败态：页面级错误块（可重试）——不静默吞掉，也不与「空」混淆
    failed.value = true
    console.error('[home] 首页取数失败', e)
  } finally {
    loading.value = false
    await nextTick()
    syncAllTracks()
  }
}

async function reload() {
  await Promise.allSettled([load(), heroReload()])
}

// ---------- 行入场：滚入视口才播、交错一次、不重播（motion-v 变体，见 styles/serata/README §5） ----------
const reduced = useReducedMotionSafe()
/** IntersectionObserver 不可用时直接给终态 —— 内容绝不能停在 opacity 0 */
const canObserve = typeof window !== 'undefined' && 'IntersectionObserver' in window

/**
 * 行容器变体：行壳淡入 + 子项交错（子项用 riseVariants 上浮）。
 * reduced / 无 IO → 初始即终态，一次位移都不挂（铁律 16）。
 */
const rowVariants = computed(() => {
  if (reduced.value || !canObserve) return { hidden: { opacity: 1 }, visible: { opacity: 1 } }
  const { visible } = staggerContainer(reduced.value)
  return {
    hidden: { opacity: 0 },
    visible: {
      opacity: 1,
      transition: { ...visible.transition, duration: S_DUR.slow / 1000, ease: S_EASE_CSS.std },
    },
  }
})

/** 卡片变体：时长/缓动显式走令牌镜像，不吃库默认值（铁律 14） */
const cardVariants = computed(() => {
  const v = riseVariants(reduced.value)
  if (reduced.value) return v
  return {
    hidden: v.hidden,
    visible: { ...v.visible, transition: { duration: S_DUR.slow / 1000, ease: S_EASE_CSS.std } },
  }
})

/** 进场初始态：reduced / 无 IO 时直接从终态开始，等于完全不播 */
const entranceInitial = computed(() => (reduced.value || !canObserve ? 'visible' : 'hidden'))

/** 列表 FLIP：切库/刷新时行与卡片不硬跳；reduce 时 duration=0（双保险） */
const flipOptions = computed(() => flipConfig(reduced.value))

// ---------- 横向滚动 / 箭头可用性 ----------
const tracks = new Map<string, HTMLElement>()
const scrollState = ref<Record<string, { back: boolean; fwd: boolean }>>({})

function syncScroll(key: string, el?: HTMLElement | null) {
  const node = el ?? tracks.get(key)
  if (!node) return
  const max = node.scrollWidth - node.clientWidth
  const back = node.scrollLeft <= 1
  const fwd = max <= 1 || node.scrollLeft >= max - 1
  const prev = scrollState.value[key]
  // 值没变就不写：v-for 里的函数 ref 每次渲染都会重新调用，写新对象会引发渲染回环
  if (prev && prev.back === back && prev.fwd === fwd) return
  scrollState.value = { ...scrollState.value, [key]: { back, fwd } }
}

function onScroll(key: string, e: Event) {
  syncScroll(key, e.currentTarget as HTMLElement)
}

function setTrack(key: string, el: Element | ComponentPublicInstance | null) {
  if (el instanceof HTMLElement) {
    if (tracks.get(key) !== el) tracks.set(key, el)
    void nextTick(() => syncScroll(key, el))
    return
  }
  // Vue 每次重渲染都会先用 null 摘一次函数 ref：只有旧节点真的离开 DOM 才清状态，
  // 否则箭头会被重置成「两端都禁用」
  const node = tracks.get(key)
  if (!node || node.isConnected) return
  tracks.delete(key)
  if (scrollState.value[key]) {
    const next = { ...scrollState.value }
    delete next[key]
    scrollState.value = next
  }
}

function syncAllTracks() {
  for (const key of tracks.keys()) syncScroll(key)
}

function atStart(key: string) {
  return scrollState.value[key]?.back ?? true
}
function atEnd(key: string) {
  return scrollState.value[key]?.fwd ?? true
}

/** 点击滚动一屏（0.8 × 容器宽）；减少动效时不做平滑滚动 */
function scrollRow(key: string, dir: -1 | 1) {
  const el = tracks.get(key)
  if (!el) return
  const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
  el.scrollBy({ left: dir * el.clientWidth * 0.8, behavior: reduce ? 'auto' : 'smooth' })
}

/** 「查看全部」目标（无目标则不渲染该按钮） */
function targetFor(row: HomeRow): { name: 'library'; params: { id: string }; query: Record<string, string> } | null {
  if (row.favOf) {
    const lib = row.favOf === 'Movie'
      ? views.value.find((v) => v.CollectionType === 'movies') ?? views.value[0]
      : views.value.find((v) => v.CollectionType === 'tvshows') ?? views.value[0]
    return lib ? { name: 'library', params: { id: lib.Id }, query: { tab: 'favorites' } } : null
  }
  if (row.libId) return { name: 'library', params: { id: row.libId }, query: { tab: 'suggestions' } }
  if (row.sectionId) {
    const lib = row.sectionId === 'nextup'
      ? views.value.find((v) => v.CollectionType === 'tvshows') ?? views.value[0]
      : views.value[0]
    return lib ? { name: 'library', params: { id: lib.Id }, query: { sectionId: row.sectionId } } : null
  }
  return null
}

function goRow(row: HomeRow) {
  const target = targetFor(row)
  if (target) void router.push(target)
}

/** 卡片/骨架共用的尺寸类（两者同宽，骨架→内容零位移） */
function sizeClass(shape: Shape, size: Size) {
  if (size === 'sm') return shape === 'backdrop' ? 's-row-card-wide-sm' : 's-row-card-sm'
  return shape === 'backdrop' ? 's-row-card-wide' : 's-row-card'
}
function posterShapeClass(shape: Shape) {
  return shape === 'backdrop' ? 'is-wide' : ''
}

watch(() => route.query.tab, () => void load())
onMounted(() => {
  void load()
  window.addEventListener('resize', syncAllTracks)
})
onBeforeUnmount(() => window.removeEventListener('resize', syncAllTracks))
</script>

<template>
  <main class="s-home" :aria-busy="loading ? 'true' : 'false'">
    <p v-if="loading" class="s-sr" role="status">{{ t('home.loading') }}</p>

    <!-- 首屏 Hero 已并入下方槽位循环（它的位置由 homeSection* 决定），这里不再单独渲染 -->

    <!-- 错误态：页面级，可重试 -->
    <div v-if="failed" class="s-state" role="alert">
      <p class="s-state-title">{{ t('home.error') }}</p>
      <button type="button" class="s-btn s-btn-primary" @click="reload">{{ t('common.retry') }}</button>
    </div>

    <!-- 空库态：所有行皆空 -->
    <div v-else-if="pageEmpty" class="s-state">
      <img class="s-state-mark" :src="markGradient" alt="" width="96" height="96" />
      <p class="s-state-title">{{ t('home.empty') }}</p>
      <p class="s-state-hint">{{ t('home.emptyHint') }}</p>
      <button type="button" class="s-btn s-btn-primary" @click="router.push('/dashboard/libraries')">
        {{ t('home.emptyAction') }}
      </button>
    </div>

    <!-- 行容器接 FLIP：切库/刷新/槽位重排时行不硬跳 -->
    <div v-else class="s-home-inner" v-auto-animate="flipOptions">
      <!-- 加载中：每个槽位 6 张骨架卡；hero 槽位出 hero 自己的骨架 -->
      <template v-if="loading">
        <template v-for="sk in skeletonRows" :key="sk.key">
          <HomeHero v-if="sk.isHero" :items="heroItems" :loading="true" />
          <section v-else class="s-row s-row-skel">
            <div class="s-row-head">
              <div class="s-skel-title s-skeleton"></div>
            </div>
            <div class="s-skel-rail">
              <div v-for="n in SKELETON_CARDS" :key="n" class="s-skel-card" :class="sizeClass(sk.shape, sk.size)">
                <div class="s-skel-poster s-skeleton" :class="posterShapeClass(sk.shape)"></div>
                <div class="s-skel-line s-skeleton"></div>
                <div class="s-skel-line s-skel-line-sm s-skeleton"></div>
              </div>
            </div>
          </section>
        </template>
      </template>

      <!-- 行：首次滚入视口时整行淡入 + 卡片按序上浮（motion-v 变体，once 不重播）。
           reduced-motion 下 entranceInitial 直接是终态，等于完全不播。
           hero 也走同一个 v-for —— 它的位置由设置页的 homeSection* 槽位决定（铁律 12，勿动）。

           ⚠️ 这里的 prop 名必须是 `in-view-options`，**不是** `viewport`。
           motion-v 2.8.0 只声明了 `inViewOptions`（node_modules/motion-v/dist/es/components/motion/props.mjs:110），
           whileInView 特性读的也是 `state.options.inViewOptions`（.../features/gestures/in-view/index.mjs:20）。
           写成 `:viewport="..."` 时它是个**未声明 prop**，整个对象会落进 $attrs 渲染成 DOM 属性
           `viewport="[object Object]"`，而 `inViewOptions` 是 undefined → IO 退回默认 threshold:0、
           `once` 也拿不到。后果是行在**完全没进视口（ratio≈0）时就播完**，用户永远看不到入场动效，
           并且滚出会被动画回 hidden、滚回再重播一次。这个 bug 曾真实存在过，别再改回去。

           ⚠️ amount 也不能拍脑袋写小值。实测「卡片顶部相对整行的高度」：
             桌面 1440px：行 289/356px，卡片顶 49/63px → 需要 amount > 0.169 / 0.176
             移动  390px：行 256/301px，卡片顶 49/63px → 需要 amount > 0.191 / 0.208
           原先写的 0.15 **恰好卡在所有阈值之下**，于是触发时只有标题在屏幕里、
           卡片可见比例实测为 0 —— 交错动效全在视口外播完，用户根本看不到。
           取 0.3（比最大需求 0.208 高约 44%），触发时卡片已露出 15~44px。
           注意 framer 的 `amount` 只吃数字或 'some'/'all'，**不支持像素值**，
           所以无法写成「露出 60px」这种与行高无关的形式；行高变了要重新核这个数。 -->
      <template v-else>
        <template v-for="row in rows" :key="row.key">
          <HomeHero v-if="row.isHero" :items="heroItems" :loading="heroLoading" />
          <motion.section
            v-else
            class="s-row"
            :variants="rowVariants"
            :initial="entranceInitial"
            while-in-view="visible"
            :in-view-options="{ once: true, amount: 0.3 }"
          >
            <div class="s-row-head">
              <h2 class="s-row-title">{{ row.title }}</h2>
              <button v-if="targetFor(row)" type="button" class="s-row-all" @click="goRow(row)">
                {{ t('home.viewAll') }}
              </button>
            </div>
            <div class="s-row-rail">
              <button
                type="button"
                class="s-arrow s-arrow-back"
                :disabled="atStart(row.key)"
                :aria-label="t('home.scrollBack')"
                @click="scrollRow(row.key, -1)"
              >
                <svg class="s-arrow-icon" viewBox="0 0 24 24" aria-hidden="true">
                  <path d="M15 5 8 12l7 7" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
                </svg>
              </button>

              <!-- 卡片容器接 FLIP：数据变化时卡片滑动归位，不硬跳 -->
              <div
                :ref="(el) => setTrack(row.key, el)"
                class="s-row-track s-scroll-x s-fade-x"
                v-auto-animate="flipOptions"
                tabindex="0"
                role="group"
                :aria-label="row.title"
                @scroll.passive="onScroll(row.key, $event)"
              >
                <!-- 交错子项：只带 variants，靠父级 hidden→visible 驱动（同一处只用 motion-v 一种机制） -->
                <motion.div
                  v-for="item in row.items"
                  :key="item.Id"
                  class="s-row-item"
                  :variants="cardVariants"
                >
                  <EmbyCard :item="item" :shape="row.shape" :class="sizeClass(row.shape, row.size)" />
                </motion.div>
              </div>

              <button
                type="button"
                class="s-arrow s-arrow-fwd"
                :disabled="atEnd(row.key)"
                :aria-label="t('home.scrollForward')"
                @click="scrollRow(row.key, 1)"
              >
                <svg class="s-arrow-icon" viewBox="0 0 24 24" aria-hidden="true">
                  <path d="M9 5l7 7-7 7" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
                </svg>
              </button>
            </div>
          </motion.section>
        </template>
      </template>
    </div>
  </main>
</template>

<style scoped>
/* ---------- 页面容器：承接 Emby 的 .page 语义（绝对铺满 + 自身纵向滚动）---------- */
.s-home {
  position: absolute;
  inset: 0;
  overflow-x: hidden;
  overflow-y: auto;
  background: var(--s-bg);
  color: var(--s-text-1);
  font-family: var(--s-font);
  font-size: var(--s-fs-body);
  line-height: var(--s-lh-snug);
  -webkit-overflow-scrolling: touch;
}

.s-home-inner {
  position: relative; /* auto-animate 的 FLIP 会给移动中的行临时 position:absolute */
  display: flex;
  flex-direction: column;
  gap: var(--s-row-gap);
  max-width: var(--s-content-max);
  margin: 0 auto;
  padding-top: var(--s-sp-9);
  padding-bottom: var(--s-sp-11);
}

/* Hero 现在也走槽位循环（位置由设置页决定），但它仍需要"贴顶 + 通栏"：
   ① 作为首项时吃掉父级的顶部留白；② 在超过版心宽度的大屏上全出血到视口两侧。
   （子组件根节点会带上父组件的 scoped 标记，所以这里能选到 .hero。） */
.s-home-inner:has(> .hero:first-child) {
  padding-top: 0;
}
.s-home-inner > .hero {
  width: 100vw;
  max-width: 100vw;
  margin-inline: calc(50% - 50vw);
}

/* ---------- 行 ---------- */
/* 行的进场（淡入 + 卡片交错上浮）由 motion-v 变体驱动，这里不再写 opacity/transform，
   避免"CSS 过渡 + JS 变体"两套机制抢同一个 transform（铁律 14 / 任务约定：同一处只用一种） */

.s-row-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--s-sp-4);
  padding: 0 var(--s-gutter) var(--s-sp-4);
}

.s-row-title {
  margin: 0;
  font-size: var(--s-fs-h2);
  font-weight: var(--s-fw-semibold);
  letter-spacing: var(--s-tracking-tight);
  color: var(--s-text-1);
}

.s-row-all {
  flex: 0 0 auto;
  padding: 0;
  border: 0;
  background: none;
  font: inherit;
  font-size: var(--s-fs-sm);
  color: var(--s-text-3);
  cursor: pointer;
  transition: color var(--s-dur-fast) var(--s-ease-std);
}
.s-row-all:hover {
  color: var(--s-accent);
}

.s-row-rail {
  position: relative;
}

/* 上下留 8px：悬停抬升 -4px 不会被 overflow-y:hidden 裁掉 */
.s-row-track {
  position: relative; /* 同上：FLIP 期间子项会被临时绝对定位 */
  display: flex;
  gap: var(--s-sp-3);
  padding: var(--s-sp-2) var(--s-gutter);
  scroll-padding-inline: var(--s-gutter);
}

/* 交错子项：宽度仍由 EmbyCard 自己的尺寸类决定，这里只做 flex 项收口 */
.s-row-item {
  flex: 0 0 auto;
}

/* ---------- 卡片尺寸（EmbyCard 与骨架共用，保证 CLS≈0）---------- */
.s-row-card {
  width: 180px;
}
.s-row-card-wide {
  width: 300px;
}
.s-row-card-sm {
  width: 140px;
}
.s-row-card-wide-sm {
  width: 220px;
}

/* ---------- 悬停显现的左右箭头 ---------- */
.s-arrow {
  position: absolute;
  top: 50%;
  z-index: 2;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  padding: 0;
  border: 1px solid var(--s-line);
  border-radius: var(--s-r-pill);
  background: var(--s-surface);
  box-shadow: var(--s-shadow-card);
  color: var(--s-text-1);
  cursor: pointer;
  opacity: 0;
  transform: translateY(-50%);
  transition: opacity var(--s-dur-fast) var(--s-ease-std);
}
.s-arrow-back {
  left: calc(var(--s-gutter) / 2);
}
.s-arrow-fwd {
  right: calc(var(--s-gutter) / 2);
}
.s-arrow-icon {
  width: 20px;
  height: 20px;
}

/* 默认 opacity 0；行 hover 或键盘 focus-within 时显现。
   到边界（disabled）的箭头在同样时机只留 0.3 —— 不写无条件的 .s-arrow:disabled 规则，
   否则没悬停时两端箭头会一直常亮，"悬停显现"就名存实亡。 */
.s-row-rail:hover .s-arrow:not(:disabled),
.s-row-rail:focus-within .s-arrow:not(:disabled) {
  opacity: 1;
}
.s-row-rail:hover .s-arrow:disabled,
.s-row-rail:focus-within .s-arrow:disabled {
  opacity: 0.3;
}
.s-arrow:disabled {
  cursor: default;
}

/* ---------- 骨架 ---------- */
.s-skel-title {
  width: 168px;
  height: 20px;
  border-radius: var(--s-r-xs);
}

.s-skel-rail {
  display: flex;
  gap: var(--s-sp-3);
  padding: var(--s-sp-2) var(--s-gutter);
  overflow: hidden;
}

.s-skel-card {
  flex: 0 0 auto;
}

/* 镜像 EmbyCard 的 .cardBox 外边距 min(.74em, max(.38em, 1.06vw))：
   :root 字号 90.15% 时 .74em = 10.67px、.38em = 5.48px —— 用同一公式的 px 版，
   让骨架的海报块与真卡同宽同高（CLS≈0） */
.s-skel-poster {
  margin: min(10.67px, max(5.48px, 1.06vw));
  aspect-ratio: 2 / 3;
}
.s-skel-poster.is-wide {
  aspect-ratio: 16 / 9;
}

.s-skel-line {
  height: 12px;
  margin: var(--s-sp-2) var(--s-sp-3) 0;
  border-radius: var(--s-r-xs);
}
.s-skel-line-sm {
  width: 60%;
  height: 10px;
}

/* ---------- 错误 / 空态 ---------- */
.s-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--s-sp-4);
  min-height: 60vh;
  padding: var(--s-sp-11) var(--s-gutter);
  text-align: center;
}

.s-state-mark {
  width: 96px;
  height: 96px;
}

.s-state-title {
  margin: 0;
  font-size: var(--s-fs-h1);
  font-weight: var(--s-fw-semibold);
  color: var(--s-text-1);
}

.s-state-hint {
  margin: 0;
  max-width: 44ch;
  font-size: var(--s-fs-body);
  line-height: var(--s-lh-body);
  color: var(--s-text-2);
}

.s-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  height: 40px;
  padding: 0 var(--s-sp-6);
  border: 1px solid var(--s-line);
  border-radius: var(--s-r-pill);
  font: inherit;
  font-size: var(--s-fs-body);
  font-weight: var(--s-fw-medium);
  cursor: pointer;
  transition:
    background-color var(--s-dur-fast) var(--s-ease-std),
    color var(--s-dur-fast) var(--s-ease-std);
}
.s-btn-primary {
  border-color: transparent;
  background: var(--s-cta-bg);
  color: var(--s-cta-fg);
}
.s-btn-primary:hover {
  background: var(--s-cta-bg-hover);
}

/* ---------- 小屏 ---------- */
@media (max-width: 640px) {
  .s-home-inner {
    gap: var(--s-sp-7);
    padding-top: var(--s-sp-6);
  }
  .s-row-head,
  .s-row-track,
  .s-skel-rail {
    padding-inline: var(--s-gutter-sm);
  }
  .s-row-card {
    width: 140px;
  }
  .s-row-card-wide {
    width: 240px;
  }
  .s-row-card-sm {
    width: 120px;
  }
  .s-row-card-wide-sm {
    width: 180px;
  }
  .s-arrow-back {
    left: var(--s-sp-1);
  }
  .s-arrow-fwd {
    right: var(--s-sp-1);
  }
}

/* ---------- 减少动效 ----------
   JS 侧已在 entranceInitial / rowVariants / cardVariants / flipOptions 里全部短路
   （reduced=true 时初始即终态、staggerChildren=0、FLIP duration=0），
   这里不再需要单独的 transform 兜底 —— 位移根本没有被挂上。 */
</style>
