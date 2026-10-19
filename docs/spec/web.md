# 前端（SPEC/web）

> 决策依据：ADR-0011（以 Emby 网页端为对齐基准）、ADR-0012（首页流媒体化 +
> Serata 独立设计层）、ADR-0005（monorepo）。
> 铁律：布局与交互先与新近版 Emby 官方网页端**严格对齐**，联调验证一致后
> 才做自主改进；顺序不可倒置（ADR-0011 Consequences）。**首页/外壳已进入
> 自主改进阶段（ADR-0012），其余页面仍受对齐铁律约束。**

## 1. 技术栈 〔定稿〕

Vue 3 + TypeScript + Vite；路由 vue-router + 状态 pinia〔草案：若实现中发现
更简方案可换，以"依赖最少"为准绳〕；构建产物由**后端静态托管**（同源，
消灭 CORS/混合内容两道墙，见 core.md 技术栈表）。

## 2. LinWeb 的引入方式 〔定稿〕

- git subtree 挂上游（可拉更新，禁止复制粘贴后失联，ADR-0005）。
- **复用**：播放核心模块（`src/demux` / `remux` / `subs` / `danmaku` /
  `player.js` / `audio` / `ui/touch.js`）与播放页形态（play.html 的编排思路）。
- **不复用**：LinWeb 浏览壳（emby.html）——浏览/详情/管理 UI 全部按
  Emby 网页端基准重写；LinWeb 的 Emby 客户端（src/emby/client.js）作为
  api.md §1 契约的参考实现，新前端写自己的 TS 客户端。

## 3. 页面清单（v1）〔定稿〕

- **登录**：服务器地址 + 用户名/密码；`/Users/Public` 快捷选择。
- **首页**（ADR-0012 重定稿）：流媒体平台形态。
  - **首屏 Hero** 占视口约 3/4：左信息区（类型·年份·时长·分级 / 标题优先
    Logo 图 / 两行简介 / 主次 CTA / 进度）+ 右侧 16:9 轮转画框（不拉伸，
    只有竖版图时降级 2:3）。8s 自动轮转，hover/focus/标签页隐藏时暂停，
    必须有显式暂停按钮（WCAG 2.2.2），←/→ 与滑动切换，
    `prefers-reduced-motion` 下不自动播放。
  - 顺序：Hero → 继续观看 → 接下来播放（与继续观看去重）→ 每库一行最新；
    不再有与库入口重复的"我的媒体"行。
  - 区块顺序由用户设置（`homeSection1..7`，含新增 `hero` 类型）真实驱动，
    越界/损坏配置回落到默认序；**设置页写、首页读，必须闭环**。
  - 每行：悬停显现左右箭头、48px 渐隐遮罩、横向 snap、骨架屏、
    一次性错峰入场；错误态可重试、空库有引导。
- **库浏览**：海报网格/列表视图、筛选（类型/年份/评分/未看）、排序、分页。
- **详情页**：电影/剧集/季/集；季标签、分集列表、媒体信息、演职员、
  相似推荐、右键/长按菜单（标记已看、收藏、点赞、加入播放列表、刷新元数据、
  识别、编辑、删除——对齐 Emby 官网右键全集，见 api.md §1.6）。
- **全局搜索**。
- **播放页**：LinWeb 播放核心 + Emby 风格入播流程；弹幕、一起看入口；
  直连/服务器代理降级链照 LinWeb 现行实现。
- **用户设置**：字幕/音轨语言偏好、播放默认值、通知订阅开关、头像
  （上传或随机头像 API 三站轮换 → WebP 持久化）、邮箱绑定、密码修改。
- **管理后台**（仅管理员）：**看板**（第一项，内容按已定稿六区块）、
  媒体库管理、用户与权限、转码设置（档位/GPU/开关）、任务面板、
  插件、API Key、Emby 迁移向导、登录审计（含 UA 黑白名单）、Branding、
  备份与恢复、弹幕治理（审核队列/举报/封禁房间解封）。

## 4. PWA 〔定稿〕

manifest + service worker；策略：静态资源 cache-first（文件名带 hash）、
API network-only；可安装到桌面/手机；**无离线内容能力**（不做媒体离线缓存）。
现状：`web/public/manifest.webmanifest` 已落地（图标用 `serata-icons` 的
192/512 方版 + maskable），`index.html` 已挂 manifest 与深浅两值的
`theme-color`；service worker 待补。

## 5. 移动端 〔定稿〕

PC/移动同代码库同一流程（ADR-0011）；断点行为对齐 Emby 网页端，触控层
（手势/粗指针/保住叠加层的全屏/`viewport-fit=cover` 安全区）复用 LinWeb
`ui/touch.js` 的既有经验。

## 6. base_path 〔定稿〕

所有资源相对引用、路由 base 可配、API 客户端统一走同一前缀常量；构建时
`VITE_BASE_PATH` 与后端 `base_path` 必须一致（部署校验项）。

## 7. i18n 〔定稿〕

默认 zh-CN；文案全部走资源文件（zh-CN + en 双维护）；禁止硬编码文案进组件。

## 8. 验收 〔部分草案〕

- 沿用 LinWeb 浏览器测试哲学：真浏览器断言（手势/控制条/菜单可点性）。
- **联调清单**：与真实 Emby 网页端并排对照的页面级 checklist（首页分区、
  详情布局、右键菜单项、设置页结构），逐页打勾后该页才允许"对齐完成"。
- **视觉验收（ADR-0012 新增，逐项可测）**：
  - 首屏 hero 高度占视口 75%±2%，1440×900 与 390×844 两个视口都不溢出。
  - 正文对比度 ≥4.5:1、大字 ≥3:1、控件与焦点环 ≥3:1（令牌表已逐项实测）。
  - 珊瑚强调色单屏 ≤5 处；品牌渐变只出现在进度条与 hero 环境光晕。
  - 动画属性只允许 `transform` / `opacity` / `filter`（grep 校验，无 `transition: all`）。
  - 键盘全可达、`:focus-visible` 可见；`prefers-reduced-motion` 下无自动轮转。
  - 深浅双模切换无白闪，且老页面（库/详情/搜索/设置/后台）同步换肤。
  - 首页区块顺序改动后刷新仍生效（设置页 → 首页闭环）。
- 弹幕/房间帧协议测试复用 room-danmaku 册验收。

## 9. 视觉系统（Serata 设计层）〔定稿〕

- 位置：`web/src/styles/serata/`（`tokens.css` 令牌、`base.css` 全局基元、
  `emby-bridge.css` 老页面换肤桥），加载顺序在所有 `emby*` CSS 之后。
- **令牌是唯一接口**：新组件只消费 `--s-*`，不写裸色值；类名 `s-` 前缀 +
  `<style scoped>`；不新增 `emby-*` 类名。契约见同目录 `README.md`。
- **配色锚点**：`serata-icons` 的黄昏色阶 `#1B1F3B → #46356F → #E0705A`；
  深色画布 `#0A0C18`、浅色画布 `#F7F5F2`；珊瑚为强调色而非主色，
  主 CTA 用"纸与墨"（珊瑚 + 奶白实测 3.01:1 不达标，墨 + 珊瑚 5.42:1 达标）。
- **主题**：`dark`（默认）/ `light` / `system`；`useTheme()` 唯一事实来源；
  持久化键 `serata.themeMode`；`index.html` 内联脚本防首帧闪白。
- **老页面**：不重写，靠 `--background-*` / `--theme-primary-color-*` /
  `--theme-accent-text-color-*` / `--theme-text-color-*` 四组 HSL 变量换肤；
  `web/src/assets/emby495/**` 视为只读遗产，只允许整体删除。
- **主题类所有权**：`<html>` 上的 `theme-dark` / `theme-light` / 结构性类只由
  `App.vue` + `useTheme()` 维护；任何页面都不得整体赋值
  `document.documentElement.className`（历史坑：后台页曾把深色模式翻成白、
  播放页曾把浅色模式切成深色，且退出后不还原）。
- **局部固定深色语境**（如播放页 OSD）在自己的根节点上覆盖局部令牌实现
  （`.s-osd`），并额外落显式 `color`——老浅色主题在 `.skinBody` 上用的是具体
  颜色而非变量，只覆盖 HSL 三件套会得到墨色文字。
- **遮罩语义分离**：`--s-scrim`（渐隐/提亮，浅色下是象牙）与
  `--s-scrim-modal`（模态压暗，两主题都必须变暗）不可混用。
- **动效可及性**：`prefers-reduced-motion: reduce` 时首屏轮转**初始暂停**，
  但允许用户显式开启（WCAG 2.2.2 只约束自动播放）。
- **焦点环双保险**：老主题 `.button-link` / `.listItem` / `.emby-button` 一族带
  `outline:0!important`，新层只画 outline 会被静默吃掉。`:focus-visible` 必须同时落
  outline（带 `!important`）和 `box-shadow` 双层环。鼠标点击不触发 `:focus-visible`，
  所以「点了几下都正常」不能证明焦点可见，必须用 Tab 逐站验证。
- **首屏所属槽位**：首页 Hero 是 `homeSection1..7` 中的一项，位置必须由槽位决定
  （`rows` 里的 `isHero` 占位行参与排序），不允许钉在内容区顶部。
- **首帧底色**：`index.html` 内联 `html.theme-dark/theme-light{background}`，
  在样式包解析前就把画布刷成主题色；否则 dev 下 reload 会出现纯白空帧。
- **常驻侧栏语义**：docked 侧栏是 `navigation`，只有覆盖式抽屉才是 `dialog` +
  `aria-modal`；否则每个老页面都会向屏幕阅读器报一个"对话框"。

## 10. 设置页真伪（ADR-0013）〔定稿〕

- **每一个设置项必须有读取方**。控件绑到 localStorage 只是「存得下」，
  必须有别的代码 `getItem` 读它并影响界面；两者缺一不算完成。
  核查手段：`grep localStorage.getItem` 与 `setItem` 全量对照。
- **不做「即将支持」的假开关**。点了没反应、改了没效果的项直接删；
  缺失可以补，骗人的开关会消耗信任额度并被当成已完成。
- **侧栏只渲染有路由的条目**：`route: null` 一律不进数据，渲染层
  （`visibleAdminSections`）再兜底过滤空路由与空分组，杜绝静默死按钮。
- **能接线的别删**：语言下拉原本是摆设，补一个 `watch → setLocale()` 即为真功能；
  选项里不允许出现没有语言包的语言。
- **选项文案必须是 `computed`**：形如 `const OPTS = [{ label: '自动' }]` 的模块级常量在切语言后
  不会重算，会留下「界面英文、下拉中文」的半截状态。一切 `t()` 都要落在 `computed` / 模板里。
- **语言名用各自语言书写**（简体中文 / English），不翻译——这是唯一允许的硬编码文案类别。
- 现状（2026-10-19）：用户设置只剩 `显示`（外观）与 `主页面`（7 个区块槽位）两组真生效项；
  播放页 / 字幕页 / 配置页的时区·启动行为·文本大小·屏保 / 主页面「媒体库顺序」拖拽
  仍是摆设，待按同一标准处理。

## 11. 组件基座与动效分层（ADR-0014）〔定稿〕

- **组件基座是 Reka UI，不是 shadcn-vue，也不引 Tailwind**。shadcn-vue 本身就是
  「Reka UI + Tailwind 皮」，而本项目的设计层是冻结的 CSS 令牌体系（`:root` 90.15%、
  单位必须 px、老页面靠 HSL 变量换肤）；再叠一层原子类等于给同一界面装两套样式真源。
  **要能力（可访问性原语 + `data-state` 状态机），不要皮。** 这个选择可增量演进：
  将来真要上 Tailwind 皮，是在 Reka UI 之上叠加，不是推倒重来。
- **弹层只走 `components/ui/SSheet.vue`**（reka-ui Dialog）。它管行为：焦点陷阱、Esc、
  点外关闭、滚动锁定、`aria-modal`、焦点归还、进出动效；不管长相：内容由插槽给，
  宿主继续用 emby495 的 `actionSheet*` 类名，保证 1:1 还原。
  **禁止手搓 `v-if` 遮罩**——那必然丢掉焦点管理、读屏隔离和滚动锁定。
- **动效分三层，不要用错工具**：CSS（`motion.css`）管弹层/toast 进出与悬停微状态；
  `motion-v` 管滚动进场与指针编排（需要弹簧物理）；`@formkit/auto-animate` 管列表 FLIP。
- **动效令牌唯一真源**：`tokens.css` 定义 → `motion.css` 出类名与关键帧 →
  `lib/motion.ts` 做 JS 镜像（CSS 变量 JS 读不到，只能镜像）。
  组件里不许再写 `@keyframes`、裸 `ms`、裸 `cubic-bezier`。
- **退出必须比进场快**：`--s-dur-exit`(180ms) < `--s-dur-sheet`(280ms)；
  进场 `--s-ease-spring`，退出 `--s-ease-exit`。
- **reduced-motion 双保险**：CSS 侧压时长 + `motion.css` 末尾一刀切 `animation`；
  **JS 侧没有兜底**，所以 motion-v / auto-animate 动手前必须过 `useReducedMotionSafe()`，
  `reduce` 时完全不位移。
- **View Transitions 只用在主题切换**（`useTheme.ts` 调 `startViewTransition`）：
  整页几十个 HSL 变量同时翻转，硬切会闪、两套主题对叠会糊成中间色，
  所以老快照保持不透明、新快照淡入。**路由转场不走它**——vue-router 没有能包住
  「DOM 更新」这一步的钩子，路由转场用 Vue `<Transition name="s-page">`。
  这是有意识的取舍，不是遗漏。
- **依赖升级先跑 `node tools/probe-libs.mjs`**：它把实际依赖的 41 个导出钉成断言，
  防止 minor 升级静默改导出。
- **reka 的两个硬约束**：① `DialogContent` 必须配 `DialogTitle`（缺失报警，SSheet 已用
  `VisuallyHidden` 兜底）；② `Presence` 靠 `animationend` 判断卸载时机，
  所以它的状态动画**必须用 `animation` 而不是 `transition`**。