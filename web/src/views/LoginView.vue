<script setup lang="ts">
/**
 * 登录页 —— 1:1 还原 Emby 4.9.5 网页端 startup/manuallogin(ADR-0011,基准=真实服务器页端):
 * skinHeader(返回+logo)> form.auto-center > h1.viewTitle "登录 {server}"
 * > 用户名/密码 emby-input > 提示文本 > 胶囊登录/取消按钮 > 居中忘记密码 button-link
 * 真实站无公共用户卡视图(单服务器直出手动表单)。
 */
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import logoUrl from '@/assets/brand/lockup-on-dark.svg'

const auth = useAuthStore()
const router = useRouter()
const { t } = useI18n()

const SERVER_NAME = 'Serata'
const username = ref('')
const password = ref('')
const busy = ref(false)
const toast = ref('')

function showToast(msg: string) {
  toast.value = msg
  setTimeout(() => { toast.value = '' }, 3000)
}

async function onSubmit() {
  if (busy.value) return
  busy.value = true
  try {
    await auth.login(username.value, password.value)
    const redirect = router.currentRoute.value.query.redirect
    router.push(typeof redirect === 'string' ? redirect : '/')
  } catch (e) {
    const status = (e as { status?: number }).status
    showToast(status === 401 ? t('MessageInvalidUser') : t('MessageUnableToConnectToServer'))
    password.value = ''
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="backgroundContainer backgroundContainer-withBackdrop"></div>
  <header class="skinHeader focuscontainer-x focuscontainer-up headroom flex align-items-center flex-grow headerTop">
    <div class="headerLeft headerSection focuscontainer-x navout-x">
      <button type="button" is="paper-icon-button-light" class="headerBackButton headerButton headerSectionItem paper-icon-button-light emby-button-focusscale" @click="router.back()">
        <i class="md-icon autortl">arrow_back</i>
      </button>
      <h2 class="pageTitle headerSectionItem pageTitleWithLogo pageTitleWithDefaultLogo" :style="{ backgroundImage: `url(${logoUrl})` }"></h2>
    </div>
  </header>

  <main class="mainAnimatedPage itemAction">
    <div class="view flex flex-direction-column scrollFrameY flex-grow emby-scroller page scrollY">
      <div class="scrollSlider flex-grow flex-direction-column padded-left padded-right padded-top scrollSliderY">
        <form class="auto-center manualLoginForm" @submit.prevent="onSubmit">
          <h1 class="viewTitle">{{ t('ButtonSignIn') }} {{ SERVER_NAME }}</h1>

          <div class="inputContainer">
            <label class="inputLabel inputLabelUnfocused" for="txtManualName">{{ t('LabelUser') }}</label>
            <input is="emby-input" id="txtManualName" v-model="username" type="text" class="emby-input txtUserName" required autocomplete="username" />
          </div>

          <div class="inputContainer">
            <label class="inputLabel inputLabelUnfocused" for="txtManualPassword">{{ t('LabelPassword') }}</label>
            <input is="emby-input" id="txtManualPassword" v-model="password" type="password" class="emby-input txtPassword" autocomplete="current-password" />
          </div>

          <p class="fieldDescription">{{ t('login.startupHint') }}</p>

          <button is="emby-button" type="submit" class="raised block paperSubmit emby-button emby-button-focusscale" :disabled="busy">
            <span>{{ t('ButtonSignIn') }}</span>
          </button>
          <button is="emby-button" type="button" class="raised block buttonCancel emby-button emby-button-focusscale" @click="router.back()">
            <span>{{ t('ButtonCancel') }}</span>
          </button>

          <div class="buttonItems">
            <button is="emby-button" type="button" class="button-link cancel btnForgotPassword secondaryText buttonItems-item emby-button emby-button-focusscale" @click="showToast(t('ButtonForgotPassword') + ' — TODO')">
              <span>{{ t('ButtonForgotPassword') }}</span>
            </button>
          </div>
        </form>
      </div>
    </div>
  </main>

  <div class="toast" :class="{ toastVisible: !!toast }">{{ toast }}</div>
</template>