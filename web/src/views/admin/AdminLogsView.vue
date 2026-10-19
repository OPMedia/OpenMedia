<script setup lang="ts">
/**
 * 管理后台 —— 日志(/logs)
 * 数据回路:GET /System/Logs + DELETE /System/Logs/{name}(删除后列表真实减少)
 */
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { getLogFiles, deleteLogFile, downloadLogFile } from '@/api/endpoints'
import type { AdminLogFile } from '@/api/endpoints'
import { showToast } from '@/api/toast'

const { t } = useI18n()

const loading = ref(true)
const files = ref<AdminLogFile[]>([])

function fmtSize(bytes: number): string {
  if (bytes >= 1024 ** 2) return `${(bytes / 1024 ** 2).toFixed(1)} MB`
  if (bytes >= 1024) return `${Math.round(bytes / 1024)} KB`
  return `${bytes} B`
}
function fmtDate(iso: string): string {
  const d = new Date(iso)
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${p(d.getMonth() + 1)}/${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`
}

async function load() {
  loading.value = true
  try { files.value = await getLogFiles() } catch { files.value = [] } finally { loading.value = false }
}
onMounted(load)

/** 下载:真实站 GET /System/Logs/{name}/File → 前端 blob 下载 */
async function download(f: AdminLogFile) {
  try {
    const text = await downloadLogFile(f.Name)
    const blob = new Blob([text], { type: 'text/plain;charset=utf-8' })
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = f.Name
    a.click()
    URL.revokeObjectURL(a.href)
    showToast(t('admin.logDownloaded', { name: f.Name }))
  } catch { showToast(t('admin.actionFailed')) }
}

async function remove(f: AdminLogFile) {
  try {
    await deleteLogFile(f.Name)
    await load()
    showToast(t('admin.logDeleted', { name: f.Name }))
  } catch { showToast(t('admin.actionFailed')) }
}
</script>

<template>
  <main class="view flex flex-direction-column withTabs page page-withFullDrawer page-withDockedDrawer focuscontainer-x">
    <div class="scrollFrameY flex flex-grow tabContent tabContent-positioned emby-scroller scrollY overflowYScroll flex-direction-column is-active focuscontainer-y">
      <div class="scrollSlider flex-grow padded-top-page padded-left padded-left-page padded-right scrollSliderY focuscontainer-y">
        <div class="flex align-items-center focuscontainer-x itemsViewSettingsContainer padded-bottom">
          <div class="listTotalRecordCount">{{ t('admin.logCount', { count: files.length }) }}</div>
        </div>
        <div v-if="loading" class="page-loading">{{ t('common.loading') }}</div>
        <div v-else class="itemsContainer vertical-list padded-bottom-page focusable focuscontainer-x">
          <div v-for="f in files" :key="f.Name" class="listItem listItem-autoactive listItem-hoverable">
            <div class="listItem-content listItem-content-bg listItemContent-touchzoom listItem-border listItem-border-offset listItem-border-offset-square">
              <div class="listItemImageContainer itemAction listItemImageContainer-square listItemImageContainer-round">
                <i class="listItemIcon md-icon autortl">description</i>
              </div>
              <div class="listItemBody itemAction listItemBody-2-lines">
                <div class="listItemBodyText listItemBodyText-lf">{{ f.Name }}</div>
                <div class="listItemBodyText listItemBodyText-lf listItemBodyText-secondary secondaryText">{{ fmtDate(f.DateCreated) }} · {{ fmtSize(f.Size) }}</div>
              </div>
              <button type="button" title="下载" class="listItemButton itemAction md-icon paper-icon-button-light emby-button-focusscale" @click.stop="download(f)">
                <i class="md-icon">&#xE2C4;</i>
              </button>
              <button type="button" title="删除" class="listItemButton itemAction md-icon paper-icon-button-light emby-button-focusscale" @click.stop="remove(f)">
                <i class="md-icon">&#xE872;</i>
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </main>
</template>