import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
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
