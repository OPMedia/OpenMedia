# OpenMedia Web 客户端（Serata）

OpenMedia 的 Web 前端，内部设计代号 **Serata**。独立于后端开发，通过 Emby 兼容的 REST API（默认 `/emby`）通信；
后端代码在仓库其他目录，**本目录与后端互不依赖**。

- 技术栈：Vue 3.5 + Vite 8 + TypeScript + Pinia + vue-router + vue-i18n
- 设计层：`src/styles/serata/`（深浅双主题令牌），契约见 [styles/serata/README.md](src/styles/serata/README.md)
- 规格与决策：[docs/spec/web.md](../docs/spec/web.md)、[ADR-0011](../docs/adr/0011-web-ui-emby-parity.md)、
  [ADR-0012](../docs/adr/0012-web-home-hero.md)、[ADR-0013](../docs/adr/0013-settings-honesty.md)

## 快速开始

要求：Node `^22.18.0 || >=24.12.0`、pnpm 10。

```sh
pnpm install
pnpm dev          # http://localhost:5173
```

**默认走 MSW 假数据**，开箱即可登录：账号 `demo` / 密码 `demo`，两个媒体库（电影 / 剧集）。
接真实后端时设 `VITE_ENABLE_MOCK=false`，并用 `VITE_API_PREFIX` 指到后端地址。

> 用 `localhost:5173` 访问，**不要用 `127.0.0.1`**：dev server 只监听 IPv6，直连 IPv4 会连不上。

## 命令

| 命令 | 作用 |
| --- | --- |
| `pnpm dev` | 开发服务器 + HMR |
| `pnpm build` | 类型检查 + 生产打包（`run-p type-check build-only`） |
| `pnpm type-check` | 仅 `vue-tsc --build` |
| `pnpm build-only` | 仅 `vite build`（产物在 `dist/`） |
| `pnpm preview` | 预览已打包产物 |

## 环境变量（都可选，默认值即开发态）

| 变量 | 默认 | 说明 |
| --- | --- | --- |
| `VITE_ENABLE_MOCK` | 开启 | 显式设为 `false` 才关闭 MSW，接真实后端 |
| `VITE_API_PREFIX` | `/emby` | 后端 API 前缀 |
| `VITE_BASE_PATH` | `/` | 路由 base，部署到子路径时改这里 |

## 目录

| 目录 | 内容 |
| --- | --- |
| `src/views/` | 页面（首页、媒体库、详情、搜索、用户设置、`admin/` 后台） |
| `src/components/` | 组件（`home/HomeHero.vue` 首屏轮播、`EmbyCard.vue` 卡片等） |
| `src/composables/` | `useTheme`（主题唯一事实来源）、`useHomeHero`（轮播状态机） |
| `src/styles/serata/` | 设计层：`tokens.css` 令牌、`base.css` 基元、`emby-bridge.css` 老页面换肤桥 |
| `src/api/`、`src/mocks/` | API 客户端与 MSW 假数据 |
| `src/i18n/locales/` | `zh-CN.json` / `en.json`（两侧键必须对齐） |
| `tools/` | 浏览器验收工具（见下） |
| `../docs/adr/`、`../docs/spec/` | 架构决策与规格 |

## 写前端时的硬规矩

1. **样式只消费 `--s-*` 令牌**，不写裸色值；类名 `s-` 前缀 + `<style scoped>`；不新增 `emby-*` 类名。
2. **设计层用 px 不用 rem**：`:root` 的 `font-size` 是 `90.15%`（1rem = 14.42px），rem 会算错。
3. **主题类只由 `useTheme()` 维护**，任何页面都不得整体赋值 `document.documentElement.className`。
4. **老 Emby 页面不重写**：靠 `emby-bridge.css` 覆盖 Emby 的 HSL 变量换肤。
5. **`t()` 必须落在 `computed` 或模板里**：模块级常量不会随语言切换重算。
6. **焦点环要 `!important` + `box-shadow` 双保险**：老主题的 `outline:0!important` 会吃掉单层 outline。
7. **`prefers-reduced-motion` 下自动轮转初始暂停**，但仍允许用户手动开启。
8. **每个设置项必须有读取方**（见 ADR-0013）：只写进 localStorage 而没人读的开关等于骗人，不加。

## 浏览器验收（`tools/`）

`tools/cdp.mjs` 是零依赖的 CDP 小工具（Node 24 自带 WebSocket），用来在真实浏览器里量尺寸、抓截图、验证交互：

```sh
# 先把无头 Edge 起起来（profile 放 .cdp-profile/，已被 .gitignore 排除）
msedge.exe --headless=new --remote-debugging-port=9222 \
  --user-data-dir=.cdp-profile --window-size=1440,900 --no-first-run --disable-gpu about:blank

node tools/cdp.mjs tools/probe-after-move.mjs
```

探针脚本默认导出 `async (cdp) => result`，可用 `cdp.send / eval / hover / click / screenshot / wait`。
截图输出到 `tools/shots/`（同样不进仓库）。

## 已知坑

- **搬目录后依赖会废掉**：pnpm 在 Windows 上给 `node_modules` 建的是**绝对路径 junction**，
  整个目录换位置后 vite 会报 `Cannot find package 'rolldown'` —— 重跑 `pnpm install --force` 即可
  （走本地 store，不必联网重下）。
- **别把 `.cdp-profile/`、`tools/shots/`、`dist/` 放进 watcher 视野**：这些目录里有被锁定的文件，
  watcher 会持续报 EBUSY 把 dev server 拖崩（`vite.config.ts` 的 `server.watch.ignored` 已排除）。
- **`web/` 之外是后端**：`src/`、`MediaBrowser.*`、`Emby.Server.Implementations/`、`tests/`、`deploy/` 不归前端改。
