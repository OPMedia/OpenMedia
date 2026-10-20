<script setup lang="ts">
/**
 * 库浏览 —— 1:1 还原 Emby 4.9.5 items 页(基准=真实站运行时 dump):
 * view.withTabs.page-withFullDrawer > scrollSlider(padded-top-page padded-left padded-left-page padded-right)
 *   > .itemsViewSettingsContainer(共 N 项 + btnSort(标题+箭头)/btnFilter/btnViewSettings 胶囊)
 *   > .itemsContainer.vertical-wrap.padded-bottom-page > .card.portraitCard(cardBox-bottompadded)
 * 排序/筛选走 Emby 端点;过滤/视图设置按钮 v1 为视觉占位。
 */
import { ref, watch, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { BaseItemDto } from '@/api/types'
import { getItems, getGenres, getItem, cardArtUrl } from '@/api/endpoints'
import { useAuthStore } from '@/stores/auth'
import EmbyCard from '@/components/EmbyCard.vue'
import SSheet from '@/components/ui/SSheet.vue'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { flipConfig, useReducedMotionSafe } from '@/lib/motion'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const { t } = useI18n()

/** 列表 FLIP：JS 动画动手前必须先问 reduced-motion（铁律 16），reduce 时 duration=0 完全不位移 */
const reducedMotion = useReducedMotionSafe()
const listFlip = computed(() => flipConfig(reducedMotion.value))

/**
 * 面板级几何必须走 content-style，不能写在 <style scoped> 里：
 * SSheet 的模板根是 fragment（scrim + panel），父视图的 scoped data-v **落不到面板上**
 * （实测面板只有 SSheet 自己的 data-v-3843251c，没有视图的 data-v-7d745af8），
 * 所以视图里的 `.dialog {...}` 是死代码；content-style 走 :style 内联，天然最高优先级。
 *
 * ① center 变体是 inset:0 + margin:auto：对 width/height:auto 的弹层会把它拉伸成 92vw×84vh
 *    （实测 1327×758），给 fit-content 才是「按内容尺寸居中」（旧实现：筛选 877×535、视图设置 509×179）。
 * ② 排序 actionSheet 旧实现是水平居中（1440 视口下 x=651、宽 137），而 pop 变体是右上角（x=1289）；
 *    左右 inset 归零 + margin-inline:auto 拉回居中，纵向 top:58px 与旧 4em 等价、不动。
 */
const CENTER_PANEL_STYLE = { width: 'fit-content', height: 'fit-content' }
const SORT_PANEL_STYLE = {
  insetInlineStart: '0',
  insetInlineEnd: '0',
  marginInline: 'auto',
  width: 'fit-content',
  height: 'fit-content',
  '--s-pop-origin': 'top center',
}

const PAGE_SIZE = 60

const loading = ref(true)
const items = ref<BaseItemDto[]>([])
/** 非"节目"tab 的内容(集/合集/文件夹/预告) */
const tabItems = ref<BaseItemDto[]>([])
const total = ref(0)
const lib = ref<BaseItemDto | null>(null)
const genresList = ref<{ Name: string; Id: string }[]>([])

/** 当前 tab 键(节目 = '') */
const currentTab = computed(() => (typeof route.query.tab === 'string' ? route.query.tab : ''))
/** 类型/标签/播出平台 tab:chip 网格(真实站 /tv 页同款) */
const CHIP_TABS: Record<string, { title: string; source: 'genres' | 'studios' | 'tags' }> = {
  genres: { title: '类型', source: 'genres' },
  studios: { title: '播出平台', source: 'studios' },
  tags: { title: '标签', source: 'tags' },
}
const chipTab = computed(() => CHIP_TABS[currentTab.value] ?? null)
/** 走 tabItems 容器(横向卡列表)的 tab;推荐/收藏仍走常规网格 */
const TAB_ITEM_TABS = ['episodes', 'collections', 'folders', 'trailers']
const usesTabItems = computed(() => TAB_ITEM_TABS.includes(currentTab.value))
const chipItems = computed<{ Id: string; Name: string }[]>(() => {
  const cfg = chipTab.value
  if (!cfg) return []
  if (cfg.source === 'genres') return genresList.value.map((g) => ({ Id: g.Id, Name: g.Name }))
  const set = new Set<string>()
  items.value.forEach((i) => {
    if (cfg.source === 'studios') i.Studios?.forEach((s) => set.add(s.Name))
    else i.Tags?.forEach((tg) => set.add(tg))
  })
  return [...set].map((n, i) => ({ Id: `${cfg.source}-${i}`, Name: n }))
})

// 排序(真实站 btnSort 显示当前排序名+方向箭头)
type SortKey = 'SortName' | 'DateCreated' | 'CommunityRating' | 'ProductionYear' | 'DatePlayed' | 'Random'
const sortKey = ref<SortKey>('SortName')
const sortDesc = ref(false)
const SORT_LABELS: Record<SortKey, string> = {
  SortName: '标题', DateCreated: '加入日期', CommunityRating: '社区评分',
  ProductionYear: '发行年份', DatePlayed: '播放日期', Random: '随机',
}
const SORT_ICON = { asc: '\uE5D8', desc: '\uE5DB' } // arrow_upward / arrow_downward

async function load() {
  loading.value = true
  try {
    const userId = auth.user!.Id
    const libId = String(route.params.id)
    if (!lib.value || lib.value.Id !== libId) {
      lib.value = await getItem(userId, libId)
      if (genresList.value.length === 0) {
        genresList.value = (await getGenres(userId, libId)).Items
      }
    }
    const isTv = lib.value?.CollectionType === 'tvshows'
    const tab = typeof route.query.tab === 'string' ? route.query.tab : undefined

    // ---- 非"节目"tab 的内容分支(真实站 /tv 页各 tab 独立内容) ----
    if (tab === 'episodes') {
      // "集"tab:递归列出全部剧集(真实站为横向 portraitCard-horiz 列表)
      //
      // ⚠️ `Filters: 'IsNotFolder'` 看着多余,其实是**让这个页签真的返回 Episode 的唯一开关**。
      // 真机实测(2026-10-20,直连 tv.wenjian.de,13 个剧集库逐一验过;脚本
      // scratch/lead2/probe-lib-types.mjs、probe-episode-forms.mjs,本文件对应复现:
      // scratch/lead2/datafix/recon17-episode-filters.mjs):
      //   · 现状 `IncludeItemTypes=Episode` + 无 Filters/无 SortBy → 返回 `{"Series":100}`,
      //     和**完全不传** IncludeItemTypes 一模一样(日番 total 1464 / 国产剧 4649 / 国漫 530 —— 全是 Series 数);
      //   · 加 `SortBy=SortName` / `DateCreated` / `SeriesSortName`、加 `IsMissing=false` → 仍是 Series;
      //   · 加 `Filters=IsNotFolder` → 返回 `{"Episode":100}`,total 变成
      //     日番 50068 / 国产剧 166810 / 国漫 37900;
      //   · `Filters=IsUnplayed` 或 `SortBy=DatePlayed` 也能拿到 Episode,但它们会改变语义,不能用。
      // 即:这台服务器**只在查询被强制到条目级枚举时才认 `IncludeItemTypes=Episode`**。
      // 对 Episode 而言 `IsNotFolder` 是恒真条件(剧集不是文件夹),所以无副作用。
      // 不加它的后果:页签叫"集",列出来的却是剧(Series)—— 用户一眼可见的功能性缺陷。
      // 修好后这些 Episode 大多没有自己的 Primary,由 `cardArtUrl` 回退到
      // `SeriesPrimaryImageTag`(实测返回的 100 条里 100 条都有该 tag)→ 卡片不会是渐变占位。
      //
      // 【已知且明确不修】有两个库即使加了 `IsNotFolder` 仍然列 Series:
      //   稳友荐剧(2000000002)、2026十月番(2000000007)。
      // 剧集本身是存在的 —— 直连真机实测:`稳友荐剧` 的《一斩苍穹》(Series 1310342)
      // 有 **14 集**(`/Shows/1310342/Episodes` → 200,total = 14);`2026十月番` 的《FX战士久留美》有 3 集。
      // 症结在这两个(插件/虚拟)库的**库级递归枚举不下沉**:`Recursive=true` 与 `false`
      // 返回同一批 52 / 70 个 Series,所以 `ParentId=库` 的 Episode 查询必然拿不到条目。
      // 这是**服务端枚举限制,不是本页的 bug**;客户端唯一解法是"逐个 Series 拉
      // `/Shows/{id}/Episodes` 再聚合"(这两个库要多发 52~100 条请求),**明确不做**。
      // 后人不要再把它当 bug 反复修。
      const r = await getItems(userId, {
        ParentId: libId, Recursive: true, IncludeItemTypes: 'Episode', Filters: 'IsNotFolder',
        SortBy: 'SeriesSortName,SortName', SortOrder: 'Ascending', Limit: 200,
        Fields: 'PrimaryImageAspectRatio,ProductionYear',
      })
      tabItems.value = r.Items
      items.value = []
      total.value = r.TotalRecordCount
      return
    }
    if (tab === 'collections' || tab === 'folders' || tab === 'trailers') {
      // 合集/文件夹/预告:按对应类型查询(真实站为独立容器;mock 无数据时给空态)
      const types = tab === 'collections' ? 'BoxSet' : tab === 'folders' ? 'Folder,CollectionFolder' : 'Trailer'
      const r = await getItems(userId, { ParentId: libId, Recursive: true, IncludeItemTypes: types, Limit: 100 })
      tabItems.value = r.Items
      items.value = []
      total.value = r.TotalRecordCount
      return
    }
    tabItems.value = []
    // chip 类 tab 需要完整条目集来生成工作室/标签。
    //
    // ⚠️ 必须显式要 `Fields`：Emby 的**默认返回集不含** `Studios`/`Tags`，
    // 不传就是"数据一旦存在，这两个 tab 还是空的"的潜伏缺陷（实测 2026-10-20：
    // 不传 Fields 时 100 条里 `'Studios' in item === false`、带 Studios 的 0 条）。
    // 但本服务器实测**即使显式 `Fields=Studios,Tags` 也拿不到这两个键**
    // （hasStudiosKey = 0 / hasTagsKey = 0）；对照实验 `Fields=People` 同样拿不到 `People`，
    // 即这台服务器整体**忽略 Fields 参数**，只返回一套固定字段集。
    // → 所以 studios/tags 两个 tab 当前为空是**数据/服务端字段侧**的原因，
    //   前端这层只保证"不再因为少要字段而空"。
    if (currentTab.value === 'studios' || currentTab.value === 'tags') {
      items.value = (await getItems(userId, {
        ParentId: libId, Recursive: true, IncludeItemTypes: isTv ? 'Series' : 'Movie',
        Limit: 200, Fields: 'Studios,Tags,PrimaryImageAspectRatio,ProductionYear',
      })).Items
    }
    // 类型/工作室筛选(详情页链接 → ?genre=/?studio=;真实站 list 页筛选同款)
    const genre = typeof route.query.genre === 'string' ? route.query.genre : undefined
    const studio = typeof route.query.studio === 'string' ? route.query.studio : undefined
    // 分节参数(真实站:sectionId=resume → Filters=IsResumable;tab=suggestions → 按加入日期倒序;
    // tab=favorites → Filters=IsFavorite)
    const sectionId = typeof route.query.sectionId === 'string' ? route.query.sectionId : undefined
    const sectionFilter = sectionId === 'resume'
      ? 'IsResumable,IsUnplayed'
      : (tab === 'favorites' ? 'IsFavorite' : undefined)
    const tabSort = tab === 'suggestions' ? 'DateCreated' : undefined
    const r = await getItems(userId, {
      ParentId: libId,
      Recursive: true,
      IncludeItemTypes: isTv ? 'Series' : 'Movie',
      SortBy: tabSort ?? sortKey.value,
      SortOrder: tabSort ? 'Descending' : (sortDesc.value ? 'Descending' : 'Ascending'),
      StartIndex: (page.value - 1) * PAGE_SIZE,
      Limit: PAGE_SIZE,
      Fields: 'PrimaryImageAspectRatio,ProductionYear',
      ...(sectionFilter ? { Filters: sectionFilter } : {}),
      ...(genre ? { Genres: genre } : {}),
      ...(studio ? { Studios: studio } : {}),
      ...filterQuery(),
    })
    items.value = r.Items
    total.value = r.TotalRecordCount
  } catch {
    items.value = []
    total.value = 0
  } finally {
    loading.value = false
  }
}

/** 排序按钮 → actionSheet(真实站:标题"排序："+ 排序项 + 当前项勾选;再选同项切换方向) */
const SORT_OPTIONS: { key: SortKey; label: string }[] = [
  { key: 'SortName', label: '标题' },
  { key: 'DateCreated', label: '加入日期' },
  { key: 'DatePlayed', label: '播放日期' },
  { key: 'CommunityRating', label: '社区评分' },
  { key: 'ProductionYear', label: '年份' },
  { key: 'Random', label: '随机' },
]
const sortMenuOpen = ref(false)
const sortMenuItems = computed(() => SORT_OPTIONS.map((o) => ({
  id: o.key,
  icon: o.key === sortKey.value ? '\uE5CA' : '',
  label: o.label,
  selected: o.key === sortKey.value,
})))
function pickSort(item: { id?: string }) {
  sortMenuOpen.value = false
  const key = item.id as SortKey
  if (key === sortKey.value) sortDesc.value = !sortDesc.value
  else { sortKey.value = key; sortDesc.value = false }
}

/** 筛选 → formDialog(真实站 .filterMenuDialog:播放状态/收藏/类型/年份/工作室) */
const filterOpen = ref(false)
const filterPlayed = ref<'all' | 'played' | 'unplayed' | 'resumable'>('all')
const filterFavorite = ref(false)
const filterGenres = ref<string[]>([])
const filterYears = ref<string[]>([])
const filterStudios = ref<string[]>([])
const yearsList = computed(() => {
  const ys = new Set<string>()
  items.value.forEach((i) => { if (i.ProductionYear) ys.add(String(i.ProductionYear)) })
  return [...ys].sort((a, b) => Number(b) - Number(a)).slice(0, 12)
})
const studiosList = computed(() => {
  const ss = new Set<string>()
  items.value.forEach((i) => i.Studios?.forEach((s) => ss.add(s.Name)))
  return [...ss].slice(0, 12)
})
const hasFilter = computed(() => filterPlayed.value !== 'all' || filterFavorite.value || filterGenres.value.length > 0 || filterYears.value.length > 0 || filterStudios.value.length > 0)
function clearFilter() {
  filterPlayed.value = 'all'
  filterFavorite.value = false
  filterGenres.value = []
  filterYears.value = []
  filterStudios.value = []
}
function toggleIn(list: string[], v: string) {
  const i = list.indexOf(v)
  if (i >= 0) list.splice(i, 1)
  else list.push(v)
}
function applyFilter() {
  filterOpen.value = false
  page.value = 1
  void load()
}

/** 视图设置 → dialog(真实站:视图 + 图像大小 + 完成) */
const viewSettingsOpen = ref(false)
const viewMode = ref<'grid' | 'list'>('grid')
const imageSize = ref(0) // -1 小 / 0 中 / 1 大(真实站滑条三档)

const pageCount = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)))
const page = ref(1)

watch([sortKey, sortDesc], () => { page.value = 1; void load() })
watch(() => route.params.id, () => { lib.value = null; page.value = 1; clearFilter(); void load() })
// query 变化(genre/studio/sectionId/tab)必须重载 —— 之前只监听 params,导致箭头跳转后列表不刷新
watch(() => JSON.stringify(route.query), () => { page.value = 1; void load() })
watch(page, () => void load())
onMounted(() => void load())

const ICON_SORT = '\uE164'
const ICON_FILTER = '\uE152'
const ICON_SETTINGS = '\uE5D3'
const ICON_MORE = '\uE5D3'

const PLAYED_OPTIONS = [
  { value: 'all' as const, label: '全部' },
  { value: 'unplayed' as const, label: '未播放' },
  { value: 'played' as const, label: '已播放' },
  { value: 'resumable' as const, label: '继续观看' },
]

/** 筛选参数拼接(真实站 filterMenuDialog → 查询参数) */
function filterQuery(): Record<string, string> {
  const q: Record<string, string> = {}
  if (filterPlayed.value === 'played') q.Filters = 'IsPlayed'
  else if (filterPlayed.value === 'unplayed') q.Filters = 'IsUnplayed'
  else if (filterPlayed.value === 'resumable') q.Filters = 'IsResumable'
  if (filterFavorite.value) q.Filters = q.Filters ? `${q.Filters},IsFavorite` : 'IsFavorite'
  if (filterGenres.value.length) q.Genres = filterGenres.value.join('|')
  if (filterStudios.value.length) q.Studios = filterStudios.value.join('|')
  if (filterYears.value.length) q.Years = filterYears.value.join(',')
  return q
}

/**
 * 列表行缩略图（2:3 竖版）。
 *
 * 原实现无条件拼 `imageUrl(item.Id, 'Primary', item.ImageTags?.Primary)` —— tag 是 undefined 也照拼，
 * 而真机 Emby 4.9.5.0 对「没有 Primary 图」的条目 `/Items/{id}/Images/Primary` 返回 **HTTP 500**
 * （不是 404；全站 13739 条真实条目里 1931 条没有 Primary）。
 * 改走 `cardArtUrl`：没有 tag 就返回 undefined，Vue 会把 src 属性整个去掉 ——
 * 浏览器不发任何请求，也不会画一个碎图图标。
 */
function rowArt(item: BaseItemDto): string | undefined {
  return cardArtUrl(item, 'portrait', { maxWidth: 120, maxHeight: 180, quality: 90 }) || undefined
}
</script>

<template>
  <main class="view flex flex-direction-column withTabs page page-withFullDrawer page-withDockedDrawer focuscontainer-x">
    <div class="scrollFrameY flex flex-grow tabContent tabContent-positioned itemsTab emby-scroller scrollY overflowYScroll flex-direction-column is-active focuscontainer-y navout-up">
      <div class="scrollSlider flex-grow padded-top-page padded-left padded-left-page padded-right scrollSliderY focuscontainer-y navout-up">

        <!-- 工具行(真实站 itemsViewSettingsContainer) -->
        <div class="flex align-items-center focuscontainer-x itemsViewSettingsContainer padded-bottom">
          <div class="listTotalRecordCount">{{ t('library.total', { count: total }) }}</div>
          <button type="button" is="emby-button" class="itemsViewSettingsContainer-button btnSort s-press raised raised-mini listTextButton-autohide emby-button emby-button-focusscale" title="排序" @click="sortMenuOpen = true">
            <i class="md-icon button-icon button-icon-left autortl">{{ ICON_SORT }}</i>
            <span class="emby-button-text btnSortText">{{ SORT_LABELS[sortKey] ?? '标题' }}</span>
            <i class="md-icon btnSortIcon button-icon button-icon-right">{{ sortDesc ? SORT_ICON.desc : SORT_ICON.asc }}</i>
          </button>
          <button type="button" is="emby-button" class="itemsViewSettingsContainer-button btnFilter s-press fab fab-mini listTextButton-autohide emby-button emby-button-focusscale" title="过滤器" @click="filterOpen = true">
            <i class="md-icon button-icon">{{ ICON_FILTER }}</i>
          </button>
          <button type="button" is="emby-button" class="itemsViewSettingsContainer-button btnViewSettings s-press fab fab-mini listTextButton-autohide emby-button emby-button-focusscale" title="设置" @click="viewSettingsOpen = true">
            <i class="md-icon button-icon">{{ ICON_SETTINGS }}</i>
          </button>
        </div>

        <!-- 网格 / 列表(视图设置切换)
             注:真实站 itemsContainer 带 virtualItemsContainer/virtual-scroller 类,但那套布局由
             其 JS 虚拟滚动引擎逐项写入 inline 尺寸;静态套用会塌成单列,故此处只保留基础容器类
             动效:增删/重排走 auto-animate FLIP(铁律 16:先问 reduced-motion,reduce 时 duration=0) -->
        <div v-if="loading" class="page-loading">{{ t('common.loading') }}</div>

        <!-- 类型 / 标签 / 播出平台 tab:chip 网格(真实站 /tv 页同款) -->
        <div v-else-if="chipTab" class="verticalSection">
          <div class="sectionTitleContainer sectionTitleContainer-cards padded-bottom">
            <h2 class="sectionTitle sectionTitle-cards">{{ chipTab.title }}</h2>
          </div>
          <div v-if="!chipItems.length" class="page-loading">{{ t('library.empty') }}</div>
          <div v-else class="filterChips padded-bottom-page">
            <button
              v-for="c in chipItems" :key="c.Id" type="button"
              class="filterChip s-press raised raised-mini emby-button emby-button-focusscale"
              @click="chipTab.source === 'genres'
                ? router.push({ name: 'library', params: { id: String(route.params.id) }, query: { genre: c.Name } })
                : router.push({ name: 'library', params: { id: String(route.params.id) }, query: { studio: c.Name } })"
            >{{ c.Name }}</button>
          </div>
        </div>

        <!-- 集 / 合集 / 文件夹 / 预告 tab:横向卡片列表(仅这几类走 tabItems;推荐/收藏仍走下方网格) -->
        <div v-else-if="usesTabItems" class="verticalSection">
          <div v-if="!tabItems.length" class="page-loading">{{ t('library.empty') }}</div>
          <div v-else class="emby-scroller padded-top-focusscale padded-bottom-focusscale scrollX hiddenScrollX scrollFrameX flex-direction-row">
            <div class="scrollSlider focusable itemsContainer focuscontainer-x scrollSliderX">
              <!-- 形态必须是 Emby CSS 里**真实存在**的 padder 类。
                   原来 episodes 传 'portrait-horiz' → EmbyCard 拼出 `cardPadder-horiz cardPadder-portrait-horiz`，
                   而这两个类在 assets/emby495/modules__cardbuilder__card.css 里**没有任何定义**
                   （@supports 块只定义了 backdrop / square / portrait / fourThree / banner，
                   参考实现 cardbuilder.js 也只是拼 `cardPadder-{shape}`），
                   于是图容器塌成一条线：实测 `.cardPadder-portrait-horiz` = **165×1 px**
                   （同一页网格视图的 `.cardPadder-portrait` = 135×202 px），标题可见、图不可见。
                   剧集图源本身就是 16:9（自己的 Primary 缩略图，或父级 series backdrop；
                   实测这三个库 allNoArt = 0），所以用真实存在的 backdrop(16:9) padder。 -->
              <EmbyCard v-for="it in tabItems" :key="it.Id" :item="it" shape="backdrop" />
            </div>
          </div>
        </div>

        <div v-else-if="!items.length" class="page-loading">{{ t('library.empty') }}</div>
        <div
          v-else-if="viewMode === 'grid'"
          v-auto-animate="listFlip"
          class="itemsContainer vertical-wrap padded-bottom-page focusable focuscontainer-x focuscontainer-y navout-up navout-x"
          data-virtualscrolllayout="vertical-grid"
        >
          <EmbyCard v-for="item in items" :key="item.Id" :item="item" shape="portrait" />
        </div>
        <div
          v-else
          v-auto-animate="listFlip"
          class="itemsContainer vertical-list padded-bottom-page focusable focuscontainer-x focuscontainer-y navout-up navout-x"
          data-virtualscrolllayout="vertical"
        >
          <div
            v-for="item in items" :key="item.Id"
            class="listItem s-lift listItem-autoactive itemAction listItemCursor listItem-hoverable"
            @click="router.push({ name: 'item', params: { id: item.Id } })"
          >
            <div class="listItem-content listItem-content-margin listItem-content-bg listItemContent-touchzoom listItem-border listItem-border-offset listItem-border-offset-portrait">
              <div class="listItemImageContainer listItemImageContainer-portrait">
                <img draggable="false" class="listItemImage listItemImage-portrait coveredImage coveredImage-noScale" :src="rowArt(item)" loading="lazy" decoding="async" />
              </div>
              <div class="listItemBody itemAction listItemBody-2-lines">
                <div class="listItemBodyText listItemBodyText-lf">{{ item.Name }}</div>
                <div class="listItemBodyText listItemBodyText-lf listItemBodyText-secondary secondaryText">
                  {{ item.ProductionYear ?? '' }}<template v-if="item.CommunityRating"> · ★{{ item.CommunityRating.toFixed(1) }}</template>
                </div>
              </div>
              <button type="button" title="更多" class="listItemButton listItemContextMenuButton itemAction md-icon paper-icon-button-light emby-button-focusscale" @click.stop>
                <i class="md-icon">{{ ICON_MORE }}</i>
              </button>
            </div>
          </div>
        </div>

        <div v-if="pageCount > 1 && !loading" class="pager">
          <button class="s-press" :disabled="page <= 1" @click="page--">‹</button>
          <span class="cur">{{ page }} / {{ pageCount }}</span>
          <button class="s-press" :disabled="page >= pageCount" @click="page++">›</button>
        </div>
      </div>
    </div>

    <!-- 排序(actionSheet;真实站标题"排序："+ 当前项勾选)
         行为(焦点陷阱/Esc/点外关闭/滚动锁定/进出动效)全部由 SSheet 提供,这里只管长相 -->
    <SSheet
      :open="sortMenuOpen"
      variant="pop"
      :title="t('library.sortTitle')"
      panel-class="dialog actionSheet sortSheet"
      :content-style="SORT_PANEL_STYLE"
      @close="sortMenuOpen = false"
    >
          <p class="actionSheetTitle actionSheetTitle-hideNonFullscreen secondaryText actionSheetTitle-small">{{ t('library.sortTitle') }}</p>
          <div class="actionSheetScroller focuscontainer-x actionSheetScroller-withheader actionSheetScroller-padding-inline emby-scroller dialog-scrollY scrollY scrollY-mini scrollFrameY flex-direction-column">
            <div class="actionsheetScrollSlider scrollSlider flex flex-direction-column itemsContainer actionSheetScroller-padding-top actionSheetScroller-padding-bottom vertical-list scrollSliderY">
              <button
                v-for="s in sortMenuItems" :key="s.id" type="button"
                class="listItem listItem-autoactive itemAction listItemCursor listItem-hoverable actionSheetMenuItem"
                @click="pickSort(s)"
              >
                <div class="listItem-content listItem-content-bg listItemContent-touchzoom listItem-border actionsheet-noborder">
                  <div v-if="s.selected" class="actionSheetItemImageContainer actionSheetItemImageContainer-transparent listItemImageContainer listItemImageContainer-margin listItemImageContainer-square">
                    <i class="actionsheetMenuItemIcon listItemIcon md-icon autortl">&#xE5CA;</i>
                  </div>
                  <div class="listItemBody actionsheetListItemBody listItemBody-1-lines">
                    <div class="listItemBodyText actionSheetItemText listItemBodyText-nowrap listItemBodyText-lf">{{ s.label }}</div>
                  </div>
                </div>
              </button>
            </div>
          </div>
    </SSheet>

    <!-- 筛选(formDialog .filterMenuDialog;真实站分组:播放状态/收藏/类型/年份/工作室) -->
    <SSheet
      :open="filterOpen"
      variant="center"
      :title="t('library.filterTitle')"
      panel-class="dialog formDialog filterMenuDialog"
      :content-style="CENTER_PANEL_STYLE"
      @close="filterOpen = false"
    >
          <div class="formDialogHeader">
            <button type="button" class="dialogCloseButton s-press hide-mouse-idle-tv dialogHeaderButton paper-icon-button-light emby-button-focusscale" tabindex="-1" title="关闭" @click="filterOpen = false">
              <i class="md-icon">&#xE5CD;</i>
            </button>
            <h3 class="formDialogHeaderTitle">{{ t('library.filterTitle') }}</h3>
            <button v-if="hasFilter" type="button" class="button-link s-press filterMenuClearButton emby-button emby-button-focusscale" @click="clearFilter">{{ t('library.clearFilter') }}</button>
          </div>
          <div class="formDialogContent scrollY">
            <div class="filterGroup">
              <h4 class="sectionTitle sectionTitle-cards">{{ t('library.filterPlayed') }}</h4>
              <label v-for="opt in PLAYED_OPTIONS" :key="opt.value" class="emby-radio-label filterRow">
                <input type="radio" class="emby-radio" :value="opt.value" :checked="filterPlayed === opt.value" @change="filterPlayed = opt.value" />
                <span>{{ opt.label }}</span>
              </label>
            </div>
            <div class="filterGroup">
              <h4 class="sectionTitle sectionTitle-cards">{{ t('library.filterFavorite') }}</h4>
              <label class="emby-toggle-label filterRow">
                <input v-model="filterFavorite" type="checkbox" class="emby-toggle" role="switch" />
                <span>{{ t('library.onlyFavorites') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
            </div>
            <div v-if="genresList.length" class="filterGroup">
              <h4 class="sectionTitle sectionTitle-cards">{{ t('library.genres') }}</h4>
              <div class="filterChips">
                <button
                  v-for="g in genresList.slice(0, 24)" :key="g.Id" type="button"
                  class="filterChip s-press raised raised-mini emby-button emby-button-focusscale"
                  :class="{ 'filterChip-on': filterGenres.includes(g.Name) }"
                  @click="toggleIn(filterGenres, g.Name)"
                >{{ g.Name }}</button>
              </div>
            </div>
            <div v-if="studiosList.length" class="filterGroup">
              <h4 class="sectionTitle sectionTitle-cards">{{ t('library.studios') }}</h4>
              <div class="filterChips">
                <button
                  v-for="s in studiosList" :key="s" type="button"
                  class="filterChip s-press raised raised-mini emby-button emby-button-focusscale"
                  :class="{ 'filterChip-on': filterStudios.includes(s) }"
                  @click="toggleIn(filterStudios, s)"
                >{{ s }}</button>
              </div>
            </div>
            <div v-if="yearsList.length" class="filterGroup">
              <h4 class="sectionTitle sectionTitle-cards">{{ t('library.years') }}</h4>
              <div class="filterChips">
                <button
                  v-for="y in yearsList" :key="y" type="button"
                  class="filterChip s-press raised raised-mini emby-button emby-button-focusscale"
                  :class="{ 'filterChip-on': filterYears.includes(y) }"
                  @click="toggleIn(filterYears, y)"
                >{{ y }}</button>
              </div>
            </div>
          </div>
          <div class="formDialogFooter">
            <button type="button" class="raised button-submit s-press formDialogFooterItem emby-button emby-button-focusscale" @click="applyFilter">{{ t('library.done') }}</button>
          </div>
    </SSheet>

    <!-- 视图设置(dialog;真实站:视图 + 图像大小 + 完成) -->
    <SSheet
      :open="viewSettingsOpen"
      variant="center"
      :title="t('library.viewSettings')"
      panel-class="dialog formDialog"
      :content-style="CENTER_PANEL_STYLE"
      @close="viewSettingsOpen = false"
    >
          <div class="formDialogHeader">
            <button type="button" class="dialogCloseButton s-press hide-mouse-idle-tv dialogHeaderButton paper-icon-button-light emby-button-focusscale" tabindex="-1" title="关闭" @click="viewSettingsOpen = false">
              <i class="md-icon">&#xE5CD;</i>
            </button>
            <h3 class="formDialogHeaderTitle">{{ t('library.viewSettings') }}</h3>
          </div>
          <div class="formDialogContent">
            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('library.view') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="viewMode" class="emby-select emby-select-focusscale">
                    <option value="grid">{{ t('library.viewGrid') }}</option>
                    <option value="list">{{ t('library.viewList') }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>
            <div class="fieldset-field">
              <div class="selectLabelText">{{ t('library.imageSize') }}</div>
              <input v-model.number="imageSize" type="range" min="-1" max="1" step="1" class="emby-slider" />
            </div>
          </div>
          <div class="formDialogFooter">
            <button type="button" class="raised button-submit s-press formDialogFooterItem emby-button emby-button-focusscale" @click="viewSettingsOpen = false">{{ t('library.done') }}</button>
          </div>
    </SSheet>
  </main>
</template>

<style scoped>
/* 分页(虚拟滚动 v1 缺位,先以分页兜底;胶囊版式贴合 4.9.5,取色改走 tokens——原 rgba(62,62,62,.7)
   是 Emby 深色主题抄死的值,浅色下深灰块突兀、.cur 的白 60% 直接看不见) */
.pager { display: flex; justify-content: center; align-items: center; gap: 12px; padding: 1em 0 2em; }
.pager button {
  padding: .66em 2ch; background: var(--s-raised); border: 1px solid var(--s-line);
  border-radius: 100em; color: var(--s-text-1); cursor: pointer; font-size: 110%;
}
.pager button:disabled { opacity: .4; cursor: default; }
.pager .cur { color: var(--s-text-2); font-size: 14px; }

/* 弹层的遮罩/底色/圆角/层级/进出动效已统一交给 SSheet(tokens + motion.css 取类名)，
   本地只保留原 .dialogScrim 规则里属于「长相」的部分：formDialog 内容区的内距与滚动。
   注意：面板本身（.dialog）的几何不能写在这里——它是 SSheet 渲染的 fragment 根，
   拿不到本视图的 scoped data-v，面板级样式请走 :content-style（见 script 里的常量说明） */
.dialog .formDialogContent { padding: 0 1.5em 1em; overflow-y: auto; }

/* 排序 actionSheet 的横向居中：已改为 :content-style（见 script 里 SORT_PANEL_STYLE 的说明）——
   面板拿不到本视图的 scoped data-v，写在这里是死代码 */

.filterGroup { margin-bottom: 1.2em; }
.filterRow { display: flex; align-items: center; gap: .6em; padding: .35em 0; }
.filterChips { display: flex; flex-wrap: wrap; gap: .5em; }
/* 未选中 = 抬升面 + 发丝边(原「白 12% 叠浅底」会与底色糊成一片,只剩文字飘着)；
   选中 = 珊瑚底 + 墨字(5.42:1,达 WCAG AA；原珊瑚 + 白字只有 3.01:1 不达标) */
.filterChip { padding: .4em 1em; background: var(--s-hover); border: 1px solid var(--s-line); border-radius: 100em; color: var(--s-text-1); cursor: pointer; font-size: 95%; }
.filterChip-on { background: var(--s-accent); color: var(--s-accent-ink); border-color: transparent; }
.filterChip-on:hover { background: var(--s-accent-hi); }
</style>