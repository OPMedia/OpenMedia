import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'
import { resolveWebBasePath } from './src/config/basePath'

// https://vite.dev/config/
export default defineConfig({
  // 资源 URL 与 router base 必须一致，否则子路径部署时 index.html 里的
  // /assets/*.js 会 404。服务端 BaseUrl=/media 时设 VITE_API_PREFIX=/media/emby
  // 即可同时推导两者；VITE_BASE_PATH 可显式覆盖。
  base: resolveWebBasePath(process.env.VITE_BASE_PATH, process.env.VITE_API_PREFIX),
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
