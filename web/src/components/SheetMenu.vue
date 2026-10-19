<script setup lang="ts">
/**
 * 通用 actionSheet 菜单 —— 结构照真实站(4.9.5)运行时 dump:
 * .actionSheet > .actionsheetItemPreview(图标/图片 + 主文本 + 副文本) + .actionSheetScroller > .actionSheetMenuItem(iconright)
 * 用于:header 用户菜单 / 投屏"播放到" / 库页排序与筛选 / 分节更多 等。
 *
 * 行为层已收敛到基座 SSheet(reka-ui Dialog):Esc / 点外关闭 / 焦点陷阱 / 焦点归还 / 进出动效 / aria。
 * 旧实现是 v-if 手搓遮罩,这五样全丢。面板仍吃 emby495 的 actionSheet* 类名,长相 1:1。
 */
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import SSheet from '@/components/ui/SSheet.vue'
import { flipConfig, useReducedMotionSafe } from '@/lib/motion'

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

const { t } = useI18n()

/* 列表 FLIP:auto-animate 动手前必须过 useReducedMotionSafe(铁律 16) */
const reduced = useReducedMotionSafe()
const listFlip = computed(() => flipConfig(reduced.value))

function pick(item: SheetMenuItem) {
  emit('pick', item)
}
</script>

<template>
  <SSheet
    :open="open"
    variant="pop"
    :title="t('shell.userMenu')"
    panel-class="actionSheet actionSheet-automobile actionSheet-largefont"
    @close="emit('close')"
  >
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
      <div
        v-auto-animate="listFlip"
        class="actionsheetScrollSlider scrollSlider flex flex-direction-column itemsContainer actionSheetScroller-padding-top actionSheetScroller-padding-bottom vertical-list scrollSliderY"
      >
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
  </SSheet>
</template>

<style scoped>
/* 遮罩/面板底色/圆角/阴影/层级全部由基座 SSheet 提供:
   --s-scrim-modal(浅色下也压暗,不能复用会提亮的 --s-scrim)、--s-raised、--s-text-1、
   --s-line、--s-shadow-sheet、--s-z-sheet。
   旧实现这里手搓 .sheetScrim + .dialog.actionSheet(z-index 1000、margin-top 4em/margin-inline-end 1em),
   定位改由 .s-sheet--pop 承担(58px / 14px,与 4em / 1em 对齐)。 */
</style>
