<script setup lang="ts">
/**
 * 管理后台 —— 列表类页面(设备/插件/API 密钥/元数据管理器)
 * 数据回路:GET /Devices、GET /Plugins、GET /Auth/Keys(+POST/DELETE 密钥)
 *
 * ⚠️ 缺陷与根因(2026-10-20 真机实测,task-7;证据见 scratch/lead2/datafix-admin/upstream-probe.json):
 *   /dashboard/apikeys 恒显示"共 0 个密钥"。真机 `GET /Auth/Keys` → **200** `{Items:[8 条], TotalRecordCount:0}`,
 *   首条字段清单 = Id, AccessToken, ReportedDeviceId, DeviceId, AppName, AppVersion, IpAddress,
 *   DeviceName, UserId, DateCreated, DateLastActivity, IsActive —— **没有 `Key` 字段,也没有 `Name` 字段**
 *   (`'Key' in item === false`、`'Name' in item === false`、`'AccessToken' in item === true`、`'AppName' in item === true`)。
 *   而这里执行 `k.Key.slice(0, 8)` → TypeError → 被外层那个空 catch(静默吞掉)→ `rows` 停在 `[]`。
 *   放大器就是那个静默 catch:失败不留痕,页面还理直气壮地说"共 0 个"。
 *   修法:① 读 `AccessToken`(列表展示与删除一起改,`DELETE /Auth/Keys/{Key}` 的 {Key} 就是 access token);
 *         ② 同一类缺陷的第二处:标题原来读 `k.Name`(真机同样不存在)→ 改读 `AppName`,否则 8 行全是空标题;
 *         ③ 加载失败必须显示出来,不许静默吞。
 *   附带上游怪癖:真机 `TotalRecordCount` 恒为 0(Items 却有 8 条)—— 行数一律按 `Items.length` 算。
 */
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { getDevices, getPlugins, getApiKeys, createApiKey, deleteApiKey, enablePlugin, disablePlugin } from '@/api/endpoints'
import type { AdminApiKey } from '@/api/endpoints'
import { showToast } from '@/api/toast'

const route = useRoute()
const { t } = useI18n()

const section = computed(() => String(route.meta.section ?? 'devices'))
const loading = ref(true)
/** 加载失败原因:非 null 就显示出来(不许再静默吞) */
const loadError = ref<string | null>(null)

/**
 * Id 在真机上是混合形态:/Devices、/Plugins 是数字字符串,而 /Auth/Keys 的 `Id` 是 **number**。
 * 这里只当列表 key 用,所以如实收成 string | number。
 */
interface Row { Id: string | number; Name: string; Secondary: string; Icon: string; AccessToken?: string; deletable?: boolean }
const rows = ref<Row[]>([])

/**
 * 真机 /Auth/Keys 条目上的密钥字段叫 `AccessToken`、应用名字段叫 `AppName`,
 * 而 endpoints.ts 的 `AdminApiKey` 声明成 `Key` / `Name`,mock(mocks/handlers.ts:478)也照那份声明造数据。
 * 这两个文件都不在本任务写入范围,所以这里做结构化窄化读取(与 endpoints.ts 里 SeriesPrimaryImageTag 同一手法):
 * **真机字段优先,声明/mock 字段兜底;两者都没有就抛** —— 绝不静默吞(那正是本缺陷恒显示 0 的放大器)。
 * 实测(2026-10-20,8/8 条):`'Key' in item === false`、`'Name' in item === false`、
 * `'AccessToken' in item === true`、`'AppName' in item === true`(AppName 实测值:mp / MC / jellystat / Seerr / bot / …)。
 */
type ApiKeyRow = AdminApiKey & { AppName?: string; AccessToken?: string }

/** 取第一个非空字符串候选;都不满足返回 null(不编值) */
function pickString(...candidates: unknown[]): string | null {
  for (const c of candidates) if (typeof c === 'string' && c.length > 0) return c
  return null
}

/** 密钥本身(列表展示与删除都用它:`DELETE /Auth/Keys/{Key}` 的 {Key} 就是 access token) */
function accessTokenOf(k: AdminApiKey): string {
  const row = k as ApiKeyRow
  const token = pickString(row.AccessToken, row.Key)
  if (!token) throw new Error(`GET /Auth/Keys item has no AccessToken field; fields=${Object.keys(k).join(',')}`)
  return token
}

/** 应用名(真机 AppName / 声明·mock Name);两者都没有返回空串,由页面留白,不编名字 */
function appNameOf(k: AdminApiKey): string {
  const row = k as ApiKeyRow
  return pickString(row.AppName, row.Name) ?? ''
}

const COUNT_KEY: Record<string, string> = {
  devices: 'deviceCount', plugins: 'pluginCount', apikeys: 'apiKeyCount', metadatamanager: 'metadataCount',
}
const countKey = computed(() => COUNT_KEY[section.value] ?? 'deviceCount')

function fmtDate(iso: string): string {
  const d = new Date(iso)
  return `${d.getFullYear()}/${d.getMonth() + 1}/${d.getDate()}`
}

async function load() {
  loading.value = true
  rows.value = []
  loadError.value = null
  try {
    if (section.value === 'devices') {
      const r = await getDevices()
      rows.value = r.Items.map((d) => ({ Id: d.Id, Name: d.Name, Secondary: `${d.LastUserName} · ${d.AppName} · ${d.IpAddress}`, Icon: 'desktop_windows' }))
    } else if (section.value === 'plugins') {
      const r = await getPlugins()
      rows.value = r.map((p) => ({ Id: p.Id, Name: p.Name, Secondary: `${p.Version} · ${p.Status === 'Active' ? t('admin.enabled') : t('admin.disabled')}`, Icon: 'extension', deletable: false }))
    } else if (section.value === 'apikeys') {
      const r = await getApiKeys()
      // 行数按 Items.length:真机 TotalRecordCount 恒为 0(实测 Items 8 条 / TotalRecordCount 0)。
      // 标题用 AppName(真机没有 Name)、副标题用 AccessToken 前缀 —— 都是真机真实字段。
      rows.value = r.Items.map((k) => ({
        Id: k.Id,
        Name: appNameOf(k),
        Secondary: `${t('admin.createdAt')} ${fmtDate(k.DateCreated)} · ${accessTokenOf(k).slice(0, 8)}…`,
        Icon: 'vpn_key',
        AccessToken: accessTokenOf(k),
        deletable: true,
      }))
    } else {
      // 元数据管理器(mock:真实站 /metadatamanager 为代理配置列表)
      rows.value = [
        { Id: 'm1', Name: 'TheMovieDb', Secondary: `电影 / 剧集 · ${t('admin.enabled')}`, Icon: 'movie' },
        { Id: 'm2', Name: 'TheTVDB', Secondary: `剧集 · ${t('admin.enabled')}`, Icon: 'tv' },
        { Id: 'm3', Name: 'Open Movie Database', Secondary: `评分 · ${t('admin.enabled')}`, Icon: 'star' },
      ]
    }
  } catch (e) {
    // 不许静默吞:把失败原因暴露到页面上,而不是留下一个"共 0 个"的假象。
    loadError.value = e instanceof Error ? e.message : String(e)
    console.error('[AdminListView] 加载失败:', e)
  } finally { loading.value = false }
}
onMounted(load)

async function addKey() {
  try {
    await createApiKey('Serata Web')
    await load()
    showToast(t('admin.keyCreated'))
  } catch { showToast(t('admin.actionFailed')) }
}

async function togglePlugin(r: Row) {
  try {
    const p = (await getPlugins()).find((x) => x.Id === r.Id)
    if (!p) return
    if (p.Status === 'Active') await disablePlugin(p.Id)
    else await enablePlugin(p.Id, p.Version)
    await load()
    showToast(p.Status === 'Active' ? t('admin.pluginDisabled', { name: p.Name }) : t('admin.pluginEnabled', { name: p.Name }))
  } catch { showToast(t('admin.actionFailed')) }
}

async function removeRow(r: Row) {
  // 删除用的是 access token 本身(DELETE /Auth/Keys/{Key} 的 {Key} 就是它),与列表展示同一个字段
  if (!r.deletable || !r.AccessToken) { showToast('操作 — v1 mock'); return }
  try {
    await deleteApiKey(r.AccessToken)
    await load()
    showToast(t('admin.keyDeleted'))
  } catch { showToast(t('admin.actionFailed')) }
}
</script>

<template>
  <main class="view flex flex-direction-column withTabs page page-withFullDrawer page-withDockedDrawer focuscontainer-x">
    <div class="scrollFrameY flex flex-grow tabContent tabContent-positioned emby-scroller scrollY overflowYScroll flex-direction-column is-active focuscontainer-y">
      <div class="scrollSlider flex-grow padded-top-page padded-left padded-left-page padded-right scrollSliderY focuscontainer-y">
        <div class="flex align-items-center focuscontainer-x itemsViewSettingsContainer padded-bottom">
          <div class="listTotalRecordCount">{{ t(`admin.${countKey}`, { count: rows.length }) }}</div>
          <button v-if="section === 'apikeys'" type="button" class="itemsViewSettingsContainer-button raised raised-mini submit emby-button emby-button-focusscale" @click="addKey">
            <i class="md-icon button-icon button-icon-left">&#xE03B;</i>
            <span class="emby-button-text">{{ t('admin.newApiKey') }}</span>
          </button>
          <button v-else-if="section === 'plugins'" type="button" class="itemsViewSettingsContainer-button raised raised-mini submit emby-button emby-button-focusscale" @click="showToast(t('admin.pluginCatalog') + ' — v1 mock')">
            <i class="md-icon button-icon button-icon-left">&#xE8B6;</i>
            <span class="emby-button-text">{{ t('admin.pluginCatalog') }}</span>
          </button>
        </div>
        <div v-if="loading" class="page-loading">{{ t('common.loading') }}</div>
        <!-- 加载失败必须可见(不许静默吞):否则页面会显示"共 0 个"这种假象 -->
        <fieldset v-else-if="loadError">
          <legend>{{ t('common.error') }}</legend>
          <div class="fieldDescription">{{ loadError }}</div>
          <button type="button" class="raised button-submit emby-button emby-button-focusscale" @click="load">{{ t('common.retry') }}</button>
        </fieldset>
        <div v-else class="itemsContainer vertical-list padded-bottom-page focusable focuscontainer-x">
          <div v-for="r in rows" :key="r.Id" class="listItem listItem-autoactive listItem-hoverable">
            <div class="listItem-content listItem-content-bg listItemContent-touchzoom listItem-border listItem-border-offset listItem-border-offset-square">
              <div class="listItemImageContainer itemAction listItemImageContainer-square listItemImageContainer-round">
                <i class="listItemIcon md-icon autortl">{{ r.Icon }}</i>
              </div>
              <div class="listItemBody itemAction listItemBody-2-lines">
                <div class="listItemBodyText listItemBodyText-lf">{{ r.Name }}</div>
                <div class="listItemBodyText listItemBodyText-lf listItemBodyText-secondary secondaryText">{{ r.Secondary }}</div>
              </div>
              <button
                v-if="section === 'plugins'"
                type="button" :title="r.Secondary.includes(t('admin.enabled')) ? '停用' : '启用'"
                class="listItemButton itemAction md-icon paper-icon-button-light emby-button-focusscale"
                @click.stop="togglePlugin(r)"
              >
                <i class="md-icon">&#xE5CA;</i>
              </button>
              <button
                v-else
                type="button" :title="r.deletable ? '删除' : '更多'"
                class="listItemButton itemAction md-icon paper-icon-button-light emby-button-focusscale"
                @click.stop="removeRow(r)"
              >
                <i class="md-icon">{{ r.deletable ? '\uE872' : '\uE5D3' }}</i>
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </main>
</template>