<script setup lang="ts">
/**
 * Emby select —— 1:1 还原 4.9.5 select actionsheet 弹层(基准=真实站取证):
 * - 触发态:胶囊 select(selectContainer-inline + emby-select-inline,圆角 100em,
 *   bg var(--button-background) rgba(104,98,106,.7),右侧 selectArrow expand_more)
 * - 弹层:actionSheet dialog,positionY=bottom / positionX=left / transformOrigin=center top,
 *   minWidthToElement(与触发器同宽),项 = Name(+secondaryText)+ 选中态 check 图标
 * - 选项结构:{ label, secondaryText?, icon? },受控 v-model
 *
 * 行为层已收敛到基座 SSheet(reka-ui Dialog,modal=false):
 *   ✅ 点弹层外关闭 —— 旧实现把 `@click.stop="onDocClick"` 挂在 rootEl 上,
 *      而 onDocClick 判断 `!rootEl.contains(e.target)` 恒为假、又没有 document 级监听,
 *      于是不点选项就永远关不掉;现在由 DismissableLayer 的 document 监听负责。
 *   ✅ Esc 关闭 + 焦点归还触发器;进出动效;列表 role="listbox"/"option" 语义
 *      —— 旧 v-if 硬弹这三样全丢。
 * 键盘:Tab 到触发器 → Enter/Space 开合 → ↑↓ 移动 → Enter 选中 → Esc 关闭 → 焦点回触发器。
 * 触发器自身 pointerdown 用 .stop 拦住:否则"点触发器关闭"会被当成点外关闭,先被 reka 关掉,
 * 再被本次 click 重新打开(典型双触发)。
 */
import { computed, onBeforeUnmount, ref, useId, watch } from 'vue'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import SSheet from '@/components/ui/SSheet.vue'
import { flipConfig, useReducedMotionSafe } from '@/lib/motion'

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
const facadeEl = ref<HTMLButtonElement | null>(null)

/* 列表/选项 id 用 Vue 3.5 的 useId 生成,多实例同页不撞 id(aria-activedescendant 依赖它) */
const uid = useId()
const listId = `emby-select-${uid}`
function optionId(index: number): string {
  return `${listId}-option-${index}`
}

const current = computed(() => props.options.find((o) => o.value === props.modelValue))
const currentLabel = computed(() => current.value?.label ?? props.options[0]?.label ?? '')
/** 触发器可读名 = 字段名 + 当前值(不新增文案,只用调用方已给的 label) */
const triggerLabel = computed(() => (props.label ? `${props.label} ${currentLabel.value}` : currentLabel.value))

/** 最宽选项测量层:把所有选项叠在同一格渲染,宽度 = max(渲染宽度)(精确等价原生 select 固有宽度) */
const sizerLabels = computed(() => props.options.flatMap((o) => [o.label, o.secondaryText].filter(Boolean) as string[]))

/* ---------- 列表 FLIP:auto-animate 动手前必须过 useReducedMotionSafe ---------- */
const reduced = useReducedMotionSafe()
const listFlip = computed(() => flipConfig(reduced.value))

/* ---------- 定位:触发器正下方,左对齐,宽度 >= 触发器(真实站 positionTo=select 元素) ---------- */
const positionTick = ref(0)
const popupStyle = computed<Record<string, string | number>>(() => {
  // 依赖 tick:resize/scroll 时重算(纯 DOM 测量不会自己触发响应)
  void positionTick.value
  const anchor = facadeEl.value ?? rootEl.value
  const style: Record<string, string | number> = { maxHeight: '70vh' }
  if (!anchor) return style
  const r = anchor.getBoundingClientRect()
  const vw = window.innerWidth
  const GAP = 8
  const wantW = props.matchWidth ? r.width : 0

  style.top = r.bottom + 4 + 'px'

  // 面板的 left 原本直接取触发器左沿,而宽度上限是 SSheet 的固定 92vw —— 两者互不知情。
  // 于是「靠右的触发器 + 较宽的选项文字」会把面板顶出视口:固定定位元素溢出时不产生横向滚动条,
  // 结果是被静默裁掉(实测 390px 视口下可溢出 67px)。真实音频/字幕轨名普遍很长,这不是理论问题。
  // 修法:先让 left 在右侧放不下时左移,再用 maxWidth 把宽度夹到剩余空间 —— 让「面板必在视口内」
  // 成为构造上的不变式,而不是依赖数据恰好不长。
  const left = Math.max(GAP, Math.min(r.left, vw - wantW - GAP))
  style.left = left + 'px'
  if (props.matchWidth) style.minWidth = wantW + 'px'
  style.maxWidth = Math.max(120, vw - left - GAP) + 'px'
  return style
})

function onViewportChange() {
  if (open.value) positionTick.value += 1
}
function bindViewport() {
  window.addEventListener('resize', onViewportChange, { passive: true })
  window.addEventListener('scroll', onViewportChange, { passive: true, capture: true })
}
function unbindViewport() {
  window.removeEventListener('resize', onViewportChange)
  window.removeEventListener('scroll', onViewportChange, { capture: true })
}

/* ---------- 键盘高亮项(combobox + aria-activedescendant,焦点始终留在触发器上) ---------- */
const activeIndex = ref(-1)
/** 高亮只在键盘导航时出现:鼠标打开时不画底色,保持与旧实现的 1:1 视觉 */
const keyboardNavigating = ref(false)
const activeDescendant = computed(() =>
  open.value && activeIndex.value >= 0 ? optionId(activeIndex.value) : undefined,
)

watch(open, (isOpen) => {
  if (isOpen) {
    positionTick.value += 1
    bindViewport()
  } else {
    unbindViewport()
    activeIndex.value = -1
    keyboardNavigating.value = false
  }
})
onBeforeUnmount(unbindViewport)

function openSheet() {
  if (props.disabled || props.options.length === 0) return
  const selected = props.options.findIndex((o) => o.value === props.modelValue)
  activeIndex.value = selected >= 0 ? selected : 0
  // 先把焦点交给触发器:reka 在 DialogContent 挂载时用 activeElement 记住「触发器」,
  // 之后 Esc/选中的焦点归还、以及"点触发器不算点外"都依赖它
  facadeEl.value?.focus()
  open.value = true
}

function toggle() {
  if (props.disabled) return
  if (open.value) open.value = false
  else openSheet()
}

function pick(option: EmbySelectOption) {
  emit('update:modelValue', option.value)
  open.value = false
}

function moveActive(delta: number) {
  const total = props.options.length
  if (total === 0) return
  const base = activeIndex.value < 0 ? (delta > 0 ? -1 : 0) : activeIndex.value
  activeIndex.value = (base + delta + total) % total
}

function onKeydown(event: KeyboardEvent) {
  if (props.disabled) return
  switch (event.key) {
    case 'Enter':
    case ' ':
      // 吞掉 button 的合成 click,避免与这里重复处理
      event.preventDefault()
      if (!open.value) {
        openSheet()
      } else {
        const active = activeIndex.value >= 0 ? props.options[activeIndex.value] : undefined
        if (active) pick(active)
        else open.value = false
      }
      break
    case 'ArrowDown':
      event.preventDefault()
      keyboardNavigating.value = true
      if (open.value) moveActive(1)
      else openSheet()
      break
    case 'ArrowUp':
      event.preventDefault()
      keyboardNavigating.value = true
      if (open.value) moveActive(-1)
      else openSheet()
      break
    case 'Home':
      if (!open.value) return
      event.preventDefault()
      keyboardNavigating.value = true
      activeIndex.value = 0
      break
    case 'End':
      if (!open.value) return
      event.preventDefault()
      keyboardNavigating.value = true
      activeIndex.value = props.options.length - 1
      break
    case 'Tab':
      // Tab 离开即收起(不拦默认行为,焦点照常前进)
      if (open.value) open.value = false
      break
    default:
      break
  }
}

/** 焦点留在触发器上做 aria-activedescendant 导航,所以按住基座的初始聚焦 */
function onOpenAutoFocus(event: Event) {
  event.preventDefault()
}

const ICON_CHECK = '\uE5CA'
const ICON_ARROW = '\uE5C5' // expand_more 码位
</script>

<template>
  <div
    ref="rootEl"
    class="selectContainer selectContainer-inline trackSelectionFieldContainer fieldItems-item flex-shrink-zero"
    @pointerdown.stop
    @keydown="onKeydown"
  >
    <label class="selectLabel selectLabel-inline">
      <div v-if="label" class="selectLabelText selectLabelText-inline">{{ label }}</div>
      <div class="emby-select-wrapper emby-select-wrapper-inline" :class="{ 'emby-select-wrapper-inline-disabled': disabled }">
        <!-- 视觉:胶囊 + 当前值 + 箭头(真实站 select 为透明原生元素,视觉由 wrapper 层给出)
             宽度用「最宽选项」的无形测量层钉死 —— 与原生 select 固有宽度一致,切换选项不再抖 -->
        <button
          ref="facadeEl"
          type="button"
          class="emby-select-facade"
          :class="{ 'emby-select-facade-disabled': disabled }"
          role="combobox"
          aria-haspopup="listbox"
          :aria-expanded="open"
          :aria-controls="listId"
          :aria-activedescendant="activeDescendant"
          :aria-label="triggerLabel"
          :disabled="disabled"
          @click="toggle"
        >
          <span class="emby-select-sizer" aria-hidden="true">
            <span v-for="(l, i) in sizerLabels" :key="i" class="emby-select-sizer-item">{{ l }}</span>
          </span>
          <span class="emby-select-selectedName">{{ currentLabel }}</span>
          <span v-if="!disabled" class="selectArrowContainer selectArrowContainer-inline"><i class="selectArrow md-icon">{{ ICON_ARROW }}</i></span>
        </button>
      </div>
    </label>

    <SSheet
      :open="open"
      variant="pop-start"
      :modal="false"
      :title="label ?? currentLabel"
      panel-class="actionSheet select-actionSheet"
      :content-style="popupStyle"
      @close="open = false"
      @open-auto-focus="onOpenAutoFocus"
    >
      <div class="actionSheetScroller focuscontainer-x actionSheetScroller-padding-inline emby-scroller dialog-scrollY scrollY scrollFrameY flex-direction-column">
        <div
          :id="listId"
          v-auto-animate="listFlip"
          role="listbox"
          :aria-label="label ?? currentLabel"
          class="actionsheetScrollSlider scrollSlider flex flex-direction-column itemsContainer vertical-list scrollSliderY"
        >
          <button
            v-for="(o, i) in options" :key="o.value"
            :id="optionId(i)"
            type="button"
            role="option"
            tabindex="-1"
            :aria-selected="o.value === modelValue"
            class="listItem listItem-autoactive itemAction listItemCursor listItem-hoverable actionSheetMenuItem"
            :class="{ 'is-keyboard-active': keyboardNavigating && i === activeIndex }"
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
    </SSheet>
  </div>
</template>

<style scoped>
/* 弹层外观全部由基座 SSheet 提供(--s-raised / --s-text-1 / --s-line / --s-shadow-sheet):
   旧实现这里是 position:fixed + rgb(40,40,43) + rgba(255,255,255,.87),浅色模式下是黑方块。
   这里只保留 .select-actionSheet 作为 DOM 钩子(定位/尺寸走 content-style)。 */

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
  /* <button> 复位:盒子模型与旧 <div> 完全一致(尺寸/字重/颜色都继承),这不是重设计 */
  appearance: none;
  -webkit-appearance: none;
  border: 0;
  margin: 0;
  font: inherit;
  color: inherit;
  text-align: start;
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

/* 键盘高亮项:选项本身 tabindex=-1(焦点留在触发器上),没有这个指示 ↑↓ 就成了盲盒 */
.is-keyboard-active { background: var(--s-hover); }
</style>
