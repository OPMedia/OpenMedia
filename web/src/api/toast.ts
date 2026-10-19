/**
 * 全局 toast —— 由 reka-ui 的 Toast 体系驱动（见 components/ui/SToastHost.vue）。
 *
 * 旧实现（19 行单例）的三个硬伤，这里一次性解决：
 *  ① 位置错：`.toast` 自身没有 position，只 append 到 body 末尾 → 实际渲染在文档流尾部，
 *     常常落在视口之外（modules__toast__toast.css 里定位靠的是 `.toast-group` 包裹层，旧代码没建）。
 *  ② 不能堆叠：全局只有一个 div，后一条直接覆盖前一条。
 *  ③ 没有语义：成功/失败长得一模一样，也没有可关闭按钮。
 *
 * 现在：`createToastManager()` 在模块作用域建单例，任何地方（含非组件代码、路由守卫、
 * 网络层）都能 add；渲染与动效归 SToastHost。
 */
import { createToastManager } from 'reka-ui'

/** 全局 toast 管理器。必须传给 SToastHost 的 ToastProvider，否则 add 出去没人接。 */
export const toastManager = createToastManager()

export type ToastStatus = 'info' | 'success' | 'error'

export interface ToastOptions {
  /** 语义状态，渲染成 `data-status`，决定左侧指示条颜色 */
  status?: ToastStatus
  /** 副文本（可选） */
  description?: string
  /** 自动消失毫秒数；0 表示不自动消失，需手动关闭 */
  duration?: number
  /** 同 id 重复 add 会就地更新并重置计时器（适合"正在保存…→ 已保存"） */
  id?: string
}

/** 默认停留时长，与旧实现保持一致（3600ms） */
const DEFAULT_DURATION = 3600

/**
 * 弹一条 toast。
 * 签名对旧代码向后兼容：`showToast('已保存')` 依然可用。
 */
export function showToast(text: string, options: ToastOptions = {}): string {
  return toastManager.add({
    id: options.id,
    title: text,
    description: options.description,
    status: options.status ?? 'info',
    duration: options.duration ?? DEFAULT_DURATION,
    // 用户操作触发的通知用 foreground（立即被读屏播报）
    type: 'foreground',
  })
}

/** 成功态（左侧绿色指示条） */
export function showSuccess(text: string, options: Omit<ToastOptions, 'status'> = {}): string {
  return showToast(text, { ...options, status: 'success' })
}

/** 失败态（左侧红色指示条） */
export function showError(text: string, options: Omit<ToastOptions, 'status'> = {}): string {
  return showToast(text, { ...options, status: 'error' })
}

/** 手动关掉某条（不传 id 则全部关闭） */
export function closeToast(id?: string): void {
  toastManager.close(id)
}
