/** 复验 A（删选项）/ D（删通知）/ G（语言接 i18n）三件事。 */
const BASE = 'http://localhost:5173'

export default async function (cdp) {
  const out = {}
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(2500)
  await cdp.eval(`localStorage.setItem('serata.themeMode','dark'); localStorage.removeItem('serata.locale'); return 1`)

  const goto = async (path) => {
    await cdp.send('Page.navigate', { url: BASE + path })
    await cdp.wait(2600)
  }

  // ---------- A1: 显示页只剩外观 ----------
  await goto('/settings/display')
  out.display = await cdp.eval(`
    const main = document.querySelector('main')
    return {
      text: main.innerText.replace(/\\s+/g, ' ').trim(),
      selects: document.querySelectorAll('main select').length,
      toggles: document.querySelectorAll('main input[type=checkbox]').length,
      legends: [...document.querySelectorAll('main legend')].map(l => l.textContent.trim()),
    }
  `)

  // ---------- A2: 主页面只剩 7 个槽位 ----------
  await goto('/settings/homescreen')
  out.homescreen = await cdp.eval(`
    const main = document.querySelector('main')
    return {
      selects: document.querySelectorAll('main select').length,
      first: (main.innerText.replace(/\\s+/g,' ').trim()).slice(0, 120),
      hasTvHome: /电视模式|详情预览/.test(main.innerText),
      labels: [...document.querySelectorAll('main .selectLabelText')].map(e => e.textContent.trim()),
    }
  `)

  // ---------- A3: 槽位仍然生效（第 1 槽设空 → hero 消失，第 2 槽上移） ----------
  await goto('/')
  await cdp.eval(`
    localStorage.setItem('serata.homescreenSettings', JSON.stringify({
      homeSection1: '', homeSection2: 'resume', homeSection3: 'hero',
      homeSection4: 'nextup', homeSection5: 'latest', homeSection6: '', homeSection7: '',
    }))
    return 1
  `)
  await goto('/')
  out.slotWiring = await cdp.eval(`
    const kids = [...document.querySelectorAll('.s-home-inner > *')].map(el => ({
      cls: el.className.toString().slice(0, 30),
      top: Math.round(el.getBoundingClientRect().top),
      title: (el.querySelector('.s-row-title') || {}).textContent || '',
    }))
    return { order: kids }
  `)
  await cdp.eval(`localStorage.removeItem('serata.homescreenSettings'); return 1`)

  // ---------- D: 通知页/条目都没了 ----------
  await goto('/settings/notifications')
  out.notificationsGone = await cdp.eval(`return { path: location.pathname }`)
  await goto('/settings/display')
  out.nav = await cdp.eval(`
    return [...document.querySelectorAll('#s-nav-drawer .s-nav__row')].map(e => e.textContent.trim())
  `)

  // ---------- G: 语言真切换 ----------
  await goto('/settings/general')
  out.langBefore = await cdp.eval(`
    const sel = document.querySelector('select')
    return {
      options: [...sel.options].map(o => o.value + '=' + o.textContent.trim()),
      value: sel.value,
      htmlLang: document.documentElement.lang,
      firstLabel: document.querySelector('.selectLabelText').textContent.trim(),
    }
  `)

  out.languageSwitch = await cdp.eval(`
    const sel = document.querySelector('select')
    const set = (el, v) => { const d = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value'); d.set.call(el, v); el.dispatchEvent(new Event('change', { bubbles: true })) }
    set(sel, 'en')
    return { picked: sel.value }
  `)
  await cdp.wait(1200)
  out.langAfter = await cdp.eval(`
    return {
      htmlLang: document.documentElement.lang,
      stored: localStorage.getItem('serata.locale'),
      firstLabel: document.querySelector('.selectLabelText').textContent.trim(),
      legends: [...document.querySelectorAll('main legend')].map(l => l.textContent.trim()),
      navFirst: [...document.querySelectorAll('#s-nav-drawer .s-nav__row')].slice(0, 4).map(e => e.textContent.trim()),
    }
  `)
  out.shotEn = await cdp.screenshot('tools/shots/x-settings-en.png')

  // 切回中文
  await cdp.eval(`
    const sel = document.querySelector('select')
    const set = (el, v) => { const d = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value'); d.set.call(el, v); el.dispatchEvent(new Event('change', { bubbles: true })) }
    set(sel, 'zh-CN')
    return 1
  `)
  await cdp.wait(900)
  out.langBack = await cdp.eval(`
    return { htmlLang: document.documentElement.lang, stored: localStorage.getItem('serata.locale'), firstLabel: document.querySelector('.selectLabelText').textContent.trim() }
  `)
  out.shotDisplay = await cdp.screenshot('tools/shots/x-settings-display-trimmed.png')

  // ---------- 控制台错误 ----------
  out.consoleErrors = await cdp.eval(`return window.__probeErrs ? window.__probeErrs() : []`)
  return out
}
