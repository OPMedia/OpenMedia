<script setup lang="ts">
/**
 * 管理后台 —— 计划任务(/scheduledtasks)
 * 数据回路:GET /ScheduledTasks + POST /ScheduledTasks/Running/{id}(点"运行"后真实刷新上次运行时间)
 */
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { getScheduledTasks, runScheduledTask } from '@/api/endpoints'
import type { AdminTask } from '@/api/endpoints'
import { showToast } from '@/api/toast'

const { t } = useI18n()

const loading = ref(true)
const tasks = ref<AdminTask[]>([])

const groups = computed(() => {
  const map = new Map<string, AdminTask[]>()
  for (const task of tasks.value) {
    const arr = map.get(task.Category) ?? []
    arr.push(task)
    map.set(task.Category, arr)
  }
  return [...map.entries()].map(([Category, list]) => ({ Category, Tasks: list }))
})

function agoLabel(task: AdminTask): string {
  const end = task.LastExecutionResult?.EndTimeUtc
  if (!end) return t('admin.neverRun')
  const mins = Math.max(0, Math.round((Date.now() - new Date(end).getTime()) / 60000))
  const time = mins >= 60 ? String(Math.round(mins / 60)) : String(mins)
  return t('admin.lastRun', { time, duration: '0 sec' })
}

async function load() {
  loading.value = true
  try { tasks.value = await getScheduledTasks() } catch { tasks.value = [] } finally { loading.value = false }
}
onMounted(load)

async function run(task: AdminTask) {
  try {
    await runScheduledTask(task.Id)
    await load()
    showToast(t('admin.taskStarted', { name: task.Name }))
  } catch { showToast(t('admin.actionFailed')) }
}
</script>

<template>
  <main class="view flex flex-direction-column scrollFrameY flex-grow emby-scroller page page-withFullDrawer page-withDockedDrawer scrollY focuscontainer-x">
    <div class="scrollSlider flex-grow padded-top-page padded-left padded-left-page padded-right scrollSliderY focuscontainer-y">
      <div v-if="loading" class="page-loading">{{ t('common.loading') }}</div>
      <div v-for="g in groups" :key="g.Category" v-else class="verticalSection verticalSection-cards scheduledTasksSection padded-bottom">
        <div class="sectionTitleContainer sectionTitleContainer-cards">
          <h2 class="sectionTitle sectionTitle-cards">{{ g.Category }}</h2>
        </div>
        <div class="itemsContainer vertical-list">
          <div v-for="task in g.Tasks" :key="task.Id" class="listItem listItem-autoactive listItem-hoverable">
            <div class="listItem-content listItem-content-bg listItemContent-touchzoom listItem-border listItem-border-offset listItem-border-offset-square">
              <div class="listItemImageContainer itemAction listItemImageContainer-square listItemImageContainer-round">
                <i class="listItemIcon md-icon autortl">schedule</i>
              </div>
              <div class="listItemBody itemAction listItemBody-3-lines">
                <div class="listItemBodyText listItemBodyText-lf">{{ task.Name }}</div>
                <div class="listItemBodyText listItemBodyText-lf listItemBodyText-secondary secondaryText">{{ agoLabel(task) }}</div>
                <div class="listItemBodyText listItemBodyText-lf listItemBodyText-secondary secondaryText">{{ task.Description }}</div>
              </div>
              <button type="button" title="运行" class="listItemButton itemAction md-icon paper-icon-button-light emby-button-focusscale" @click.stop="run(task)">
                <i class="md-icon">&#xE037;</i>
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </main>
</template>