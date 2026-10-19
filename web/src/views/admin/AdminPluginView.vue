<script setup lang="ts">
/**
 * 管理后台 —— 插件设置页(高级组:神医助手/Open Subtitles/Subtitle Options/User Sessions/
 * Backup & Restore/Diagnostic Options)
 * 真实站:第三方插件各自渲染配置表单(plugin configuration page)。
 * v1:统一插件页骨架(标题 + 说明 + 保存/取消),字段由路由 meta 提供;保存走 localStorage。
 */
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { showToast } from '@/api/toast'

const route = useRoute()
const { t } = useI18n()

const pluginName = computed(() => String(route.meta.pluginName ?? '插件'))
const LS_KEY = computed(() => `serata.plugin.${String(route.name ?? 'plugin')}`)

interface Field { key: string; label: string; kind: 'toggle' | 'text' | 'select'; options?: string[] }
const fields = ref<Field[]>([
  { key: 'enabled', label: '启用插件', kind: 'toggle' },
  { key: 'endpoint', label: '服务地址', kind: 'text' },
  { key: 'interval', label: '同步间隔', kind: 'select', options: ['每小时', '每 6 小时', '每天', '每周'] },
])
const values = ref<Record<string, string | boolean>>({ enabled: true, endpoint: '', interval: '每天' })

onMounted(() => {
  try {
    const saved = JSON.parse(localStorage.getItem(LS_KEY.value) ?? '{}')
    values.value = { ...values.value, ...saved }
  } catch { /* 忽略 */ }
})

function save() {
  localStorage.setItem(LS_KEY.value, JSON.stringify(values.value))
  showToast(t('admin.saved'))
}
</script>

<template>
  <main class="view flex flex-direction-column scrollFrameY flex-grow emby-scroller page page-withFullDrawer page-withDockedDrawer scrollY focuscontainer-x">
    <div class="scrollSlider flex-grow padded-top-page padded-left padded-left-page padded-right scrollSliderY focuscontainer-y">
      <form class="auto-center padded-top fieldsets" @submit.prevent>
        <fieldset>
          <legend>{{ pluginName }}</legend>
          <div class="fieldset-fields">
            <div v-for="f in fields" :key="f.key">
              <div v-if="f.kind === 'toggle'" class="toggleContainer fieldset-field">
                <label class="emby-toggle-label">
                  <input v-model="values[f.key]" type="checkbox" class="emby-toggle" role="switch" />
                  <span>{{ f.label }}</span>
                  <span class="toggleLabel flex-grow"></span>
                  <div class="toggleSwitch"></div>
                </label>
              </div>
              <div v-else-if="f.kind === 'select'" class="selectContainer fieldset-field">
                <label class="selectLabel">
                  <div class="selectLabelText">{{ f.label }}</div>
                  <div class="emby-select-wrapper">
                    <select v-model="values[f.key]" class="emby-select emby-select-focusscale">
                      <option v-for="o in f.options" :key="o" :value="o">{{ o }}</option>
                    </select>
                    <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                  </div>
                </label>
              </div>
              <div v-else class="inputContainer fieldset-field">
                <label class="inputLabel inputLabelUnfocused">{{ f.label }}</label>
                <input v-model="values[f.key]" type="text" class="emby-input" />
              </div>
            </div>
          </div>
        </fieldset>
        <div class="formDialogFooter formDialogFooter-clear">
          <button type="button" class="raised button-submit formDialogFooterItem emby-button emby-button-focusscale" @click="save">{{ t('admin.save') }}</button>
        </div>
      </form>
    </div>
  </main>
</template>