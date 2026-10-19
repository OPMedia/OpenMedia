<script setup lang="ts">
/**
 * 管理后台 —— 列表类页面(设备/插件/API 密钥/元数据管理器)
 * 数据回路:GET /Devices、GET /Plugins、GET /Auth/Keys(+POST/DELETE 密钥)
 */
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { getDevices, getPlugins, getApiKeys, createApiKey, deleteApiKey, enablePlugin, disablePlugin } from '@/api/endpoints'
import { showToast } from '@/api/toast'

const route = useRoute()
const { t } = useI18n()

const section = computed(() => String(route.meta.section ?? 'devices'))
const loading = ref(true)

interface Row { Id: string; Name: string; Secondary: string; Icon: string; Key?: string; deletable?: boolean }
const rows = ref<Row[]>([])

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
  try {
    if (section.value === 'devices') {
      const r = await getDevices()
      rows.value = r.Items.map((d) => ({ Id: d.Id, Name: d.Name, Secondary: `${d.LastUserName} · ${d.AppName} · ${d.IpAddress}`, Icon: 'desktop_windows' }))
    } else if (section.value === 'plugins') {
      const r = await getPlugins()
      rows.value = r.map((p) => ({ Id: p.Id, Name: p.Name, Secondary: `${p.Version} · ${p.Status === 'Active' ? t('admin.enabled') : t('admin.disabled')}`, Icon: 'extension', deletable: false }))
    } else if (section.value === 'apikeys') {
      const r = await getApiKeys()
      rows.value = r.Items.map((k) => ({ Id: k.Id, Name: k.Name, Secondary: `${t('admin.createdAt')} ${fmtDate(k.DateCreated)} · ${k.Key.slice(0, 8)}…`, Icon: 'vpn_key', Key: k.Key, deletable: true }))
    } else {
      // 元数据管理器(mock:真实站 /metadatamanager 为代理配置列表)
      rows.value = [
        { Id: 'm1', Name: 'TheMovieDb', Secondary: `电影 / 剧集 · ${t('admin.enabled')}`, Icon: 'movie' },
        { Id: 'm2', Name: 'TheTVDB', Secondary: `剧集 · ${t('admin.enabled')}`, Icon: 'tv' },
        { Id: 'm3', Name: 'Open Movie Database', Secondary: `评分 · ${t('admin.enabled')}`, Icon: 'star' },
      ]
    }
  } catch { /* 静默 */ } finally { loading.value = false }
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
  if (!r.deletable || !r.Key) { showToast('操作 — v1 mock'); return }
  try {
    await deleteApiKey(r.Key)
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