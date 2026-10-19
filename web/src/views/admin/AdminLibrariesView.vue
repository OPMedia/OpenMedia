<script setup lang="ts">
/**
 * 管理后台 —— 媒体库(/librarysetup;真实站 dump:
 * .itemsViewSettingsContainer(共 N 个媒体库 + 新增媒体库 + 刷新/扫描)+ 媒体库 listItem)
 */
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { getUserViews, refreshLibrary, createLibrary } from '@/api/endpoints'
import type { BaseItemDto } from '@/api/types'
import { showToast } from '@/api/toast'

const { t } = useI18n()
const auth = useAuthStore()

const loading = ref(true)
const libs = ref<BaseItemDto[]>([])
const addOpen = ref(false)
const newLibName = ref('')
const newLibType = ref('movies')

const TYPE_LABEL: Record<string, string> = { movies: '电影', tvshows: '电视节目', music: '音乐', books: '图书', photos: '照片' }

async function addLibrary() {
  const name = newLibName.value.trim()
  if (!name) return
  try {
    await createLibrary({ Name: name, CollectionType: newLibType.value })
    addOpen.value = false
    newLibName.value = ''
    libs.value = (await getUserViews(auth.user!.Id)).Items
    showToast(t('admin.libraryAdded', { name }))
  } catch { showToast(t('admin.actionFailed')) }
}

async function scan() {
  try { await refreshLibrary(); showToast(t('admin.scanStarted')) } catch { showToast(t('admin.actionFailed')) }
}

onMounted(async () => {
  try { libs.value = (await getUserViews(auth.user!.Id)).Items } catch { /* 静默 */ } finally { loading.value = false }
})
</script>

<template>
  <main class="view flex flex-direction-column withTabs page page-withFullDrawer page-withDockedDrawer focuscontainer-x">
    <div class="scrollFrameY flex flex-grow tabContent tabContent-positioned emby-scroller scrollY overflowYScroll flex-direction-column is-active focuscontainer-y">
      <div class="scrollSlider flex-grow padded-top-page padded-left padded-left-page padded-right scrollSliderY focuscontainer-y">
        <div class="flex align-items-center focuscontainer-x itemsViewSettingsContainer padded-bottom focusable">
          <div class="listTotalRecordCount">{{ t('admin.libCount', { count: libs.length }) }}</div>
          <button type="button" class="itemsViewSettingsContainer-button raised raised-mini btnNewLibrary submit emby-button emby-button-focusscale" title="新增媒体库" @click="addOpen = true">
            <i class="md-icon button-icon button-icon-left">&#xE03B;</i>
            <span class="emby-button-text">{{ t('admin.addLibrary') }}</span>
          </button>
          <button type="button" class="itemsViewSettingsContainer-button raised raised-mini btnRefresh submit emby-button emby-button-focusscale" title="扫描媒体库文件" @click="scan">
            <i class="md-icon button-icon button-icon-left">refresh</i>
            <span class="emby-button-text">{{ t('admin.scanLibrary') }}</span>
          </button>
        </div>

        <div v-if="loading" class="page-loading">{{ t('common.loading') }}</div>
        <div v-else class="itemsContainer vertical-list padded-bottom-page focusable focuscontainer-x">
          <div v-for="l in libs" :key="l.Id" class="listItem listItem-autoactive listItem-hoverable">
            <div class="listItem-content listItem-content-bg listItemContent-touchzoom listItem-border listItem-border-offset listItem-border-offset-square">
              <div class="listItemImageContainer itemAction listItemImageContainer-square listItemImageContainer-round">
                <i class="listItemIcon md-icon autortl">{{ l.CollectionType === 'tvshows' ? '\uE333' : '\uE02C' }}</i>
              </div>
              <div class="listItemBody itemAction listItemBody-2-lines">
                <div class="listItemBodyText listItemBodyText-lf">{{ l.Name }}</div>
                <div class="listItemBodyText listItemBodyText-lf listItemBodyText-secondary secondaryText">{{ TYPE_LABEL[l.CollectionType ?? ''] ?? l.CollectionType }}</div>
              </div>
              <button type="button" title="更多" class="listItemButton itemAction md-icon paper-icon-button-light emby-button-focusscale" @click.stop="showToast('媒体库设置 — v1 mock')">
                <i class="md-icon">&#xE5D3;</i>
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
    <Teleport to="body">
      <div v-if="addOpen" class="dialogScrim" @click.self="addOpen = false">
        <div class="dialog dialog-animated formDialog opened" @click.stop>
          <div class="formDialogHeader">
            <button type="button" class="dialogCloseButton hide-mouse-idle-tv dialogHeaderButton paper-icon-button-light emby-button-focusscale" title="关闭" @click="addOpen = false">
              <i class="md-icon">&#xE5CD;</i>
            </button>
            <h3 class="formDialogHeaderTitle">{{ t('admin.addLibrary') }}</h3>
          </div>
          <div class="formDialogContent">
            <div class="inputContainer">
              <label class="inputLabel inputLabelUnfocused" for="newLibName">名称</label>
              <input id="newLibName" v-model="newLibName" type="text" class="emby-input" />
            </div>
            <div class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">内容类型</div>
                <div class="emby-select-wrapper">
                  <select v-model="newLibType" class="emby-select emby-select-focusscale">
                    <option value="movies">电影</option>
                    <option value="tvshows">电视节目</option>
                    <option value="music">音乐</option>
                    <option value="books">图书</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>
          </div>
          <div class="formDialogFooter">
            <button type="button" class="raised button-cancel formDialogFooterItem emby-button emby-button-focusscale" @click="addOpen = false">{{ t('ButtonCancel') }}</button>
            <button type="button" class="raised button-submit formDialogFooterItem btnCreateLibrary emby-button emby-button-focusscale" @click="addLibrary">{{ t('admin.addLibrary') }}</button>
          </div>
        </div>
      </div>
    </Teleport>
  </main>
</template>
<style scoped>
.dialogScrim { position: fixed; inset: 0; z-index: 1000; background: rgba(0, 0, 0, .5); display: flex; align-items: center; justify-content: center; }
.dialogScrim .dialog.formDialog { background: rgb(40, 40, 43); color: rgba(255, 255, 255, .87); border-radius: .9em; min-width: 24em; }
</style>