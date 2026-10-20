<script setup lang="ts">
/**
 * 条目卡 —— 1:1 还原 Emby 4.9.5 cardbuilder(基准=真实站运行时 dump):
 * .card.{backdrop|portrait}Card.card-hoverable.card-autoactive
 *   > .cardBox.cardBox-touchzoom[.cardBox-bottompadded]
 *     > button.itemAction.cardContent-button.cardContent.cardImageContainer.cardContent-background
 *       .cardContent-bxsborder-fv.coveredImage.coveredImage-noScale.cardPadder-{backdrop|portrait}
 *       > img.cardImage(+进度条 itemProgressBar)(+countIndicator)
 *     > .cardOverlayContainer.itemAction.cardPadder-*
 *       > 居中大播放 + .cardOverlayButton-br.buttonItems(已播/收藏/更多)
 *       （多选 label 已按用户要求整体移除，见 .cardOverlayContainer 处的注释）
 *     > .cardText.cardText-first.cardText-first-padded(+ .cardText.cardText-secondary)
 */
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { BaseItemDto } from '@/api/types'
import { cardArtUrl, markPlayed, markFavorite, getItems } from '@/api/endpoints'
import { useAuthStore } from '@/stores/auth'
import ItemMenuSheet from '@/components/ItemMenuSheet.vue'
import { showToast } from '@/api/toast'
import markWhite from '@/assets/brand/mark-white.svg'

const props = withDefaults(defineProps<{ item: BaseItemDto; shape?: 'portrait' | 'backdrop' | 'portrait-horiz' }>(), { shape: 'portrait' })
const router = useRouter()
const auth = useAuthStore()
const { t } = useI18n()

/**
 * 卡片形态:backdrop=16:9 剧照 / portrait=2:3 海报 / portrait-horiz=横向剧集卡(真实站 /tv 集 tab)
 *
 * ⚠️ `portrait-horiz` 在 **padder 上**现在只是 `backdrop` 的别名(都是 16:9),保留这个取值只是为了不改公开 prop 类型。
 * 原因:Emby **没有** `cardPadder-horiz` / `cardPadder-portrait-horiz` 这两个类 ——
 * vendor `assets/emby495/modules__cardbuilder__card.css` 的 `@supports` 块只定义了
 * `cardPadder-{backdrop, square, portrait, fourThree, banner}`,参考实现 `reference/emby-4.9.5/cardbuilder.js`
 * 也只是拼 `cardPadder-{shape}`。实测拼出不存在的类时图容器会塌成 **165×1 px**(同页 `cardPadder-portrait` = 135×202 px),
 * 卡片标题可见、图整条看不见。**不要再往这里拼不存在的类**;要新形状先在 vendor CSS 里确认类名存在。
 */
const isBackdrop = computed(() => props.shape === 'backdrop')
const isHoriz = computed(() => props.shape === 'portrait-horiz')
const padderCls = computed(() => {
  // portrait-horiz 与 backdrop 同为 16:9,复用真实存在的 backdrop padder(剧集图源本身就是 16:9:
  // 自己的 Primary 缩略图,或父级 series backdrop;实测 allNoArt = 0)
  if (isBackdrop.value || isHoriz.value) return 'cardPadder-backdrop'
  return 'cardPadder-portrait'
})
/**
 * 卡片图源 —— 必须走 `cardArtUrl`，**不能**在这里无条件拼 Primary。
 *
 * 原实现是 `imageUrl(item.Id, 'Primary', item.ImageTags?.Primary, …)`：tag 是 undefined 也照拼，
 * 于是真机 Emby 4.9.5.0 对「没有 Primary 图」的条目返回 **HTTP 500**（不是 404）：
 *   · 详情页「一个屋檐下」(Series 1256855)：100 集全无 Primary → 进一次页面打出 99×500 + 1×502，
 *     129 张卡只有 29 张出图（22.5%）；
 *   · 80 个系列 / 2039 集实测 **1262 集（61.9%）没有 Primary**，而它们全都能从
 *     系列海报（SeriesPrimaryImageTag）或父级剧照（ParentBackdropImageTags）拿到图；
 *   · 全站 13739 条真实条目里 1931 条没有 Primary。
 * `cardArtUrl` 先看 tag 再拼 URL，拿不到图源时返回空串 → 这里不渲染 <img>、不发请求，
 * 直接走下面的品牌渐变兜底层。横版卡退剧照、竖版卡退系列海报的取舍见 endpoints.ts 的注释。
 */
const imgSrc = computed(() =>
  // 请求尺寸必须与 padder 形态一致:`portrait-horiz` 是 `backdrop` 的别名(同为 16:9),
  // 尺寸要跟着一起走 16:9,否则会出现"padder 16:9 却按 300×450 要图"的错配(竖版尺寸的图被塞进横版框)。
  cardArtUrl(props.item, props.shape, isBackdrop.value || isHoriz.value
    ? { maxWidth: 525, maxHeight: 296, quality: 90 }
    : { maxWidth: 300, maxHeight: 450, quality: 90 }),
)
/** 有图源才渲染 <img>（空串 = 这张条目真没有可用图，直接露兜底层，见 imgFailed 注释） */
const hasArt = computed(() => imgSrc.value !== '')
/**
 * 图片解码完成标记（只驱动 .s-img/.s-card-img 的 opacity/filter 过渡，不改任何交互语义）：
 * 初始 blur(12px)+opacity 0 → load/error 后 blur(0)+opacity 1。
 * error 也置 true：图挂了就露占位底色，不能留一张永远不可见的模糊图。
 */
const imgLoaded = ref(false)
const imgEl = ref<HTMLImageElement | null>(null)
/**
 * 加载失败标记。
 *
 * 原来 `@error` 里写的是 `imgLoaded = true` —— 这会把那张图**显示出来**，
 * 浏览器于是就地画一个「碎图」图标，看起来像页面坏了。
 * 真实服务器上确实有缺海报的条目（22 个库里实测有），所以这条路径必须好看：
 * 失败时让 img 保持不可见，露出下面的品牌渐变兜底层。
 *
 * 同一层兜底也服务「一开始就没有图源」的卡片：`cardArtUrl` 返回空串时根本不渲染 <img>
 * （不发那次必然 500 的请求），此时 imgFailed 保持 false，所以模板里兜底层用
 * `!hasArt || imgFailed` 两个条件一起判定。
 */
const imgFailed = ref(false)
function onImgError() {
  imgFailed.value = true
}
onMounted(() => {
  // 命中缓存时 load 可能早于监听触发，补一次判定，避免卡片永久停在模糊态
  if (imgEl.value?.complete) {
    // complete 但自然宽为 0 = 解码失败（这种情况下浏览器不会补发 error 事件）
    if (imgEl.value.naturalWidth > 0) imgLoaded.value = true
    else imgFailed.value = true
  }
})

const progressPct = computed(() => {
  const ud = props.item.UserData
  if (!ud?.PlaybackPositionTicks || !props.item.RunTimeTicks) return 0
  return Math.min(100, (ud.PlaybackPositionTicks / props.item.RunTimeTicks) * 100)
})
const count = computed(() => props.item.UserData?.UnplayedItemCount)
/**
 * 制作年份：真实 Emby 里存在 `ProductionYear: 1` 这类脏数据
 * （实测「绝代风华苏东坡」「宝宝巴士之神奇简笔画」都是 1），
 * 直接渲染会在卡片副标题上留一个孤零零的「1」，看着像渲染坏了。1900 以前一律视为无效。
 */
const year = computed(() => {
  const y = props.item.ProductionYear
  return typeof y === 'number' && y >= 1900 ? y : null
})
/**
 * 副标题。
 *
 * 剧集分支原先读 `SeasonNumber` / `EpisodeNumber` —— **`BaseItemDto` 上根本没有这两个字段**。
 * 真实 Emby 返回的是 `ParentIndexNumber` / `IndexNumber`（实测：药屋少女的呢喃那集就是
 * `IndexNumber=51` / `ParentIndexNumber=1`，而 SeasonNumber / EpisodeNumber 全是 undefined）。
 * mock 数据恰好把这两个字段名喂成了合法值，于是这个字段名错误在本地永远测不出来，
 * 一接真实服务器就渲染出字面的 `Sundefined:Eundefined - 如果没有面包`。
 *
 * 现在按真实字段名取，并且**任一缺失就不拼 S/E 前缀**——绝不让 undefined 落到 UI。
 * 剧集优先「剧名 · S1:E51」：继续观看里原来只有「第 13 集」，完全没有剧名上下文，认不出是哪部剧。
 */
const subtitle = computed(() => {
  const i = props.item
  if (i.Type === 'Episode') {
    const s = i.ParentIndexNumber
    const e = i.IndexNumber
    const ep = s != null && e != null ? `S${s}:E${String(e).padStart(2, '0')}` : null
    const series = i.SeriesName
    if (series && ep) return `${series} · ${ep}`
    if (series) return series
    if (ep) return ep
    return year.value ? String(year.value) : ''
  }
  return year.value ? String(year.value) : ''
})
const isLibrary = computed(() => props.item.Type === 'CollectionFolder')

/**
 * 长按弹菜单后，浏览器还会补发一次 click（`touchend` → `click`）。
 * 不吞掉它的话，长按会「先弹菜单、再跳详情页」——触屏上菜单根本来不及看。
 * 只吞长按那一次（500ms 内），正常轻点卡片照常跳转。
 */
let suppressClickUntil = 0

/**
 * 预取「点下去要跳去的那个路由组件」。
 *
 * 为什么必须有：`/item/:id` 的组件是 `() => import('@/views/ItemDetailView.vue')`，
 * 而 vue-router 是在**所有守卫跑完之后**才去解析这个异步组件 —— 也就是「点击之后」才开始下载它。
 * 实测（CDP 限速 700ms 延迟 / 150KB/s）：点卡片 → URL 变成 /item 用了 **4842ms**；
 * dev 下更糟，Vite 要现场编译这个 1000+ 行的 SFC，实测有一次 2.5s 后仍停在库页
 * （router.beforeEach 已经带着 to=/item 触发了，afterEach 永远没来，无任何报错）。
 * 这段时间页面零反馈 —— 用户看到的就是「点了没反应，侧栏还常驻着」，也就是本轮报的那个现象。
 *
 * 鼠标进入卡片就预取一次：模块级缓存，重复调用零成本；失败静默，绝不影响正常点击。
 */
let routePrefetched = false
function prefetchRoute() {
  if (routePrefetched) return
  routePrefetched = true
  const loading = isLibrary.value
    ? import('@/views/LibraryView.vue')
    : import('@/views/ItemDetailView.vue')
  void loading.catch(() => {})
}

function open() {
  // 多选已按用户要求整体下线（「我们现在没有多选 卡片左上角的多选按钮不需要了」）：
  // 点卡片恒定 = 跳转，不存在「先勾选不跳转」的中间态。
  if (Date.now() < suppressClickUntil) return
  // 库卡 → 浏览页(真实站 CollectionFolder 的 data-action=link 走 /list);条目卡 → 详情
  if (props.item.Type === 'CollectionFolder') {
    router.push({ name: 'library', params: { id: props.item.Id } })
    return
  }
  router.push({ name: 'item', params: { id: props.item.Id } })
}

// ---- 悬浮按钮可用性(严格按真实 4.9.5 cardbuilder.js + playbackmanager.canPlay + itemmanager.canMarkPlayed/canRate) ----
const NO_PLAY = new Set(['GameSystem', 'Person', 'GameGenre', 'CollectionFolder:boxsets', 'CollectionFolder:playlists', 'CollectionFolder:games'])
/** playbackmanager.canPlay(item) */
const canPlay = computed(() => {
  const it = props.item
  if (it.Type === 'CollectionFolder') {
    return !['boxsets', 'playlists', 'games'].includes(String(it.CollectionType))
  }
  if (it.Type === 'Episode') return it.LocationType !== 'Virtual'
  return !NO_PLAY.has(String(it.Type))
})
/** itemmanager.canMarkPlayed(item):库卡/人物等不显示 */
const canMarkPlayed = computed(() => !['CollectionFolder', 'Person', 'Genre', 'Studio', 'Tag'].includes(String(props.item.Type)))
/** itemmanager.canRate(item) */
const canRate = computed(() => !['CollectionFolder', 'Person', 'Genre', 'Studio', 'Tag', 'Season'].includes(String(props.item.Type)))
const played = computed(() => !!props.item.UserData?.Played)
const isFavorite = computed(() => !!props.item.UserData?.IsFavorite)
/** 真实站:IsFolder → data-action=resume,否则 play */
const playAction = computed(() => (props.item.IsFolder ? 'resume' : 'play'))

/** 播放(本地 mock OSD;文件夹/剧集按真实站语义解析首个可播条目) */
async function onPlay() {
  const it = props.item
  try {
    if (it.IsFolder && it.Type !== 'CollectionFolder') {
      const r = await getItems(auth.user!.Id, {
        ParentId: it.Id, Recursive: true, IncludeItemTypes: 'Episode,Movie,Video', Limit: 1,
      })
      const first = r.Items[0]?.Id
      router.push({ name: 'play', params: { id: first ?? it.Id } })
      return
    }
    router.push({ name: 'play', params: { id: it.Id } })
  } catch {
    router.push({ name: 'play', params: { id: it.Id } })
  }
}

/** 已播/未播切换(真实站 emby-playstatebutton:updatePlayedStatus) */
async function togglePlayed() {
  const next = !played.value
  if (auth.user) void markPlayed(auth.user.Id, props.item.Id, next)
  props.item.UserData = {
    ...props.item.UserData,
    Played: next,
    PlaybackPositionTicks: next ? 0 : props.item.UserData?.PlaybackPositionTicks,
  }
}

/** 收藏切换(真实站 emby-ratingbutton:updateFavoriteStatus) */
async function toggleFavorite() {
  const next = !isFavorite.value
  if (auth.user) void markFavorite(auth.user.Id, props.item.Id, next)
  props.item.UserData = { ...props.item.UserData, IsFavorite: next }
}

// ---- 右键/长按/更多按钮菜单(真实站 data-action="menu" 的 actionSheet) ----
/**
 * 菜单锚点（视口坐标）。
 *   · 更多按钮 → 该按钮的 rect（w/h > 0，菜单贴按钮展开）
 *   · 卡片右键 / 触屏长按 → 该点（w = h = 0，菜单以该点为左上角展开，标准右键菜单行为）
 * ItemMenuSheet 用它在 --s-* 之内算 top/left/翻转/夹取；这里只负责「从哪来」。
 */
const menuAnchor = ref<{ x: number; y: number; w: number; h: number } | null>(null)
const menuOpen = ref(false)

function openMenuFromButton(event: MouseEvent) {
  const el = event.currentTarget
  if (el instanceof HTMLElement) {
    const r = el.getBoundingClientRect()
    menuAnchor.value = { x: r.left, y: r.top, w: r.width, h: r.height }
  } else {
    menuAnchor.value = null
  }
  menuOpen.value = true
}

function openMenuAtPoint(x: number, y: number) {
  menuAnchor.value = { x, y, w: 0, h: 0 }
  menuOpen.value = true
}

async function togglePlayedFromMenu() {
  await togglePlayed()
}

async function toggleFavoriteFromMenu() {
  await toggleFavorite()
}

function onMenuCommand(id: string) { showToast(id + ' — v1 mock') }

let pressTimer: ReturnType<typeof setTimeout> | undefined
function onCtxMenu(e: MouseEvent) {
  e.preventDefault()
  // 右键保留（用户：卡片的任意位置右键也会弹出），但必须锚在鼠标点
  openMenuAtPoint(e.clientX, e.clientY)
}
function onTouchStart(e: TouchEvent) {
  const t = e.touches[0]
  if (!t) return
  // 移动端 .cardOverlayContainer 是 display:none → 长按是触屏唯一入口，必须锚在触摸点
  const x = t.clientX
  const y = t.clientY
  pressTimer = setTimeout(() => {
    suppressClickUntil = Date.now() + 600
    openMenuAtPoint(x, y)
  }, 500)
}
function onTouchEnd() {
  clearTimeout(pressTimer)
}
</script>

<template>
  <div
    class="card s-card s-press"
    :class="[
      isBackdrop ? 'backdropCard' : 'portraitCard',
      isHoriz ? 'card-horiz portraitCard-horiz' : '',
      'card-hoverable card-autoactive',
    ]"
    @contextmenu="onCtxMenu"
    @pointerenter="prefetchRoute"
    @touchstart.passive="onTouchStart"
    @touchend="onTouchEnd"
  >
    <div class="cardBox cardBox-touchzoom s-card-box" :class="{ 'cardBox-bottompadded': isBackdrop }">
      <button type="button" data-action="link" tabindex="-1"
              class="itemAction cardContent-button cardContent cardImageContainer cardContent-background cardContent-bxsborder-fv coveredImage coveredImage-noScale"
              :class="padderCls" @click="open">
        <!-- 有图源才渲染 <img>：没有图源时连请求都不发（真机对这种请求返回 500，见 imgSrc 注释） -->
        <img v-if="hasArt" ref="imgEl" draggable="false" alt=" " class="cardImage cardImage-bxsborder-fv coveredImage coveredImage-noScale s-img s-card-img" :class="{ 'is-loaded': imgLoaded, 'is-failed': imgFailed }" :src="imgSrc" loading="lazy" decoding="async" @load="imgLoaded = true" @error="onImgError" />
        <!-- 缺图兜底：品牌渐变 + 居中水印。盖在 .s-img 的占位底色之上、交互层之下。
             两种情况走这里：① 根本没有可用图源（!hasArt）② 图源存在但加载失败（imgFailed）。
             有图且加载成功时完全不存在（v-if），所以正常卡片零开销、零视觉变化。 -->
        <div v-if="!hasArt || imgFailed" class="s-card-fallback" aria-hidden="true">
          <img class="s-card-fallback-mark" :src="markWhite" alt="" draggable="false" />
        </div>
        <div v-if="progressPct > 0" class="innerCardFooter">
          <div class="itemProgressBar cardProgressBarContainer">
            <div class="itemProgressBarForeground" :style="{ width: progressPct + '%' }"></div>
          </div>
        </div>
        <!-- 未看集数角标：裸数字本身没有语义（真实数据里会出现 201 / 196 / 163 这种三位数），
             补 role="img" + aria-label 说明它是什么，否则读屏只会念出一个孤零零的数字。
             配色改走中性玻璃令牌（见下方 .s-count-badge），不再吃主题 accent。 -->
        <div v-if="count" class="cardIndicator cardCountIndicator CountIndicator countIndicator s-count-badge"
             role="img" :title="t('card.unwatchedEpisodes', { n: count })"
             :aria-label="t('card.unwatchedEpisodes', { n: count })">{{ count }}</div>
      </button>
      <!-- 悬浮层:对齐真实 4.9.5 cardbuilder.js 的 getHtml 输出
           顺序 = 居中大播放(cardOverlayFab-primary) → .cardOverlayButton-br.buttonItems(已播/收藏/更多)
           多选 label(.chkItemSelectContainer)已整体删除：用户明确「我们现在没有多选 卡片左上角的多选按钮不需要了」，
           对应的 stores/multiselect.ts 一并下线（全库唯一消费者就是这里）。 -->
      <div class="cardOverlayContainer itemAction" :class="padderCls" :data-action="isLibrary ? 'link' : 'play'" @click="open">
        <!-- 居中大播放按钮(cardOverlayFab-primary:font-size 280%,绝对居中) -->
        <button v-if="canPlay" tabindex="-1" type="button" :title="t('card.play')" :aria-label="t('card.play')"
                class="fab cardOverlayButton-fab buttonItems-item itemAction cardOverlayFab-primary button-hoveraccent md-icon md-icon-fill autortl"
                :data-action="playAction" @click.stop="onPlay">&#xe037;</button>

        <div class="cardOverlayButton-br buttonItems">
          <!-- 已播/未播(真实 emby-playstatebutton:图标 &#xe86c; check_circle,已播为主色) -->
          <button v-if="canMarkPlayed" tabindex="-1" type="button" data-action="none"
                  :title="played ? t('card.markUnplayed') : t('card.markPlayed')"
                  :aria-label="played ? t('card.markUnplayed') : t('card.markPlayed')"
                  class="fab cardOverlayButton-fab buttonItems-item itemAction md-icon fab playstatebutton-icon"
                  :class="{ 'playstatebutton-icon-played': played }"
                  :data-played="played" @click.stop="togglePlayed">&#xe86c;</button>

          <!-- 收藏(真实 emby-ratingbutton:图标 &#xe87d; favorite,已收藏加 md-icon-fill + 红色) -->
          <button v-if="canRate" tabindex="-1" type="button" data-action="none"
                  :title="isFavorite ? t('card.removeFavorite') : t('card.addFavorite')"
                  :aria-label="isFavorite ? t('card.removeFavorite') : t('card.addFavorite')"
                  class="fab cardOverlayButton-fab buttonItems-item itemAction md-icon fab"
                  :class="{ 'ratingbutton-icon-withrating md-icon-fill': isFavorite }"
                  :data-isfavorite="isFavorite" @click.stop="toggleFavorite">&#xe87d;</button>

          <!-- 更多(真实站 md-icon + menuClass,图标 &#xe5d3;) —— 菜单锚在这个按钮的 rect 上 -->
          <button tabindex="-1" type="button" :title="t('card.more')" :aria-label="t('card.more')"
                  class="md-icon fab cardOverlayButton-fab buttonItems-item itemAction" data-action="menu"
                  @click.stop="openMenuFromButton">&#xe5d3;</button>
        </div>
      </div>
      <div class="cardText cardText-first cardText-first-padded s-card-info">
        <button type="button" :title="item.Name" class="cardMediaInfoItem itemAction textActionButton cardTextActionButton emby-button button-link" @click="open">
          {{ item.Name }}
        </button>
      </div>
      <div v-if="subtitle" class="cardText cardText-secondary s-card-info">
        <button type="button" class="cardMediaInfoItem itemAction textActionButton cardTextActionButton emby-button button-link" @click="open">
          {{ subtitle }}
        </button>
      </div>
    </div>
    <ItemMenuSheet
      v-if="!isLibrary"
      :item="item"
      :open="menuOpen"
      :anchor="menuAnchor"
      @close="menuOpen = false"
      @toggle-played="togglePlayedFromMenu"
      @toggle-favorite="toggleFavoriteFromMenu"
      @command="onMenuCommand"
    />
  </div>
</template>
<style scoped>
/* ---------- 动效层（只碰 transform / opacity / filter；props/emits 与交互语义未改） ---------- */

/* 图片「模糊 → 清晰」淡入：初始态由 .s-img(base.css,opacity 0 + 占位底色)+ 本类(blur 12px)共同给出 */
.s-card-img {
  filter: blur(12px);
  transition:
    opacity var(--s-dur-slow) var(--s-ease-std),
    filter var(--s-dur-slow) var(--s-ease-std),
    transform var(--s-dur-slow) var(--s-ease-std);
}
.s-card-img.is-loaded {
  filter: blur(0);
}

/* 碎图不能露出来：失败时把 <img> 彻底藏掉，只留下面的品牌渐变兜底层。
   （不能用 display:none —— 那会让 .s-img 的占位底色一起消失，卡片变成一个空洞。） */
.s-card-img.is-failed {
  opacity: 0;
  visibility: hidden;
}

/* 缺图兜底：与 hero 的 .hero-brand 同一套语言（同一个 --s-gradient-dusk 令牌 + 白标） */
.s-card-fallback {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--s-gradient-dusk);
  pointer-events: none;
}
.s-card-fallback-mark {
  width: 34%;
  max-width: 88px;
  opacity: 0.34;
}

/* 悬停位移挂在 .cardBox 上；按下回弹走 motion.css 的 .s-press（挂在根节点）。
   ItemMenuSheet 已 Teleport 到 body，根节点带 transform 不会变成它的包含块。 */
.s-card-box {
  transition: transform var(--s-dur-base) var(--s-ease-std);
}

/* 信息层（片名 / 副标题）：悬停时轻微上浮，与海报放大拉出层次 */
.s-card-info {
  transition: transform var(--s-dur-base) var(--s-ease-spring);
}

/* 角标（未看计数）与悬浮按钮：悬停微反馈 */
.s-card .cardCountIndicator {
  transition: transform var(--s-dur-fast) var(--s-ease-std);
}

/* 未看集数角标改走中性玻璃。
   原版 .countIndicator 的底色是 hsl(var(--theme-primary-color-*)) —— 而 emby-bridge 把
   --theme-primary-color 映射成了珊瑚（hue 10）。mock 数据只有 2 个库，看不出问题；
   接上真实服务器后是 22 个库 × 12 张卡，几乎每个剧集卡都带未看集数，
   一屏几十个珊瑚圆点直接把海报压过去了。
   角标是「元数据」不是「告警」，所以中性化。
   **只覆盖角标自己的 background/color，绝不动 --theme-primary-color**：
   进度条 .itemProgressBarForeground 用的是同一个变量，那是真·进度，必须保留 accent。 */
.s-card .s-count-badge {
  background: var(--s-badge-bg);
  color: var(--s-badge-ink);
  box-shadow: var(--s-badge-shadow);
  backdrop-filter: blur(6px);
}
/* ---------- 悬浮按钮的边框复位（用户：「悬浮之后显示出来的按钮的右半都会有黑色的描线…还会让人觉得自己的观看进度到那了」） ----------
 * 根因：这 4 个按钮的 class 里**没有 `emby-button`**，而 vendor 只在 `.emby-button{…border:0…}` /
 * `.button-link{…border:0…}` 里清边框。于是它们落回浏览器 UA 默认的 `border: 2px outset`：
 * outset 的**上/左是亮色、下/右被压暗**，圆角按钮上就变成右下小半圈深色弧 —— 用户读成「观看进度环」。
 * 实测（1600×900 库页）：410 个 button 里 212 个带 `2px outset`，正好 53 张卡 × 4 个悬浮按钮。
 * 这里只补 `border: 0`（vendor 的 `.emby-button` 也是这个值），不动 background/color/font ——
 * 实测底色本来就是 `rgba(29, 35, 69, .7)`（来自 .cardOverlayButton 一族），不是 UA 的 buttonface。
 * 副作用：按钮盒子 34.3px → 30.3px（少的就是这 2px×2 边框），与真实站 `.emby-button` 几何一致。 */
.s-card .cardOverlayButton-fab {
  border: 0;
  transition: transform var(--s-dur-fast) var(--s-ease-std);
}

/* ---------- 卡片标题：单行 + 省略号钉死（用户：「标题过长就省略」） ----------
 * vendor 的 .cardText 已有 nowrap/overflow:hidden/ellipsis，但标题按钮是 `display:inline-block` 且宽度靠内容撑，
 * 只靠 `max-width:100%` 的 shrink-to-fit 兜底 —— 一旦宿主的 .cardText 变宽（横向卡/搜索页），就会退化成撑宽或硬裁。
 * 这里把宽度钉成 100%（显式宽度，不再依赖 shrink-to-fit），配合 nowrap + overflow:hidden + ellipsis：
 * 任何长度都只能单行省略，撑不宽卡片、不会换行、不是硬裁。
 * 刻意保留 inline-block：实测 block 会把 .cardText 的行盒从 24.8/19.9px 压到 17.6/13.3px、卡片高度 268.5 → 254.6px，
 * 那是与本次反馈无关的几何改动（block 在搜索页长标题上的截断效果与 inline-block 完全相同：cw 135 / ov 254）。
 * 不用 JS 截断字符串（保留完整片名给 title/搜索/读屏）。 */
.s-card-info .cardTextActionButton {
  display: inline-block;
  inline-size: 100%;
  max-inline-size: 100%;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

/* 悬停编排（只在真悬停设备上做）：抬升 4px + 海报放大提亮 + 信息层上浮 + 角标/悬浮按钮微放大。
   进度条（.itemProgressBar*）不动 —— 品牌渐变保持原样。 */
@media (hover: hover) {
  .s-card:hover > .s-card-box {
    transform: translateY(-4px);
  }
  .s-card:hover .s-card-img {
    filter: brightness(1.08);
    transform: scale(1.05);
  }
  .s-card:hover .s-card-info {
    transform: translateY(-2px);
  }
  .s-card:hover .cardCountIndicator {
    transform: scale(1.08);
  }
  .s-card .cardOverlayButton-fab:hover {
    transform: scale(1.12);
  }
}

/* 减少动效：位移一律归零（提亮这类非位移反馈保留，交互仍然可感知） */
@media (prefers-reduced-motion: reduce) {
  .s-card:hover > .s-card-box,
  .s-card:hover .s-card-img,
  .s-card:hover .s-card-info,
  .s-card:hover .cardCountIndicator,
  .s-card .cardOverlayButton-fab:hover {
    transform: none;
  }
}
</style>