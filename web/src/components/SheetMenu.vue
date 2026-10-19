<script setup lang="ts">
/**
 * 通用 actionSheet 菜单 —— 结构照真实站(4.9.5)运行时 dump:
 * .actionSheet > .actionsheetItemPreview(图标/图片 + 主文本 + 副文本) + .actionSheetScroller > .actionSheetMenuItem(iconright)
 * 用于:header 用户菜单 / 投屏"播放到" / 库页排序与筛选 / 分节更多 等。
 */
export interface SheetMenuItem {
  id?: string
  /** md-icon 码位或 ligature */
  icon?: string
  label: string
  secondaryText?: string
  selected?: boolean
}

const props = withDefaults(defineProps<{
  open: boolean
  items: SheetMenuItem[]
  /** 预览区:文本(头像/图标) */
  previewIcon?: string
  previewTitle?: string
  previewSecondary?: string
  previewImage?: string
}>(), { open: false })

const emit = defineEmits<{ close: []; pick: [item: SheetMenuItem] }>()

function pick(item: SheetMenuItem) {
  emit('pick', item)
}
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="sheetScrim" @click.self="emit('close')">
      <div class="dialog dialog-animated actionSheet actionSheet-automobile actionSheet-largefont opened" @click.stop>
        <div v-if="previewTitle || previewIcon || previewImage" class="actionsheetItemPreview flex flex-shrink-zero flex-direction-row">
          <div v-if="previewImage" class="actionsheetItemPreviewImage-bg actionsheetItemPreviewImage-bg-portrait" :style="{ backgroundImage: `url(${previewImage})` }"></div>
          <div v-else class="actionsheetItemPreviewImage-iconcontainer">
            <i class="actionsheetItemPreviewImage-icon md-icon autortl">{{ previewIcon }}</i>
          </div>
          <div class="actionsheetItemPreviewContent">
            <div class="actionsheetItemPreviewText mediaInfoItems actionsheetItemPreviewText-main">
              <div class="actionsheetPreviewTextItem mediaInfoItem">{{ previewTitle }}</div>
            </div>
            <div v-if="previewSecondary" class="actionsheetItemPreviewText mediaInfoItems secondaryText">
              <div class="actionsheetPreviewTextItem mediaInfoItem">{{ previewSecondary }}</div>
            </div>
          </div>
        </div>

        <div class="actionSheetScroller focuscontainer-x actionSheetScroller-padding-inline emby-scroller dialog-scrollY scrollY scrollY-mini scrollFrameY flex-direction-column">
          <div class="actionsheetScrollSlider scrollSlider flex flex-direction-column itemsContainer actionSheetScroller-padding-top actionSheetScroller-padding-bottom vertical-list scrollSliderY">
            <button
              v-for="(item, i) in items" :key="item.id ?? i"
              type="button"
              class="listItem listItem-autoactive itemAction listItemCursor listItem-hoverable actionSheetMenuItem actionSheetMenuItem-iconright"
              :data-id="item.id"
              @click="pick(item)"
            >
              <div class="listItem-content listItem-content-margin listItem-content-bg listItemContent-touchzoom listItem-border actionsheet-noborder">
                <div class="actionSheetItemImageContainer actionSheetItemImageContainer-transparent listItemImageContainer listItemImageContainer-smallest listItemImageContainer-square listItemImageContainer-round defaultCardBackground">
                  <i class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent md-icon md-icon autortl">{{ item.icon }}</i>
                </div>
                <div class="actionsheetListItemBody actionsheetListItemBody-iconright listItemBody listItemBody-reduceypadding" :class="item.secondaryText ? 'listItemBody-2-lines' : 'listItemBody-1-lines'">
                  <div class="listItemBodyText actionSheetItemText listItemBodyText-lf">{{ item.label }}</div>
                  <div v-if="item.secondaryText" class="listItemBodyText actionSheetItemText listItemBodyText-lf listItemBodyText-secondary secondaryText">{{ item.secondaryText }}</div>
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
/* 弹层遮罩(真实站 dialog 遮罩层;背景透明 + 点击关闭) */
.sheetScrim {
  position: fixed;
  inset: 0;
  z-index: 1000;
  /* 模态遮罩：浅色主题下也要压暗（--s-scrim 在浅色是提亮用的，不能复用） */
  background: var(--s-scrim-modal);
  -webkit-backdrop-filter: blur(2px);
  backdrop-filter: blur(2px);
  display: flex;
  align-items: flex-start;
  justify-content: flex-end;
}
.dialog.actionSheet {
  margin-top: 4em;
  margin-inline-end: 1em;
  max-height: 84%;
  max-width: 90%;
  border-radius: .9em;
  /* 原来硬编码 rgb(40,40,43)：浅色模式下会是一块深色方块，改为令牌跟随主题 */
  background: var(--s-raised);
  color: var(--s-text-1);
  border: 1px solid var(--s-line);
  box-shadow: var(--s-shadow-sheet);
  overflow: hidden;
}
</style>