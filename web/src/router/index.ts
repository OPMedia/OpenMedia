/** 路由(SPEC/web §3 页面清单 v1);守卫:未登录跳登录页。 */
import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(import.meta.env.VITE_BASE_PATH ?? '/'),
  routes: [
    { path: '/login', name: 'login', component: () => import('@/views/LoginView.vue'), meta: { public: true } },
    { path: '/', name: 'home', component: () => import('@/views/HomeView.vue') },
    { path: '/library/:id', name: 'library', component: () => import('@/views/LibraryView.vue') },
    { path: '/item/:id', name: 'item', component: () => import('@/views/ItemDetailView.vue') },
    { path: '/search', name: 'search', component: () => import('@/views/SearchView.vue') },
    { path: '/dashboard', name: 'dashboard', component: () => import('@/views/DashboardView.vue') },
    { path: '/dashboard/users', name: 'dashboard-users', component: () => import('@/views/admin/AdminUsersView.vue') },
    { path: '/dashboard/libraries', name: 'dashboard-libraries', component: () => import('@/views/admin/AdminLibrariesView.vue') },
    { path: '/dashboard/tasks', name: 'dashboard-tasks', component: () => import('@/views/admin/AdminTasksView.vue') },
    { path: '/dashboard/logs', name: 'dashboard-logs', component: () => import('@/views/admin/AdminLogsView.vue') },
    { path: '/dashboard/general', name: 'dashboard-general', component: () => import('@/views/admin/AdminSettingsView.vue'), meta: { section: 'general' } },
    { path: '/dashboard/transcoding', name: 'dashboard-transcoding', component: () => import('@/views/admin/AdminSettingsView.vue'), meta: { section: 'transcoding' } },
    { path: '/dashboard/network', name: 'dashboard-network', component: () => import('@/views/admin/AdminSettingsView.vue'), meta: { section: 'network' } },
    { path: '/dashboard/database', name: 'dashboard-database', component: () => import('@/views/admin/AdminSettingsView.vue'), meta: { section: 'database' } },
    { path: '/dashboard/convert', name: 'dashboard-convert', component: () => import('@/views/admin/AdminSettingsView.vue'), meta: { section: 'convert' } },
    { path: '/dashboard/livetv', name: 'dashboard-livetv', component: () => import('@/views/admin/AdminSettingsView.vue'), meta: { section: 'livetv' } },
    { path: '/dashboard/devices', name: 'dashboard-devices', component: () => import('@/views/admin/AdminListView.vue'), meta: { section: 'devices' } },
    { path: '/dashboard/plugins', name: 'dashboard-plugins', component: () => import('@/views/admin/AdminListView.vue'), meta: { section: 'plugins' } },
    { path: '/dashboard/apikeys', name: 'dashboard-apikeys', component: () => import('@/views/admin/AdminListView.vue'), meta: { section: 'apikeys' } },
    { path: '/dashboard/plugins/backup', name: 'plugin-backup', component: () => import('@/views/admin/AdminPluginView.vue'), meta: { pluginName: 'Backup & Restore' } },
    { path: '/dashboard/plugins/diagnostics', name: 'plugin-diagnostics', component: () => import('@/views/admin/AdminPluginView.vue'), meta: { pluginName: 'Diagnostic Options' } },
    { path: '/dashboard/plugins/opensubtitles', name: 'plugin-opensubtitles', component: () => import('@/views/admin/AdminPluginView.vue'), meta: { pluginName: 'Open Subtitles' } },
    { path: '/dashboard/plugins/subtitleoptions', name: 'plugin-subtitleoptions', component: () => import('@/views/admin/AdminPluginView.vue'), meta: { pluginName: 'Subtitle Options' } },
    { path: '/dashboard/plugins/usersessions', name: 'plugin-usersessions', component: () => import('@/views/admin/AdminPluginView.vue'), meta: { pluginName: 'User Sessions' } },
    { path: '/dashboard/plugins/shenyi', name: 'plugin-shenyi', component: () => import('@/views/admin/AdminPluginView.vue'), meta: { pluginName: '神医助手' } },
    { path: '/settings/display', name: 'settings-display', component: () => import('@/views/SettingsDisplayView.vue') },
    { path: '/settings/playback', name: 'settings-playback', component: () => import('@/views/SettingsPlaybackView.vue') },
    { path: '/settings/subtitles', name: 'settings-subtitles', component: () => import('@/views/SettingsSubtitlesView.vue') },
    { path: '/settings/homescreen', name: 'settings-homescreen', component: () => import('@/views/SettingsHomescreenView.vue') },
    { path: '/settings/general', name: 'settings-general', component: () => import('@/views/SettingsGeneralView.vue') },
    { path: '/person/:id', name: 'person', component: () => import('@/views/PersonView.vue') },
    { path: '/play/:id', name: 'play', component: () => import('@/views/VideoOsdView.vue') },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (!to.meta.public && !auth.isLoggedIn) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  if (to.name === 'login' && auth.isLoggedIn) {
    return { name: 'home' }
  }
})

export default router