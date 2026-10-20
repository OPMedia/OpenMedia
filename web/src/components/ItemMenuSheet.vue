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

const props = defineProps<{ item: BaseItemDto; open: boolean }>()
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

/** 真实站尺寸:宽 320px、高不超过 calc(100vh - 10em)(基座只管行为,尺寸由宿主给) */
const panelStyle = computed<Record<string, string | number>>(() => ({
  width: '320px',
  maxHeight: 'calc(100vh - 10em)',
}))

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
   左上角定位由 .s-sheet--pop-start(115px / 8px,与旧 8em / 8px 对齐)承担;宽高走 content-style。
   下面只保留插槽内容的版式(预览头),面板本身不加 padding。 */
.actionsheetItemPreview { padding: 1em 1.2em .6em; gap: 12px; align-items: center; }
.actionsheetItemPreviewImage-bg {
  width: 64px; height: 96px; border-radius: .3em;
  background-size: cover; background-position: center;
}
.actionsheetItemPreviewText-main { font-size: 120%; font-weight: 600; }
</style>
