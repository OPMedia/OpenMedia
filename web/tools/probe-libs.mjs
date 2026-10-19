/**
 * 动效/组件栈能力探针 —— 依赖冒烟测试。
 *
 * 为什么需要它：reka-ui / motion-v / auto-animate 都是"用了才发现 API 变了"的库。
 * 这个脚本把**我们实际依赖的那几个导出**钉成断言，版本升级后一条命令就能发现回归。
 *
 * 用法（工作目录必须是 web/，否则解析不到 node_modules）：
 *   cd OpenMedia/web && node tools/probe-libs.mjs
 * 退出码：0 = 全部就位；1 = 有缺失（会打印缺失项）。
 */

const REQUIRED = {
  'motion-v': ['motion', 'AnimatePresence', 'useReducedMotion', 'LayoutGroup'],
  'reka-ui': [
    'DialogRoot',
    'DialogPortal',
    'DialogOverlay',
    'DialogContent',
    'DialogTitle',
    'DialogDescription',
    'ToastProvider',
    'ToastPortal',
    'ToastViewport',
    'ToastRoot',
    'ToastTitle',
    'ToastDescription',
    'ToastClose',
    'createToastManager',
    'useToastManager',
    'SelectRoot',
    'SelectTrigger',
    'SelectPortal',
    'SelectContent',
    'SelectViewport',
    'SelectItem',
    'SelectItemText',
    'SelectItemIndicator',
    'SelectValue',
    'SelectIcon',
    'TooltipProvider',
    'TooltipRoot',
    'TooltipTrigger',
    'TooltipPortal',
    'TooltipContent',
    'VisuallyHidden',
    'SwitchRoot',
    'SwitchThumb',
  ],
  '@vueuse/core': ['usePreferredReducedMotion', 'useElementVisibility', 'useScrollLock'],
  '@formkit/auto-animate': ['default'],
}

let failed = 0

for (const [pkg, names] of Object.entries(REQUIRED)) {
  let mod
  try {
    mod = await import(pkg)
  } catch (err) {
    console.error(`✗ ${pkg} —— 无法导入：${err.message}`)
    failed++
    continue
  }
  const missing = names.filter((n) => mod[n] === undefined)
  if (missing.length) {
    console.error(`✗ ${pkg} —— 缺少导出：${missing.join(', ')}`)
    failed++
  } else {
    console.log(`✓ ${pkg} —— ${names.length} 个导出全部就位`)
  }
}

// auto-animate 的默认导出必须是可调用函数（Vue 里当指令用）
try {
  const aa = await import('@formkit/auto-animate')
  if (typeof aa.default !== 'function') {
    console.error('✗ @formkit/auto-animate —— default 不是函数，无法当指令使用')
    failed++
  }
} catch {
  /* 上面已经报过 */
}

if (failed) {
  console.error(`\n依赖栈探针失败：${failed} 项`)
  process.exit(1)
}
console.log('\n依赖栈探针通过。')
