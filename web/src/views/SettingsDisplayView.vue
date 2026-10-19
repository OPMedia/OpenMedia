<script setup lang="ts">
/**
 * 用户设置页(显示)—— 只保留**真正生效**的项(这个页面仍走老 CSS,不重写版式)。
 *
 * 外观（深色 / 浅色 / 跟随系统）由 useTheme() 统一管理：
 *   v-model 直接绑 mode，@change 再调 setMode() 把选择写进 localStorage；
 *   <html> 的 theme-* 类与老页面浅色 CSS 都由 initTheme/useTheme 负责，本页不再自己切主题。
 *
 * 已删除（写进 localStorage 之后**没有任何代码读回来**，等于骗人的开关）：
 *   播放音频时的背景样式 / 首选电视节目显示 / 自动打开文件夹视图中的单个项目 /
 *   隐藏未观看剧集的剧透信息 / 「在以下页面中隐藏已播放的媒体」三项。
 * 已删除的强调色选择：固定为品牌珊瑚，由 tokens.css 按深浅模式自动取对应档位。
 */
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useTheme } from '@/composables/useTheme'

const { t } = useI18n()
const { mode, setMode } = useTheme()

/** 外观三选（文案随 locale 变化，所以用 computed 而不是常量） */
const APPEARANCE_OPTIONS = computed(() => [
  { value: 'dark', label: t('settings.themeDark') },
  { value: 'light', label: t('settings.themeLight') },
  { value: 'system', label: t('settings.themeSystem') },
])
</script>

<template>
  <main class="view flex flex-direction-column scrollFrameY flex-grow emby-scroller page scrollY overflowYScroll focuscontainer-x settingsView view-settings-display page-withFullDrawer page-withDockedDrawer">
    <div class="scrollSlider flex-grow flex-direction-column padded-left padded-left-page padded-right padded-top-page padded-bottom-page scrollSliderY">
      <form class="auto-center padded-top fieldsets" @submit.prevent>
        <fieldset>
          <legend class="hide">{{ t('app.settings') }}</legend>
          <div class="fieldset-fields">
            <!-- 外观：深色 / 浅色 / 跟随系统 -->
            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.themeMode') }}</div>
                <div class="emby-select-wrapper">
                  <select
                    v-model="mode"
                    class="selectAppearanceMode autoSetting autoSave emby-select-focusscale emby-select"
                    :label="t('settings.themeMode')"
                    @change="setMode(mode)"
                  >
                    <option v-for="o in APPEARANCE_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
              <div class="fieldDescription">{{ t('settings.themeDesc') }}</div>
            </div>

            <!-- 强调色：固定为品牌色（不再提供 6 套 Emby 主题色） -->
            <div class="fieldset-field">
              <div class="fieldDescription">{{ t('settings.accentFixed') }}</div>
            </div>
          </div>
        </fieldset>
      </form>
    </div>
  </main>
</template>
