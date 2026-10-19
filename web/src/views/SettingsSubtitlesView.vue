<script setup lang="ts">
/**
 * 用户设置页(字幕)—— 1:1 还原 Emby 4.9.5 settings/subtitles.html
 * (基准=真实站运行时 dump,emby495-settings-subtitles.md;URL 直达取证,零点击零保存)。
 */
import { ref, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const LS_KEY = 'serata.subtitleSettings'

const store = ref<Record<string, string | boolean>>({
  subtitleLanguage: '',
  subtitleMode: 'default',
  rememberSubtitleSelections: true,
  textSize: 'normal',
  verticalPosition: 'Bottom',
  textColor: '#ffffff',
  dropShadow: 'uniform',
  backgroundColor: 'transparent',
  backgroundOpacity: '50',
  bottomPosition: '0',
  topPosition: '0',
})

const LANG_OPTIONS = [
  { value: '', label: '全部' },
  { value: 'chi', label: '中文' },
  { value: 'eng', label: '英语' },
  { value: 'jpn', label: '日语' },
  { value: 'kor', label: '韩语' },
]
const MODE_OPTIONS = [
  { value: 'default', label: '默认' },
  { value: 'smart', label: '智能' },
  { value: 'onlyforced', label: '仅强制字幕' },
  { value: 'hearingimpaired', label: '听力障碍' },
  { value: 'always', label: '始终播放字幕' },
  { value: 'none', label: '无字幕' },
]
const SIZE_OPTIONS = [
  { value: 'smallest', label: '特小' },
  { value: 'smaller', label: '小' },
  { value: 'normal', label: '正常' },
  { value: 'larger', label: '大' },
  { value: 'largest', label: '特大' },
  { value: 'x-large', label: '超大' },
]
const VPOS_OPTIONS = [
  { value: 'Bottom', label: 'Bottom' },
  ...Array.from({ length: 7 }, (_, i) => ({ value: `${(i + 1) * 10}`, label: `${(i + 1) * 10}%` })),
]
const COLOR_OPTIONS = [
  { value: 'black', label: '黑色' },
  { value: 'blue', label: '蓝色' },
  { value: 'cyan', label: '青色' },
  { value: 'darkgray', label: '深灰色' },
  { value: 'darkgrey', label: '暗灰色' },
  { value: 'gray', label: '灰色' },
  { value: 'green', label: '绿色' },
  { value: 'lightgray', label: '浅灰色' },
  { value: 'magenta', label: '品红' },
  { value: 'red', label: '红色' },
  { value: 'white', label: '白色' },
  { value: 'yellow', label: '黄色' },
]
const SHADOW_OPTIONS = [
  { value: 'none', label: '无' },
  { value: 'raised', label: '凸' },
  { value: 'depressed', label: '凹' },
  { value: 'uniform', label: '统一' },
  { value: 'dropshadow', label: '投影' },
]
const OPACITY_OPTIONS = Array.from({ length: 8 }, (_, i) => ({ value: String([10, 20, 25, 30, 40, 50, 60, 70][i]), label: `${[10, 20, 25, 30, 40, 50, 60, 70][i]}%` }))
const EDGE_OPTIONS = (max: number, step = 5) =>
  Array.from({ length: Math.floor(max / step) + 1 }, (_, i) => ({ value: String(i * step), label: `${i * step}%` }))

onMounted(() => {
  try {
    const saved = JSON.parse(localStorage.getItem(LS_KEY) ?? '{}')
    store.value = { ...store.value, ...saved }
  } catch { /* 损坏则忽略 */ }
})

watch(store, (v) => localStorage.setItem(LS_KEY, JSON.stringify(v)), { deep: true })

const COLOR_MAP: Record<string, string> = {
  black: '#000', blue: '#00f', cyan: '#0ff', darkgray: '#444', darkgrey: '#333', gray: '#888',
  green: '#0f0', lightgray: '#ccc', magenta: '#f0f', red: '#f00', white: '#fff', yellow: '#ff0',
}
</script>

<template>
  <main class="view flex flex-direction-column scrollFrameY flex-grow emby-scroller page scrollY overflowYScroll focuscontainer-x settingsView page-withFullDrawer page-withDockedDrawer">
    <div class="scrollSlider flex-grow flex-direction-column padded-left padded-left-page padded-right padded-top-page padded-bottom-page scrollSliderY">
      <form class="auto-center padded-top fieldsets" @submit.prevent>
        <fieldset>
          <legend>{{ t('settings.subGroup') }}</legend>
          <div class="fieldset-fields">
            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.subtitleLanguage') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.subtitleLanguage" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.subtitleLanguage')">
                    <option v-for="o in LANG_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.subtitleMode') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.subtitleMode" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.subtitleMode')">
                    <option v-for="o in MODE_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
              <div class="fieldDescription">{{ t('settings.subtitleModeDesc') }}</div>
            </div>

            <div class="toggleContainer fieldset-field">
              <label class="emby-toggle-label">
                <input v-model="store.rememberSubtitleSelections" type="checkbox" class="autoSetting autoSave emby-toggle" role="switch" />
                <span>{{ t('settings.rememberSubs') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
              <div class="fieldDescription toggleFieldDescription">{{ t('settings.rememberSubsDesc') }}</div>
            </div>
          </div>
        </fieldset>

        <fieldset>
          <legend>{{ t('settings.appearanceGroup') }}</legend>
          <div class="fieldset-fields">
            <!-- 预览块(真实站 subtitleappearance-preview) -->
            <div class="fieldset-field previewContainer">
              <div class="subtitleappearance-preview flex align-items-center justify-content-center">
                <div class="subtitleappearance-preview-window flex align-items-center justify-content-center">
                  <div
                    class="subtitleappearance-preview-text flex align-items-center justify-content-center"
                    :style="{
                      fontSize: { smallest: '.7em', smaller: '.85em', normal: '1em', larger: '1.2em', largest: '1.4em', 'x-large': '1.6em' }[store.textSize as string] ?? '1em',
                      color: COLOR_MAP[store.textColor as string] ?? '#fff',
                      textShadow: store.dropShadow === 'none' ? 'none' : '0 0 .25em #000',
                      backgroundColor: store.backgroundColor === 'transparent' ? 'transparent' : (COLOR_MAP[store.backgroundColor as string] ?? 'transparent'),
                      opacity: Number(store.backgroundOpacity) / 100,
                    }"
                  >{{ t('settings.previewText') }}</div>
                </div>
              </div>
              <div class="fieldDescription">{{ t('settings.appearanceNotApplicable') }}</div>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.textSize') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.textSize" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.textSize')">
                    <option v-for="o in SIZE_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.verticalPosition') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.verticalPosition" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.verticalPosition')">
                    <option v-for="o in VPOS_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.textColor') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.textColor" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.textColor')">
                    <option v-for="o in COLOR_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.dropShadow') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.dropShadow" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.dropShadow')">
                    <option v-for="o in SHADOW_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.backgroundColor') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.backgroundColor" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.backgroundColor')">
                    <option v-for="o in COLOR_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.backgroundOpacity') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.backgroundOpacity" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.backgroundOpacity')">
                    <option v-for="o in OPACITY_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>
          </div>
        </fieldset>

        <fieldset>
          <legend>{{ t('settings.positionGroup') }}</legend>
          <div class="fieldset-fields">
            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.bottomEdge') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.bottomPosition" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.bottomEdge')">
                    <option v-for="o in EDGE_OPTIONS(35)" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
              <div class="fieldDescription">{{ t('settings.bottomEdgeDesc') }}</div>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.topEdge') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.topPosition" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.topEdge')">
                    <option v-for="o in EDGE_OPTIONS(30)" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
              <div class="fieldDescription">{{ t('settings.topEdgeDesc') }}</div>
            </div>
          </div>
        </fieldset>
      </form>
    </div>
  </main>
</template>

<style scoped>
/* 字幕预览块(真实站 inline 形态静态化) */
.subtitleappearance-preview {
  border: .08em solid gray;
  border-radius: .24em;
  padding: 1em;
  height: 3em;
}
.subtitleappearance-preview-window { width: 90%; padding: .25em; }
.subtitleappearance-preview-text { font-size: 1em; }
</style>