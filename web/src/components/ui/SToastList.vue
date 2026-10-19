<script setup lang="ts">
/**
 * toast 列表 —— 必须是 ToastProvider 的**后代**才能拿到 useToastManager()，
 * 所以它单独成一个组件（SToastHost 负责 Provider，这里负责渲染）。
 */
import { useI18n } from 'vue-i18n'
import { ToastClose, ToastDescription, ToastRoot, ToastTitle, ToastViewport, useToastManager } from 'reka-ui'

const { t } = useI18n()
const { toasts } = useToastManager()
</script>

<template>
  <ToastViewport class="s-toast__viewport" :label="t('ui.notifications')">
    <ToastRoot
      v-for="toast in toasts"
      :key="toast.id"
      :toast="toast"
      class="s-toast s-anim-up"
      :class="`s-toast--${toast.status ?? 'info'}`"
      :data-toast-id="toast.id"
    >
      <span class="s-toast__marker" aria-hidden="true"></span>
      <div class="s-toast__body">
        <ToastTitle class="s-toast__title" />
        <ToastDescription v-if="toast.description" class="s-toast__desc" />
      </div>
      <ToastClose class="s-toast__close" :aria-label="t('ui.dismiss')">
        <svg class="s-toast__closeicon" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M6 6l12 12M18 6 6 18" />
        </svg>
      </ToastClose>
    </ToastRoot>
  </ToastViewport>
</template>

<!--
  ⚠️ 这里**故意不用 `scoped`**，不是漏写。

  reka 的 ToastViewport / ToastRoot 经过 Primitive 转发属性，Vue 的 `data-v-*` 作用域标记
  落不到真正的 `<ol class="s-toast__viewport">` / `<li class="s-toast">` 上——实测标记落在了它们
  **父级那个 div** 上（探针输出：OL/LI 的 attributeNames 里没有 data-v，父级 div 有）。
  后果：`scoped` 版本编译出的 `.s-toast__viewport[data-v-xxx]` 全部空转，toast 会退化成
  文档流顶部一条 1400px 宽的裸列表（position:static、透明底），而全局的 `.s-anim-up` 却照常生效，
  非常容易被"有动画在跑"骗过去。

  这些类名（.s-toast / .s-toast__* / .s-toast--*）在全项目唯一，且 toast 本身被 teleport 到
  <body>，逻辑上就在所有 scoped 子树之外，所以用全局样式是正解而非权宜。
  对比：SSheet.vue 用 scoped 是有效的（DialogContent/DialogOverlay 能拿到 data-v），不要照抄这里。
-->
<style>
/* 视口：右下角。容器不吃指针事件（否则会挡住页面点击），只有 toast 本体吃 */
.s-toast__viewport {
  position: fixed;
  z-index: var(--s-z-toast);
  inset-inline-end: var(--s-sp-5);
  bottom: calc(var(--s-sp-5) + env(safe-area-inset-bottom, 0px));
  display: flex;
  flex-direction: column;
  gap: var(--s-sp-2);
  width: min(380px, calc(100vw - var(--s-sp-8)));
  margin: 0;
  padding: 0;
  list-style: none;
  outline: none;
  pointer-events: none;
}

.s-toast {
  position: relative;
  display: flex;
  align-items: flex-start;
  gap: var(--s-sp-3);
  padding: var(--s-sp-3) var(--s-sp-4);
  background: var(--s-raised);
  color: var(--s-text-1);
  border: 1px solid var(--s-line);
  border-radius: var(--s-r-md);
  box-shadow: var(--s-shadow-sheet);
  pointer-events: auto;
  /* 支持 reka 的滑动手势：只动 transform，交给库接管 */
  touch-action: pan-y;
}

/* 左侧语义指示条：用绝对定位的窄条而不是 border-inline-start，
   这样圆角不会被切掉，状态色也只占 3px */
.s-toast__marker {
  position: absolute;
  inset-block: var(--s-sp-2);
  inset-inline-start: 0;
  width: 3px;
  border-radius: 0 var(--s-r-pill) var(--s-r-pill) 0;
  background: var(--s-accent);
}
.s-toast--success .s-toast__marker {
  background: var(--s-ok);
}
.s-toast--error .s-toast__marker {
  background: var(--s-danger);
}

.s-toast__body {
  flex: 1 1 auto;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.s-toast__title {
  font-size: var(--s-fs-sm);
  font-weight: var(--s-fw-medium);
  line-height: var(--s-lh-snug);
  word-break: break-word;
}
.s-toast__desc {
  font-size: var(--s-fs-xs);
  color: var(--s-text-2);
  line-height: var(--s-lh-snug);
  word-break: break-word;
}

.s-toast__close {
  flex: 0 0 auto;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 0;
  border: 0;
  border-radius: var(--s-r-sm);
  background: transparent;
  color: var(--s-text-3);
  cursor: pointer;
  transition:
    color var(--s-dur-fast) var(--s-ease-std),
    background-color var(--s-dur-fast) var(--s-ease-std);
}
.s-toast__close:hover {
  color: var(--s-text-1);
  background: var(--s-hover);
}
.s-toast__closeicon {
  width: 14px;
  height: 14px;
  fill: none;
  stroke: currentColor;
  stroke-width: 2;
  stroke-linecap: round;
}

/* 超出 Provider limit 的老 toast 会被 reka 标成 data-limited + inert：
   它们只是"暂存在 DOM 里"，视觉上必须消失，否则会堆成一坨 */
.s-toast[data-limited] {
  display: none;
}

@media (max-width: 600px) {
  .s-toast__viewport {
    inset-inline: var(--s-sp-3);
    width: auto;
  }
}
</style>
