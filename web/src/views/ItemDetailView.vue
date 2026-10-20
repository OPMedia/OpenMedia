<script setup lang="ts">
/**
 * 详情页 —— 1:1 还原 Emby 4.9.5 itemView(基准=真实站运行时 dump,见 reference/dumps/emby495-item-library.md):
 * topDetailsContainer(itemBackdrop + 详情主区:海报卡/标题+编辑钮/mediaInfo 行/主按钮组
 * /进度行/overview) + details-additionalContent(季分集横排 + 演职人员 + 相似)
 * 铁律:不实现播放;btnResume/播放按钮仅视觉还原。
 */
import { ref, computed, onMounted, nextTick, watch, watchEffect } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { BaseItemDto, BaseItemPerson } from '@/api/types'
import { getItem, getSeasons, getEpisodes, getSimilarItems, markPlayed, markFavorite, imageUrl, cardArtUrl, personArtUrl, backdropUrl, updateItem, uploadItemImage, deleteItemImage } from '@/api/endpoints'
import { showToast } from '@/api/toast'
import { useAuthStore } from '@/stores/auth'
import EmbyCard from '@/components/EmbyCard.vue'
import ItemMenuSheet from '@/components/ItemMenuSheet.vue'
import EmbySelect from '@/components/EmbySelect.vue'
import type { EmbySelectOption } from '@/components/EmbySelect.vue'
import SSheet from '@/components/ui/SSheet.vue'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { flipConfig, useReducedMotionSafe } from '@/lib/motion'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const { t } = useI18n()

/** 分集列表 FLIP(切季时整列换血)：JS 动画先问 reduced-motion（铁律 16） */
const reducedMotion = useReducedMotionSafe()
const listFlip = computed(() => flipConfig(reducedMotion.value))

/**
 * 面板级几何只能走 content-style：SSheet 的模板根是 fragment(scrim + panel)，
 * 父视图的 scoped data-v 落不到面板上（实测面板只有 SSheet 自己的 data-v），
 * 写在 <style scoped> 里的 `.imageEditorDialog{width:...}` 是死代码。
 * center 变体是 inset:0 + margin:auto，width/height:auto 会被拉伸成 92vw×84vh，
 * 故内容尺寸弹层必须显式 fit-content（旧实现：编辑元数据 509×388、人物 137×129）。
 */
const CENTER_PANEL_STYLE = { width: 'fit-content', height: 'fit-content' }
/** 图像编辑器保持真实站宽度（原 scoped 规则 `.imageEditorDialog{width:min(60em,92vw)}`，实测 867×524） */
const IMAGE_EDITOR_PANEL_STYLE = { width: 'min(60em, 92vw)', height: 'fit-content' }

const loading = ref(true)
const notFound = ref(false)
const item = ref<BaseItemDto | null>(null)
const similar = ref<BaseItemDto[]>([])
/**
 * 人物列表。原声明是手写的窄类型 `{ Name; Role?; Id }`，把 `BaseItemPerson.PrimaryImageTag`
 * 丢掉了 —— 而人物头像的 tag 恰恰只在这个字段上（不在 `ImageTags` 里）。
 * 补回真实 DTO 类型，人物卡才能做到「没有 tag 就不请求」（见 personArtUrl 注释）。
 */
const people = ref<BaseItemPerson[]>([])

// 剧集:季/分集(真实站形态:seasonTitle + 分集横排卡)
const seasons = ref<BaseItemDto[]>([])
const currentSeasonId = ref<string | null>(null)
const episodes = ref<BaseItemDto[]>([])

const ICON_PLAY = '\uE037'
const ICON_MOVIES = '\uE02C'
const ICON_CHECK = '\uE5CA'
const ICON_CIRCLE_CHECK = '\uE86C'
const ICON_EDIT = '\uE3C9'
const ICON_PHOTO = 'photo' // ligature
const ICON_STAR = '\uE838'
const ICON_FAV = '\uE87D'
const ICON_MORE = '\uE5D3'
const ICON_CHEV_R = '\uE5C8'
const ICON_CHEV_L = '\uE5C4'

/**
 * 加载详情页。
 *
 * ⚠️ 这里的分段是有原因的，不许再合并回一个 `await` 串：
 *
 * 改前是「getItem → getSimilarItems → getSeasons → loadEpisodes」四个**串行** await，
 * 而 `<main>`（唯一滚动壳）当时写在 `v-else-if="item"` 分支里，`loading` 又要等四个请求全完
 * 才置 false。于是点进详情页后的那段时间里：
 *   · 页面上**不存在 `<main>`**，全屏唯一可滚元素是侧栏自己的 `.s-nav.s-scroll`；
 *   · 用户滚轮滚到的是侧栏，详情页一动不动 —— 这就是「侧边栏常驻、滑不动、看不到信息」。
 * 真机实测该窗口期 = 浏览器首屏 **8.5s**（4 个串行请求 5304ms + 首帧渲染），
 * 而首屏真正需要的只有 getItem 的 **630ms**。
 *
 * 现在：① 首屏只等 getItem，一到就撤加载态（`<main>` 已常驻，见模板）；
 *       ② 相似 / 季 / 分集并行补，谁先回来谁先渲染，不再阻塞首屏。
 */
async function load() {
  loading.value = true
  notFound.value = false
  // 换条目先清场：否则上一条的相似推荐/分集会在新条目下先闪一下（真实站不会）
  item.value = null
  similar.value = []
  seasons.value = []
  episodes.value = []
  people.value = []
  currentSeasonId.value = null

  const userId = auth.user!.Id
  const id = String(route.params.id)

  try {
    // ① 首屏关键路径：只有「条目本体」这一个请求
    item.value = await getItem(userId, id)
  } catch {
    notFound.value = true
    loading.value = false
    return
  }

  document.title = `${item.value.Name} · ${t('app.name')}`
  people.value = item.value.People ?? []
  loading.value = false
  // 简介 clamp 检测(真实站仅在文本被截断时显示"更多")。必须等 DOM 真的渲染完再量，
  // 改前是在 finally 里同步调的，那时内容还没进 DOM，`overviewClamped` 恒为 false。
  void nextTick().then(() => measureOverview())

  // ② 次屏：并行补，失败不阻塞（真实站也是"有就显示"）
  const tasks: Promise<unknown>[] = [
    getSimilarItems(id, userId)
      .then((r) => { similar.value = r.Items ?? [] })
      .catch(() => {}),
  ]
  if (item.value.Type === 'Series') {
    tasks.push(
      getSeasons(id, userId)
        .then(async (r) => {
          seasons.value = r.Items ?? []
          const next = seasons.value[0]?.Id ?? null
          if (next === currentSeasonId.value) {
            // 季 id 没变（含变 null）→ watch 不会触发，这里显式拉一次
            await loadEpisodes()
          } else {
            // 变了 → 交给 watch(currentSeasonId)，保证只发一次请求
            currentSeasonId.value = next
          }
        })
        .catch(() => {}),
    )
  }
  void Promise.allSettled(tasks)
}

async function loadEpisodes() {
  if (!item.value || item.value.Type !== 'Series' || !currentSeasonId.value) return
  episodes.value = (await getEpisodes(item.value.Id, auth.user!.Id, currentSeasonId.value)).Items
}

watch(currentSeasonId, () => void loadEpisodes())
watch(() => route.params.id, () => void load())
onMounted(() => void load())

/**
 * 海报图源 —— 走 `cardArtUrl`，**不能**无条件拼 Primary。
 *
 * 真机 Emby 4.9.5.0 对「没有 Primary 图」的条目 `/Items/{id}/Images/Primary` 返回 **HTTP 500**
 * （不是 404；全站 13739 条真实条目里 1931 条没有 Primary）。
 * `cardArtUrl` 没有 tag 时返回空串，模板据此不渲染 <img>，绝不发这次必然 5xx 的请求。
 */
const posterSrc = computed(() =>
  cardArtUrl(item.value, 'portrait', { maxWidth: 375, maxHeight: 563, quality: 90 }))

/**
 * 分集卡（16:9 横版）图源。原实现无条件拼 `ep.Id` 的 Primary —— 真机上这是最大的 5xx 来源：
 * 详情页「一个屋檐下」100 集全部没有 Primary，改前一次进页 **99×500 + 1×502**。
 * 走 `cardArtUrl(ep, 'backdrop')` 后退到父级剧照（ParentBackdropImageTags），100 集全部出图。
 * 返回 undefined 而不是空串：Vue 会把 src 属性整个去掉，浏览器不会发任何请求，也不会画碎图图标。
 */
function epArt(ep: BaseItemDto): string | undefined {
  return cardArtUrl(ep, 'backdrop', { maxWidth: 375, maxHeight: 212, quality: 90 }) || undefined
}

/** 人物卡图源：tag 在 `PrimaryImageTag` 上；没有 tag 就不请求（真机对无头像人物同样返回 500） */
function personArt(p: BaseItemPerson): string | undefined {
  return personArtUrl(p, { maxWidth: 300, maxHeight: 450, quality: 90 }) || undefined
}
// 背景大图不一定属于条目自己：剧集/季的 backdrop 挂在父级系列上
// （真实 Emby：BackdropImageTags 为空数组，另有 ParentBackdropItemId/ParentBackdropImageTags）。
// backdropUrl 内部已处理这条回退链，没有大图时返回空串。
const backdropSrc = computed(() => backdropUrl(item.value, { maxWidth: 1600 }))

/** 全局 backdrop 层(真实站详情页顶部暗背景由 body 级 backdropContainer 提供) */
watchEffect(() => {
  const container = document.querySelector('.backdropContainer')
  if (container) {
    ;(container as HTMLElement).style.backgroundImage = backdropSrc.value ? `url(${backdropSrc.value})` : 'none'
  }
})

const ud = computed(() => item.value?.UserData ?? {})
const resumePct = computed(() => {
  if (!item.value?.RunTimeTicks) return 0
  return (ud.value.PlaybackPositionTicks ?? 0) / item.value.RunTimeTicks
})
const resumeRemaining = computed(() => {
  if (!item.value?.RunTimeTicks || !ud.value.PlaybackPositionTicks) return ''
  const leftSec = (item.value.RunTimeTicks - ud.value.PlaybackPositionTicks) / 10_000_000
  return `剩余 ${Math.round(leftSec / 60)}分钟`
})
/** 时长汉化(真实站形态:1小时48分钟) */
const runtimeText = computed(() => {
  const ticks = item.value?.RunTimeTicks
  if (!ticks) return ''
  const min = Math.round(ticks / 600_000_000)
  const h = Math.floor(min / 60)
  const m = min % 60
  return h > 0 ? `${h}小时${m}分钟` : `${m}分钟`
})
/** 预计结束时间(真实站:预计 23:55 结束;仅电影有) */
const endsAtText = computed(() => {
  const i = item.value
  const ticks = i?.RunTimeTicks
  if (!ticks || !i || i.Type !== 'Movie') return ''
  const end = new Date(Date.now() + ticks / 10_000)
  return `预计 ${String(end.getHours()).padStart(2, '0')}:${String(end.getMinutes()).padStart(2, '0')} 结束`
})
/** 媒体流选择(真实站 trackSelections;disabled 形态) */
const videoText = computed(() => {
  const v = item.value?.MediaSources?.[0]?.MediaStreams?.find((s) => s.Type === 'Video')
  if (!v?.Height) return ''
  const p = v.Height >= 2000 ? '4K' : v.Height >= 1000 ? `${v.Height}p` : `${v.Height}p`
  return `${p} ${v.Codec?.toUpperCase() ?? ''}`.trim()
})
const audioText = computed(() => {
  const a = item.value?.MediaSources?.[0]?.MediaStreams?.find((s) => s.Type === 'Audio')
  if (!a) return ''
  const ch = a.Channels ? ` ${a.Channels === 6 ? '5.1' : a.Channels === 2 ? '2.0' : a.Channels}` : ''
  const lang = a.Language === 'chi' ? 'Chinese' : ''
  return `${lang} ${a.Codec?.toUpperCase()}${ch}${a.IsDefault ? ' (默认)' : ''}`.trim().replace(/\s+/g, ' ')
})
/** mediaInfo 行:年份区间/年份 + 时长(电影)/studio/共 N 季(剧)/类型 */
const yearText = computed(() => {
  const i = item.value
  if (!i?.ProductionYear) return ''
  return `${i.ProductionYear}${i.EndYear ? ` - ${i.EndYear}` : ''}`
})
const seasonCountText = computed(() => {
  const i = item.value
  if (i?.Type !== 'Series') return ''
  return `共 ${seasons.value.length} 季`
})

function fmtDate(iso?: string): string {
  if (!iso) return ''
  const d = new Date(iso)
  return `${d.getFullYear()}年${d.getMonth() + 1}月${d.getDate()}日`
}

function formatSize(bytes?: number | null): string {
  if (!bytes) return ''
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(1)} GB`
  if (bytes >= 1024 ** 2) return `${Math.round(bytes / 1024 ** 2)} MB`
  return `${Math.round(bytes / 1024)} KB`
}

async function togglePlayed() {
  if (!item.value) return
  const next = !ud.value.Played
  void markPlayed(auth.user!.Id, item.value.Id, next)
  item.value = { ...item.value, UserData: { ...ud.value, Played: next, PlaybackPositionTicks: next ? 0 : ud.value.PlaybackPositionTicks } }
}

async function toggleFavorite() {
  if (!item.value) return
  const next = !ud.value.IsFavorite
  void markFavorite(auth.user!.Id, item.value.Id, next)
  item.value = { ...item.value, UserData: { ...ud.value, IsFavorite: next } }
}

const menuOpen = ref(false)

// ---- trackSelections(真实站形态:多版本/多音轨/多字幕全可选;当前版本源可切) ----
const currentSourceId = ref('')
const videoTrackId = ref('0')
const audioTrackId = ref('')
const subtitleTrackId = ref('-1')

// 活动源(用户切版本后 detail 数据跟随)
const activeSource = computed(() => {
  const sources = item.value?.MediaSources ?? []
  return sources.find((s) => s.Id === currentSourceId.value) ?? sources[0]
})

const videoTrackOptions = computed<EmbySelectOption[]>(() => {
  const src = activeSource.value
  const streams = (src?.MediaStreams ?? []).filter((s) => s.Type === 'Video')
  return streams.map((v) => ({
    value: String(v.Index),
    label: v.DisplayTitle ?? `${v.Height}p ${v.Codec?.toUpperCase()}`,
  }))
})

const audioTrackOptions = computed<EmbySelectOption[]>(() => {
  const src = activeSource.value
  return (src?.MediaStreams ?? []).filter((s) => s.Type === 'Audio').map((a) => ({
    value: String(a.Index),
    label: `${a.Language === 'chi' ? 'Chinese' : a.Language === 'eng' ? 'English' : ''} ${a.Codec?.toUpperCase()}${a.Channels === 6 ? ' 5.1' : a.Channels === 2 ? ' 2.0' : ''}${a.IsDefault ? ' (默认)' : ''}`.trim().replace(/\s+/g, ' '),
    secondaryText: a.DisplayTitle,
  }))
})

const subtitleTrackOptions = computed<EmbySelectOption[]>(() => {
  const src = activeSource.value
  const subs = (src?.MediaStreams ?? []).filter((s) => s.Type === 'Subtitle')
  return [
    { value: '-1', label: '关' },
    ...subs.map((s) => ({
      value: String(s.Index),
      label: s.DisplayTitle ?? s.Title ?? s.Language ?? String(s.Index),
      secondaryText: s.IsForced ? '强制' : undefined,
    })),
  ]
})

/** 版本(媒体源)选择:切源后重置轨道 */
const versionOptions = computed<EmbySelectOption[]>(() => (item.value?.MediaSources ?? []).map((s) => ({ value: s.Id, label: s.Name ?? s.Path ?? '版本' })))

watchEffect(() => {
  // 初始:默认源 + 默认轨道
  const src = activeSource.value
  if (src && !currentSourceId.value) currentSourceId.value = src.Id
  if (src) {
    const defAudio = src.MediaStreams?.find((s) => s.Type === 'Audio' && s.IsDefault) ?? src.MediaStreams?.find((s) => s.Type === 'Audio')
    if (defAudio && !audioTrackOptions.value.some((o) => o.value === audioTrackId.value)) audioTrackId.value = String(defAudio.Index)
  }
})

function pickVersion(id: string) {
  currentSourceId.value = id
  const src = (item.value?.MediaSources ?? []).find((s) => s.Id === id)
  const defAudio = src?.MediaStreams?.find((s) => s.Type === 'Audio' && s.IsDefault) ?? src?.MediaStreams?.find((s) => s.Type === 'Audio')
  audioTrackId.value = defAudio ? String(defAudio.Index) : ''
  subtitleTrackId.value = '-1'
}

function goEpisode(ep: BaseItemDto) {
  router.push({ name: 'item', params: { id: ep.Id } })
}

/** 季选择器(真实站剧集页:多季时切换,重建分集列表) */
const seasonOptions = computed<EmbySelectOption[]>(() => seasons.value.map((s) => ({ value: s.Id, label: s.Name ?? '' })))
async function pickSeason(id: string) {
  currentSeasonId.value = id
  await loadEpisodes()
}

/** 播放(本地 OSD UI;零真实播放) */
function play(id?: string) {
  const target = id ?? item.value?.Id
  if (target) router.push({ name: 'play', params: { id: target } })
}

function playTrailer() {
  showToast('预告片 v1 未接入(真实站走 trailer 播放)')
}

/** 概览展开/收起(真实站:文本被 clamp 时才显示"更多") */
const overviewExpanded = ref(false)
const overviewEl = ref<HTMLElement | null>(null)
const overviewClamped = ref(false)

async function measureOverview() {
  await nextTick()
  const el = overviewEl.value
  if (!el) { overviewClamped.value = false; return }
  overviewClamped.value = el.scrollHeight > el.clientHeight + 2
}

/** 横向滚动条(真实站 emby-scrollbuttons:滚动 .emby-scroller 容器,非 scrollSlider) */
function scrollStrip(e: MouseEvent, dir: number) {
  const btn = e.currentTarget as HTMLElement
  const section = btn.closest('.emby-scrollbuttons-scroller, .verticalSection')
  const scroller = (section?.querySelector('.emby-scroller') ?? section?.querySelector('.scrollX')) as HTMLElement | null
  const slider = (scroller ?? section?.querySelector('.scrollSlider')) as HTMLElement | null
  if (!slider) return
  slider.scrollBy({ left: dir * Math.max(240, slider.clientWidth * 0.8), behavior: 'smooth' })
}

/** 分集:播放 / 标记已看 / 菜单 */
/** 分集卡收藏切换(真实站 emby-ratingbutton) */
function toggleEpisodeFavorite(ep: BaseItemDto) {
  const next = !ep.UserData?.IsFavorite
  void markFavorite(auth.user!.Id, ep.Id, next)
  const idx = episodes.value.findIndex((x) => x.Id === ep.Id)
  if (idx >= 0) episodes.value[idx] = { ...ep, UserData: { ...ep.UserData, IsFavorite: next } }
}

function toggleEpisodePlayed(ep: BaseItemDto) {
  const next = !ep.UserData?.Played
  void markPlayed(auth.user!.Id, ep.Id, next)
  const idx = episodes.value.findIndex((x) => x.Id === ep.Id)
  if (idx >= 0) episodes.value[idx] = { ...ep, UserData: { ...ep.UserData, Played: next } }
}

const episodeMenuTarget = ref<BaseItemDto | null>(null)
function openEpisodeMenu(ep: BaseItemDto) {
  episodeMenuTarget.value = ep
}
function toggleEpisodePlayedFromMenu() {
  if (episodeMenuTarget.value) toggleEpisodePlayed(episodeMenuTarget.value)
}
function toggleEpisodeFavoriteFromMenu() {
  const ep = episodeMenuTarget.value
  if (!ep) return
  const next = !ep.UserData?.IsFavorite
  void markFavorite(auth.user!.Id, ep.Id, next)
  const idx = episodes.value.findIndex((x) => x.Id === ep.Id)
  if (idx >= 0) episodes.value[idx] = { ...ep, UserData: { ...ep.UserData, IsFavorite: next } }
}

/** 人物(真实站跳人物页;v1:人物信息面板 + 类型/工作室筛选跳库页) */
const personSheet = ref<BaseItemPerson | null>(null)
function openPerson(p: BaseItemPerson) {
  // 真实站人物卡 → 人物详情页
  router.push({ name: 'person', params: { id: p.Id }, query: p.Role ? { role: p.Role } : {} })
}
function openPersonMenu(p: BaseItemPerson) { personSheet.value = p }
/** 人物面板头图（同上：没有 tag 就返回空串，模板不设 backgroundImage，不发那次必然 5xx 的请求） */
const personSheetArt = computed(() =>
  personSheet.value ? personArtUrl(personSheet.value, { maxWidth: 120, maxHeight: 180, quality: 90 }) : '')

function filterByGenre(genre?: string) {
  if (!genre || !item.value) return
  router.push({ name: 'library', params: { id: item.value.ParentId ?? '100' }, query: { genre } })
}
function filterByStudio(studio?: string) {
  if (!studio || !item.value) return
  router.push({ name: 'library', params: { id: item.value.ParentId ?? '100' }, query: { studio } })
}

/** 编辑元数据 / 编辑图像(formdialog;真实站同款弹窗) */
const editMetadataOpen = ref(false)
const editImagesOpen = ref(false)
const editForm = ref({ Name: '', ProductionYear: '', CommunityRating: '', Overview: '' })
function openEditMetadata() {
  const i = item.value
  if (!i) return
  editForm.value = {
    Name: i.Name,
    ProductionYear: i.ProductionYear ? String(i.ProductionYear) : '',
    CommunityRating: i.CommunityRating ? String(i.CommunityRating) : '',
    Overview: i.Overview ?? '',
  }
  editMetadataOpen.value = true
}
async function saveMetadata() {
  const i = item.value
  if (!i) return
  const patch: Partial<BaseItemDto> = {
    Name: editForm.value.Name || i.Name,
    ProductionYear: editForm.value.ProductionYear ? Number(editForm.value.ProductionYear) : i.ProductionYear,
    CommunityRating: editForm.value.CommunityRating ? Number(editForm.value.CommunityRating) : i.CommunityRating,
    Overview: editForm.value.Overview,
  }
  try {
    const updated = await updateItem(i.Id, patch)
    item.value = { ...i, ...updated, ...patch }
    showToast('元数据已保存(mock)')
  } catch {
    item.value = { ...i, ...patch }
    showToast('元数据已保存(mock)')
  }
  editMetadataOpen.value = false
}

/** 编辑图像(真实站 imageeditor 的图像类型集:Primary/Backdrop/Logo/Thumb/Banner/Disc/Art) */
const imageType = ref('Primary')
const IMAGE_TYPES = [
  { type: 'Primary', label: '主要' },
  { type: 'Backdrop', label: '背景图' },
  { type: 'Logo', label: '标志' },
  { type: 'Thumb', label: '缩略图' },
  { type: 'Banner', label: '横幅' },
  { type: 'Disc', label: '光盘' },
  { type: 'Art', label: '艺术图' },
]
/** 预览图版本号(上传/删除后强制刷新,绕过浏览器缓存) */
const imageVersion = ref(0)
const imageBusy = ref(false)

function imagePreviewSrc(): string {
  if (!item.value) return ''
  const t0 = imageType.value
  const tag = t0 === 'Primary'
    ? item.value.ImageTags?.Primary
    : t0 === 'Backdrop'
      ? item.value.BackdropImageTags?.[0]
      : item.value.ImageTags?.[t0]
  const base = imageUrl(item.value.Id, t0, tag, { maxWidth: 400, maxHeight: 400, quality: 90 })
  return imageVersion.value ? `${base}&v=${imageVersion.value}` : base
}

/** 上传(真实站 POST /Items/{id}/Images/{type},multipart) */
async function onImagePicked(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file || !item.value) return
  imageBusy.value = true
  try {
    await uploadItemImage(item.value.Id, imageType.value, file)
    imageVersion.value = Date.now()
    showToast(t('item.imageUploaded'))
  } catch { showToast(t('admin.actionFailed')) } finally {
    imageBusy.value = false
    input.value = ''
  }
}

/** 删除(真实站 DELETE /Items/{id}/Images/{type}) */
async function deleteImage() {
  if (!item.value) return
  imageBusy.value = true
  try {
    await deleteItemImage(item.value.Id, imageType.value)
    imageVersion.value = Date.now()
    showToast(t('item.imageDeleted'))
  } catch { showToast(t('admin.actionFailed')) } finally { imageBusy.value = false }
}

function imageAction(action: string) {
  showToast(`${action} — v1 mock(真实站走 /Items/{id}/Images 上传删除)`)
}

/** 更多菜单命令(真实站 data-id:editmeta/editimages/identify/refreshmeta…) */
function onItemCommand(id: string) {
  if (id === 'editmeta') { openEditMetadata(); return }
  if (id === 'editimages') { editImagesOpen.value = true; return }
  showToast(`${id} — v1 mock`)
}

function libIcon(): string {
  return item.value?.CollectionType === 'tvshows' ? '\uE333' : '\uE02C'
}
</script>

<template>
  <div>
    <!-- 加载态 / 错误态**也必须带 `<main class="itemView …">` 滚动壳**。
         改前这两个状态是裸 <div>，而 `<main>` 写在 v-else-if="item" 里 —— 于是从点卡片到数据
         回来的那段时间（真机实测浏览器首屏 8.5s）整页**不存在滚动容器**，侧栏又是 docked 常驻，
         全屏唯一可滚的就只剩侧栏自己的 `.s-nav.s-scroll`：滚轮滚的是侧栏，详情页一动不动。
         这就是用户报的「侧边栏会常驻 导致不能滑动详情页 看不到信息」。
         壳上那一串 class 与正常态逐字相同，所以三种状态互相切换时布局零位移。 -->
    <main
      v-if="loading"
      class="itemView darkContentContainer-item view flex flex-direction-column scrollFrameY flex-grow emby-scroller page page-withFullDrawer page-withDockedDrawer focuscontainer-x"
      aria-busy="true"
    >
      <div class="scrollSlider flex-grow flex-direction-column padded-top-page scrollSliderY focuscontainer-y navout-up">
        <div class="s-detailSkeleton" role="status">
          <div class="s-detailSkeleton-hero">
            <div class="s-detailSkeleton-poster"></div>
            <div class="s-detailSkeleton-main">
              <div class="s-detailSkeleton-bar s-detailSkeleton-barTitle"></div>
              <div class="s-detailSkeleton-bar s-detailSkeleton-barMeta"></div>
              <div class="s-detailSkeleton-bar"></div>
              <div class="s-detailSkeleton-bar s-detailSkeleton-barShort"></div>
              <div class="s-detailSkeleton-actions"><span></span><span></span><span></span></div>
              <span class="s-detailSkeleton-text">{{ t('common.loading') }}</span>
            </div>
          </div>
          <!-- 下方补一排卡片位：只留上半截时，1600×900 下下半屏全黑，像"只加载了一半" -->
          <div class="s-detailSkeleton-row" aria-hidden="true">
            <span></span><span></span><span></span><span></span><span></span><span></span>
          </div>
        </div>
      </div>
    </main>
    <main
      v-else-if="notFound"
      class="itemView darkContentContainer-item view flex flex-direction-column scrollFrameY flex-grow emby-scroller page page-withFullDrawer page-withDockedDrawer focuscontainer-x"
    >
      <div class="scrollSlider flex-grow flex-direction-column padded-top-page scrollSliderY focuscontainer-y navout-up">
        <div class="page-error">{{ t('item.notFound') }}</div>
      </div>
    </main>
    <template v-else-if="item">
      <main class="itemView darkContentContainer-item view flex flex-direction-column scrollFrameY flex-grow emby-scroller page page-withFullDrawer page-withDockedDrawer focuscontainer-x">
        <div class="scrollSlider flex-grow flex-direction-column padded-top-page scrollSliderY focuscontainer-y navout-up">
          <div class="topDetailsContainer flex flex-direction-row verticalSections-item">
            <div class="item-fixed-side padded-left padded-left-page hide flex flex-direction-column flex-shrink-zero">
              <div class="detailImageContainer detailImageContainer-side justify-content-center itemsContainer hide"></div>
            </div>
            <div class="flex-grow topDetailsMain flex flex-direction-column padded-bottom-focusscale topDetailsMain-graphic">
              <div class="itemBackdropContainer">
                <div v-if="backdropSrc" class="itemBackdrop" :style="{ backgroundImage: `url(${backdropSrc})` }"></div>
              </div>
              <div class="detailMainContainerParent verticalSections verticalSections-condensed detailMainContainerParent-fade">
                <div class="padded-left padded-left-page padded-right verticalSection detailMainContainer flex flex-direction-row align-items-flex-start detailMainContainer-withbackdrop detailMainContainer-withitembackdrop">
                  <!-- 海报 -->
                  <div class="detailImageContainer detailImageContainer-main flex flex-direction-column justify-content-center itemsContainer detailImageContainer-hidemobile detailImageContainer-portrait generalItemsContainer vertical-wrap">
                    <div class="card portraitCard detailImageContainerCard card-hoverable">
                      <div class="detailImageContainerCard-cardBox cardBox">
                        <div data-action="none" class="itemAction cardContent cardImageContainer cardContent-background detailImage cardContent-bxsborder-fv coveredImage coveredImage-noScale cardPadder-portrait detailImage-transparent">
                          <!-- 没有图源时 posterSrc 为空串，用 v-if 整个不渲染 <img>：避免 src="" 让浏览器去请求页面本身。
                               但"不发请求"≠"没问题" —— 少了 <img> 之后这个 2:3 槽位里**什么都没有**，
                               实测变成一块纯色空白（不是品牌渐变）：
                                 · /item/1142553、/item/1107398（自己无 Primary、父级无 Backdrop、无系列海报）
                                   槽区 339×492，向下 4 层唯一带背景的是 .cardOverlayContainer 与 .fab 按钮，
                                   容器本身 background-image: none / background-color: rgba(0,0,0,0)；
                                   槽区逐像素 luma 标准差 0.49094、唯一颜色数 27（对照有图的 /item/1310342：70.12841 / 77520）。
                               所以补一个同级占位层，用与 EmbyCard 缺图兜底**同一个令牌** --s-gradient-dusk。
                               （不加 mark-white.svg：那会多一个网络请求，纯 CSS 渐变就够。） -->
                          <div v-if="!posterSrc" class="s-card-fallback detailPosterFallback" aria-hidden="true"></div>
                          <img v-if="posterSrc" draggable="false" alt=" " class="cardImage cardImage-bxsborder-fv coveredImage coveredImage-noScale" :src="posterSrc" loading="lazy" decoding="async" />
                        </div>
                        <div class="cardOverlayContainer itemAction cardPadder-portrait" data-action="none">
                          <button tabindex="-1" type="button" title="播放" class="fab cardOverlayButton-fab buttonItems-item itemAction cardOverlayFab-primary button-hoveraccent md-icon md-icon-fill autortl" data-action="resume" @click="play()">{{ ICON_PLAY }}</button>
                          <div class="cardOverlayButton-br buttonItems"></div>
                        </div>
                      </div>
                    </div>
                  </div>

                  <!-- 文本区 -->
                  <div class="flex-grow detailTextContainer verticalFieldItems details-largefont">
                    <div class="detailNameContainer focuscontainer-x verticalFieldItem verticalFieldItems detail-lineItem focusable">
                      <div class="verticalFieldItem flex align-items-center flex-wrap-wrap itemPrimaryNameContainer">
                        <h1 class="itemName-primary">{{ item.Name }}</h1>
                        <button type="button" class="btnDetailEdit btnEditMetadata secondaryText flex-shrink-zero paper-icon-button-light emby-button-focusscale" title="编辑元数据" @click="openEditMetadata">
                          <i class="md-icon autortl">{{ ICON_EDIT }}</i>
                        </button>
                        <button type="button" class="btnDetailEdit btnEditImages secondaryText flex-shrink-zero paper-icon-button-light emby-button-focusscale" title="编辑图像" @click="editImagesOpen = true">
                          <i class="md-icon">{{ ICON_PHOTO }}</i>
                        </button>
                      </div>
                    </div>

                    <div class="mediaInfo verticalFieldItem detail-mediaInfoPrimary flex align-items-center flex-wrap-wrap mediaInfoItems readOnlyContent detail-lineItem detail-mediaInfoPrimary-autocondense focusable focuscontainer-x">
                      <div v-if="item.CommunityRating" class="starRatingContainer mediaInfoItem">
                        <i class="md-icon md-icon-fill starIcon">{{ ICON_STAR }}</i>{{ item.CommunityRating.toFixed(1) }}
                      </div>
                      <div v-if="yearText" class="mediaInfoItem">{{ yearText }}</div>
                      <div v-if="runtimeText" class="mediaInfoItem">{{ runtimeText }}</div>
                      <div v-if="item.Studios?.length" class="mediaInfoItem">
                        <a class="button-link button-link-color-inherit emby-button emby-button-focusscale" @click="filterByStudio(item.Studios?.[0]?.Name)">{{ item.Studios?.[0]?.Name }}</a>
                      </div>
                      <div v-if="seasonCountText" class="mediaInfoItem">{{ seasonCountText }}</div>
                      <div v-for="g in item.Genres ?? []" :key="g" class="mediaInfoItem">
                        <a class="button-link button-link-color-inherit emby-button emby-button-focusscale" @click="filterByGenre(g)">{{ g }}</a>
                      </div>
                      <div v-if="endsAtText" class="mediaInfoItem">{{ endsAtText }}</div>
                    </div>

                    <!-- 媒体流选择(真实站 trackSelections:多版本/多视频轨/多音轨/字幕全可选) -->
                    <div v-if="activeSource" class="verticalFieldItem trackSelections fieldItems flex align-items-center flex-wrap-wrap detail-lineItem">
                      <EmbySelect
                        v-if="versionOptions.length > 1"
                        label="版本"
                        :model-value="currentSourceId"
                        :options="versionOptions"
                        @update:model-value="pickVersion"
                      />
                      <EmbySelect
                        v-if="videoTrackOptions.length > 1"
                        label="视频"
                        v-model="videoTrackId"
                        :options="videoTrackOptions"
                      />
                      <EmbySelect
                        v-else
                        label="视频"
                        :model-value="videoTrackId"
                        :options="videoTrackOptions"
                        disabled
                      />
                      <EmbySelect
                        v-if="audioTrackOptions.length > 1"
                        label="音频"
                        v-model="audioTrackId"
                        :options="audioTrackOptions"
                      />
                      <EmbySelect
                        v-else
                        label="音频"
                        :model-value="audioTrackId"
                        :options="audioTrackOptions"
                        disabled
                      />
                      <EmbySelect
                        label="字幕"
                        v-model="subtitleTrackId"
                        :options="subtitleTrackOptions"
                      />
                    </div>

                    <!-- 主按钮组(真实站:无进度 → btnPlay 白胶囊;有进度 → btnResume 白胶囊;另一颗 hide) -->
                    <div class="verticalFieldItem detailButtons mainDetailButtons flex align-items-flex-start flex-wrap-wrap focuscontainer-x detail-lineItem detailButtons-margin focusable">
                      <button type="button" class="btnResume raised detailButton detailButton-primary detailButton-stacked emby-button emby-button-focusscale" :class="{ hide: resumePct <= 0 }" data-mode="resume" title="恢复播放" @click="play()">
                        <i class="md-icon md-icon-fill button-icon button-icon-left autortl">{{ ICON_PLAY }}</i>
                        <span class="resumeButtonText detailButton-highres">恢复播放</span>
                      </button>
                      <button type="button" class="btnPlay btnMainPlay raised detailButton detailButton-primary detailButton-stacked emby-button emby-button-focusscale" :class="{ hide: resumePct > 0 }" data-mode="play" title="播放" @click="play()">
                        <i class="md-icon md-icon-fill button-icon button-icon-left autortl">{{ ICON_PLAY }}</i>
                        <span class="playButtonText">播放</span>
                      </button>
                      <button type="button" class="btnPlayTrailer btnPlayTrailer-main raised detailButton emby-button emby-button-focusscale detailButton-stacked hide" :class="{ hide: item.Type === 'Movie' }" title="预告片" @click="playTrailer">
                        <i class="md-icon button-icon button-icon-left">{{ ICON_MOVIES }}</i>
                        <span>{{ t('item.trailer') }}</span>
                      </button>
                      <button type="button" class="btnPlaystate fab detailButton detailButton-autotext emby-button emby-button-focusscale" :data-played="String(!!ud.Played)" title="标记为已播放" @click="togglePlayed">
                        <i class="md-icon detailButtonIcon detailButton-autotext-icon fab-icon playstatebutton-icon" :style="ud.Played ? 'color:var(--theme-primary-color)' : ''">{{ ICON_CHECK }}</i>
                        <div class="detailButton-autotext-text secondaryText button-text">{{ ud.Played ? '已播放' : '未播放' }}</div>
                      </button>
                      <button type="button" class="btnUserRating fab detailButton detailButton-autotext emby-button emby-button-focusscale" :data-isfavorite="String(!!ud.IsFavorite)" title="收藏" @click="toggleFavorite">
                        <i class="md-icon detailButtonIcon detailButton-autotext-icon fab-icon" :style="ud.IsFavorite ? 'color:var(--theme-primary-color)' : ''">{{ ICON_FAV }}</i>
                        <div class="detailButton-autotext-text secondaryText button-text">{{ ud.IsFavorite ? '收藏中' : '收藏' }}</div>
                      </button>
                      <button type="button" class="btnMoreCommands fab detailButton detailButton-autotext emby-button emby-button-focusscale" title="更多" @click="menuOpen = true">
                        <i class="md-icon detailButton-autotext-icon fab-icon">{{ ICON_MORE }}</i>
                        <div class="detailButton-autotext-text secondaryText button-text">更多</div>
                      </button>
                    </div>

                    <!-- 进度行 -->
                    <div v-if="resumePct > 0" class="detailResumeInfo flex flex-wrap-wrap flex-direction-column">
                      <div class="resumeInfoDetail verticalFieldItems">
                        <div class="resumeProgress verticalFieldItem flex align-items-center">
                          <div class="itemProgressBar resumeInfoProgressBar">
                            <div class="itemProgressBarForeground" :style="{ width: resumePct * 100 + '%' }"></div>
                          </div>
                          <div class="resumeTimeRemaining secondaryText">{{ resumeRemaining }}</div>
                        </div>
                      </div>
                    </div>

                    <div class="verticalFieldItem overview-container flex flex-direction-row align-items-flex-end focuscontainer-x detail-lineItem detailText-moveup">
                      <div class="overview-text readOnlyContent" :class="{ 'overview-text-expanded': overviewExpanded }" ref="overviewEl">
                        <span>{{ item.Overview }}</span>
                        <button v-if="!overviewExpanded && overviewClamped" type="button" class="btnReadMore button-link button-link-color-inherit emby-button emby-button-focusscale" @click="overviewExpanded = true">更多</button>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- 追加内容:分集/演职人员/相似 -->
          <div class="details-additionalContent padded-bottom-page verticalSections verticalSections-item details-additionalContent-withbackdrop">
            <div v-if="item.Type === 'Series'" class="verticalSection verticalSection-cards seriesItemsSection emby-scrollbuttons-scroller">
              <div class="emby-scrollbuttons">
                <div class="scrollbuttoncontainer scrollbuttoncontainer-backwards">
                  <button tabindex="-1" type="button" data-direction="backwards" class="emby-scrollbuttons-scrollbutton paper-icon-button-light emby-button-focusscale" @click="scrollStrip($event, -1)">
                    <i class="md-icon autortl">{{ ICON_CHEV_L }}</i>
                  </button>
                </div>
                <div class="scrollbuttoncontainer scrollbuttoncontainer-forwards">
                  <button tabindex="-1" type="button" data-direction="forwards" class="emby-scrollbuttons-scrollbutton paper-icon-button-light emby-button-focusscale" @click="scrollStrip($event, 1)">
                    <i class="md-icon autortl">{{ ICON_CHEV_R }}</i>
                  </button>
                </div>
              </div>
              <div class="sectionTitleContainer sectionTitleContainer-cards padded-left padded-left-page padded-right">
                <h2 class="sectionTitle sectionTitle-cards">{{ seasons.find((s) => s.Id === currentSeasonId)?.Name ?? t('item.episodes') }}</h2>
                <!-- 多季选择(真实站:剧集页季选择器;单季不显示) -->
                <EmbySelect
                  v-if="seasons.length > 1"
                  class="seasonSelect"
                  :model-value="currentSeasonId ?? ''"
                  :options="seasonOptions"
                  @update:model-value="pickSeason"
                />
              </div>
              <div class="emby-scroller padded-top-focusscale padded-bottom-focusscale padded-left padded-left-page padded-right scrollX hiddenScrollX scrollFrameX flex-direction-row">
                <div v-auto-animate="listFlip" class="scrollSlider focuscontainer-x itemsContainer focusable scrollSliderX emby-scrollbuttons-scrollSlider">
                  <div v-for="ep in episodes" :key="ep.Id" class="card backdropCard card-horiz backdropCard-horiz card-hoverable card-autoactive" @click="goEpisode(ep)">
                    <div class="cardBox cardBox-touchzoom">
                      <button type="button" data-action="link" tabindex="-1" class="itemAction cardContent-button cardContent cardImageContainer cardContent-background cardContent-bxsborder-fv coveredImage coveredImage-noScale cardPadder-backdrop">
                        <!-- 图源走 epArt（没图源就不请求，见其注释）；分集卡自己的 Primary 在真机上普遍缺失 -->
                        <img draggable="false" alt=" " class="cardImage cardImage-bxsborder-fv coveredImage coveredImage-noScale" :src="epArt(ep)" loading="lazy" decoding="async" />
                      </button>
                      <div class="cardOverlayContainer itemAction cardPadder-backdrop" data-action="link" @click.stop="goEpisode(ep)">
                        <button tabindex="-1" type="button" title="播放" class="fab cardOverlayButton-fab buttonItems-item itemAction cardOverlayFab-primary button-hoveraccent md-icon md-icon-fill autortl" data-action="play" @click.stop="play(ep.Id)">{{ ICON_PLAY }}</button>
                        <div class="cardOverlayButton-br buttonItems">
                          <button tabindex="-1" type="button" data-action="none" :title="ep.UserData?.Played ? '标记为未播放' : '标记为已播放'" class="fab cardOverlayButton-fab buttonItems-item itemAction md-icon fab playstatebutton-icon" :class="{ 'playstatebutton-icon-played': ep.UserData?.Played }" :data-played="String(!!ep.UserData?.Played)" @click.stop="toggleEpisodePlayed(ep)">{{ ICON_CIRCLE_CHECK }}</button>
                          <button tabindex="-1" type="button" data-action="none" :title="ep.UserData?.IsFavorite ? '取消收藏' : '添加到收藏'" class="fab cardOverlayButton-fab buttonItems-item itemAction md-icon fab" :class="{ 'ratingbutton-icon-withrating md-icon-fill': ep.UserData?.IsFavorite }" :data-isfavorite="String(!!ep.UserData?.IsFavorite)" @click.stop="toggleEpisodeFavorite(ep)">{{ ICON_FAV }}</button>
                          <button tabindex="-1" type="button" title="更多" class="md-icon fab cardOverlayButton-fab buttonItems-item itemAction" data-action="menu" @click.stop="openEpisodeMenu(ep)">{{ ICON_MORE }}</button>
                        </div>
                      </div>
                      <div class="cardText text-align-start cardText-first cardText-first-padded">
                        <!-- 集号用真实字段 IndexNumber（EpisodeNumber 在真实 Emby 的 BaseItemDto 上不存在）；
                             缺失时只显示标题，不要渲染出一个 "undefined. 第 13 集" -->
                        <button type="button" :title="ep.Name" class="cardMediaInfoItem itemAction textActionButton cardTextActionButton emby-button button-link">{{ ep.IndexNumber != null ? `${ep.IndexNumber}. ` : '' }}{{ ep.Name ?? '' }}</button>
                      </div>
                      <div class="cardText cardText-secondary cardText text-align-start mediaInfoItems cardText-mediaInfo">
                        <div v-if="ep.PremiereDate" class="mediaInfoItem">{{ fmtDate(ep.PremiereDate) }}</div>
                        <div v-if="ep.RunTimeTicks" class="mediaInfoItem">{{ Math.round(ep.RunTimeTicks / 600_000_000) }}分钟</div>
                      </div>
                      <div v-if="ep.Overview" class="cardText cardText-secondary cardText-overview">
                        <div class="cardText-overview-text">{{ ep.Overview }}</div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div v-if="people.length" class="verticalSection verticalSection-cards peopleSection emby-scrollbuttons-scroller">
              <div class="emby-scrollbuttons">
                <div class="scrollbuttoncontainer scrollbuttoncontainer-backwards">
                  <button tabindex="-1" type="button" data-direction="backwards" class="emby-scrollbuttons-scrollbutton paper-icon-button-light emby-button-focusscale" @click="scrollStrip($event, -1)">
                    <i class="md-icon autortl">{{ ICON_CHEV_L }}</i>
                  </button>
                </div>
                <div class="scrollbuttoncontainer scrollbuttoncontainer-forwards">
                  <button tabindex="-1" type="button" data-direction="forwards" class="emby-scrollbuttons-scrollbutton paper-icon-button-light emby-button-focusscale" @click="scrollStrip($event, 1)">
                    <i class="md-icon autortl">{{ ICON_CHEV_R }}</i>
                  </button>
                </div>
              </div>
              <div class="sectionTitleContainer sectionTitleContainer-cards padded-left padded-left-page padded-right">
                <h2 class="sectionTitle sectionTitle-cards">{{ t('item.cast') }}</h2>
              </div>
              <div class="emby-scroller padded-top-focusscale padded-bottom-focusscale padded-left padded-left-page padded-right scrollX hiddenScrollX scrollFrameX flex-direction-row">
                <div class="scrollSlider focuscontainer-x itemsContainer focusable scrollSliderX emby-scrollbuttons-scrollSlider">
                  <div v-for="p in people" :key="p.Id + p.Role" class="card portraitCard card-horiz portraitCard-horiz card-hoverable card-autoactive">
                    <div class="cardBox cardBox-touchzoom">
                      <button type="button" data-action="link" tabindex="-1" class="itemAction cardContent-button cardContent cardImageContainer cardContent-background cardContent-bxsborder-fv coveredImage coveredImage-noScale cardPadder-portrait" @click="openPerson(p)">
                        <!-- 图源走 personArt（tag 在 PrimaryImageTag 上；没 tag 就不请求，见其注释） -->
                        <img draggable="false" alt=" " class="cardImage cardImage-bxsborder-fv coveredImage coveredImage-noScale" :src="personArt(p)" loading="lazy" decoding="async" />
                      </button>
                      <div class="cardOverlayContainer itemAction cardPadder-portrait" data-action="link">
                        <div class="cardOverlayButton-br buttonItems">
                          <button type="button" title="更多" class="md-icon fab cardOverlayButton-fab buttonItems-item itemAction emby-button emby-button-focusscale" @click.stop="openPersonMenu(p)">
                            <i class="md-icon">{{ ICON_MORE }}</i>
                          </button>
                        </div>
                      </div>
                      <div class="cardText cardText-first cardText-first-padded">
                        <button type="button" :title="p.Name" class="cardMediaInfoItem itemAction textActionButton cardTextActionButton emby-button button-link" @click="openPerson(p)">{{ p.Name }}</button>
                      </div>
                      <div v-if="p.Role" class="cardText cardText-secondary">
                        <button type="button" class="cardMediaInfoItem itemAction textActionButton cardTextActionButton emby-button button-link" @click="openPerson(p)">{{ p.Role }}</button>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <!-- 其他信息(真实站 aboutSection;电影/剧集通用) -->
            <div class="verticalSection verticalSection-cards aboutSection">
              <h2 class="sectionTitle sectionTitle-cards padded-left padded-left-page padded-right">{{ t('item.about') }}</h2>
              <div class="flex flex-direction-column verticalSections verticalSections-condensed">
                <div v-if="item.Genres?.length" class="verticalSection verticalSection-cards genresSection">
                  <h4 class="sectionTitle sectionTitle-cards padded-left padded-left-page padded-right">{{ t('item.genres') }}</h4>
                  <div class="sectionTitle sectionTitle-cards itemGenres padded-left padded-left-page padded-right secondaryText textItems">
                    <a v-for="(g, i) in item.Genres" :key="g" class="button-link button-link-color-inherit button-link-fontweight-inherit emby-button emby-button-focusscale" @click="filterByGenre(g)">{{ g }}{{ i < item.Genres.length - 1 ? ',' : '' }}</a>
                  </div>
                </div>
                <div v-if="item.Studios?.length" class="verticalSection verticalSection-cards studiosSection">
                  <h4 class="sectionTitle sectionTitle-cards padded-left padded-left-page padded-right">{{ t('item.studios') }}</h4>
                  <div class="sectionTitle sectionTitle-cards itemStudios padded-left padded-left-page padded-right secondaryText textItems">
                    <a v-for="st in item.Studios" :key="st.Id" class="button-link button-link-color-inherit button-link-fontweight-inherit emby-button emby-button-focusscale" @click="filterByStudio(st.Name)">{{ st.Name }}</a>
                  </div>
                </div>
                <div v-if="item.MediaSources?.length" class="verticalSection verticalSection-cards audioVideoMediaInfo">
                  <h4 class="padded-left padded-left-page padded-right sectionTitle sectionTitle-cards mediaInfoHeader">{{ t('item.mediaInfo') }}</h4>
                  <div class="mediaSources">
                    <div v-for="ms in item.MediaSources" :key="ms.Id" class="mediaSource emby-scrollbuttons-scroller">
                      <div class="padded-left padded-left-page padded-right">
                        <div class="sectionTitle sectionTitle-cards secondaryText">
                          <div>{{ ms.Path }}</div>
                          <div class="mediaInfoItems">
                            {{ (ms.Container ?? '').toUpperCase() }}<span class="mediaInfoItem">{{ formatSize(ms.Size) }}</span><span class="mediaInfoItem">{{ t('item.addedAt', { time: fmtDate(item.DateCreated) }) }}</span>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div v-if="similar.length" class="verticalSection verticalSection-cards similarSection emby-scrollbuttons-scroller">
              <div class="sectionTitleContainer sectionTitleContainer-cards padded-left padded-left-page padded-right">
                <h2 class="sectionTitle sectionTitle-cards">{{ t('item.similar') }}</h2>
              </div>
              <div class="emby-scroller padded-top-focusscale padded-bottom-focusscale padded-left padded-left-page padded-right scrollX hiddenScrollX scrollFrameX flex-direction-row">
                <div class="scrollSlider focusable itemsContainer focuscontainer-x scrollSliderX emby-scrollbuttons-scrollSlider">
                  <EmbyCard v-for="s in similar" :key="s.Id" :item="s" shape="portrait" />
                </div>
              </div>
            </div>
          </div>
        </div>
      </main>
      <ItemMenuSheet
        v-if="item"
        :item="item"
        :open="menuOpen"
        @close="menuOpen = false"
        @toggle-played="togglePlayed"
        @toggle-favorite="toggleFavorite"
        @command="onItemCommand"
      />

      <!-- 分集"更多"菜单(复用 actionSheet 组件) -->
      <ItemMenuSheet
        v-if="episodeMenuTarget"
        :item="episodeMenuTarget"
        :open="!!episodeMenuTarget"
        @close="episodeMenuTarget = null"
        @toggle-played="toggleEpisodePlayedFromMenu"
        @toggle-favorite="toggleEpisodeFavoriteFromMenu"
      />

      <!-- 编辑元数据(formdialog;真实站 dialog/formdialog 形态)
           行为(焦点陷阱/Esc/点外关闭/滚动锁定/进出动效)交给 SSheet;可见标题仍留在插槽里 -->
      <SSheet
        :open="editMetadataOpen"
        variant="center"
        :title="t('menu.editMeta')"
        panel-class="dialog formDialog"
        :content-style="CENTER_PANEL_STYLE"
        @close="editMetadataOpen = false"
      >
            <div class="formDialogHeader">
              <button type="button" class="paper-icon-button-light s-press btnCloseDialog emby-button-focusscale" title="关闭" @click="editMetadataOpen = false">
                <i class="md-icon autortl">&#xE2EA;</i>
              </button>
              <h3 class="formDialogHeaderTitle">编辑元数据</h3>
            </div>
            <div class="formDialogContent">
              <div class="inputContainer">
                <label class="inputLabel inputLabelUnfocused" for="editName">名称</label>
                <input id="editName" v-model="editForm.Name" type="text" class="emby-input" />
              </div>
              <div class="inputContainer">
                <label class="inputLabel inputLabelUnfocused" for="editYear">年份</label>
                <input id="editYear" v-model="editForm.ProductionYear" type="number" class="emby-input" />
              </div>
              <div class="inputContainer">
                <label class="inputLabel inputLabelUnfocused" for="editRating">评分</label>
                <input id="editRating" v-model="editForm.CommunityRating" type="number" step="0.1" class="emby-input" />
              </div>
              <div class="inputContainer">
                <label class="inputLabel inputLabelUnfocused" for="editOverview">简介</label>
                <textarea id="editOverview" v-model="editForm.Overview" class="emby-textarea emby-input" rows="4"></textarea>
              </div>
            </div>
            <div class="formDialogFooter">
              <button type="button" class="raised button-cancel s-press formDialogFooterItem emby-button emby-button-focusscale" @click="editMetadataOpen = false">取消</button>
              <button type="button" class="raised button-submit s-press formDialogFooterItem emby-button emby-button-focusscale" @click="saveMetadata">保存</button>
            </div>
      </SSheet>

      <!-- 编辑图像(结构照真实站 imageeditor:图像类型标签 + 类型列表 + 当前图预览 + 上传/删除/刷新) -->
      <SSheet
        :open="editImagesOpen"
        variant="center"
        :title="t('menu.editImages')"
        panel-class="dialog formDialog imageEditorDialog"
        :content-style="IMAGE_EDITOR_PANEL_STYLE"
        @close="editImagesOpen = false"
      >
            <div class="formDialogHeader">
              <button type="button" class="paper-icon-button-light s-press btnCloseDialog emby-button-focusscale" title="关闭" @click="editImagesOpen = false">
                <i class="md-icon autortl">&#xE2EA;</i>
              </button>
              <h3 class="formDialogHeaderTitle">编辑图像</h3>
            </div>
            <div class="imageEditorBody flex flex-direction-row">
              <div class="imageTypeList vertical-list">
                <button
                  v-for="it in IMAGE_TYPES" :key="it.type" type="button"
                  class="listItem s-press listItem-autoactive itemAction listItemCursor imageTypeOption emby-button-focusscale"
                  :class="{ 'imageTypeOption-selected': imageType === it.type }"
                  @click="imageType = it.type"
                >
                  <div class="listItem-content listItem-content-bg listItem-border actionsheet-noborder">
                    <div class="listItemBody listItemBody-1-lines">
                      <div class="listItemBodyText listItemBodyText-lf">{{ it.label }}</div>
                    </div>
                  </div>
                </button>
              </div>
              <div class="imageEditorPreview flex flex-direction-column align-items-center justify-content-center">
                <img
                  v-if="item?.Id" alt=""
                  class="imageEditorPreviewImage"
                  :src="imagePreviewSrc()"
                />
                <div class="fieldDescription">{{ t('item.imageEditorHint') }}</div>
              </div>
            </div>
            <div class="formDialogFooter">
              <button type="button" class="raised button-cancel s-press formDialogFooterItem emby-button emby-button-focusscale" title="删除" @click="deleteImage">删除</button>
              <button type="button" class="raised button-cancel s-press formDialogFooterItem emby-button emby-button-focusscale" title="刷新" @click="imageAction('刷新')">刷新</button>
              <label class="raised button-submit s-press formDialogFooterItem emby-button emby-button-focusscale imageUploadButton">
                <span>{{ t('item.imageUpload') }}</span>
                <input type="file" accept="image/*" class="imageUploadInput" @change="onImagePicked" />
              </label>
              <button type="button" class="raised button-submit s-press formDialogFooterItem emby-button emby-button-focusscale" @click="editImagesOpen = false">{{ t('library.done') }}</button>
            </div>
      </SSheet>

      <!-- 人物信息窗体(真实站跳人物页;v1 面板 + 关闭) -->
      <SSheet
        :open="!!personSheet"
        variant="center"
        :title="personSheet?.Name"
        panel-class="dialog actionSheet actionSheet-automobile"
        :content-style="CENTER_PANEL_STYLE"
        @close="personSheet = null"
      >
            <div class="actionsheetItemPreview flex flex-shrink-zero flex-direction-row">
              <!-- 人物面板头图：原实现无条件拼 Primary（tag 传 undefined）—— 真机对没有头像的人物返回 500。
                   改走 personArtUrl：没 tag 返回空串，此时不设 backgroundImage，不发请求。 -->
              <div class="actionsheetItemPreviewImage-bg actionsheetItemPreviewImage-bg-portrait" :style="personSheetArt ? { backgroundImage: `url(${personSheetArt})` } : undefined"></div>
              <div class="actionsheetItemPreviewContent">
                <div class="actionsheetItemPreviewText mediaInfoItems actionsheetItemPreviewText-main">
                  <div class="actionsheetPreviewTextItem mediaInfoItem">{{ personSheet?.Name }}</div>
                </div>
                <div class="actionsheetItemPreviewText mediaInfoItems secondaryText">
                  <div class="actionsheetPreviewTextItem mediaInfoItem">{{ personSheet?.Role }}</div>
                </div>
              </div>
            </div>
            <div class="actionSheetScroller focuscontainer-x actionSheetScroller-padding-inline emby-scroller dialog-scrollY scrollY scrollFrameY flex-direction-column">
              <div class="actionsheetScrollSlider scrollSlider flex flex-direction-column itemsContainer vertical-list scrollSliderY">
                <button type="button" class="listItem s-press listItem-autoactive itemAction listItemCursor actionSheetMenuItem" @click="personSheet = null">
                  <div class="listItem-content listItem-content-bg actionsheet-noborder">
                    <div class="listItemBody actionsheetListItemBody listItemBody-1-lines">
                      <div class="listItemBodyText actionSheetItemText listItemBodyText-lf">关闭</div>
                    </div>
                  </div>
                </button>
              </div>
            </div>
      </SSheet>
    </template>
  </div>
</template>

<style scoped>
/* 主海报缺图兜底。
   `EmbyCard.vue` 里的 `.s-card-fallback` 是 **scoped** 样式（带 EmbyCard 的 data-v 属性），
   到不了这个视图，所以这里的几何要自带一份；**渐变令牌共用** var(--s-gradient-dusk)，
   与 .s-card-fallback / hero 的 .hero-brand / DashboardView 的空态占位是同一套语言。
   父容器（.cardPadder-portrait）实测 position: relative + 328×492，inset:0 可铺满；
   绝对定位的兄弟层不参与布局，槽位几何（.detailImageContainerCard 339×492）保持原样。 */
.detailPosterFallback {
  position: absolute;
  inset: 0;
  display: block;
  background: var(--s-gradient-dusk);
  pointer-events: none;
}

/* 简介折叠(真实站 .overview-text + btnReadMore:未展开限 3 行,点"更多"展开) */
.overview-text {
  display: -webkit-box;
  -webkit-line-clamp: 3;
  line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
  white-space: pre-line;
  text-align: start;
}
.overview-text-expanded {
  -webkit-line-clamp: unset;
  line-clamp: unset;
  overflow: visible;
}
.btnReadMore {
  display: block;
  margin-top: .2em;
  text-align: start;
}

/* 图像编辑器(imageeditor 结构:左侧类型列表 + 右侧预览)
   面板宽度已移到 :content-style(IMAGE_EDITOR_PANEL_STYLE)：面板由 SSheet 渲染，
   拿不到本视图的 scoped data-v，`.imageEditorDialog{width:...}` 在这里是死代码 */
.imageEditorBody { display: flex; gap: 1em; padding: 0 1.5em 1em; max-height: 60vh; overflow: auto; }
.imageTypeList { flex-shrink: 0; width: 10em; }
/* 选中态走 token：原「白 12%」在浅色抬升面上几乎不可见（与库页 .filterChip 同一类遗留硬伤） */
.imageTypeOption-selected { background: var(--s-hover); border-radius: .4em; }
.imageEditorPreview { flex-grow: 1; min-height: 16em; gap: .6em; }
.imageEditorPreviewImage { max-width: 100%; max-height: 45vh; border-radius: .3em; }
.imageUploadButton { position: relative; overflow: hidden; cursor: pointer; }
.imageUploadInput { position: absolute; inset: 0; opacity: 0; cursor: pointer; }

/* 详情主按钮(detailButton-primary)—— 真实站实测:白 90% 底 + 黑字 */
.detailButton-primary {
  background: rgba(255, 255, 255, .9) !important;
  color: #000 !important;
}
.detailButton-primary .md-icon { color: #000; }

/* ============================================================================
   详情页骨架屏（加载态专用）
   ============================================================================
   为什么要有：改前加载态是 `emby495-overrides.css` 里 `.page-loading`（padding 4em + 一行居中灰字），
   详情页在那个状态下既没有 `<main>` 壳也没有任何结构，用户看到的是「常驻侧栏 + 一行"加载中…"」，
   判断成"页面坏了/滑不动"。骨架先把版式立起来：海报位、标题位、信息行、按钮位。
   时长上它现在只覆盖 getItem 的 630ms（次屏已并行），不是原来的 8.5s。
   颜色全部走 --s-* 令牌，深浅主题自动跟。 */
.s-detailSkeleton {
  display: flex;
  flex-direction: column;
  gap: 28px;
  padding: 0 var(--s-gutter) 48px;
  max-inline-size: 1100px;
}
.s-detailSkeleton-hero {
  display: flex;
  flex-direction: row;
  align-items: flex-start;
  gap: 24px;
}
.s-detailSkeleton-row {
  display: flex;
  flex-direction: row;
  gap: var(--s-gutter-sm);
}
.s-detailSkeleton-row span {
  flex: 1 1 0;
  min-inline-size: 0;
  aspect-ratio: 2 / 3;
  border-radius: var(--s-r-sm);
  background: var(--s-raised);
  animation: s-skeleton-pulse 1.4s ease-in-out infinite;
}
.s-detailSkeleton-poster {
  flex-shrink: 0;
  inline-size: 232px;
  aspect-ratio: 2 / 3;
  border-radius: var(--s-r-md);
  background: var(--s-raised);
  animation: s-skeleton-pulse 1.4s ease-in-out infinite;
}
.s-detailSkeleton-main {
  flex: 1 1 auto;
  min-inline-size: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding-top: 24px;
}
.s-detailSkeleton-bar {
  block-size: 14px;
  inline-size: 88%;
  border-radius: var(--s-r-sm);
  background: var(--s-raised);
  animation: s-skeleton-pulse 1.4s ease-in-out infinite;
}
.s-detailSkeleton-barTitle { block-size: 34px; inline-size: 56%; }
.s-detailSkeleton-barMeta { block-size: 12px; inline-size: 34%; }
.s-detailSkeleton-barShort { inline-size: 62%; }
.s-detailSkeleton-actions {
  display: flex;
  gap: 12px;
  margin-top: 8px;
}
.s-detailSkeleton-actions span {
  inline-size: 44px;
  block-size: 44px;
  border-radius: 50%;
  background: var(--s-raised);
  animation: s-skeleton-pulse 1.4s ease-in-out infinite;
}
.s-detailSkeleton-text {
  margin-top: 4px;
  font-size: var(--s-fs-sm);
  color: var(--s-text-3);
}
@keyframes s-skeleton-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: .45; }
}
/* 尊重系统"减少动效"：不做呼吸，静态灰块 */
@media (prefers-reduced-motion: reduce) {
  .s-detailSkeleton-poster,
  .s-detailSkeleton-bar,
  .s-detailSkeleton-row span,
  .s-detailSkeleton-actions span { animation: none; }
}
/* 窄屏：海报收到上方、内容在下（否则 232px 海报 + 文字列在 390px 下会挤成一条）；
   占位卡从 6 列降到 3 列 */
@media (max-width: 700px) {
  .s-detailSkeleton { gap: 16px; }
  .s-detailSkeleton-hero { flex-direction: column; gap: 16px; }
  .s-detailSkeleton-poster { inline-size: 38%; }
  .s-detailSkeleton-main { padding-top: 0; }
  .s-detailSkeleton-row { flex-wrap: wrap; }
  .s-detailSkeleton-row span { flex: 0 0 calc((100% - 2 * var(--s-gutter-sm)) / 3); }
}
</style>