<script setup lang="ts">
/**
 * 用户设置页(主页面)—— 只保留**真正生效**的项(仍走老 CSS,不重写版式)。
 *
 * 生效的部分：主页面区块 1-7(HomeView 按槽位顺序组装行，hero 也参与排序)。
 * 已删除（写进 localStorage 后没有任何代码读取）：
 *   电视模式主页面(水平/垂直) / 在主页面上显示所选项目的详情预览。
 */
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { getUserViews } from '@/api/endpoints'
import { useAuthStore } from '@/stores/auth'
import type { BaseItemDto } from '@/api/types'

const { t } = useI18n()
const auth = useAuthStore()

const LS_KEY = 'serata.homescreenSettings'

const store = ref<Record<string, string | boolean>>({
  // 默认序与 HomeView 读取的 7 个槽位一一对应：第 1 槽「精选」(hero) + 第 2 槽「继续观看」，
  // 新用户开箱即流媒体首页形态
  homeSection1: 'hero',
  homeSection2: 'resume',
  homeSection3: 'nextup',
  homeSection4: 'latest',
  homeSection5: '',
  homeSection6: '',
  homeSection7: '',
})

/** 区块下拉选项（computed：切语言要跟着变，不能是模块级常量） */
const SECTION_OPTIONS = computed(() => [
  { value: '', label: t('settings.off') },
  { value: 'hero', label: t('home.hero') },
  { value: 'mymedia', label: t('home.myMedia') },
  { value: 'mymediasmall', label: t('settings.secMyMediaSmall') },
  { value: 'recordings', label: t('settings.secRecordings') },
  { value: 'resume', label: t('home.continueWatching') },
  { value: 'resumeaudio', label: t('settings.secResumeAudio') },
  { value: 'nextup', label: t('home.nextUp') },
  { value: 'latest', label: t('settings.secLatest') },
])

const views = ref<BaseItemDto[]>([])
const libOrder = ref<BaseItemDto[]>([])

onMounted(async () => {
  try {
    const saved = JSON.parse(localStorage.getItem(LS_KEY) ?? '{}')
    store.value = { ...store.value, ...saved }
  } catch { /* 损坏则忽略 */ }
  try {
    views.value = (await getUserViews(auth.user!.Id)).Items
    libOrder.value = [...views.value]
  } catch { /* 静默 */ }
})

watch(store, (v) => localStorage.setItem(LS_KEY, JSON.stringify(v)), { deep: true })

// 拖拽排序(HTML5;真实站 drop-target/draghandle 同款语义)
const dragIndex = ref(-1)
function onDragStart(i: number) { dragIndex.value = i }
function onDragOver(e: DragEvent, i: number) { e.preventDefault() }
function onDrop(i: number) {
  if (dragIndex.value < 0 || dragIndex.value === i) return
  const moved = libOrder.value.splice(dragIndex.value, 1)[0]
  if (!moved) return
  libOrder.value.splice(i, 0, moved)
  dragIndex.value = -1
}
</script>

<template>
  <main class="view flex flex-direction-column scrollFrameY flex-grow emby-scroller page scrollY overflowYScroll focuscontainer-x settingsView page-withFullDrawer page-withDockedDrawer">
    <div class="scrollSlider flex-grow flex-direction-column padded-left padded-left-page padded-right padded-top-page padded-bottom-page scrollSliderY">
      <form class="auto-center padded-top fieldsets" @submit.prevent>
        <fieldset>
          <legend>{{ t('settings.homeSections') }}</legend>
          <div class="fieldset-fields">
            <div v-for="n in 7" :key="n" class="selectContainer fieldset-field">
              <label class="selectLabel">
                <div class="selectLabelText">{{ t('settings.homeSectionN', { n }) }}</div>
                <div class="emby-select-wrapper">
                  <select v-model="store[`homeSection${n}` as keyof typeof store.value]" class="autoSetting autoSave emby-select-focusscale emby-select" :label="t('settings.homeSectionN', { n })">
                    <option v-for="o in SECTION_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                  </select>
                  <div class="selectArrowContainer"><i class="selectArrow md-icon">expand_more</i></div>
                </div>
              </label>
            </div>
          </div>
        </fieldset>

        <fieldset>
          <legend>{{ t('settings.libraryOrder') }}</legend>
          <div class="viewOrderList itemsContainer vertical-list fieldset-fields fieldset-fields-fieldmargins">
            <div
              v-for="(lib, i) in libOrder" :key="lib.Id"
              class="listItem listItem-autoactive listItem-hoverable drop-target"
              :data-id="lib.Id" draggable="true"
              @dragstart="onDragStart(i)" @dragover="onDragOver($event, i)" @drop="onDrop(i)"
            >
              <div class="listItem-content listItem-content-bg listItemContent-touchzoom listItem-border listItem-border-offset listItem-border-offset-square">
                <div class="listItemImageContainer itemAction listItemImageContainer-square listItemImageContainer-round">
                  <i class="listItemIcon md-icon autortl">&#xE02C;</i>
                </div>
                <div class="listItemBody itemAction listItemBody-draghandle listItemBody-reduceypadding listItemBody-1-lines">
                  <div class="listItemBodyText listItemBodyText-nowrap listItemBodyText-lf">{{ lib.Name }}</div>
                </div>
              </div>
            </div>
          </div>
        </fieldset>

        <fieldset v-for="lib in views" :key="lib.Id">
          <legend>{{ lib.Name }}</legend>
          <div class="fieldset-fields">
            <div class="toggleContainer toggleContainer-listItem fieldset-field emby-toggle-label">
              <label class="fldIncludeInLatest toggleContainer toggleContainer-listItem fieldset-field emby-toggle-label">
                <input type="checkbox" class="chkIncludeInLatest emby-toggle" :data-folderid="lib.Id" checked role="switch" />
                <span>{{ t('settings.includeInLatest') }}</span>
                <span class="toggleLabel flex-grow"></span>
                <div class="toggleSwitch"></div>
              </label>
            </div>
          </div>
        </fieldset>
      </form>
    </div>
  </main>
</template>