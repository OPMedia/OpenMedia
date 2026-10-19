/**
 * MSW 浏览器端启动(仅 mock;VITE_ENABLE_MOCK=false 时不加载本模块)。
 */
import { setupWorker } from 'msw/browser'
import { handlers } from './handlers'

export const worker = setupWorker(...handlers)