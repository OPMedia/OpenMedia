import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
  // VITE_BASE_PATH 必须同时作用于资源 URL，否则部署到子路径时 index.html 里的
  // /assets/*.js 会 404（router 的 base 只管路由，管不到构建产物路径）。
  base: process.env.VITE_BASE_PATH ?? '/',
  plugins: [
    vue(),
    vueDevTools(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    watch: {
      // 验证用的无头浏览器 profile / 截图 / 构建产物里有大量被锁定的临时文件,
      // 之前 watcher 持续报 EBUSY 把 dev server 拖崩(exit 1),这里直接排除
      ignored: ['**/.cdp-profile/**', '**/tools/shots/**', '**/dist/**', '**/.tmp/**', '**/*.tmpdir/**'],
    },
  },
})
