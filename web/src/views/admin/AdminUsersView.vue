<script setup lang="ts">
/**
 * 管理后台 —— 用户管理(/users)
 * 结构照真实站:viewScroller + 字母索引 + 用户 listItem;数据走 GET /Users、POST /Users/New、DELETE /Users/{id}
 */
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { UserDto } from '@/api/types'
import { getUsers, createUser, deleteUser } from '@/api/endpoints'
import { showToast } from '@/api/toast'
import SSheet from '@/components/ui/SSheet.vue'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { flipConfig, useReducedMotionSafe } from '@/lib/motion'

const { t } = useI18n()

/** 用户列表 FLIP(字母筛选/增删后重排)：JS 动画先问 reduced-motion（铁律 16） */
const reducedMotion = useReducedMotionSafe()
const listFlip = computed(() => flipConfig(reducedMotion.value))

/**
 * 面板级几何只能走 content-style：SSheet 的模板根是 fragment(scrim + panel)，
 * 父视图的 scoped data-v 落不到面板上；center 变体是 inset:0 + margin:auto，
 * width/height:auto 会被拉伸成 92vw×84vh，故内容尺寸弹层必须显式 fit-content
 * （旧实现：添加用户 509×150）。
 */
const CENTER_PANEL_STYLE = { width: 'fit-content', height: 'fit-content' }

const loading = ref(true)
const users = ref<UserDto[]>([])
const newName = ref('')
const addOpen = ref(false)

const alphabet = ['#', ...'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split('')]
const activeLetter = ref('#')

const filtered = computed(() => {
  if (activeLetter.value === '#') return users.value
  const L = activeLetter.value.toLowerCase()
  return users.value.filter((u) => u.Name.toLowerCase().startsWith(L))
})

async function load() {
  loading.value = true
  try { users.value = await getUsers() } catch { users.value = [] } finally { loading.value = false }
}
onMounted(load)

async function addUser() {
  const name = newName.value.trim()
  if (!name) return
  try {
    await createUser({ Name: name })
    addOpen.value = false
    newName.value = ''
    await load()
    showToast(t('admin.userAdded', { name }))
  } catch { showToast(t('admin.actionFailed')) }
}

async function removeUser(u: UserDto) {
  try {
    await deleteUser(u.Id)
    await load()
    showToast(t('admin.userDeleted', { name: u.Name }))
  } catch { showToast(t('admin.actionFailed')) }
}
</script>

<template>
  <main class="view viewScroller scrollFrameY flex flex-direction-row virtualScrollerScrollContainer emby-scroller page page-withFullDrawer page-withDockedDrawer">
    <div class="scrollSlider flex-grow padded-top-page padded-left padded-left-page padded-right scrollSliderY focuscontainer-y">
      <div class="flex align-items-center focuscontainer-x itemsViewSettingsContainer padded-bottom">
        <div class="listTotalRecordCount">{{ t('admin.userCount', { count: users.length }) }}</div>
        <button type="button" class="itemsViewSettingsContainer-button raised raised-mini s-press btnAddUser submit emby-button emby-button-focusscale" title="添加用户" @click="addOpen = true">
          <i class="md-icon button-icon button-icon-left">&#xE7FE;</i>
          <span class="emby-button-text">{{ t('admin.addUser') }}</span>
        </button>
      </div>

      <div class="alphaPicker flex flex-wrap-wrap padded-bottom focuscontainer-x">
        <button
          v-for="l in alphabet" :key="l" type="button"
          class="alphaPickerButton s-press emby-button emby-button-focusscale"
          :class="{ 'alphaPickerButton-selected': activeLetter === l }"
          @click="activeLetter = l"
        >{{ l }}</button>
      </div>

      <div v-if="loading" class="page-loading">{{ t('common.loading') }}</div>
      <div v-else v-auto-animate="listFlip" class="itemsContainer vertical-list padded-bottom-page focusable focuscontainer-x">
        <div v-for="u in filtered" :key="u.Id" class="listItem s-lift listItem-autoactive listItem-hoverable">
          <div class="listItem-content listItem-content-bg listItemContent-touchzoom listItem-border listItem-border-offset listItem-border-offset-square">
            <div class="listItemImageContainer itemAction listItemImageContainer-square listItemImageContainer-round">
              <i class="listItemIcon md-icon autortl">&#xE7FD;</i>
            </div>
            <div class="listItemBody itemAction listItemBody-2-lines">
              <div class="listItemBodyText listItemBodyText-lf">{{ u.Name }}</div>
              <div class="listItemBodyText listItemBodyText-lf listItemBodyText-secondary secondaryText">
                {{ u.IsAdministrator ? t('admin.administrator') : t('admin.standardUser') }}
              </div>
            </div>
            <button type="button" title="删除" class="listItemButton itemAction md-icon paper-icon-button-light emby-button-focusscale" @click.stop="removeUser(u)">
              <i class="md-icon">&#xE872;</i>
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- 添加用户(formDialog;真实站 /users/new)
         行为(焦点陷阱/Esc/点外关闭/滚动锁定/进出动效)交给 SSheet;可见标题仍留在插槽里 -->
    <SSheet
      :open="addOpen"
      variant="center"
      :title="t('admin.addUser')"
      panel-class="dialog formDialog"
      :content-style="CENTER_PANEL_STYLE"
      @close="addOpen = false"
    >
          <div class="formDialogHeader">
            <button type="button" class="dialogCloseButton s-press hide-mouse-idle-tv dialogHeaderButton paper-icon-button-light emby-button-focusscale" title="关闭" @click="addOpen = false">
              <i class="md-icon">&#xE5CD;</i>
            </button>
            <h3 class="formDialogHeaderTitle">{{ t('admin.addUser') }}</h3>
          </div>
          <div class="formDialogContent">
            <div class="inputContainer">
              <label class="inputLabel inputLabelUnfocused" for="newUserName">{{ t('LabelUser') }}</label>
              <input id="newUserName" v-model="newName" type="text" class="emby-input" />
            </div>
          </div>
          <div class="formDialogFooter">
            <button type="button" class="raised button-cancel s-press formDialogFooterItem emby-button emby-button-focusscale" @click="addOpen = false">{{ t('ButtonCancel') }}</button>
            <button type="button" class="raised button-submit s-press formDialogFooterItem emby-button emby-button-focusscale" @click="addUser">{{ t('admin.addUser') }}</button>
          </div>
    </SSheet>
  </main>
</template>

<style scoped>
.alphaPicker { display: flex; flex-wrap: wrap; gap: .3em; }
/* 字母索引：原 «白 70% 文字» 是深色主题抄死的值，浅色下整排字母看不见，改走 token */
.alphaPickerButton {
  min-width: 2em; padding: .25em .5em; border: 0; background: transparent;
  color: var(--s-text-2); cursor: pointer; border-radius: 100em; font-size: 95%;
}
/* 选中态：珊瑚底 + 墨字(原 #fff 只有 3.01:1 不达标) */
.alphaPickerButton-selected { background: var(--s-accent); color: var(--s-accent-ink); }
</style>