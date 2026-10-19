/**
 * 应用入口。mock 模式:先启动 MSW service worker 再挂载应用,
 * 保证首屏 API 请求即被拦截(VITE_ENABLE_MOCK=false 或生产构建可关闭)。
 */
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import { i18n } from './i18n'

// Emby 4.9.5 网页端 CSS 资产(1:1 还原基准,来自真实服务器 /web/)
import './assets/emby495'
// 4.9.5 补充(logo 路径/搜索页容器/登录页 startup 形态)
import './assets/login.css'
// Serata 设计层(tokens → base → motion → Emby 老页面换肤桥)。必须在所有 emby* CSS 之后加载
import './styles/serata/tokens.css'
import './styles/serata/base.css'
// 动效原语(关键帧 + reka data-state 动画 + Vue <Transition> 类族 + 交错进场)
import './styles/serata/motion.css'
import './styles/serata/emby-bridge.css'
import { initTheme } from './composables/useTheme'

// 主题必须在首帧之前落到 <html>(index.html 里还有一段内联脚本兜住更早的绘制)
initTheme()

async function enableMocking(): Promise<void> {
  if (import.meta.env.VITE_ENABLE_MOCK === 'false') return
  const { worker } = await import('./mocks/browser')
  await worker.start({ onUnhandledFrame: 'bypass' })
}

enableMocking().then(() => {
  const app = createApp(App)
  app.use(createPinia())
  app.use(router)
  app.use(i18n)
  app.mount('#app')
})