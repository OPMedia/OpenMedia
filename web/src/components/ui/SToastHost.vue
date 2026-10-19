<script setup lang="ts">
/**
 * 全局 toast 宿主 —— 在 App.vue 里挂一次即可（含登录/播放页，因为它是壳的第三个根节点）。
 *
 * 拆两个组件的原因：`useToastManager()` 只能拿到**祖先** Provider 的 toasts，
 * 而 Provider 本身在这个组件的模板里，所以列表必须下沉到 SToastList。
 */
import { useI18n } from 'vue-i18n'
import { ToastPortal, ToastProvider } from 'reka-ui'
import { toastManager } from '@/api/toast'
import SToastList from './SToastList.vue'

const { t } = useI18n()
</script>

<template>
  <ToastProvider
    :toast-manager="toastManager"
    :label="t('ui.notifications')"
    :duration="3600"
    :limit="4"
    swipe-direction="right"
  >
    <ToastPortal>
      <SToastList />
    </ToastPortal>
  </ToastProvider>
</template>
