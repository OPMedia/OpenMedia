<script setup lang="ts">
/**
 * 用户设置页(通用)—— 1:1 还原 Emby 4.9.5 settings/appgeneral.html
 * (基准=真实站运行时 dump,emby495-settings-appgeneral.md;URL 直达取证,零点击零保存)。
 * 组"显示":语言/时区/启动行为/文本大小/图像大小/屏幕保护/音效/幻灯片时长/主题曲×3/背景图/投屏遥控/退出菜单。
 * (开机运行 = 桌面版专属,web 隐藏;电视显示模式同。)
 */
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { setLocale } from '@/i18n'

const { t, locale } = useI18n()

const LS_KEY = 'serata.generalSettings'

const store = ref<Record<string, string | boolean>>({
  language: 'zh-CN',
  timezone: '',
  startupBehavior: 'home',
  textSize: 'auto',
  imageSize: 'auto',
  screensaver: 'none',
  soundEffects: 'none',
  slideshowDuration: '5',
  playThemeSongs: false,
  playThemeVideos: false,
  repeatThemeMedia: false,
  enableBackdrop: true,
  showRemoteControl: true,
  exitMenu: 'back',
})

/**
 * 语言选项：语言名按惯例用各自的语言书写（不翻译），并且只列**真的有语言包**的两种。
 * 之前还有一条「日本語」，但 i18n 里没有 ja 资源，选了只会静默回落到英文——假选项。
 */
const LANG_OPTIONS = [
  { value: 'zh-CN', label: '简体中文' },
  { value: 'en', label: 'English' },
]
/**
 * 下拉选项一律用 computed 包一层：语言开关一改，选项文案要立刻跟着变。
 * 模块级常量只在组件首次求值时算一次，切语言后会留着旧文案——这正是
 * 「切了英文但下拉框还是中文」的成因。
 * "UTC" 是时区缩写，两种语言同形，不做翻译。
 */
const TZ_OPTIONS = computed(() => [
  { value: '', label: t('settings.tzAuto') },
  { value: 'Asia/Shanghai', label: t('settings.tzShanghai') },
  { value: 'UTC', label: 'UTC' },
])
const STARTUP_OPTIONS = computed(() => [
  { value: 'home', label: t('settings.startHome') },
  { value: 'login', label: t('settings.startLogin') },
  { value: 'last', label: t('settings.startLast') },
])
/** 文本大小与图像大小共用同一组选项 */
const SIZE_OPTIONS = computed(() => [
  { value: 'auto', label: t('settings.optAuto') },
  { value: 'small', label: t('settings.optSmall') },
  { value: 'normal', label: t('settings.optNormal') },
  { value: 'large', label: t('settings.optLarge') },
])
const SAVER_OPTIONS = computed(() => [
  { value: 'none', label: t('settings.optNone') },
  { value: 'backdrop', label: t('settings.saverBackdrop') },
  { value: 'logo', label: t('settings.saverLogo') },
])
const SOUND_OPTIONS = computed(() => [
  { value: 'none', label: t('settings.optNone') },
  { value: 'subtle', label: t('settings.optSubtle') },
  { value: 'standard', label: t('settings.optStandard') },
])
const EXIT_OPTIONS = computed(() => [
  { value: 'back', label: t('settings.exitBack') },
  { value: 'home', label: t('settings.exitHome') },
  { value: 'menu', label: t('settings.exitMenuShow') },
])

/** 应用界面语言：i18n 全局 locale + <html lang> + localStorage 的 serata.locale 三件事都由 setLocale 负责 */
function applyLanguage(v: unknown): void {
  if (v === 'zh-CN' || v === 'en') setLocale(v)
}

onMounted(() => {
  try {
    const saved = JSON.parse(localStorage.getItem(LS_KEY) ?? '{}')
    store.value = { ...store.value, ...saved }
  } catch { /* 损坏则忽略 */ }
  // 语言以 i18n 当前的 locale 为准（createI18n 初始化时已经读过 serata.locale），
  // 否则 generalSettings 里的历史值会让下拉框显示一个并没有生效的语言
  store.value.language = locale.value
})

watch(store, (v) => localStorage.setItem(LS_KEY, JSON.stringify(v)), { deep: true })

// 语言是**真功能**：选完立刻切界面文案，不用刷新
watch(() => store.value.language, applyLanguage)
</script>

<template>
  <main class="view flex flex-direction-column scrollFrameY flex-grow emby-scroller page scrollY overflowYScroll focuscontainer-x settingsView page-withFullDrawer page-withDockedDrawer">
    <div class="scrollSlider flex-grow flex-direction-column padded-left padded-left-page padded-right padded-top-page padded-bottom-page scrollSliderY">
      <form class="auto-center padded-top fieldsets" @submit.prevent>
        <fieldset>
          <legend>{{ t('settings.generalDisplay') }}</legend>
          <div class="fieldset-fields">
            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.language') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.language" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.language')">
                    <option v-for="o in LANG_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.timezone') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.timezone" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.timezone')">
                    <option v-for="o in TZ_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.startupBehavior') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.startupBehavior" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.startupBehavior')">
                    <option v-for="o in STARTUP_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
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
                <div class="selectLabelText">{{ t('settings.imageSize') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.imageSize" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.imageSize')">
                    <option v-for="o in SIZE_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.screensaver') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.screensaver" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.screensaver')">
                    <option v-for="o in SAVER_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.soundEffects') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.soundEffects" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.soundEffects')">
                    <option v-for="o in SOUND_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.slideshowDuration') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.slideshowDuration" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.slideshowDuration')">
                    <option value="5">5 {{ t('settings.seconds') }}</option>
                    <option value="8">8 {{ t('settings.seconds') }}</option>
                    <option value="12">12 {{ t('settings.seconds') }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>

            <div class="toggleContainer fieldset-field">
              <label class="emby-toggle-label">
                <input v-model="store.playThemeSongs" type="checkbox" class="autoSetting autoSave emby-toggle" role="switch" />
                <span>{{ t('settings.playThemeSongs') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
            </div>

            <div class="toggleContainer fieldset-field">
              <label class="emby-toggle-label">
                <input v-model="store.playThemeVideos" type="checkbox" class="autoSetting autoSave emby-toggle" role="switch" />
                <span>{{ t('settings.playThemeVideos') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
            </div>

            <div class="toggleContainer fieldset-field">
              <label class="emby-toggle-label">
                <input v-model="store.repeatThemeMedia" type="checkbox" class="autoSetting autoSave emby-toggle" role="switch" />
                <span>{{ t('settings.repeatThemeMedia') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
            </div>

            <div class="toggleContainer fieldset-field">
              <label class="emby-toggle-label">
                <input v-model="store.enableBackdrop" type="checkbox" class="autoSetting autoSave emby-toggle" role="switch" />
                <span>{{ t('settings.enableBackdrop') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
            </div>

            <div class="toggleContainer fieldset-field">
              <label class="emby-toggle-label">
                <input v-model="store.showRemoteControl" type="checkbox" class="autoSetting autoSave emby-toggle" role="switch" />
                <span>{{ t('settings.showRemoteControl') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
            </div>

            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.exitMenu') }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store.exitMenu" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.exitMenu')">
                    <option v-for="o in EXIT_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>
          </div>
        </fieldset>
      </form>
    </div>
  </main>
</template>