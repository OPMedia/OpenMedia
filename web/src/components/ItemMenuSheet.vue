<script setup lang="ts">
/**
 * 条目"更多"菜单 —— 1:1 还原 Emby 4.9.5 actionSheet(基准=真实站 dump,emby495-item-menu.md):
 * .dialog.actionSheet > .actionsheetItemPreview(条目预览头) + .actionSheetScroller > 菜单项
 * (.listItem.actionSheetMenuItem.actionSheetMenuItem-iconright:icon square 池 + 文本)
 * v1 实做:收藏/已播放;其余项视觉占位(close)。
 */
import { useI18n } from 'vue-i18n'
import type { BaseItemDto } from '@/api/types'
import { imageUrl } from '@/api/endpoints'
import { computed } from 'vue'

const props = defineProps<{ item: BaseItemDto; open: boolean }>()
const emit = defineEmits<{
  close: []
  togglePlayed: []
  toggleFavorite: []
  /** 菜单项动作(真实站 data-id;编辑元数据/编辑图像等由宿主处理) */
  command: [id: string]
}>()
const { t } = useI18n()

const previewBg = computed(() =>
  imageUrl(props.item.Id, 'Primary', props.item.ImageTags?.Primary, { maxWidth: 120, maxHeight: 180, quality: 90 }),
)

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

function onItem(item: MenuItem) {
  if (item.action === 'played') emit('togglePlayed')
  else if (item.action === 'favorite') emit('toggleFavorite')
  else emit('command', item.id)
  emit('close')
}
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="dialogContainer" @click.self="emit('close')">
      <div class="dialog dialog-animated actionSheet actionSheet-automobile actionSheet-largefont opened" @click.stop>
        <div class="actionsheetItemPreview flex flex-shrink-zero flex-direction-row">
          <div class="actionsheetItemPreviewImage-bg actionsheetItemPreviewImage-bg-portrait" :style="{ backgroundImage: `url(${previewBg})` }"></div>
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
          <div class="actionsheetScrollSlider scrollSlider flex flex-direction-column itemsContainer actionSheetScroller-padding-top actionSheetScroller-padding-bottom vertical-list scrollSliderY">
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
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.dialogContainer {
  position: fixed; inset: 0; z-index: 999;
  background: rgba(0, 0, 0, 0.62);
  display: flex; align-items: flex-start; justify-content: flex-start;
}
.dialog.actionSheet {
  margin: 8em 0 0 8px;
  width: 320px; max-height: calc(100vh - 10em);
  overflow: hidden;
  border-radius: .9em;
  background: rgb(40, 40, 43);
  color: rgba(255, 255, 255, .87);
}
.actionsheetItemPreview { padding: 1em 1.2em .6em; gap: 12px; align-items: center; }
.actionsheetItemPreviewImage-bg {
  width: 64px; height: 96px; border-radius: .3em;
  background-size: cover; background-position: center;
}
.actionsheetItemPreviewText-main { font-size: 120%; font-weight: 600; }
</style>