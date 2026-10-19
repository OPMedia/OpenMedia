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
onMounted(() => {
  // 命中缓存时 load 可能早于监听触发，补一次判定，避免卡片永久停在模糊态
  if (imgEl.value?.complete) imgLoaded.value = true
})

const progressPct = computed(() => {
  const ud = props.item.UserData
  if (!ud?.PlaybackPositionTicks || !props.item.RunTimeTicks) return 0
  return Math.min(100, (ud.PlaybackPositionTicks / props.item.RunTimeTicks) * 100)
})
const count = computed(() => props.item.UserData?.UnplayedItemCount)
const subtitle = computed(() => {
  const i = props.item
  if (i.Type === 'Episode') return `S${i.SeasonNumber}:E${i.EpisodeNumber} - ${i.Name}`
  return i.ProductionYear ? String(i.ProductionYear) : ''
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
        <img ref="imgEl" draggable="false" alt=" " class="cardImage cardImage-bxsborder-fv coveredImage coveredImage-noScale s-img s-card-img" :class="{ 'is-loaded': imgLoaded }" :src="imgSrc" loading="lazy" decoding="async" @load="imgLoaded = true" @error="imgLoaded = true" />
        <div v-if="progressPct > 0" class="innerCardFooter">
          <div class="itemProgressBar cardProgressBarContainer">
            <div class="itemProgressBarForeground" :style="{ width: progressPct + '%' }"></div>
          </div>
        </div>
        <div v-if="count" class="cardIndicator cardCountIndicator CountIndicator countIndicator">{{ count }}</div>
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