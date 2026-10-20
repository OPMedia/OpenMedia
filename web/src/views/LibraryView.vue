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
 * ⚠️ 旧值是 `{ width:'fit-content', height:'fit-content' }`，两个 center 弹层都被它压坏（实测 @1600×900）：
 *   · 设置弹层 509.4×179.2 —— vendor `.formDialogFooter{position:absolute;bottom:0;z-index:1}` 不占文档流，
 *     面板高度又按内容收缩，于是 footer 直接盖在内容区上：内容区 108.1 高、**只有 24.2px 露在 footer 之上**；
 *     「视图」select（bottom 488.1 > footer.top 454.7）被 footer 盖住，图像大小滑条（bottom 540.1）
 *     整个被面板 overflow 裁掉 —— 用户看到的就是"只剩被裁的「视图」两个字"。
 *   · 筛选弹层 509.4×399.5 —— 内容区 scrollHeight 328 === clientHeight 328（**滚不动**），
 *     footer 从 y=564.9 起压住内容区，最后一个分组「年份」（bottom 617）6 个 chip 全部点不到。
 *
 * 所以面板级几何在这里一次给全：显式宽度 + `height:auto` + `max-height`，
 * 再配合下方 scoped 样式把 `.formDialogFooter` 拉回文档流、内容区 `flex:1 1 auto; min-height:0` —— 三段式互不重叠。
 *   · `minWidth:0` 必须显式写：vendor `.formDialog{min-width:clamp(20%,60ch,50%)}` 在 1600 视口下算出 ≈432px，
 *     不归零就会把我给的 width 顶掉（旧实现 509.4 宽就是这么来的）。
 *   · `maxHeight` 取 `min(84vh,720px)`：内容再长也不高出视口，超出部分由内容区自己滚。
 *   · 宽度必须显式给：center 变体是 inset:0 + margin:auto，width:auto 会被拉伸成 92vw（实测 1327×758）。
 *
 * 排序 actionSheet 旧实现是水平居中（1440 视口下 x=651、宽 137），而 pop 变体是右上角（x=1289）；
 * 左右 inset 归零 + margin-inline:auto 拉回居中，纵向 top:58px 与旧 4em 等价、不动。
 */
const SHEET_PANEL_BASE = {
  display: 'flex',
  flexDirection: 'column',
  minWidth: '0',
  // ⚠️ 必须是 fit-content，**不能写 auto**：center 变体是 `inset:0; margin:auto`，
  // 绝对定位元素四边归零 + height:auto 时高度会撑满包含块（实测两个弹层都被拉成 720px 高，
  // 内容只有 250px 也是 720）。fit-content 才是"按内容收缩、居中"，再由 max-height 封顶；
  // 封顶后内容区靠 flex:1 1 auto + min-height:0 收缩并自己滚。
  height: 'fit-content',
  maxHeight: 'min(84vh, 720px)',
  maxWidth: '92vw',
  overflow: 'hidden',
}
/** 筛选弹层：分组多（播放状态/收藏/类型/年份/工作室），给宽一点 */
const FILTER_PANEL_STYLE = { ...SHEET_PANEL_BASE, width: '560px' }
/** 视图设置弹层：只有「视图」select + 图像大小滑条，窄一些 */
const SETTINGS_PANEL_STYLE = { ...SHEET_PANEL_BASE, width: '420px' }
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
      ...(genre ? { Genres: genre } : {}),
      ...(studio ? { Studios: studio } : {}),
      ...filterQuery(sectionFilter),
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

/**
 * 视图模式 / 图像大小 —— **持久化到 localStorage**（验收要求：刷新、切库后保持）。
 * 键名沿用本仓既有的 `serata.*` 约定（serata.token / serata.deviceId）。
 * 读取时做形状校验：localStorage 里可能是旧版本或手改的脏值（'grid2' / 'NaN' / '5'），
 * 非法一律回落默认 —— 否则滑条会变成一个"拖到边界也不动"的死控件（本次要修的正是这个）。
 */
const VIEW_MODE_KEY = 'serata.library.viewMode'
const IMAGE_SIZE_KEY = 'serata.library.imageSize'
function readStoredViewMode(): 'grid' | 'list' {
  return localStorage.getItem(VIEW_MODE_KEY) === 'list' ? 'list' : 'grid'
}
function readStoredImageSize(): -1 | 0 | 1 {
  const n = Number(localStorage.getItem(IMAGE_SIZE_KEY))
  return n === -1 || n === 1 ? n : 0
}
const viewMode = ref<'grid' | 'list'>(readStoredViewMode())
/** 滑条三档：-1 小 / 0 中 / +1 大（真实站滑条方向：往右更大） */
const imageSize = ref<-1 | 0 | 1>(readStoredImageSize())
watch(viewMode, (v) => localStorage.setItem(VIEW_MODE_KEY, v))
watch(imageSize, (v) => localStorage.setItem(IMAGE_SIZE_KEY, String(v)))

/** 三档的文字标签（复用既有 i18n 键 settings.optSmall/optNormal/optLarge —— 本任务只许改两个文件，不新增语言包） */
const SIZE_STOPS: { v: -1 | 0 | 1; key: string }[] = [
  { v: -1, key: 'settings.optSmall' },
  { v: 0, key: 'settings.optNormal' },
  { v: 1, key: 'settings.optLarge' },
]

/**
 * 图像大小 → vendor 的卡宽变量（缺陷 5「死控件」的修法）。
 *
 * 机制（实测 @1600×900）：卡宽来自
 *   `.portraitCard{width:calc(100% / max((var(--portrait-cards) + var(--user-cards-size-adjust) + var(--app-cards-size-adjust)), 3))}`
 * （原版 modules__cardbuilder__card_sizes_varcalcmax.css；本仓在 assets/emby495-overrides.css 里按原样重写）
 * 实测 `--portrait-cards:8`、`--app-cards-size-adjust:0`、`--user-cards-size-adjust:0`
 * （后两个定义在 modules__cardbuilder__card.css 的 `:root` 里），容器 1250.5px → 8 列、卡宽 156.3px。
 *
 * ⚠️ 方向是**反的**：滑条 +1 表示"大"，而 vendor 的 adjust 是**列数增量** ——
 *   adjust -1 → 8+(-1)=7 列 → 卡片更大；adjust +1 → 9 列 → 卡片更小。所以这里取负号。
 * 三档实测：7 列 / 8 列 / 9 列（数字见 task-11 报告）。
 *
 * 变量写在 `<main>` 的内联 style 上：自定义属性会继承到卡片，而内联声明比 `:root` 那份更近；
 * 不必、也不该去改 vendor 的 css 文件（链路里没有别的元素再声明这个变量，
 * 唯一的例外 `.itemsContainer-defaultCardSize` 本页没用 —— 见 modules__cardbuilder__card.css）。
 */
const cardSizeStyle = computed(() => ({ '--user-cards-size-adjust': String(-imageSize.value) }))

const pageCount = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)))
const page = ref(1)

watch([sortKey, sortDesc], () => { page.value = 1; void load() })
/**
 * 切库时把类型列表一起清掉。
 * 旧实现只在 `genresList.length === 0` 时才去拉（见 load() 里的 `if (!lib.value || lib.value.Id !== libId)`），
 * 于是从库 A 切到库 B 时**沿用了 A 的类型**：筛选弹层里点一个 B 里根本不存在的类型，
 * 结果列表直接空 —— 用户会以为"这个库坏了"。清空后 load() 会按新 libId 重拉（配合 getGenres 的降级重试）。
 */
watch(() => route.params.id, () => { lib.value = null; genresList.value = []; page.value = 1; clearFilter(); void load() })
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

/**
 * 筛选参数拼接(真实站 filterMenuDialog → 查询参数)。
 *
 * ⚠️ `Filters` 必须把「分节筛选」和「弹层筛选」**合并成一个串再去重**。
 * 旧实现是两处分别写 `Filters`，而调用点是
 *   `{ ...(sectionFilter ? {Filters: sectionFilter} : {}), ...filterQuery() }`
 * —— 展开顺序在后的一方**整个覆盖**前者：收藏分节（tab=favorites → `IsFavorite`）或
 * 继续观看分节（sectionId=resume → `IsResumable,IsUnplayed`）一旦与弹层筛选同时用，
 * 分节条件就静默消失（用户看到的是"选了收藏，列表却混进了没收藏的"）。
 * 现在由本函数统一产出唯一的 `Filters`，并按出现顺序去重（`IsUnplayed` 不会出现两次）。
 *
 * @param sectionFilter 来自路由的分节条件（sectionId/tab），由 load() 传进来一起合并
 */
function filterQuery(sectionFilter?: string): Record<string, string> {
  const parts: string[] = []
  const push = (raw?: string) => {
    for (const t of (raw ?? '').split(',')) {
      const v = t.trim()
      if (v && !parts.includes(v)) parts.push(v)
    }
  }
  push(sectionFilter)
  if (filterPlayed.value === 'played') push('IsPlayed')
  else if (filterPlayed.value === 'unplayed') push('IsUnplayed')
  else if (filterPlayed.value === 'resumable') push('IsResumable')
  if (filterFavorite.value) push('IsFavorite')

  const q: Record<string, string> = {}
  if (parts.length) q.Filters = parts.join(',')
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
  <main class="view flex flex-direction-column withTabs page page-withFullDrawer page-withDockedDrawer focuscontainer-x" :style="cardSizeStyle">
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
          <!-- 有筛选时带可见态（accent 描边 + 图标转 accent）并暴露 aria-pressed：
               旧实现里 hasFilter 只在弹层内部用，按钮本身没有任何"已筛选"提示，
               用户关掉弹层后完全看不出列表为什么少了内容。 -->
          <button type="button" is="emby-button" class="itemsViewSettingsContainer-button btnFilter s-press fab fab-mini listTextButton-autohide emby-button emby-button-focusscale" :class="{ 'btnFilter-on': hasFilter }" :title="hasFilter ? t('library.clearFilter') : t('library.filterTitle')" :aria-pressed="hasFilter ? 'true' : 'false'" @click="filterOpen = true">
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
      :content-style="FILTER_PANEL_STYLE"
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
      panel-class="dialog formDialog viewSettingsDialog"
      :content-style="SETTINGS_PANEL_STYLE"
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
              <!-- 滑条三档 + 文字标签：光有一根滑条用户不知道"哪头是大"。
                   标签复用既有 i18n 键（小/正常/大），并给 range 一个 aria-valuetext，
                   读屏用户听到的是"小/正常/大"而不是裸数字 -1/0/1。 -->
              <input
                v-model.number="imageSize"
                type="range" min="-1" max="1" step="1"
                class="emby-slider"
                :aria-valuetext="t(SIZE_STOPS.find((s) => s.v === imageSize)?.key ?? 'settings.optNormal')"
              />
              <div class="sizeStops">
                <span
                  v-for="s in SIZE_STOPS" :key="s.v"
                  class="sizeStop"
                  :class="{ 'sizeStop-on': imageSize === s.v }"
                >{{ t(s.key) }}</span>
              </div>
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

/* ============================================================
   弹层三段式（缺陷 1 的另一半）
   面板级几何走 :content-style（见 script 里 FILTER_PANEL_STYLE / SETTINGS_PANEL_STYLE）：
   面板是 SSheet 的 fragment 根，拿不到本视图的 scoped data-v，写在这里是死代码。
   但**插槽内容**拿得到 data-v（实测 `.dialog .formDialogContent` 的 padding 生效），
   所以内部布局写在这里 —— 选择器保留 `.formDialog .xxx` 这种带祖先类名的形式。
   vendor 的病灶是 `.formDialogFooter{position:absolute;bottom:0;z-index:1}`：它不占文档流，
   于是直接盖在内容区上（实测设置弹层内容区只剩 24.2px 可见、筛选弹层最后一个分组点不到）。
   这里把 footer 拉回文档流，让内容区吃掉剩余高度并自己滚 —— header 固定 / 内容滚动 / footer 固定。
   ============================================================ */
.dialog .formDialogContent { padding: 0 1.5em 1em; overflow-y: auto; flex: 1 1 auto; min-height: 0; }
.dialog .formDialogFooter { position: static; flex: 0 0 auto; }

/* 排序 actionSheet 的横向居中：已改为 :content-style（见 script 里 SORT_PANEL_STYLE 的说明）——
   面板拿不到本视图的 scoped data-v，写在这里是死代码 */

/* 「筛选已生效」的可见态（缺陷 4）：accent 描边 + 图标转 accent 文字色。
   旧实现 btnFilter 在有无筛选时长得一模一样，用户关掉弹层后完全看不出列表为什么变短。
   ⚠️ 用 box-shadow 画环，不用 outline：老 emby CSS 里 `.emby-button{outline:0!important}`
   会把 outline **静默吃掉**（本仓 serata/README.md 铁律 11 记过这个坑，实测这里 outline 果然被吃成 none）。
   inset 环不吃盒模型、不引起重排。 */
.btnFilter-on { box-shadow: inset 0 0 0 2px var(--s-accent); }
.btnFilter-on .button-icon { color: var(--s-accent-text); }

/* ---------- 单选（缺陷 3）----------
   全站**没有任何 .emby-radio 的基样式**（assets/emby495 里只有 checkbox/toggle/slider/select/input；
   主题文件里那几条 .emby-radio-* 都是给 Emby 自己的 label 结构用的）。
   实测 4 个单选 computed = `appearance:auto` / 13×13 / `accent-color:auto` → 系统蓝点，深色下与界面完全不搭。
   这里用令牌自己画，但**保留原生 input 的可访问性**：不改 role、不用 div 假装单选，
   键盘 Tab/方向键、读屏语义全部照旧，只把绘制层换成 appearance:none + ::after 圆点。 */
.filterMenuDialog .emby-radio {
  appearance: none;
  -webkit-appearance: none;
  flex: 0 0 auto;
  width: 18px;
  height: 18px;
  margin: 0;
  /* 未选中态：--s-line 在深色下只有 8% 白，1.5px 圆环实测几乎看不见，
     所以取同族的强档 --s-line-strong（发丝边家族，不是新颜色） */
  border: 1.5px solid var(--s-line-strong);
  border-radius: 50%;
  background: transparent;
  position: relative;
  cursor: pointer;
  transition: border-color var(--s-transition), background-color var(--s-transition);
}
.filterMenuDialog .emby-radio::after {
  content: '';
  position: absolute;
  inset: 0;
  margin: auto;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--s-accent-ink);
  transform: scale(0);
  transition: transform var(--s-transition);
}
.filterMenuDialog .emby-radio:checked { border-color: var(--s-accent); background: var(--s-accent); }
.filterMenuDialog .emby-radio:checked::after { transform: scale(1); }
.filterMenuDialog .emby-radio:focus-visible { outline: 2px solid var(--s-accent); outline-offset: 2px; }

/* ---------- 收藏开关（缺陷 3）----------
   vendor 的 .toggleSwitch 背景来自主题变量，实测"关"态是硬编码深灰 `rgb(69,69,74)`（浅色模式下同样一块灰）。
   结构保持 vendor 原样（input.emby-toggle + 兄弟 div.toggleSwitch + ::before 圆点），只把两态换成令牌：
   关 = 抬升面 + 发丝边，开 = accent 填充。 */
.filterMenuDialog .toggleSwitch { background: var(--s-hover); box-shadow: inset 0 0 0 1px var(--s-line); }
.filterMenuDialog .emby-toggle:checked ~ .toggleSwitch { background: var(--s-accent); box-shadow: none; }
.filterMenuDialog .toggleSwitch::before { background: var(--s-cream); }

/* ---------- 「图像大小」滑条（缺陷 5 的可见性部分）----------
   vendor `.emby-slider` 是 Emby 自家 JS 组件的一半，**两个独立原因**让裸 input 完全看不见：
   ① 基础规则里就写着 `opacity:0`（`.emby-slider{...height:100%;...opacity:0}`）——
      整根 input（含下面我自己画的轨与滑块）一起透明，这是主因；
   ② 原生轨背景被设成透明（`::-webkit-slider-runnable-track{background:0 0}`）、thumb `opacity:0`，
      可见部分靠 JS 另外插入的 `.emby-slider-background` / `.emby-slider-thumb` 元素。
   本仓没有那套 JS，所以这里把 opacity 与原生轨/滑块都用令牌画回来（只在本弹层内覆盖，不动 vendor 文件）。
   另：vendor 还给 .emby-slider 留了 `padding:1em 0` + `box-sizing:border-box`，不归零的话
   我给的 height 会被 padding 顶成 2em（实测 24px → 28.84px），所以一并把 padding 归零。 */
.viewSettingsDialog .emby-slider {
  appearance: none;
  -webkit-appearance: none;
  width: 100%;
  height: 24px;
  padding: 0;
  opacity: 1;
  margin: 4px 0 0;
  background: transparent;
  cursor: pointer;
}
.viewSettingsDialog .emby-slider::-webkit-slider-runnable-track {
  height: 4px;
  border-radius: var(--s-r-pill);
  background: var(--s-hover);
  box-shadow: inset 0 0 0 1px var(--s-line);
}
.viewSettingsDialog .emby-slider::-webkit-slider-thumb {
  appearance: none;
  -webkit-appearance: none;
  width: 16px;
  height: 16px;
  margin-top: -6px;
  border: 0;
  border-radius: 50%;
  background: var(--s-accent);
  opacity: 1;
}
.viewSettingsDialog .emby-slider::-moz-range-track {
  height: 4px;
  border-radius: var(--s-r-pill);
  background: var(--s-hover);
}
.viewSettingsDialog .emby-slider::-moz-range-thumb {
  width: 16px;
  height: 16px;
  border: 0;
  border-radius: 50%;
  background: var(--s-accent);
  opacity: 1;
}
.viewSettingsDialog .emby-slider:focus-visible { outline: 2px solid var(--s-accent); outline-offset: 2px; }

/* 三档文字标签：与轨道两端对齐（小 ←→ 大），当前档位转 accent 文字色 */
.sizeStops { display: flex; justify-content: space-between; margin-top: 2px; }
.sizeStop { font-size: var(--s-fs-xs); color: var(--s-text-2); }
.sizeStop-on { color: var(--s-accent-text); font-weight: var(--s-fw-semibold); }

.filterGroup { margin-bottom: 1.2em; }
.filterRow { display: flex; align-items: center; gap: .6em; padding: .35em 0; }
.filterChips { display: flex; flex-wrap: wrap; gap: .5em; }
/* 未选中 = 抬升面 + 发丝边(原「白 12% 叠浅底」会与底色糊成一片,只剩文字飘着)；
   选中 = 珊瑚底 + 墨字(5.42:1,达 WCAG AA；原珊瑚 + 白字只有 3.01:1 不达标) */
.filterChip { padding: .4em 1em; background: var(--s-hover); border: 1px solid var(--s-line); border-radius: 100em; color: var(--s-text-1); cursor: pointer; font-size: 95%; }
.filterChip-on { background: var(--s-accent); color: var(--s-accent-ink); border-color: transparent; }
.filterChip-on:hover { background: var(--s-accent-hi); }
</style>