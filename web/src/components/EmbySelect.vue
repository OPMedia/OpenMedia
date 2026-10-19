<script setup lang="ts">
/**
 * Emby select —— 1:1 还原 4.9.5 select actionsheet 弹层(基准=真实站取证):
 * - 触发态:胶囊 select(selectContainer-inline + emby-select-inline,圆角 100em,
 *   bg var(--button-background) rgba(104,98,106,.7),右侧 selectArrow expand_more)
 * - 弹层:actionSheet dialog,positionY=bottom / positionX=left / transformOrigin=center top,
 *   minWidthToElement(与触发器同宽),项 = Name(+secondaryText)+ 选中态 check 图标
 * - 选项结构:{ label, secondaryText?, icon? },受控 v-model
 */
import { ref, computed } from 'vue'

export interface EmbySelectOption {
  value: string
  label: string
  secondaryText?: string
}

const props = withDefaults(defineProps<{
  modelValue: string
  options: EmbySelectOption[]
  label?: string
  disabled?: boolean
  /** 弹层与触发器同宽(真实站 minWidthToElement) */
  matchWidth?: boolean
}>(), { disabled: false, matchWidth: true })

const emit = defineEmits<{ 'update:modelValue': [value: string] }>()

const open = ref(false)
const rootEl = ref<HTMLElement | null>(null)
const facadeEl = ref<HTMLElement | null>(null)

const current = computed(() => props.options.find((o) => o.value === props.modelValue))

/** 最宽选项测量层:把所有选项叠在同一格渲染,宽度 = max(渲染宽度)(精确等价原生 select 固有宽度) */
const sizerLabels = computed(() => props.options.flatMap((o) => [o.label, o.secondaryText].filter(Boolean) as string[]))

function toggle() {
  if (props.disabled) return
  open.value = !open.value
}

function pick(option: EmbySelectOption) {
  emit('update:modelValue', option.value)
  open.value = false
}

/** 点击弹层外关闭 */
function onDocClick(e: MouseEvent) {
  if (rootEl.value && !rootEl.value.contains(e.target as Node)) open.value = false
}

// 弹层定位(真实站 positionTo=select 元素):facade 正下方,左对齐,宽度 >= facade
const popupStyle = computed(() => {
  const anchor = facadeEl.value ?? rootEl.value
  if (!anchor) return {}
  const r = anchor.getBoundingClientRect()
  return {
    top: r.bottom + 4 + 'px',
    left: r.left + 'px',
    minWidth: props.matchWidth ? r.width + 'px' : undefined,
  }
})

const ICON_CHECK = '\uE5CA'
const ICON_ARROW = '\uE5C5' // expand_more 码位
</script>

<template>
  <div
    ref="rootEl"
    class="selectContainer selectContainer-inline trackSelectionFieldContainer fieldItems-item flex-shrink-zero"
    @click.stop="onDocClick"
  >
    <label class="selectLabel selectLabel-inline" @click="toggle">
      <div v-if="label" class="selectLabelText selectLabelText-inline">{{ label }}</div>
      <div class="emby-select-wrapper emby-select-wrapper-inline" :class="{ 'emby-select-wrapper-inline-disabled': disabled }">
        <!-- 视觉:胶囊 + 当前值 + 箭头(真实站 select 为透明原生元素,视觉由 wrapper 层给出)
             宽度用「最宽选项」的无形测量层钉死 —— 与原生 select 固有宽度一致,切换选项不再抖 -->
        <div class="emby-select-facade" ref="facadeEl" :class="{ 'emby-select-facade-disabled': disabled }">
          <span class="emby-select-sizer" aria-hidden="true">
            <span v-for="(l, i) in sizerLabels" :key="i" class="emby-select-sizer-item">{{ l }}</span>
          </span>
          <span class="emby-select-selectedName">{{ current?.label ?? options[0]?.label }}</span>
          <span v-if="!disabled" class="selectArrowContainer selectArrowContainer-inline"><i class="selectArrow md-icon">{{ ICON_ARROW }}</i></span>
        </div>
      </div>
    </label>

    <Teleport to="body">
      <div v-if="open" class="actionSheet select-actionSheet" :style="popupStyle" @click.stop>
        <div class="actionSheetScroller focuscontainer-x actionSheetScroller-padding-inline emby-scroller dialog-scrollY scrollY scrollFrameY flex-direction-column">
          <div class="actionsheetScrollSlider scrollSlider flex flex-direction-column itemsContainer vertical-list scrollSliderY">
            <button
              v-for="o in options" :key="o.value"
              type="button"
              class="listItem listItem-autoactive itemAction listItemCursor listItem-hoverable actionSheetMenuItem"
              @click="pick(o)"
            >
              <div class="listItem-content listItem-content-bg listItemContent-touchzoom listItem-border actionsheet-noborder">
                <div class="listItemBody actionsheetListItemBody listItemBody-1-lines">
                  <div class="listItemBodyText actionSheetItemText listItemBodyText-nowrap listItemBodyText-lf">{{ o.label }}</div>
                  <div v-if="o.secondaryText" class="listItemBodyText actionSheetItemText listItemBodyText-nowrap listItemBodyText-lf listItemBodyText-secondary secondaryText">{{ o.secondaryText }}</div>
                </div>
                <div v-if="o.value === modelValue" class="actionSheetItemImageContainer actionSheetItemImageContainer-transparent listItemImageContainer listItemImageContainer-margin listItemImageContainer-square defaultCardBackground">
                  <i class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent md-icon md-icon autortl">{{ ICON_CHECK }}</i>
                </div>
              </div>
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
/* select 弹层(actionsheet 形态;独立定位,不用 dialog 全屏类) */
.select-actionSheet {
  position: fixed;
  z-index: 1000;
  background: rgb(40, 40, 43);
  color: rgba(255, 255, 255, .87);
  border-radius: .9em;
  box-shadow: 0 .278em 1.39em rgba(0, 0, 0, .4);
  max-height: 70vh;
  overflow: hidden;
}
/* select 视觉胶囊 —— 尺寸/底色按真实站实测:
   enabled:高 2.2em(31.7px)、底色 hsla(button 变量)、右侧箭头、内距 1ch/3.6ch
   disabled:高 1.65em(23.8px)、**无底色**、无箭头、无内距、文字 .7 透明(真实站 .emby-select[disabled])
   宽度:由无形 sizer(最宽选项)钉死 = 原生 select 固有宽度 —— 切换选项不改变尺寸 */
.emby-select-facade {
  display: inline-grid;
  grid-template-columns: auto;
  align-items: center;
  height: 100%; padding: 0 3.6ch 0 1ch;
  background: hsla(var(--button-background-hue), var(--button-background-saturation), var(--button-background-lightness), var(--button-background-alpha));
  border-radius: 100em;
  cursor: pointer;
  user-select: none;
}
.emby-select-facade > * { grid-area: 1 / 1; }
.emby-select-sizer { visibility: hidden; pointer-events: none; display: grid; }
.emby-select-sizer-item { grid-area: 1 / 1; white-space: nowrap; }
.emby-select-facade-disabled {
  cursor: default;
  background: none;
  padding: 0;
  opacity: .7;
}
.emby-select-selectedName { overflow: hidden; white-space: nowrap; text-overflow: ellipsis; display: flex; align-items: center; }
.selectArrowContainer { position: absolute; display: flex; top: .15em; bottom: .15em; right: .5em; align-items: center; pointer-events: none; }
.selectArrow { font-size: 1.7em; }
.selectLabel { display: flex; align-items: center; flex-shrink: 0; margin-bottom: 0; }
.selectLabelText-inline { margin-bottom: 0; margin-inline-end: .5em; }
.emby-select-wrapper { position: relative; height: 2.2em; }
/* disabled 包裹层高度 1.65em(真实站 emby-select_6.css) */
.emby-select-wrapper-inline-disabled { height: 1.65em; }
</style>