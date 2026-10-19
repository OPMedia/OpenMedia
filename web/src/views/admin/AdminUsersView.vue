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

const { t } = useI18n()

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
        <button type="button" class="itemsViewSettingsContainer-button raised raised-mini btnAddUser submit emby-button emby-button-focusscale" title="添加用户" @click="addOpen = true">
          <i class="md-icon button-icon button-icon-left">&#xE7FE;</i>
          <span class="emby-button-text">{{ t('admin.addUser') }}</span>
        </button>
      </div>

      <div class="alphaPicker flex flex-wrap-wrap padded-bottom focuscontainer-x">
        <button
          v-for="l in alphabet" :key="l" type="button"
          class="alphaPickerButton emby-button emby-button-focusscale"
          :class="{ 'alphaPickerButton-selected': activeLetter === l }"
          @click="activeLetter = l"
        >{{ l }}</button>
      </div>

      <div v-if="loading" class="page-loading">{{ t('common.loading') }}</div>
      <div v-else class="itemsContainer vertical-list padded-bottom-page focusable focuscontainer-x">
        <div v-for="u in filtered" :key="u.Id" class="listItem listItem-autoactive listItem-hoverable">
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

    <!-- 添加用户(formDialog;真实站 /users/new) -->
    <Teleport to="body">
      <div v-if="addOpen" class="dialogScrim" @click.self="addOpen = false">
        <div class="dialog dialog-animated formDialog opened" @click.stop>
          <div class="formDialogHeader">
            <button type="button" class="dialogCloseButton hide-mouse-idle-tv dialogHeaderButton paper-icon-button-light emby-button-focusscale" title="关闭" @click="addOpen = false">
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
            <button type="button" class="raised button-cancel formDialogFooterItem emby-button emby-button-focusscale" @click="addOpen = false">{{ t('ButtonCancel') }}</button>
            <button type="button" class="raised button-submit formDialogFooterItem emby-button emby-button-focusscale" @click="addUser">{{ t('admin.addUser') }}</button>
          </div>
        </div>
      </div>
    </Teleport>
  </main>
</template>

<style scoped>
.alphaPicker { display: flex; flex-wrap: wrap; gap: .3em; }
.alphaPickerButton {
  min-width: 2em; padding: .25em .5em; border: 0; background: transparent;
  color: rgba(255, 255, 255, .7); cursor: pointer; border-radius: 100em; font-size: 95%;
}
.alphaPickerButton-selected { background: var(--theme-primary-color); color: #fff; }
.dialogScrim {
  position: fixed; inset: 0; z-index: 1000; background: rgba(0, 0, 0, .5);
  display: flex; align-items: center; justify-content: center;
}
.dialogScrim .dialog.formDialog { background: rgb(40, 40, 43); color: rgba(255, 255, 255, .87); border-radius: .9em; }
</style>