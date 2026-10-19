/**
 * Serata 首页验收探针（深/浅双模 + 桌面/移动）。
 * 用法：
 *   1) 起 dev：  cd web && pnpm dev        (默认 http://localhost:5173)
 *   2) 起浏览器：msedge --headless=new --remote-debugging-port=9222 --user-data-dir=<tmp>
 *   3) 跑：      cd web && node tools/cdp.mjs tools/probe-serata.mjs
 * 输出 JSON：各视口的度量 + 截图路径 + 控制台错误。
 */
const BASE = process.env.SERATA_URL || 'http://localhost:5173'

const COLLECT_ERRORS = `
  window.__serataErrs = []
  window.addEventListener('error', e => window.__serataErrs.push('error: ' + (e.message || e.type)))
  window.addEventListener('unhandledrejection', e => window.__serataErrs.push('reject: ' + (e.reason && e.reason.message || e.reason)))
  const ce = console.error
  console.error = function (...a) { try { window.__serataErrs.push('console.error: ' + a.map(String).join(' ').slice(0, 300)) } catch {} ; return ce.apply(this, a) }
`

const MEASURE = `
  const q = (s) => document.querySelector(s)
  const hero = q('.s-hero') || q('[class*="hero"]')
  const header = q('.s-header') || q('.skinHeader')
  const nav = q('.s-bottom-nav')
  const libRows = [...document.querySelectorAll('[class*="s-lib"], .s-drawer a, .navMenuOption')]
  const heroImg = hero ? hero.querySelector('img') : null
  const ratio = hero ? +(hero.getBoundingClientRect().height / innerHeight).toFixed(3) : null
  return {
    url: location.pathname,
    theme: document.documentElement.className,
    bg: getComputedStyle(document.body).backgroundColor,
    headerH: header ? Math.round(header.getBoundingClientRect().height) : null,
    heroH: hero ? Math.round(hero.getBoundingClientRect().height) : null,
    heroRatio: ratio,
    heroHasImg: !!heroImg,
    heroPauseBtn: !!document.querySelector('[aria-label*="暂停"],[aria-label*="Pause"],[aria-label*="继续轮播"],[aria-label*="Resume"]'),
    heroSlides: document.querySelectorAll('[class*="slide"],[class*="dot"]').length,
    bottomNav: !!nav,
    bottomNavItems: nav ? nav.querySelectorAll('button,a').length : 0,
    premiereInDom: /Emby Premiere/i.test(document.body.innerText),
    metaManagerInDom: /元数据管理器|Metadata manager/i.test(document.body.innerText),
    metadataManagerNav: /metadatamanager/i.test(document.body.innerHTML),
    accentCount: (() => {
      // 统计页面里"使用强调色"的元素数（近似：命中 --s-accent 或 #E0705A/#D9604C 的元素）
      let n = 0
      const want = getComputedStyle(document.documentElement).getPropertyValue('--s-accent').trim().toLowerCase()
      for (const el of document.querySelectorAll('*')) {
        const cs = getComputedStyle(el)
        if (!cs || cs.display === 'none') continue
        const hit = [cs.color, cs.backgroundColor, cs.borderTopColor, cs.outlineColor].some(v => v && v.replace(/\\s/g,'') === want.replace(/\\s/g,''))
        if (hit) n++
      }
      return n
    })(),
    rows: [...document.querySelectorAll('[class*="s-row"] h2, [class*="s-row"] [class*="title"]')].slice(0, 8).map(e => e.textContent.trim()).filter(Boolean),
    errs: (window.__serataErrs || []).slice(0, 12),
  }
`

async function login(cdp) {
  await cdp.send('Page.navigate', { url: `${BASE}/login` })
  await cdp.wait(3500)
  const done = await cdp.eval(`
    const set = (el, v) => {
      const d = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')
      d.set.call(el, v)
      el.dispatchEvent(new Event('input', { bubbles: true }))
      el.dispatchEvent(new Event('change', { bubbles: true }))
    }
    const ins = [...document.querySelectorAll('input')]
    const user = ins.find(i => i.type === 'text' || i.type === 'email') || ins[0]
    const pw = ins.find(i => i.type === 'password')
    if (!user) return { ok: false, why: 'no input', path: location.pathname }
    set(user, 'demo')
    if (pw) set(pw, 'demo')
    const btn = [...document.querySelectorAll('button')].find(b => /登录|Sign in/i.test(b.textContent || ''))
    if (btn) btn.click()
    return { ok: true, inputs: ins.length, clicked: !!btn, path: location.pathname }
  `)
  await cdp.wait(4000)
  return done
}

export default async function (cdp) {
  const out = { base: BASE, steps: {} }

  await cdp.send('Page.addScriptToEvaluateOnNewDocument', { source: COLLECT_ERRORS })
  out.steps.login = await login(cdp)
  out.steps.afterLogin = await cdp.eval(`return { path: location.pathname, hasHeader: !!document.querySelector('.s-header,.skinHeader') }`)

  // ---- 桌面 · 深色 ----
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.eval(`localStorage.setItem('serata.themeMode','dark'); return 1`)
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(5000)
  out.steps.desktopDark = await cdp.eval(MEASURE)
  out.steps.desktopDark.shot = await cdp.screenshot('tools/shots/serata-desktop-dark.png')
  out.steps.desktopDark.fullShot = await (async () => {
    const m = await cdp.send('Page.getLayoutMetrics')
    const h = Math.min(Math.ceil(m.cssContentSize.height), 6000)
    await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: h, deviceScaleFactor: 1, mobile: false })
    await cdp.wait(1200)
    const p = await cdp.screenshot('tools/shots/serata-desktop-dark-full.png')
    await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
    await cdp.wait(400)
    return p
  })()

  // ---- 桌面 · 浅色 ----
  await cdp.eval(`localStorage.setItem('serata.themeMode','light'); return 1`)
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(4500)
  out.steps.desktopLight = await cdp.eval(MEASURE)
  out.steps.desktopLight.shot = await cdp.screenshot('tools/shots/serata-desktop-light.png')

  // ---- 设置页（浅色下验证外观选择器）----
  await cdp.send('Page.navigate', { url: `${BASE}/settings/display` })
  await cdp.wait(3500)
  out.steps.settingsDisplay = await cdp.eval(`
    const txt = document.body.innerText
    return {
      theme: document.documentElement.className,
      hasDark: /深色|Dark/.test(txt),
      hasLight: /浅色|Light/.test(txt),
      hasSystem: /跟随系统|System/.test(txt),
      hasAccentPicker: /强调色|Accent color/.test(txt),
      hasPremiere: /Premiere/i.test(txt),
      selectCount: document.querySelectorAll('select').length,
      shot: null,
    }
  `)
  out.steps.settingsDisplay.shot = await cdp.screenshot('tools/shots/serata-settings-display-light.png')

  // ---- 移动端 · 深色 ----
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true })
  await cdp.eval(`localStorage.setItem('serata.themeMode','dark'); return 1`)
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(4500)
  out.steps.mobileDark = await cdp.eval(MEASURE)
  out.steps.mobileDark.shot = await cdp.screenshot('tools/shots/serata-mobile-dark.png')

  // ---- 打开侧栏（验证媒体库行无图标）----
  out.steps.drawer = await cdp.eval(`
    const btns = [...document.querySelectorAll('button')]
    const open = btns.find(b => /打开导航|Open navigation|菜单|Menu/.test(b.getAttribute('aria-label') || ''))
    if (!open) return { err: 'no drawer button', labels: btns.map(b => b.getAttribute('aria-label')).filter(Boolean).slice(0, 20) }
    open.click()
    await new Promise(r => setTimeout(r, 700))
    const drawer = document.querySelector('[role="dialog"], .s-drawer')
    const rows = drawer ? [...drawer.querySelectorAll('a,button')] : []
    const libRow = rows.find(r => /电影|剧集|Movies|TV/.test(r.textContent || ''))
    return {
      drawerOpen: !!drawer,
      libRowText: libRow ? libRow.textContent.trim() : null,
      libRowHasImg: libRow ? !!libRow.querySelector('img,svg,i,span[class*="icon"]') : null,
      rowCount: rows.length,
    }
  `)
  out.steps.drawer.shot = await cdp.screenshot('tools/shots/serata-mobile-drawer.png')

  return out
}
