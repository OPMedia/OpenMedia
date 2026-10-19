/** i18n(SPEC/web §7:默认 zh-CN,文案全走资源文件,禁止硬编码进组件) */
import { createI18n } from 'vue-i18n'
import zhCN from './locales/zh-CN.json'
import en from './locales/en.json'

export const i18n = createI18n({
  legacy: false,
  locale: localStorage.getItem('serata.locale') ?? 'zh-CN',
  fallbackLocale: 'en',
  messages: { 'zh-CN': zhCN, en },
})

export function setLocale(locale: 'zh-CN' | 'en'): void {
  i18n.global.locale.value = locale
  localStorage.setItem('serata.locale', locale)
  document.documentElement.lang = locale
}