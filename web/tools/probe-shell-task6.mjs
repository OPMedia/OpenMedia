/**
 * task-6 验收探针：路由转场 + 全局 toast 宿主 + 壳层层级 + 列表 FLIP + 深浅双主题。
 *
 * 用法（端口/ профиль 必须用自己的，见 Lead 的协作纪律）：
 *   $env:CDP_PORT = "9223"
 *   node tools/cdp.mjs tools/probe-shell-task6.mjs
 *
 * 证据口径：
 *   · 转场不是看"有没有 class"，而是监听 animationstart 拿到 s-page-in/s-fade-out 的真实
 *     animationName + 时长，并在转场过程中采样进入元素的 computed opacity/transform；
 *   · toast 走真实模块单例（import('/src/api/toast.ts') → showToast），再数 [data-toast-id]；
 *   · 层级读 computed z-index，并验证抽屉没有被遮罩盖住（left>=0 + visibility:visible）。
 */
const BASE = process.env.SERATA_URL || 'http://localhost:5173'

const COLLECT = `
  window.__errs = []
  window.__anims = []
  const rec = (kind) => (...a) => {
    try {
      window.__errs.push(kind + ': ' + a.map(x => (typeof x === 'string' ? x : (() => { try { return JSON.stringify(x) } catch { return String(x) } })())).join(' ').slice(0, 400))
    } catch {}
  }
  const ce = console.error.bind(console)
  const cw = console.warn.bind(console)
  console.error = (...a) => { rec('console.error')(...a); return ce(...a) }
  console.warn = (...a) => { rec('console.warn')(...a); return cw(...a) }
  window.addEventListener('error', e => rec('window.error')(e.message || e.type))
  window.addEventListener('unhandledrejection', e => rec('unhandledrejection')(String((e.reason && e.reason.message) || e.reason)))
  document.addEventListener('animationstart', (e) => {
    try {
      const t = e.target
      const cls = typeof t.className === 'string' ? t.className.split(' ')[0] : ''
      window.__anims.push({
        name: e.animationName,
        el: (t.tagName || '?').toLowerCase() + (cls ? '.' + cls : ''),
        dur: getComputedStyle(t).animationDuration,
      })
    } catch {}
  }, true)
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
    return { ok: true, clicked: !!btn }
  `)
  await cdp.wait(4000)
  return done
}

const errs = `
  return (window.__errs || []).slice(0, 14)
`

export default async function (cdp) {
  const out = { base: BASE, steps: {} }
  await cdp.send('Page.addScriptToEvaluateOnNewDocument', { source: COLLECT })
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })

  out.steps.login = await login(cdp)

  // ---------------- 1. 桌面 · 深色：路由转场 ----------------
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(4500)
  out.steps.boot = await cdp.eval(`
    return {
      path: location.pathname,
      theme: document.documentElement.className,
      animsAtBoot: (window.__anims || []).slice(0, 8),
      toastHost: {
        provider: !!document.querySelector('[data-reka-toast-provider], [role="region"][aria-label]'),
        listRegion: !!document.querySelector('[data-toast-id]') || !!document.querySelector('ol[tabindex]'),
      },
      ink: (() => {
        const el = document.querySelector('.s-tabs__ink')
        if (!el) return null
        return { visible: el.classList.contains('is-visible'), transform: el.style.transform, w: Math.round(el.getBoundingClientRect().width) }
      })(),
      errs: (window.__errs || []).slice(0, 14),
    }
  `)

  // 点顶栏搜索 → 观察 s-fade-out(离场 140ms) + s-page-in(进场 320ms) 与进入元素的 opacity/transform 采样
  out.steps.transition = await cdp.eval(`
    window.__anims.length = 0
    const btn = [...document.querySelectorAll('.s-header__right button')]
      .find(b => /搜索|Search/i.test(b.getAttribute('aria-label') || ''))
    if (!btn) return { err: 'no search button', labels: [...document.querySelectorAll('.s-header__right button')].map(b => b.getAttribute('aria-label')) }
    const from = location.pathname
    btn.click()
    const samples = []
    for (let i = 0; i < 26; i++) {
      await new Promise(r => setTimeout(r, 35))
      const el = document.querySelector('.s-page-enter-active')
      if (el) samples.push({ o: +getComputedStyle(el).opacity, tf: getComputedStyle(el).transform })
    }
    await new Promise(r => setTimeout(r, 500))
    return {
      from, to: location.pathname,
      anims: (window.__anims || []).slice(0, 10),
      enterSamples: samples.filter((_, i) => i % 3 === 0).slice(0, 6),
      minOpacity: samples.length ? Math.min(...samples.map(s => s.o)) : null,
      sawTranslate: samples.some(s => /matrix/.test(s.tf) && !/matrix\\(1, 0, 0, 1, 0, 0\\)/.test(s.tf)),
      errs: (window.__errs || []).slice(0, 14),
    }
  `)

  // ---------------- 2. toast 宿主（真模块单例） ----------------
  out.steps.toast = await cdp.eval(`
    const mod = await import('/src/api/toast.ts')
    const id = mod.showToast('task-6 验收 toast')
    await new Promise(r => setTimeout(r, 600))
    const nodes = [...document.querySelectorAll('[data-toast-id]')]
    const first = nodes[0] || null
    const cs = first ? getComputedStyle(first) : null
    return {
      id,
      count: nodes.length,
      ids: nodes.map(n => n.getAttribute('data-toast-id')),
      text: first ? (first.textContent || '').trim().slice(0, 60) : null,
      opacity: cs ? cs.opacity : null,
      animated: !!document.querySelector('[data-toast-id] *'),
      errs: (window.__errs || []).slice(0, 14),
    }
  `)
  out.steps.toast.shot = await cdp.screenshot('tools/shots/task6-toast-dark.png')

  // ---------------- 2b. toast 真的被样式化了吗（位置/尺寸/底色，不只数节点） ----------------
  // 教训（Lead 用 9229 抓到）：断言"节点存在 + 有文本 + opacity=1"证明不了组件可用。
  // scoped 样式在 reka Primitive 转发的元素上会全部空转，toast 会退化成文档流顶部一条
  // 1400px 宽的透明裸列表，而全局动画照常跑，正好伪装成"一切正常"。
  // 所以这里断言 computed style 的关键项：position / z-index / 宽度 / 是否落在右下角 / 底色。
  const TOAST_STYLE_PROBE = `
    const mod = await import('/src/api/toast.ts')
    mod.closeToast()
    await new Promise(r => setTimeout(r, 250))
    mod.showToast('info 态 toast')
    mod.showError('error 态 toast')
    mod.showSuccess('success 态 toast')
    await new Promise(r => setTimeout(r, 800))
    const vp = document.querySelector('.s-toast__viewport')
    const vcs = vp ? getComputedStyle(vp) : null
    const vr = vp ? vp.getBoundingClientRect() : null
    const items = [...document.querySelectorAll('[data-toast-id]')].map(li => {
      const cs = getComputedStyle(li)
      const r = li.getBoundingClientRect()
      const mk = li.querySelector('.s-toast__marker')
      return {
        cls: li.className,
        status: li.getAttribute('data-status'),
        pos: cs.position,
        bg: cs.backgroundColor,
        color: cs.color,
        radius: cs.borderRadius,
        shadow: cs.boxShadow === 'none' ? 'none' : 'has-shadow',
        w: Math.round(r.width), h: Math.round(r.height),
        inViewport: !!(vr && r.left >= vr.left - 1 && r.right <= vr.right + 1),
        rightGap: Math.round(innerWidth - r.right),
        bottomGap: Math.round(innerHeight - r.bottom),
        marker: mk ? getComputedStyle(mk).backgroundColor : null,
      }
    })
    const vpInfo = vp ? {
      pos: vcs.position, z: vcs.zIndex, width: Math.round(vr.width),
      rightGap: Math.round(innerWidth - vr.right), bottomGap: Math.round(innerHeight - vr.bottom),
      pointerEvents: vcs.pointerEvents,
    } : null
    const errMarker = (items.find(i => /s-toast--error/.test(i.cls)) || {}).marker
    const okMarker = (items.find(i => /s-toast--success/.test(i.cls)) || {}).marker
    const infoMarker = (items.find(i => /s-toast--info/.test(i.cls)) || {}).marker
    // 语义色深浅两档不同（深色档用更亮的红/绿），所以按"红/绿主色相 + 三色互异"断言，而不是写死 rgb
    const rgb = (s) => ((s || '').match(/\\d+/g) || []).map(Number)
    const errRgb = rgb(errMarker), okRgb = rgb(okMarker)
    return {
      theme: document.documentElement.className,
      viewport: vpInfo,
      count: items.length,
      items,
      markers: { info: infoMarker, error: errMarker, success: okMarker },
      checks: {
        viewportFixed: !!vpInfo && vpInfo.pos === 'fixed',
        viewportZ1200: !!vpInfo && vpInfo.z === '1200',
        viewportWidth380: !!vpInfo && Math.abs(vpInfo.width - 380) <= 1,
        viewportBottomRight: !!vpInfo && vpInfo.rightGap >= 0 && vpInfo.rightGap <= 24 && vpInfo.bottomGap >= 0 && vpInfo.bottomGap <= 24,
        everyToastOpaque: items.length > 0 && items.every(i => i.bg !== 'rgba(0, 0, 0, 0)'),
        everyToastInViewport: items.length > 0 && items.every(i => i.inViewport),
        everyToastRounded: items.length > 0 && items.every(i => parseFloat(i.radius) > 0),
        everyToastHasShadow: items.length > 0 && items.every(i => i.shadow === 'has-shadow'),
        errorIsRed: errRgb.length >= 3 && errRgb[0] > errRgb[1] && errRgb[0] > errRgb[2],
        successIsGreen: okRgb.length >= 3 && okRgb[1] > okRgb[0] && okRgb[1] > okRgb[2],
        threeDistinctMarkers: new Set([infoMarker, errMarker, okMarker]).size === 3,
      },
      errs: (window.__errs || []).slice(0, 14),
    }
  `
  out.steps.toastStyled = await cdp.eval(TOAST_STYLE_PROBE)
  out.steps.toastStyled.shot = await cdp.screenshot('tools/shots/task6-toast-styled-dark.png')

  // ---------------- 3. 深→浅：主题按钮（真按钮，不直接改 <html> class） ----------------
  out.steps.themeToggle = await cdp.eval(`
    const before = document.documentElement.className
    const btn = [...document.querySelectorAll('.s-header__right button')]
      .find(b => /切换|深浅|主题|theme/i.test(b.getAttribute('aria-label') || ''))
    if (!btn) return { err: 'no theme button', labels: [...document.querySelectorAll('.s-header__right button')].map(b => b.getAttribute('aria-label')) }
    btn.click()
    await new Promise(r => setTimeout(r, 600))
    const afterOne = document.documentElement.className
    const shotState = { resolvedLight: /theme-light/.test(afterOne), ls: localStorage.getItem('serata.themeMode') }
    return { before, label: btn.getAttribute('aria-label'), afterOne, ...shotState }
  `)
  await cdp.wait(400)
  out.steps.lightHomeShot = await cdp.screenshot('tools/shots/task6-light-home.png')

  // 浅色档同样要量 toast 的样式化（不只看节点存在）
  out.steps.toastStyledLight = await cdp.eval(TOAST_STYLE_PROBE)
  out.steps.toastStyledLight.shot = await cdp.screenshot('tools/shots/task6-toast-styled-light.png')

  // 浅色下再走一次真跨路由（在 /search，点侧栏「首页」行）确认浅色档同样走 s-page 类族
  out.steps.lightTransition = await cdp.eval(`
    window.__anims.length = 0
    const homeRow = [...document.querySelectorAll('.s-drawer .s-nav__row')].find(r => /^首页$/.test((r.textContent || '').trim()))
    if (!homeRow) return { err: 'no home row in drawer' }
    homeRow.click()
    await new Promise(r => setTimeout(r, 1200))
    return { to: location.pathname, theme: document.documentElement.className, anims: (window.__anims || []).slice(0, 6), errs: (window.__errs || []).slice(0, 14) }
  `)

  // 浅色：设置页（docked 语境）
  await cdp.send('Page.navigate', { url: `${BASE}/settings/display` })
  await cdp.wait(3000)
  out.steps.lightSettings = await cdp.eval(`
    return { path: location.pathname, theme: document.documentElement.className, errs: (window.__errs || []).slice(0, 14) }
  `)
  out.steps.lightSettings.shot = await cdp.screenshot('tools/shots/task6-light-settings.png')

  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(4000)
  out.steps.lightHome = await cdp.eval(`
    const ink = document.querySelector('.s-tabs__ink')
    return {
      theme: document.documentElement.className,
      inkVisible: !!ink && ink.classList.contains('is-visible'),
      inkTransform: ink ? ink.style.transform : null,
      errs: (window.__errs || []).slice(0, 14),
    }
  `)
  out.steps.inkMove = await cdp.eval(`
    window.__anims.length = 0
    const t = [...document.querySelectorAll('.s-tab')].find(b => /收藏|Favorites/i.test(b.textContent || ''))
    if (!t) return { err: 'no favorites tab' }
    t.click()
    await new Promise(r => setTimeout(r, 700))
    const ink = document.querySelector('.s-tabs__ink')
    return { url: location.search, inkTransform: ink ? ink.style.transform : null, anims: (window.__anims || []).slice(0, 6) }
  `)
  out.steps.lightHome.shot = await cdp.screenshot('tools/shots/task6-light-home.png')

  // ---------------- 4. 移动端：底栏 + 抽屉层级 ----------------
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true })
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(4500)
  out.steps.mobile = await cdp.eval(`
    const nav = document.querySelector('.s-bottom-nav')
    return {
      theme: document.documentElement.className,
      bottomNav: !!nav,
      items: nav ? nav.querySelectorAll('button').length : 0,
      labels: nav ? [...nav.querySelectorAll('.s-bottom-nav__label')].map(e => e.textContent.trim()) : [],
      zIndex: nav ? getComputedStyle(nav).zIndex : null,
      enterAnim: (window.__anims || []).find(a => a.name === 's-up-in') || null,
      errs: (window.__errs || []).slice(0, 14),
    }
  `)
  out.steps.mobile.shot = await cdp.screenshot('tools/shots/task6-mobile-home.png')

  out.steps.drawer = await cdp.eval(`
    window.__anims.length = 0
    const open = [...document.querySelectorAll('button')].find(b => /媒体库|Libraries/i.test((b.querySelector('.s-bottom-nav__label') || {}).textContent || ''))
    if (!open) return { err: 'no library nav item' }
    open.click()
    await new Promise(r => setTimeout(r, 520))
    const d = document.querySelector('.s-drawer')
    const scrim = document.querySelector('.s-backdrop')
    if (!d) return { err: 'no drawer' }
    const cs = getComputedStyle(d)
    const r = d.getBoundingClientRect()
    const hdr = document.querySelector('.s-header').getBoundingClientRect()
    const head = document.querySelector('.s-drawer__head')
    const brand = head ? head.querySelector('.s-brand') : null
    return {
      theme: document.documentElement.className,
      isOpen: d.classList.contains('is-open'),
      drawerZ: cs.zIndex,
      scrimZ: scrim ? getComputedStyle(scrim).zIndex : null,
      scrimPresent: !!scrim,
      visibility: cs.visibility,
      left: Math.round(r.left),
      width: Math.round(r.width),
      paddingBottom: cs.paddingBottom,
      /* 覆盖式抽屉必须从顶栏下沿开始（层级 60 < 顶栏 70），且不重复顶栏品牌 */
      drawerTop: Math.round(r.top),
      headerBottom: Math.round(hdr.bottom),
      headHeight: head ? Math.round(head.getBoundingClientRect().height) : null,
      brandInHead: brand ? getComputedStyle(brand).display : null,
      closeBtnVisible: (() => {
        const b = document.querySelector('.s-drawer__close')
        return b ? getComputedStyle(b).display !== 'none' : null
      })(),
      libRows: [...d.querySelectorAll('.s-nav__row--lib')].map(x => x.textContent.trim()),
      drawerRows: d.querySelectorAll('.s-nav__row').length,
      /* 位置/尺寸/底色断言（不只"节点存在"） */
      drawerPos: cs.position,
      drawerBg: cs.backgroundColor,
      drawerRadius: cs.borderRadius,
      scrimPos: scrim ? getComputedStyle(scrim).position : null,
      scrimBg: scrim ? getComputedStyle(scrim).backgroundColor : null,
      scrimCoversViewport: scrim ? (() => { const r = scrim.getBoundingClientRect(); return Math.abs(r.width - innerWidth) <= 1 && Math.abs(r.height - innerHeight) <= 1 })() : null,
      scrimAnim: (window.__anims || []).find(a => a.name === 's-fade-in') || null,
      errs: (window.__errs || []).slice(0, 14),
    }
  `)
  out.steps.drawer.shot = await cdp.screenshot('tools/shots/task6-mobile-drawer.png')

  // 抽屉里点一个库 → 关抽屉 + 路由转场
  out.steps.drawerNav = await cdp.eval(`
    window.__anims.length = 0
    const row = document.querySelector('.s-drawer .s-nav__row--lib')
    if (!row) return { err: 'no lib row' }
    row.click()
    await new Promise(r => setTimeout(r, 1400))
    return {
      theme: document.documentElement.className,
      path: location.pathname,
      drawerOpen: document.querySelector('.s-drawer').classList.contains('is-open'),
      anims: (window.__anims || []).slice(0, 8),
      errs: (window.__errs || []).slice(0, 14),
    }
  `)
  out.steps.drawerNav.shot = await cdp.screenshot('tools/shots/task6-mobile-library.png')

  // ---------------- 5. 移动端 · 深色：抽屉再验一遍（双主题各一遍） ----------------
  out.steps.mobileDark = await cdp.eval(`
    const btn = [...document.querySelectorAll('.s-header__right button')]
      .find(b => /切换|深浅|主题|theme/i.test(b.getAttribute('aria-label') || ''))
    if (!btn) return { err: 'no theme button' }
    btn.click()
    await new Promise(r => setTimeout(r, 600))
    return { theme: document.documentElement.className, ls: localStorage.getItem('serata.themeMode') }
  `)
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(4000)
  out.steps.drawerDark = await cdp.eval(`
    const nav = document.querySelector('.s-bottom-nav')
    const lib = [...(nav ? nav.querySelectorAll('button') : [])].find(b => /媒体库|Libraries/i.test((b.querySelector('.s-bottom-nav__label') || {}).textContent || ''))
    if (!lib) return { err: 'no library nav item' }
    window.__anims.length = 0
    lib.click()
    await new Promise(r => setTimeout(r, 520))
    const d = document.querySelector('.s-drawer')
    const cs = getComputedStyle(d)
    return {
      theme: document.documentElement.className,
      isOpen: d.classList.contains('is-open'),
      drawerZ: cs.zIndex,
      scrimZ: document.querySelector('.s-backdrop') ? getComputedStyle(document.querySelector('.s-backdrop')).zIndex : null,
      visibility: cs.visibility,
      left: Math.round(d.getBoundingClientRect().left),
      scrimAnim: (window.__anims || []).find(a => a.name === 's-fade-in') || null,
      errs: (window.__errs || []).slice(0, 14),
    }
  `)
  out.steps.drawerDark.shot = await cdp.screenshot('tools/shots/task6-mobile-drawer-dark.png')

  // 抽屉关闭：退场比进场快（读 .s-drawer 的 computed transition-duration）
  out.steps.drawerClose = await cdp.eval(`
    const d = document.querySelector('.s-drawer')
    const openDur = getComputedStyle(d).transitionDuration
    const close = [...document.querySelectorAll('.s-drawer__close, .s-header__right button, button')]
      .find(b => /关闭导航|Close navigation|关闭|Close/i.test(b.getAttribute('aria-label') || ''))
    if (close) close.click()
    await new Promise(r => setTimeout(r, 700))
    const cs = getComputedStyle(d)
    return {
      openStateTransition: openDur,
      closedStateTransition: cs.transitionDuration,
      closedEasing: cs.transitionTimingFunction,
      isOpen: d.classList.contains('is-open'),
      transform: cs.transform,
      visibility: cs.visibility,
      scrimPresent: !!document.querySelector('.s-backdrop'),
    }
  `)

  return out
}
