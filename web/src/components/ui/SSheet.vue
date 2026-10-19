<script setup lang="ts">
/**
 * SSheet —— Serata 弹层基元（reka-ui Dialog 驱动）。
 *
 * 它只负责「行为」，不负责「长相」：
 *   ✅ 焦点陷阱（modal 时）、Esc 关闭、点外关闭、滚动锁定、aria-modal
 *   ✅ 关闭后焦点归还 —— modal=true 由 reka 的 FocusScope 负责；
 *      modal=false 没有 FocusScope，所以由本组件自己补（见下方 focusReturnTarget 的注释）
 *   ✅ 进入/退出动效（由 styles/serata/motion.css 的 data-state 动画提供，此处零关键帧）
 *   ❌ 具体版式 —— 由默认插槽提供；宿主可以继续用 emby495 的 actionSheet 类名
 *
 * 为什么是 reka-ui 而不是 shadcn-vue：shadcn-vue 就是 Reka UI 套了一层 Tailwind 皮，
 * 而本项目的设计层是冻结的 CSS 令牌（禁止原子类进来当第二真源）。要能力，不要皮。
 *
 * 用法：
 *   <SSheet :open="open" variant="pop" :title="t('shell.userMenu')" @close="open = false">
 *     <div class="actionSheetScroller">…</div>
 *   </SSheet>
 */
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  DialogContent,
  DialogDescription,
  DialogOverlay,
  DialogPortal,
  DialogRoot,
  DialogTitle,
  VisuallyHidden,
} from 'reka-ui'

type SheetVariant = 'pop' | 'pop-start' | 'center' | 'bottom' | 'end'
type ClassBind = string | Record<string, boolean> | Array<string | Record<string, boolean>>

const props = withDefaults(
  defineProps<{
    /** 受控开合；内部一律通过 close 事件改它，不在组件里存第二份状态 */
    open: boolean
    /** 版式：pop=右上角 actionSheet / pop-start=左上角 / center=居中对话框 / bottom=底部弹层 / end=贴行尾侧栏 */
    variant?: SheetVariant
    /** true = 真模态（焦点陷阱 + 对读屏隐藏其余内容）；false = 轻量浮层（如下拉选择） */
    modal?: boolean
    /** 点遮罩是否关闭 */
    dismissible?: boolean
    /** Esc 是否关闭 */
    closeOnEscape?: boolean
    /** 无障碍标题（只做可访问名，**不渲染成可见标题**）。为空时回落到 t('ui.dialog') */
    title?: string
    /** 无障碍描述（同上，只做可访问描述） */
    description?: string
    /** 面板附加类（宿主自己的版式类） */
    panelClass?: ClassBind
    /** 遮罩附加类 */
    scrimClass?: ClassBind
    /** 面板内联样式（如 EmbySelect 的锚点定位） */
    contentStyle?: Record<string, string | number>
    /** 覆盖 z-index（默认吃 --s-z-sheet） */
    zIndex?: number
  }>(),
  {
    variant: 'pop',
    modal: true,
    dismissible: true,
    closeOnEscape: true,
  },
)

const emit = defineEmits<{
  close: []
  /** 面板刚获得焦点（可 preventDefault 接管初始焦点） */
  openAutoFocus: [event: Event]
  /** 面板即将关闭、焦点要归还（可 preventDefault 接管） */
  closeAutoFocus: [event: Event]
}>()

const { t } = useI18n()

const VARIANT_CLASS: Record<SheetVariant, string> = {
  pop: 's-sheet--pop',
  'pop-start': 's-sheet--pop-start',
  center: 's-sheet--center',
  bottom: 's-sheet--bottom',
  end: 's-sheet--end',
}

/** 动效类与版式解耦：pop/center 用缩放，bottom/end 用位移 */
const MOTION_CLASS: Record<SheetVariant, string> = {
  pop: 's-anim-pop',
  'pop-start': 's-anim-pop',
  center: 's-anim-pop',
  bottom: 's-anim-up',
  end: 's-anim-end',
}

const panelClasses = computed(() => [
  's-sheet__panel',
  VARIANT_CLASS[props.variant],
  MOTION_CLASS[props.variant],
  props.panelClass,
])

const panelStyle = computed(() => ({
  ...(props.zIndex === undefined ? {} : { zIndex: props.zIndex }),
  ...props.contentStyle,
}))

function onOpenChange(value: boolean) {
  if (!value) emit('close')
}

/** dismissible=false 时把 reka 的默认关闭行为按下去（不改 open，只 preventDefault） */
function onPointerDownOutside(event: Event) {
  if (!props.dismissible) event.preventDefault()
}
function onFocusOutside(event: Event) {
  if (!props.dismissible) event.preventDefault()
}
function onEscapeKeyDown(event: KeyboardEvent) {
  if (!props.closeOnEscape) event.preventDefault()
}

/**
 * 非模态弹层（modal=false）的焦点归还。
 *
 * 为什么需要单独补：reka 只在**模态**分支里挂 FocusScope，焦点归还也是 FocusScope 的职责。
 * modal=false 时没有 FocusScope，于是关闭后 activeElement 会掉到 <body>。
 * 实测证据（/item/101 的 EmbySelect，点面板外空白关闭）：
 *   activeElement === body.mainAnimatedPages，且用 HTMLElement.prototype.focus 插桩证明
 *   **应用层从未调用过 focus()** —— 键盘/读屏用户关闭下拉后直接失去位置。
 *   同一轮里 Esc 路径、以及其余 6 个 modal=true 弹层的点外路径都正常归还。
 *
 * 归还策略刻意保守：只在焦点「无处安放」（落到 body / 丢失）时归还。
 * 用户若点了别的可聚焦元素，绝不许抢焦点。
 */
const focusReturnTarget = ref<HTMLElement | null>(null)

watch(
  () => props.open,
  (isOpen, wasOpen) => {
    if (isOpen && !wasOpen) {
      const active = document.activeElement
      // EmbySelect 这类宿主会先 focus() 触发器再 open，所以这里拿到的是触发器
      focusReturnTarget.value = active instanceof HTMLElement && active !== document.body ? active : null
      return
    }
    if (isOpen || !wasOpen) return
    // modal=true 交给 reka 的 FocusScope，别插手（会和它打架）
    if (props.modal) return
    const target = focusReturnTarget.value
    if (!target) return
    // 等退出动效走完（--s-dur-exit 180ms）再归还，否则面板还在离场时会被 reka 覆盖掉
    window.setTimeout(() => {
      if (!target.isConnected) return
      const active = document.activeElement
      if (active === null || active === document.body) target.focus()
    }, 200)
  },
)
</script>

<template>
  <DialogRoot :open="open" :modal="modal" @update:open="onOpenChange">
    <DialogPortal>
      <DialogOverlay class="s-sheet__scrim s-anim-scrim" :class="scrimClass" />
      <DialogContent
        class="s-sheet__panel-host"
        :class="panelClasses"
        :style="panelStyle"
        data-s-sheet
        @escape-key-down="onEscapeKeyDown"
        @pointer-down-outside="onPointerDownOutside"
        @focus-outside="onFocusOutside"
        @open-auto-focus="emit('openAutoFocus', $event)"
        @close-auto-focus="emit('closeAutoFocus', $event)"
      >
        <!-- 标题/描述只做「可访问名」，一律屏读可见、视觉不可见。
             原因：已迁移的 8 个弹层各自都带可见标题区（actionSheetTitle / formDialogHeaderTitle /
             actionsheetItemPreviewText-main），这里再画一行就会多出一个贴边角的重复标题，
             而且面板本身没有 padding，那行标题会直接顶到边框上。
             需要可见标题的宿主把 <h2> 放进默认插槽——**长相归插槽**，SSheet 只管行为。 -->
        <VisuallyHidden>
          <DialogTitle>{{ title || t('ui.dialog') }}</DialogTitle>
          <DialogDescription>{{ description || t('ui.dialog') }}</DialogDescription>
        </VisuallyHidden>

        <slot />
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>

<style scoped>
/* 遮罩：两种主题都必须「压暗」，所以用 --s-scrim-modal 而不是浅色下会提亮的 --s-scrim */
.s-sheet__scrim {
  position: fixed;
  inset: 0;
  z-index: var(--s-z-sheet);
  background: var(--s-scrim-modal);
  -webkit-backdrop-filter: blur(2px);
  backdrop-filter: blur(2px);
}

.s-sheet__panel {
  position: fixed;
  z-index: var(--s-z-sheet);
  max-width: 92vw;
  max-height: 84vh;
  overflow: hidden;
  background: var(--s-raised);
  color: var(--s-text-1);
  border: 1px solid var(--s-line);
  border-radius: var(--s-r-lg);
  box-shadow: var(--s-shadow-sheet);
  outline: none;
}

/* ---------- 版式 ---------- */
/* 右上角 actionSheet（header 用户菜单 / 库页排序）：与旧 SheetMenu 的 4em/1em 对齐 */
.s-sheet--pop {
  top: 58px;
  inset-inline-end: 14px;
  --s-pop-origin: top right;
}
/* 左上角 actionSheet（条目「更多」）：与旧 ItemMenuSheet 的 8em/8px 对齐 */
.s-sheet--pop-start {
  top: 115px;
  inset-inline-start: 8px;
  --s-pop-origin: top left;
}
/* 居中对话框：用 inset:0 + margin:auto 居中，避免 transform 与关键帧打架 */
.s-sheet--center {
  inset: 0;
  margin: auto;
  --s-pop-origin: center center;
}
/* 底部弹层（移动端） */
.s-sheet--bottom {
  inset-inline: 0;
  bottom: 0;
  border-end-start-radius: 0;
  border-end-end-radius: 0;
  max-height: 88vh;
}
/* 贴行尾的侧边弹层 */
.s-sheet--end {
  top: 0;
  bottom: 0;
  inset-inline-end: 0;
  max-height: none;
  border-start-end-radius: 0;
  border-end-end-radius: 0;
  --s-pop-origin: center right;
}

/* 标题/描述的可见样式已删除：它们现在由 reka 的 VisuallyHidden 兜住，只做可访问名。
   宿主若需要可见标题，在自己的插槽内容里写，别指望 SSheet 出样式。 */
</style>
