<script setup lang="ts">
/**
 * 条目卡 —— 1:1 还原 Emby 4.9.5 cardbuilder(基准=真实站运行时 dump):
 * .card.{backdrop|portrait}Card.card-hoverable.card-autoactive
 *   > .cardBox.cardBox-touchzoom[.cardBox-bottompadded]
 *     > button.itemAction.cardContent-button.cardContent.cardImageContainer.cardContent-background
 *       .cardContent-bxsborder-fv.coveredImage.coveredImage-noScale.cardPadder-{backdrop|portrait}
 *       > img.cardImage(+进度条 itemProgressBar)(+countIndicator)
 *     > .cardOverlayContainer.itemAction.cardPadder-*
 *       > 多选 label + .cardOverlayButton-br.buttonItems(内容卡:播放/已看/更多;库卡:仅更多)
 *     > .cardText.cardText-first.cardText-first-padded(+ .cardText.cardText-secondary)
 */
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { BaseItemDto } from '@/api/types'
import { imageUrl, markPlayed, markFavorite, getItems } from '@/api/endpoints'
import { useAuthStore } from '@/stores/auth'
import ItemMenuSheet from '@/components/ItemMenuSheet.vue'
import { showToast } from '@/api/toast'
import { isMultiSelected, multiSelectActive, toggleMultiSelected } from '@/stores/multiselect'
import markWhite from '@/assets/brand/mark-white.svg'

const props = withDefaults(defineProps<{ item: BaseItemDto; shape?: 'portrait' | 'backdrop' | 'portrait-horiz' }>(), { shape: 'portrait' })
const router = useRouter()
const auth = useAuthStore()
const { t } = useI18n()

/** 卡片形态:backdrop=16:9 剧照 / portrait=2:3 海报 / portrait-horiz=横向剧集卡(真实站 /tv 集 tab) */
const isBackdrop = computed(() => props.shape === 'backdrop')
const isHoriz = computed(() => props.shape === 'portrait-horiz')
const padderCls = computed(() => {
  if (isBackdrop.value) return 'cardPadder-backdrop'
  if (isHoriz.value) return 'cardPadder-horiz cardPadder-portrait-horiz'
  return 'cardPadder-portrait'
})
const imgSrc = computed(() => {
  if (isBackdrop.value) {
    return imageUrl(props.item.Id, 'Primary', props.item.ImageTags?.Primary, { maxWidth: 525, maxHeight: 296, quality: 90 })
  }
  return imageUrl(props.item.Id, 'Primary', props.item.ImageTags?.Primary, { maxWidth: 300, maxHeight: 450, quality: 90 })
})
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

function open() {
  // 多选态下点卡片 = 勾选/取消(真实站 multiselect 行为),不跳转
  if (multiSelectActive.value) {
    toggleMultiSelected(props.item.Id)
    return
  }
  // 库卡 → 浏览页(真实站 CollectionFolder 的 data-action=link 走 /list);条目卡 → 详情
  if (props.item.Type === 'CollectionFolder') {
    router.push({ name: 'library', params: { id: props.item.Id } })
    return
  }
  router.push({ name: 'item', params: { id: props.item.Id } })
}

// ---- 多选(真实站 multiselect:<html>.multi-select-active + 卡 .item-multiselected) ----
const multiSelected = computed(() => isMultiSelected(props.item.Id))
function toggleMultiSelect() {
  toggleMultiSelected(props.item.Id)
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

// ---- 右键/长按菜单(真实站 data-action="menu" 的 actionSheet) ----
const menuOpen = ref(false)

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
  menuOpen.value = true
}
function onTouchStart(e: TouchEvent) {
  const t = e.touches[0]
  if (!t) return
  pressTimer = setTimeout(() => { menuOpen.value = true }, 500)
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
      { 'item-multiselected': multiSelected },
    ]"
    @contextmenu="onCtxMenu"
    @touchstart.passive="onTouchStart"
    @touchend="onTouchEnd"
  >
    <div class="cardBox cardBox-touchzoom s-card-box" :class="{ 'cardBox-bottompadded': isBackdrop }">
      <button type="button" data-action="link" tabindex="-1"
              class="itemAction cardContent-button cardContent cardImageContainer cardContent-background cardContent-bxsborder-fv coveredImage coveredImage-noScale"
              :class="padderCls" @click="open">
        <img ref="imgEl" draggable="false" alt=" " class="cardImage cardImage-bxsborder-fv coveredImage coveredImage-noScale s-img s-card-img" :class="{ 'is-loaded': imgLoaded, 'is-failed': imgFailed }" :src="imgSrc" loading="lazy" decoding="async" @load="imgLoaded = true" @error="onImgError" />
        <!-- 缺图兜底：品牌渐变 + 居中水印。盖在 .s-img 的占位底色之上、交互层之下。
             有图时完全不存在（v-if），所以正常卡片零开销、零视觉变化。 -->
        <div v-if="imgFailed" class="s-card-fallback" aria-hidden="true">
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
      <!-- 悬浮层:1:1 对齐真实 4.9.5 cardbuilder.js 的 getHtml 输出
           顺序 = 多选 label → 居中大播放(cardOverlayFab-primary) → .cardOverlayButton-br.buttonItems(已播/收藏/更多) -->
      <div class="cardOverlayContainer itemAction" :class="padderCls" :data-action="isLibrary ? 'link' : 'play'" @click="open">
        <label tabindex="-1" :title="t('card.multiSelect')" :aria-label="t('card.multiSelect')" data-action="multiselect"
               class="chkItemSelectContainer chkCardSelectContainer cardOverlayButton itemAction emby-checkbox-label"
               @click.stop="toggleMultiSelect">
          <!-- 真实 CSS 里 .emby-checkbox 是 z-index:-1 的 1px 覆盖层,永远不被命中;
               label 的默认行为会把点击转发给 input,所以这里吞掉转发的那一次,避免一次点击切换两次 -->
          <input tabindex="-1" class="chkItemSelect chkCardSelect emby-checkbox emby-checkbox-notext" type="checkbox"
                 :checked="multiSelected" data-classes="true" @click.stop.prevent />
          <span class="checkboxLabel chkCardSelect-checkboxLabel"></span>
        </label>

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

          <!-- 更多(真实站 md-icon + menuClass,图标 &#xe5d3;) -->
          <button tabindex="-1" type="button" :title="t('card.more')" :aria-label="t('card.more')"
                  class="md-icon fab cardOverlayButton-fab buttonItems-item itemAction" data-action="menu"
                  @click.stop="menuOpen = true">&#xe5d3;</button>
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
      @close="menuOpen = false"
      @toggle-played="togglePlayedFromMenu"
      @toggle-favorite="toggleFavoriteFromMenu"
      @command="onMenuCommand"
    />
  </div>
</template>
<style scoped>
/* 多选勾选框的视觉与命中都走真实站原版 CSS(.emby-checkbox z-index:-1、label::before 画方块),
   这里不做任何覆盖,只保证 label 上的点击只切换一次(见模板注释) */

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
.s-card .cardOverlayButton-fab {
  transition: transform var(--s-dur-fast) var(--s-ease-std);
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