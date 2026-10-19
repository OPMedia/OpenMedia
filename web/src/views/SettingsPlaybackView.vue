<script setup lang="ts">
/**
 * 用户设置页(播放)—— 1:1 还原 Emby 4.9.5 settings/playback.html
 * (基准=真实站运行时 dump,emby495-settings-playback.md;URL 直达取证,零点击零保存)。
 * 结构与 SettingsDisplayView 同型(form.fieldsets > fieldset > select/toggle 字段)。
 */
import { ref, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const LS_KEY = 'serata.playbackSettings'

const store = ref<Record<string, string | boolean>>({
  audioLanguage: '',
  playDefaultAudioTrack: true,
  rememberAudioSelections: true,
  autoPlayNext: true,
  showNextUpPreview: true,
  skipForwardLength: '10',
  skipBackwardLength: '10',
  resumeRewindSeconds: '',
  introSkipMode: 'button',
  enableIdleCheck: true,
  showRatingInfoOnStart: true,
  upAction: 'toggleosd',
})

const LANG_OPTIONS = [
  { value: '', label: '全部' },
  { value: 'chi', label: '中文' },
  { value: 'eng', label: '英语' },
  { value: 'jpn', label: '日语' },
  { value: 'kor', label: '韩语' },
]
const SKIP_LENGTH_OPTIONS = [
  { value: '5', label: '5秒钟' },
  { value: '10', label: '10秒钟' },
  { value: '15', label: '15秒钟' },
  { value: '20', label: '20秒钟' },
  { value: '25', label: '25秒钟' },
  { value: '30', label: '30秒钟' },
]
const RESUME_REWIND_OPTIONS = [
  { value: '', label: '关' },
  { value: '5', label: '5秒钟' },
  { value: '10', label: '10秒钟' },
  { value: '15', label: '15秒钟' },
  { value: '20', label: '20秒钟' },
]
const INTRO_SKIP_OPTIONS = [
  { value: 'off', label: '关' },
  { value: 'button', label: '显示 “跳过片头” 按钮' },
  { value: 'auto', label: '自动跳过片头' },
]
const UP_ACTION_OPTIONS = [
  { value: 'toggleosd', label: '显示屏幕控件' },
  { value: 'chapters', label: '章节 / 电视直播指南' },
]

onMounted(() => {
  try {
    const saved = JSON.parse(localStorage.getItem(LS_KEY) ?? '{}')
    store.value = { ...store.value, ...saved }
  } catch { /* 损坏则忽略 */ }
})

watch(store, (v) => localStorage.setItem(LS_KEY, JSON.stringify(v)), { deep: true })
</script>

<template>
  <main class="view flex flex-direction-column scrollFrameY flex-grow emby-scroller page scrollY overflowYScroll focuscontainer-x settingsView page-withFullDrawer page-withDockedDrawer">
    <div class="scrollSlider flex-grow flex-direction-column padded-left padded-left-page padded-right padded-top-page padded-bottom-page scrollSliderY">
      <form class="auto-center padded-top fieldsets" @submit.prevent>
        <fieldset>
          <legend>{{ t('settings.audioGroup') }}</legend>
          <div class="fieldset-fields">
            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.audioLanguage') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.audioLanguage" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.audioLanguage')">
                    <option v-for="o in LANG_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="toggleContainer fieldset-field">
              <label class="emby-toggle-label">
                <input v-model="store.playDefaultAudioTrack" type="checkbox" class="autoSetting autoSave emby-toggle" role="switch" />
                <span>{{ t('settings.playDefaultAudio') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
            </div>

            <div class="toggleContainer fieldset-field">
              <label class="emby-toggle-label">
                <input v-model="store.rememberAudioSelections" type="checkbox" class="autoSetting autoSave emby-toggle" role="switch" />
                <span>{{ t('settings.rememberAudio') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
              <div class="fieldDescription toggleFieldDescription">{{ t('settings.rememberAudioDesc') }}</div>
              <button type="button" class="button-link button-link-color-inherit emby-button emby-button-focusscale btnClearAudioSelection">
                <i class="md-icon button-icon button-icon-left">&#xE16B;</i>
                <span>{{ t('settings.clearAudio') }}</span>
              </button>
            </div>
          </div>
        </fieldset>

        <fieldset>
          <legend>{{ t('settings.advancedGroup') }}</legend>
          <div class="fieldset-fields">
            <div class="toggleContainer fieldset-field">
              <label class="emby-toggle-label">
                <input v-model="store.autoPlayNext" type="checkbox" class="autoSetting autoSave emby-toggle" role="switch" />
                <span>{{ t('settings.autoPlayNext') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
            </div>

            <div class="toggleContainer fieldset-field">
              <label class="emby-toggle-label">
                <input v-model="store.showNextUpPreview" type="checkbox" class="autoSetting autoSave emby-toggle" role="switch" />
                <span>{{ t('settings.nextUpPreview') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
              <div class="fieldDescription toggleFieldDescription">{{ t('settings.nextUpPreviewDesc') }}</div>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.skipForward') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.skipForwardLength" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.skipForward')">
                    <option v-for="o in SKIP_LENGTH_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.skipBackward') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.skipBackwardLength" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.skipBackward')">
                    <option v-for="o in SKIP_LENGTH_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.resumeRewind') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.resumeRewindSeconds" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.resumeRewind')">
                    <option v-for="o in RESUME_REWIND_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
              <div class="fieldDescription">{{ t('settings.resumeRewindDesc') }}</div>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.introSkipMode') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.introSkipMode" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.introSkipMode')">
                    <option v-for="o in INTRO_SKIP_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
              <div class="fieldDescription">{{ t('settings.introSkipDesc') }}</div>
            </div>

            <div class="toggleContainer fieldset-field">
              <label class="emby-toggle-label">
                <input v-model="store.enableIdleCheck" type="checkbox" class="autoSetting autoSave emby-toggle" role="switch" />
                <span>{{ t('settings.idleCheck') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
              <div class="fieldDescription toggleFieldDescription">{{ t('settings.idleCheckDesc') }}</div>
            </div>

            <div class="toggleContainer fieldset-field">
              <label class="emby-toggle-label">
                <input v-model="store.showRatingInfoOnStart" type="checkbox" class="autoSetting autoSave emby-toggle" role="switch" />
                <span>{{ t('settings.ratingInfoOnStart') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
              <div class="fieldDescription toggleFieldDescription">{{ t('settings.ratingInfoDesc') }}</div>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.upAction') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.upAction" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.upAction')">
                    <option v-for="o in UP_ACTION_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
              <div class="fieldDescription">{{ t('settings.upActionDesc') }}</div>
            </div>
          </div>
        </fieldset>
      </form>
    </div>
  </main>
</template>