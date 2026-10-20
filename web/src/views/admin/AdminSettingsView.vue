<script setup lang="ts">
/**
 * 管理后台 —— 服务器设置类页面(通用/网络/数据库/转换)
 *
 * 数据回路是两条(一条给"值",一条给"字段定义"):
 *   GET /System/Configuration        → 真机 200,返回 61 个真实配置键
 *   GET /System/Configuration/Fields → 真机恒 500 "Sequence contains no matching element"
 *
 * ⚠️ 缺陷与根因(2026-10-20 真机实测,task-7;证据见 scratch/lead2/datafix-admin/upstream-probe.json):
 *   原来这里是 `await Promise.all([getServerConfig(), getServerConfigFields()])` —— **一条失败就整组丢弃**,
 *   明明 200 的 /System/Configuration 也被一起扔掉,于是这些设置页
 *   (/dashboard/{general,network,database,convert}) 实测 verdict=empty、fieldCount=0,
 *   页面上一个 fieldset 都渲染不出来 —— 不是样式问题,是功能性不可用。
 *   放大器是同一个:原来那个空 catch(静默吞掉)让失败连个痕迹都不留。
 *   修法:改用 Promise.allSettled,两条回路各自成败、不连坐;失败原因显示到页面上,不静默吞。
 *
 * ⚠️ 降级口径(ADR-0013「设置页不装」—— 不装,也不伪造):
 *   字段定义接口拿不到时,**不编造** label / 分组 / 下拉选项(上游 500 是服务器侧的事,前端不绕、不假)。
 *   页面上给一条诚实的提示(哪个接口、什么状态码、值接口真实拿到了多少条),
 *   并把 /System/Configuration 真实返回的键值直接当字段渲染:
 *     · 标量(string / number / boolean) → 可编辑控件,kind 由真实值类型推导,label 就是真机字段名
 *     · 数组 / 对象 → 只读行,绝不回写(save() 只提交标量;把数组 POST 成字符串会污染服务器配置)
 *   所以降级模式下这些页面看到的是同一份"未分组"真实配置项 —— 这是字段定义缺失的必然结果,不是伪造的分组。
 */
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { getServerConfig, getServerConfigFields, saveServerConfig } from '@/api/endpoints'
import type { AdminConfigField } from '@/api/endpoints'
import { ApiError } from '@/api/client'
import { showToast } from '@/api/toast'

/**
 * 本视图自用的字段视图模型:在真机字段定义(AdminConfigField)上补两个展示位。
 * 不改 endpoints.ts 的共享类型(不在本任务写入范围),也不引入 any。
 */
interface SettingsField extends AdminConfigField {
  /** true = 只读行(数组/对象值):不绑输入、不回写 */
  readOnly?: boolean
  /** 只读行展示的真实值(JSON 摘要) */
  raw?: string
}
interface SettingsGroup {
  section: string
  legend: string
  fields: SettingsField[]
}

const route = useRoute()
const { t } = useI18n()

const section = computed(() => String(route.meta.section ?? 'general'))
const loading = ref(true)
const groups = ref<SettingsGroup[]>([])
/**
 * 真机 /System/Configuration 的原始返回。
 * endpoints.ts 的 getServerConfig() 声明成 Record<string, string | number | boolean>,
 * 但真机返回里含 6 个数组值(SortRemoveWords / LocalNetworkSubnets / LocalNetworkAddresses /
 * RemoteIPFilter / PathSubstitutions / UninstalledPlugins)—— 声明低估了真实形态;
 * endpoints.ts 不在本任务写入范围,所以这里按真机真实形态收窄,不猜、不改别人的类型。
 */
const rawConfig = ref<Record<string, unknown>>({})
const dirty = ref<Record<string, string | number | boolean>>({})

/** 字段定义接口失败(真机上游 500):非 null 即降级模式 */
const fieldsError = ref<{ path: string; status: number | null } | null>(null)
/** 配置值接口失败:非 null 时一条真数据都没有,整页只留诚实提示 */
const valuesError = ref<{ path: string; status: number | null } | null>(null)

/** 本视图在用的 section 真实页名(与 App.vue 侧栏同一批 i18n 键),降级模式的 legend 复用它,不另造文案 */
const SECTION_LABEL: Record<string, string> = {
  general: 'dash.serverGeneral',
  network: 'dash.serverNetwork',
  database: 'dash.serverDatabase',
  convert: 'dash.serverSync',
}
const sectionLabel = computed(() => t(SECTION_LABEL[section.value] ?? 'dash.serverGeneral'))

/**
 * 本页要渲染的分组。字段定义可用时按 section 过滤;
 * 降级模式下只有一组未分组的真实配置项,不做 section 过滤。
 */
const pageGroups = computed(() =>
  fieldsError.value ? groups.value : groups.value.filter((g) => g.section === section.value),
)

/** 真机配置键(降级提示里的条数取真实值,不写死数字) */
const rawConfigKeys = computed(() => Object.keys(rawConfig.value))
/** 降级提示的第二条事实:值接口的真实状态与真实条数 */
const valuesSummary = computed(() => `/System/Configuration → HTTP 200 · ${rawConfigKeys.value.length} keys`)

function valueOf(key: string): string | number | boolean {
  const d = dirty.value[key]
  if (d !== undefined) return d
  const v = rawConfig.value[key]
  return typeof v === 'string' || typeof v === 'number' || typeof v === 'boolean' ? v : ''
}
function setValue(key: string, v: string | number | boolean) {
  dirty.value = { ...dirty.value, [key]: v }
}

/** 把 request() 抛出的 ApiError 取成可展示的状态码;非 ApiError 不猜,原样进 console */
function statusOf(reason: unknown): number | null {
  if (reason instanceof ApiError) return reason.status
  console.error('[AdminSettingsView] 非 HTTP 异常:', reason)
  return null
}

/**
 * 字段定义缺失时的降级字段清单:数据全部来自真机 200 的 /System/Configuration。
 * label 就是真实配置键名(不翻译、不美化),kind 由真实值类型推导 —— 没有一个字段是编造的。
 * 数组/对象值渲染成只读行:save() 只提交标量,避免把数组写成字符串污染服务器配置。
 */
function buildDegradedGroups(cfg: Record<string, unknown>): SettingsGroup[] {
  const fields = Object.keys(cfg)
    .sort((a, b) => a.localeCompare(b))
    .map((key): SettingsField => {
      const v = cfg[key]
      if (typeof v === 'boolean') return { key, label: key, kind: 'toggle' }
      if (typeof v === 'number') return { key, label: key, kind: 'number' }
      if (typeof v === 'string') return { key, label: key, kind: 'text' }
      return { key, label: key, kind: 'text', readOnly: true, raw: JSON.stringify(v), desc: 'read-only' }
    })
  return [{ section: section.value, legend: sectionLabel.value, fields }]
}

async function load() {
  loading.value = true
  fieldsError.value = null
  valuesError.value = null
  dirty.value = {}
  // 两条回路各自成败:字段定义(上游 500)失败,不能让 200 的配置值一起被扔掉。
  const [cfgR, fldsR] = await Promise.allSettled([getServerConfig(), getServerConfigFields()])

  if (cfgR.status === 'fulfilled') {
    // 真机返回含数组值,声明类型低估了真实形态 → 按真实形态收窄
    rawConfig.value = cfgR.value as Record<string, unknown>
  } else {
    // 配置值是唯一的真数据来源,它失败就没有任何真数据可渲染 —— 明说,不装。
    valuesError.value = { path: '/System/Configuration', status: statusOf(cfgR.reason) }
    rawConfig.value = {}
  }

  if (fldsR.status === 'fulfilled') {
    groups.value = fldsR.value
  } else {
    fieldsError.value = { path: '/System/Configuration/Fields', status: statusOf(fldsR.reason) }
    groups.value = cfgR.status === 'fulfilled' ? buildDegradedGroups(rawConfig.value) : []
  }

  loading.value = false
}
onMounted(load)

async function save() {
  try {
    await saveServerConfig(dirty.value)
    rawConfig.value = { ...rawConfig.value, ...dirty.value }
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
          <!--
            诚实提示(ADR-0013):哪个接口、什么状态码,明写;失败不静默吞。
            配置值接口也失败时一条真数据都没有,整页只留提示 —— 不装。
          -->
          <fieldset v-if="valuesError">
            <legend>{{ t('common.error') }}</legend>
            <div class="fieldDescription">{{ valuesError.path }} → HTTP {{ valuesError.status ?? '?' }}</div>
            <button type="button" class="raised button-submit emby-button emby-button-focusscale" @click="load">{{ t('common.retry') }}</button>
          </fieldset>

          <template v-else>
            <!-- 字段定义接口不可用(真机上游 500):提示 + 按真实配置项降级渲染,不伪造字段 -->
            <fieldset v-if="fieldsError">
              <legend>{{ t('common.error') }}</legend>
              <div class="fieldDescription">{{ fieldsError.path }} → HTTP {{ fieldsError.status ?? '?' }}</div>
              <div class="fieldDescription">{{ valuesSummary }}</div>
              <button type="button" class="raised button-submit emby-button emby-button-focusscale" @click="load">{{ t('common.retry') }}</button>
            </fieldset>

            <fieldset v-for="g in pageGroups" :key="g.legend">
              <legend>{{ g.legend }}</legend>
              <div class="fieldset-fields">
                <template v-for="f in g.fields" :key="f.key">
                  <!-- 降级模式下的数组/对象值:只读展示,绝不回写 -->
                  <div v-if="f.readOnly" class="inputContainer fieldset-field">
                    <label class="inputLabel inputLabelUnfocused">{{ f.label }}</label>
                    <input :value="f.raw ?? ''" type="text" class="emby-input" readonly disabled />
                    <div v-if="f.desc" class="fieldDescription">{{ f.desc }}</div>
                  </div>

                  <div v-else-if="f.kind === 'select'" class="selectContainer fieldset-field">
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
          </template>
        </form>
      </div>
    </div>
  </main>
</template>