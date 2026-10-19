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
import { getItems, getGenres, getItem, imageUrl } from '@/api/endpoints'
import { useAuthStore } from '@/stores/auth'
import EmbyCard from '@/components/EmbyCard.vue'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const { t } = useI18n()

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
      const r = await getItems(userId, {
        ParentId: libId, Recursive: true, IncludeItemTypes: 'Episode',
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
    // chip 类 tab 需要完整条目集来生成工作室/标签
    if (currentTab.value === 'studios' || currentTab.value === 'tags') {
      items.value = (await getItems(userId, { ParentId: libId, Recursive: true, IncludeItemTypes: isTv ? 'Series' : 'Movie', Limit: 200 })).Items
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
</script>

<template>
  <main class="view flex flex-direction-column withTabs page page-withFullDrawer page-withDockedDrawer focuscontainer-x">
    <div class="scrollFrameY flex flex-grow tabContent tabContent-positioned itemsTab emby-scroller scrollY overflowYScroll flex-direction-column is-active focuscontainer-y navout-up">
      <div class="scrollSlider flex-grow padded-top-page padded-left padded-left-page padded-right scrollSliderY focuscontainer-y navout-up">

        <!-- 工具行(真实站 itemsViewSettingsContainer) -->
        <div class="flex align-items-center focuscontainer-x itemsViewSettingsContainer padded-bottom">
          <div class="listTotalRecordCount">{{ t('library.total', { count: total }) }}</div>
          <button type="button" is="emby-button" class="itemsViewSettingsContainer-button btnSort raised raised-mini listTextButton-autohide emby-button emby-button-focusscale" title="排序" @click="sortMenuOpen = true">
            <i class="md-icon button-icon button-icon-left autortl">{{ ICON_SORT }}</i>
            <span class="emby-button-text btnSortText">{{ SORT_LABELS[sortKey] ?? '标题' }}</span>
            <i class="md-icon btnSortIcon button-icon button-icon-right">{{ sortDesc ? SORT_ICON.desc : SORT_ICON.asc }}</i>
          </button>
          <button type="button" is="emby-button" class="itemsViewSettingsContainer-button btnFilter fab fab-mini listTextButton-autohide emby-button emby-button-focusscale" title="过滤器" @click="filterOpen = true">
            <i class="md-icon button-icon">{{ ICON_FILTER }}</i>
          </button>
          <button type="button" is="emby-button" class="itemsViewSettingsContainer-button btnViewSettings fab fab-mini listTextButton-autohide emby-button emby-button-focusscale" title="设置" @click="viewSettingsOpen = true">
            <i class="md-icon button-icon">{{ ICON_SETTINGS }}</i>
          </button>
        </div>

        <!-- 网格 / 列表(视图设置切换)
             注:真实站 itemsContainer 带 virtualItemsContainer/virtual-scroller 类,但那套布局由
             其 JS 虚拟滚动引擎逐项写入 inline 尺寸;静态套用会塌成单列,故此处只保留基础容器类 -->
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
              class="filterChip raised raised-mini emby-button emby-button-focusscale"
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
              <EmbyCard v-for="it in tabItems" :key="it.Id" :item="it" :shape="currentTab === 'episodes' ? 'portrait-horiz' : 'backdrop'" />
            </div>
          </div>
        </div>

        <div v-else-if="!items.length" class="page-loading">{{ t('library.empty') }}</div>
        <div
          v-else-if="viewMode === 'grid'"
          class="itemsContainer vertical-wrap padded-bottom-page focusable focuscontainer-x focuscontainer-y navout-up navout-x"
          layout="vertical-grid" data-virtualscrolllayout="vertical-grid"
        >
          <EmbyCard v-for="item in items" :key="item.Id" :item="item" shape="portrait" />
        </div>
        <div
          v-else
          class="itemsContainer vertical-list padded-bottom-page focusable focuscontainer-x focuscontainer-y navout-up navout-x"
          layout="vertical" data-virtualscrolllayout="vertical"
        >
          <div
            v-for="item in items" :key="item.Id"
            class="listItem listItem-autoactive itemAction listItemCursor listItem-hoverable"
            @click="router.push({ name: 'item', params: { id: item.Id } })"
          >
            <div class="listItem-content listItem-content-margin listItem-content-bg listItemContent-touchzoom listItem-border listItem-border-offset listItem-border-offset-portrait">
              <div class="listItemImageContainer listItemImageContainer-portrait">
                <img draggable="false" class="listItemImage listItemImage-portrait coveredImage coveredImage-noScale" :src="imageUrl(item.Id, 'Primary', item.ImageTags?.Primary, { maxWidth: 120, maxHeight: 180, quality: 90 })" loading="lazy" decoding="async" />
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
          <button :disabled="page <= 1" @click="page--">‹</button>
          <span class="cur">{{ page }} / {{ pageCount }}</span>
          <button :disabled="page >= pageCount" @click="page++">›</button>
        </div>
      </div>
    </div>

    <!-- 排序(actionSheet;真实站标题"排序："+ 当前项勾选) -->
    <Teleport to="body">
      <div v-if="sortMenuOpen" class="dialogScrim" @click.self="sortMenuOpen = false">
        <div class="dialog dialog-animated actionSheet opened sortSheet" @click.stop>
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
        </div>
      </div>
    </Teleport>

    <!-- 筛选(formDialog .filterMenuDialog;真实站分组:播放状态/收藏/类型/年份/工作室) -->
    <Teleport to="body">
      <div v-if="filterOpen" class="dialogScrim centered" @click.self="filterOpen = false">
        <div class="dialog dialog-animated formDialog filterMenuDialog opened" @click.stop>
          <div class="formDialogHeader">
            <button type="button" class="dialogCloseButton hide-mouse-idle-tv dialogHeaderButton paper-icon-button-light emby-button-focusscale" tabindex="-1" title="关闭" @click="filterOpen = false">
              <i class="md-icon">&#xE5CD;</i>
            </button>
            <h3 class="formDialogHeaderTitle">{{ t('library.filterTitle') }}</h3>
            <button v-if="hasFilter" type="button" class="button-link filterMenuClearButton emby-button emby-button-focusscale" @click="clearFilter">{{ t('library.clearFilter') }}</button>
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
                  class="filterChip raised raised-mini emby-button emby-button-focusscale"
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
                  class="filterChip raised raised-mini emby-button emby-button-focusscale"
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
                  class="filterChip raised raised-mini emby-button emby-button-focusscale"
                  :class="{ 'filterChip-on': filterYears.includes(y) }"
                  @click="toggleIn(filterYears, y)"
                >{{ y }}</button>
              </div>
            </div>
          </div>
          <div class="formDialogFooter">
            <button type="button" class="raised button-submit formDialogFooterItem emby-button emby-button-focusscale" @click="applyFilter">{{ t('library.done') }}</button>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 视图设置(dialog;真实站:视图 + 图像大小 + 完成) -->
    <Teleport to="body">
      <div v-if="viewSettingsOpen" class="dialogScrim centered" @click.self="viewSettingsOpen = false">
        <div class="dialog dialog-animated formDialog opened" @click.stop>
          <div class="formDialogHeader">
            <button type="button" class="dialogCloseButton hide-mouse-idle-tv dialogHeaderButton paper-icon-button-light emby-button-focusscale" tabindex="-1" title="关闭" @click="viewSettingsOpen = false">
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
            <button type="button" class="raised button-submit formDialogFooterItem emby-button emby-button-focusscale" @click="viewSettingsOpen = false">{{ t('library.done') }}</button>
          </div>
        </div>
      </div>
    </Teleport>
  </main>
</template>

<style scoped>
/* 分页(虚拟滚动 v1 缺位,先以分页兜底;样式贴合 4.9.5 胶囊) */
.pager { display: flex; justify-content: center; align-items: center; gap: 12px; padding: 1em 0 2em; }
.pager button {
  padding: .66em 2ch; background: rgba(62, 62, 62, .7); border: 0;
  border-radius: 100em; color: rgba(255, 255, 255, .87); cursor: pointer; font-size: 110%;
}
.pager button:disabled { opacity: .4; cursor: default; }
.pager .cur { color: rgba(255, 255, 255, .6); font-size: 14px; }

/* 弹层遮罩 + 筛选/视图设置本地补充(本体样式走 4.9.5 dialoghelper/formdialog) */
.dialogScrim {
  position: fixed; inset: 0; z-index: 1000;
  background: rgba(0, 0, 0, .5);
  display: flex; align-items: flex-start; justify-content: center;
}
.dialogScrim.centered { align-items: center; }
.dialogScrim .dialog.actionSheet,
.dialogScrim .dialog.formDialog {
  background: rgb(40, 40, 43);
  color: rgba(255, 255, 255, .87);
  border-radius: .9em;
  max-height: 84vh;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
.dialogScrim .formDialogContent { padding: 0 1.5em 1em; overflow-y: auto; }
.filterGroup { margin-bottom: 1.2em; }
.filterRow { display: flex; align-items: center; gap: .6em; padding: .35em 0; }
.filterChips { display: flex; flex-wrap: wrap; gap: .5em; }
.filterChip { padding: .4em 1em; background: rgba(255, 255, 255, .12); border: 0; border-radius: 100em; color: inherit; cursor: pointer; font-size: 95%; }
.filterChip-on { background: var(--theme-primary-color); color: #fff; }
.sortSheet { margin-top: 4em; max-height: 70vh; }
</style>