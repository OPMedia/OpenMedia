<script setup lang="ts">
/**
 * 管理后台 —— 服务器设置类页面(通用/转码/网络/数据库/转换/电视直播/Premiere)
 * 数据回路:GET /System/Configuration/Fields(真实站字段清单)+ GET/POST /System/Configuration(保存落库)
 */
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { getServerConfig, getServerConfigFields, saveServerConfig } from '@/api/endpoints'
import type { AdminConfigGroup } from '@/api/endpoints'
import { showToast } from '@/api/toast'

const route = useRoute()
const { t } = useI18n()

const section = computed(() => String(route.meta.section ?? 'general'))
const loading = ref(true)
const groups = ref<AdminConfigGroup[]>([])
const values = ref<Record<string, string | number | boolean>>({})
const dirty = ref<Record<string, string | number | boolean>>({})

const pageGroups = computed(() => groups.value.filter((g) => g.section === section.value))

function valueOf(key: string): string | number | boolean {
  return dirty.value[key] ?? values.value[key] ?? ''
}
function setValue(key: string, v: string | number | boolean) {
  dirty.value = { ...dirty.value, [key]: v }
}
async function load() {
  loading.value = true
  try {
    const [cfg, flds] = await Promise.all([getServerConfig(), getServerConfigFields()])
    values.value = cfg
    groups.value = flds
    dirty.value = {}
  } catch { /* 静默 */ } finally { loading.value = false }
}
onMounted(load)

async function save() {
  try {
    await saveServerConfig(dirty.value)
    values.value = { ...values.value, ...dirty.value }
    dirty.value = {}
    showToast(t('admin.saved'))
  } catch { showToast(t('admin.actionFailed')) }
}
</script>

<template>
  <main class="view flex flex-direction-column withTabs page page-withFullDrawer page-withDockedDrawer focuscontainer-x">
    <div class="scrollFrameY flex flex-grow tabContent tabContent-positioned emby-scroller scrollY overflowYScroll flex-direction-column is-active focuscontainer-y">
      <div class="scrollSlider flex-grow padded-top-page padded-left padded-left-page padded-right scrollSliderY focuscontainer-y">
        <div v-if="loading" class="page-loading">{{ t('common.loading') }}</div>
        <form v-else class="auto-center padded-top fieldsets" @submit.prevent>
          <fieldset v-for="g in pageGroups" :key="g.legend">
            <legend>{{ g.legend }}</legend>
            <div class="fieldset-fields">
              <template v-for="f in g.fields" :key="f.key">
                <div v-if="f.kind === 'select'" class="selectContainer fieldset-field">
                  <label class="selectLabel">
                    <div class="selectLabelText">{{ f.label }}</div>
                    <div class="emby-select-wrapper">
                      <select :value="String(valueOf(f.key))" class="emby-select emby-select-focusscale" @change="setValue(f.key, ($event.target as HTMLSelectElement).value)">
                        <option v-for="o in f.options" :key="o" :value="o">{{ o }}</option>
                      </select>
                      <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                    </div>
                  </label>
                  <div v-if="f.desc" class="fieldDescription">{{ f.desc }}</div>
                </div>

                <div v-else-if="f.kind === 'toggle'" class="toggleContainer fieldset-field">
                  <label class="emby-toggle-label">
                    <input :checked="!!valueOf(f.key)" type="checkbox" class="emby-toggle" role="switch" @change="setValue(f.key, ($event.target as HTMLInputElement).checked)" />
                    <span>{{ f.label }}</span>
                    <span class="toggleLabel flex-grow"></span>
                    <div class="toggleSwitch"></div>
                  </label>
                  <div v-if="f.desc" class="fieldDescription toggleFieldDescription">{{ f.desc }}</div>
                </div>

                <div v-else class="inputContainer fieldset-field">
                  <label class="inputLabel inputLabelUnfocused">{{ f.label }}</label>
                  <input :value="String(valueOf(f.key))" :type="f.kind === 'number' ? 'number' : 'text'" class="emby-input" @input="setValue(f.key, ($event.target as HTMLInputElement).value)" />
                  <div v-if="f.desc" class="fieldDescription">{{ f.desc }}</div>
                </div>
              </template>
            </div>
          </fieldset>

          <div class="formDialogFooter formDialogFooter-clear">
            <button type="button" class="raised button-submit formDialogFooterItem btnSaveServerConfig emby-button emby-button-focusscale" @click="save">{{ t('admin.save') }}</button>
          </div>
        </form>
      </div>
    </div>
  </main>
</template>