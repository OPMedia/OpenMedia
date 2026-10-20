<script setup lang="ts">
/**
 * 条目"更多"菜单 —— 1:1 还原 Emby 4.9.5 actionSheet(基准=真实站 dump,emby495-item-menu.md):
 * .dialog.actionSheet > .actionsheetItemPreview(条目预览头) + .actionSheetScroller > 菜单项
 * (.listItem.actionSheetMenuItem.actionSheetMenuItem-iconright:icon square 池 + 文本)
 * v1 实做:收藏/已播放;其余项视觉占位(close)。
 *
 * 行为层已收敛到基座 SSheet(reka-ui Dialog):Esc / 点遮罩关闭 / 焦点陷阱 / 焦点归还 / 进出动效 / aria。
 * 颜色一律走令牌 —— 旧实现硬编码 background:rgb(40,40,43) + color:rgba(255,255,255,.87),
 * 浅色模式下就是一整块黑方块(铁律 17)。
 */
import { useI18n } from 'vue-i18n'
import type { BaseItemDto } from '@/api/types'
import { cardArtUrl } from '@/api/endpoints'
import { computed } from 'vue'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import SSheet from '@/components/ui/SSheet.vue'
import { flipConfig, useReducedMotionSafe } from '@/lib/motion'

const props = defineProps<{
  item: BaseItemDto
  open: boolean
  /**
   * 触发点（视口坐标）。
   *   · 更多按钮 → 该按钮的 rect（w/h > 0）
   *   · 卡片任意位置右键 / 触屏长按 → 该点（w = h = 0）
   * 为空时（例如老宿主没传）回落成 variant 的默认位置，行为与改动前一致。
   */
  anchor?: { x: number; y: number; w: number; h: number } | null
}>()
const emit = defineEmits<{
  close: []
  togglePlayed: []
  toggleFavorite: []
  /** 菜单项动作(真实站 data-id;编辑元数据/编辑图像等由宿主处理) */
  command: [id: string]
}>()
const { t } = useI18n()

/**
 * 菜单头部缩略图。
 *
 * 原实现无条件拼 `imageUrl(item.Id, 'Primary', item.ImageTags?.Primary)` —— tag 是 undefined 也照拼，
 * 真机 Emby 4.9.5.0 对没有 Primary 的条目返回 **HTTP 500**（不是 404；全站 13739 条真实条目里 1931 条没有 Primary）。
 * 改走 `cardArtUrl`：没有 tag 就返回空串 → 下面模板不设 backgroundImage，不发这次必然 5xx 的请求。
 */
const previewBg = computed(() =>
  cardArtUrl(props.item, 'portrait', { maxWidth: 120, maxHeight: 180, quality: 90 }),
)

/* 列表 FLIP:auto-animate 动手前必须过 useReducedMotionSafe(铁律 16) */
const reduced = useReducedMotionSafe()
const listFlip = computed(() => flipConfig(reduced.value))

interface MenuItem {
  id: string
  icon: string
  label: string
  action?: 'played' | 'favorite'
}

const menuItems = computed<MenuItem[]>(() => [
  { id: 'addtoplaylist', icon: '\uE03B', label: t('menu.addToPlaylist') },
  { id: 'addtocollection', icon: '\uE03B', label: t('menu.addToCollection') },
  { id: 'favorite', icon: '\uE87D', label: props.item.UserData?.IsFavorite ? t('menu.unfavorite') : t('menu.favorite'), action: 'favorite' },
  { id: 'played', icon: 'check', label: props.item.UserData?.Played ? t('menu.markUnplayed') : t('menu.markPlayed'), action: 'played' },
  { id: 'editmeta', icon: 'edit', label: t('menu.editMeta') },
  { id: 'editimages', icon: 'photo', label: t('menu.editImages') },
  { id: 'identify', icon: '\uE85D', label: t('menu.identify') },
  { id: 'removeidentify', icon: '\uE0B8', label: t('menu.removeIdentify') },
  { id: 'refreshmeta', icon: 'refresh', label: t('menu.refreshMeta') },
  { id: 'scanlibrary', icon: 'refresh', label: t('menu.scanLibrary') },
  { id: 'lockmeta', icon: props.item.LockedFields?.length ? 'lock_open' : 'lock', label: t('menu.lockMeta') },
  { id: 'clearmeta', icon: 'cleaning_services', label: t('menu.clearMeta') },
  { id: 'clearintro', icon: 'bookmark_remove', label: t('menu.clearIntro') },
  { id: 'clearchapterimages', icon: 'hide_image', label: t('menu.clearChapterImages') },
])

/** 面板宽度:真实站 actionSheet 320px。宽度固定 → 横向翻转/夹取可以纯算术,不用先渲染再测 */
const PANEL_W = 320
/** 面板与触发点之间的间距 */
const ANCHOR_GAP = 8
/** 视口安全边（任何情况下面板边缘都不许贴出这条线） */
const VIEWPORT_EDGE = 8
/** 下方至少留这么多才优先向下展开,否则向上翻 */
const MIN_SPACE_BELOW = 240

/**
 * 定位（用户：「卡片的右下角的更多按钮 点击之后弹出的列表不是按照位置出现的」）。
 *
 * 改前实测：无论点哪个按钮、在哪右键,面板恒为 `left:8px / top:115px`（= SSheet variant="pop-start"
 * 的静态左上角），和触发点毫无关系 —— 右下角按钮在 (1493, 843) 时面板还在 (8, 115)。
 *
 * 现在按锚点算：
 *   纵向：优先向下（top = 锚点底 + 8px）；下方放不下且上方更宽裕 → 向上翻，
 *        用 `bottom` 定位（面板高度是内容决定的，用 bottom 就不必先测高度再回填）。
 *   横向：按钮 → 与按钮左缘对齐；点 → 左上角落在光标处；
 *        右侧越界 → 翻到锚点左侧（按钮右对齐 / 右键菜单向左展开），最后夹进视口。
 *   尺寸：max-height 钉成「该侧真实可用高度」，面板永远完整落在视口内，超出部分内部滚动。
 *   动效：同时覆盖 --s-pop-origin，让缩放原点跟着翻转方向（否则向上翻时动画从错误的一角展开）。
 * 只动 top/left/right/max-height/自定义属性这些「几何」，SSheet 的 reka 行为（焦点陷阱/Esc/点外关闭）零改动。
 */
const panelStyle = computed<Record<string, string | number>>(() => {
  // SSheet 的面板自带 1px 边框；默认 content-box 会让「320px」实占 322px，夹取算式就会差 2px。
  // 这里钉成 border-box，让 PANEL_W 就是真实占位宽度（右边夹取才精确落在 VIEWPORT_EDGE 上）。
  const base: Record<string, string | number> = { width: `${PANEL_W}px`, boxSizing: 'border-box' }
  const a = props.anchor
  if (!a || typeof window === 'undefined') return { ...base, maxHeight: 'calc(100vh - 10em)' }

  const vw = window.innerWidth
  const vh = window.innerHeight
  const aLeft = a.x
  const aTop = a.y
  const aRight = a.x + a.w
  const aBottom = a.y + a.h

  const spaceBelow = vh - aBottom - VIEWPORT_EDGE
  const spaceAbove = aTop - VIEWPORT_EDGE
  const below = spaceBelow >= MIN_SPACE_BELOW || spaceBelow >= spaceAbove
  const verticalSpace = Math.max(96, (below ? spaceBelow : spaceAbove) - ANCHOR_GAP)

  const overflowRight = aLeft + PANEL_W + VIEWPORT_EDGE > vw
  let left = overflowRight ? (a.w > 0 ? aRight - PANEL_W : aLeft - PANEL_W) : aLeft
  left = Math.min(Math.max(VIEWPORT_EDGE, left), Math.max(VIEWPORT_EDGE, vw - PANEL_W - VIEWPORT_EDGE))
  const originX = left < aLeft ? 'right' : 'left'

  return {
    ...base,
    top: below ? `${aBottom + ANCHOR_GAP}px` : 'auto',
    bottom: below ? 'auto' : `${vh - aTop + ANCHOR_GAP}px`,
    left: `${left}px`,
    right: 'auto',
    // variant 用的是 inset-inline-*，物理/逻辑两套都写，避免「谁赢」的歧义
    insetInlineStart: `${left}px`,
    insetInlineEnd: 'auto',
    maxHeight: `${verticalSpace}px`,
    '--s-pop-origin': `${below ? 'top' : 'bottom'} ${originX}`,
  }
})

function onItem(item: MenuItem) {
  if (item.action === 'played') emit('togglePlayed')
  else if (item.action === 'favorite') emit('toggleFavorite')
  else emit('command', item.id)
  emit('close')
}
</script>

<template>
  <SSheet
    :open="open"
    variant="pop-start"
    :title="t('item.more')"
    :content-style="panelStyle"
    panel-class="actionSheet actionSheet-automobile actionSheet-largefont"
    @close="emit('close')"
  >
    <div class="actionsheetItemPreview flex flex-shrink-zero flex-direction-row">
      <div class="actionsheetItemPreviewImage-bg actionsheetItemPreviewImage-bg-portrait" :style="previewBg ? { backgroundImage: `url(${previewBg})` } : undefined"></div>
      <div class="actionsheetItemPreviewContent">
        <div class="actionsheetItemPreviewText mediaInfoItems actionsheetItemPreviewText-main">
          <div class="actionsheetPreviewTextItem mediaInfoItem">{{ item.Name }}</div>
        </div>
        <div class="actionsheetItemPreviewText mediaInfoItems secondaryText">
          <div class="actionsheetPreviewTextItem mediaInfoItem">{{ item.ProductionYear }}</div>
        </div>
      </div>
    </div>
    <div class="actionSheetScroller focuscontainer-x actionSheetScroller-padding-inline emby-scroller dialog-scrollY scrollY scrollFrameY flex-direction-column">
      <div
        v-auto-animate="listFlip"
        class="actionsheetScrollSlider scrollSlider flex flex-direction-column itemsContainer actionSheetScroller-padding-top actionSheetScroller-padding-bottom vertical-list scrollSliderY"
      >
        <button
          v-for="(mi, i) in menuItems" :key="i"
          type="button"
          class="listItem listItem-autoactive itemAction listItemCursor listItem-hoverable actionSheetMenuItem actionSheetMenuItem-iconright"
          @click="onItem(mi)"
        >
          <div class="listItem-content listItem-content-bg listItemContent-touchzoom listItem-border actionsheet-noborder">
            <div class="actionSheetItemImageContainer actionSheetItemImageContainer-customsize actionSheetItemImageContainer-transparent listItemImageContainer listItemImageContainer-margin listItemImageContainer-square defaultCardBackground">
              <i class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent md-icon md-icon autortl">{{ mi.icon }}</i>
            </div>
            <div class="actionsheetListItemBody actionsheetListItemBody-iconright listItemBody listItemBody-1-lines">
              <div class="listItemBodyText actionSheetItemText listItemBodyText-nowrap listItemBodyText-lf">{{ mi.label }}</div>
            </div>
          </div>
        </button>
      </div>
    </div>
  </SSheet>
</template>

<style scoped>
/* 面板底色/边框/圆角/阴影/遮罩/层级全部由基座 SSheet 提供(--s-raised / --s-text-1 / --s-line /
   --s-shadow-sheet / --s-scrim-modal / --s-z-sheet)。旧实现手搓 .dialogContainer(rgba(0,0,0,.62))
   与 .dialog.actionSheet(rgb(40,40,43) + rgba(255,255,255,.87)),浅色下是黑方块。
   **定位已不在样式里**：面板位置由 script 的 panelStyle 按锚点算（top/bottom/left/max-height/--s-pop-origin
   全内联），`.s-sheet--pop-start` 的 115px / 8px 只是「宿主没传 anchor」时的兜底。
   下面只保留插槽内容的版式(预览头),面板本身不加 padding。 */
.actionsheetItemPreview { padding: 1em 1.2em .6em; gap: 12px; align-items: center; }
.actionsheetItemPreviewImage-bg {
  width: 64px; height: 96px; border-radius: .3em;
  background-size: cover; background-position: center;
}
.actionsheetItemPreviewText-main { font-size: 120%; font-weight: 600; }
</style>
