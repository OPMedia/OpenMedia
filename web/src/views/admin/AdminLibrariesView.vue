<script setup lang="ts">
/**
 * 管理后台 —— 媒体库(/librarysetup;真实站 dump:
 * .itemsViewSettingsContainer(共 N 个媒体库 + 新增媒体库 + 刷新/扫描)+ 媒体库 listItem)
 */
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { getUserViews, refreshLibrary, createLibrary } from '@/api/endpoints'
import type { BaseItemDto } from '@/api/types'
import { showToast } from '@/api/toast'
import SSheet from '@/components/ui/SSheet.vue'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { flipConfig, useReducedMotionSafe } from '@/lib/motion'

const { t } = useI18n()
const auth = useAuthStore()

/** 媒体库列表 FLIP(新增后重排)：JS 动画先问 reduced-motion（铁律 16） */
const reducedMotion = useReducedMotionSafe()
const listFlip = computed(() => flipConfig(reducedMotion.value))

/**
 * 面板级几何只能走 content-style：SSheet 的模板根是 fragment(scrim + panel)，
 * 父视图的 scoped data-v 落不到面板上；center 变体是 inset:0 + margin:auto，
 * width/height:auto 会拉伸成 92vw×84vh，故必须显式 fit-content 回到内容尺寸。
 * min-width:24em 是原 scoped 规则里的值（实测面板宽 348px ≈ 24em），必须保留，
 * 否则会退回 emby495 .formDialog 的 clamp(20%,60ch,50%)=509px。
 */
const LIBRARY_PANEL_STYLE = { width: 'fit-content', height: 'fit-content', minWidth: '24em' }

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
          <button type="button" class="itemsViewSettingsContainer-button raised raised-mini s-press btnNewLibrary submit emby-button emby-button-focusscale" title="新增媒体库" @click="addOpen = true">
            <i class="md-icon button-icon button-icon-left">&#xE03B;</i>
            <span class="emby-button-text">{{ t('admin.addLibrary') }}</span>
          </button>
          <button type="button" class="itemsViewSettingsContainer-button raised raised-mini btnRefresh submit emby-button emby-button-focusscale" title="扫描媒体库文件" @click="scan">
            <i class="md-icon button-icon button-icon-left">refresh</i>
            <span class="emby-button-text">{{ t('admin.scanLibrary') }}</span>
          </button>
        </div>

        <div v-if="loading" class="page-loading">{{ t('common.loading') }}</div>
        <div v-else v-auto-animate="listFlip" class="itemsContainer vertical-list padded-bottom-page focusable focuscontainer-x">
          <div v-for="l in libs" :key="l.Id" class="listItem s-lift listItem-autoactive listItem-hoverable">
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
    <!-- 新增媒体库(formDialog;行为交给 SSheet,可见标题留在插槽里) -->
    <SSheet
      :open="addOpen"
      variant="center"
      :title="t('admin.addLibrary')"
      panel-class="dialog formDialog"
      :content-style="LIBRARY_PANEL_STYLE"
      @close="addOpen = false"
    >
          <div class="formDialogHeader">
            <button type="button" class="dialogCloseButton s-press hide-mouse-idle-tv dialogHeaderButton paper-icon-button-light emby-button-focusscale" title="关闭" @click="addOpen = false">
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
            <button type="button" class="raised button-cancel s-press formDialogFooterItem emby-button emby-button-focusscale" @click="addOpen = false">{{ t('ButtonCancel') }}</button>
            <button type="button" class="raised button-submit s-press formDialogFooterItem btnCreateLibrary emby-button emby-button-focusscale" @click="addLibrary">{{ t('admin.addLibrary') }}</button>
          </div>
    </SSheet>
  </main>
</template>
<style scoped>
/* 弹层遮罩/底色/圆角/动效已交给 SSheet；面板几何走 :content-style（见 LIBRARY_PANEL_STYLE 说明） */
</style>