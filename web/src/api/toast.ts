/**
 * 全局 toast(真实站 toast css 已装载;单例 DOM)
 */
let container: HTMLDivElement | null = null

export function showToast(text: string): void {
  if (!container) {
    container = document.createElement('div')
    container.className = 'toast'
    document.body.appendChild(container)
  }
  container.textContent = text
  container.classList.add('toastVisible')
  // 动画 3s 后自隐(toast css 自带 fade-out 动画;类移除复位)
  clearTimeout((showToast as unknown as { _t?: ReturnType<typeof setTimeout> })._t)
  ;(showToast as unknown as { _t?: ReturnType<typeof setTimeout> })._t = setTimeout(() => {
    container?.classList.remove('toastVisible')
  }, 3600)
}