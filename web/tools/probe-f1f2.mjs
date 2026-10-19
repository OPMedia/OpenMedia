/** Lead 复验 F1（焦点环）与 F2（hero 槽位顺序）。 */
const BASE = 'http://localhost:5173'

export default async function (cdp) {
  const out = {}

  // ---------- F2：把 hero 放到第 2 槽 ----------
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(3500)
  await cdp.eval(`
    localStorage.setItem('serata.themeMode', 'dark')
    localStorage.setItem('serata.homescreenSettings', JSON.stringify({
      homeSection1: 'resume', homeSection2: 'hero', homeSection3: 'nextup',
      homeSection4: 'latest', homeSection5: '', homeSection6: '', homeSection7: '',
    }))
    return 1
  `)
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(5000)

  out.slotOrder = await cdp.eval(`
    const hero = document.querySelector('.hero')
    const rows = [...document.querySelectorAll('.s-row')]
    const kids = [...document.querySelectorAll('.s-home-inner > *')].map(el => ({
      cls: el.className.toString().slice(0, 40),
      top: Math.round(el.getBoundingClientRect().top),
      title: (el.querySelector('.s-row-title') || {}).textContent || '',
    }))
    return {
      heroTop: hero ? Math.round(hero.getBoundingClientRect().top) : null,
      heroH: hero ? Math.round(hero.getBoundingClientRect().height) : null,
      firstRowTop: rows.length ? Math.round(rows[0].getBoundingClientRect().top) : null,
      firstRowTitle: rows.length ? rows[0].querySelector('.s-row-title')?.textContent : null,
      order: kids,
    }
  `)

  // 恢复默认槽位
  await cdp.eval(`localStorage.removeItem('serata.homescreenSettings'); return 1`)
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(5000)

  out.defaultOrder = await cdp.eval(`
    const hero = document.querySelector('.hero')
    const rows = [...document.querySelectorAll('.s-row')]
    return {
      heroTop: hero ? Math.round(hero.getBoundingClientRect().top) : null,
      heroW: hero ? Math.round(hero.getBoundingClientRect().width) : null,
      firstRowTitle: rows.length ? rows[0].querySelector('.s-row-title')?.textContent : null,
      titles: rows.map(r => r.querySelector('.s-row-title')?.textContent),
    }
  `)

  // ---------- F1：Tab 走一圈看焦点环 ----------
  out.focus = await cdp.eval(`document.body.focus(); return 1`)
  const stops = []
  for (let i = 0; i < 30; i++) {
    await cdp.send('Input.dispatchKeyEvent', { type: 'rawKeyDown', windowsVirtualKeyCode: 9, key: 'Tab', code: 'Tab' })
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', windowsVirtualKeyCode: 9, key: 'Tab', code: 'Tab' })
    await cdp.wait(60)
    const info = await cdp.eval(`
      const el = document.activeElement
      if (!el || el === document.body) return { stop: 'body' }
      const cs = getComputedStyle(el)
      return {
        tag: el.tagName,
        cls: (el.className || '').toString().slice(0, 46),
        fv: el.matches(':focus-visible'),
        ow: cs.outlineWidth, os: cs.outlineStyle, oc: cs.outlineColor,
        shadow: cs.boxShadow.slice(0, 60),
        text: (el.textContent || '').trim().slice(0, 18),
      }
    `)
    stops.push(info)
  }
  out.tabStops = stops
  out.tabSummary = {
    total: stops.filter((s) => s.tag && s.tag !== 'body').length,
    focusVisible: stops.filter((s) => s.fv).length,
    withOutline: stops.filter((s) => s.ow && s.ow !== '0px').length,
    withShadow: stops.filter((s) => s.shadow && s.shadow !== 'none').length,
    naked: stops.filter((s) => s.fv && (s.ow === '0px' || s.os === 'none') && (!s.shadow || s.shadow === 'none')).map((s) => s.cls),
  }
  out.shot = await cdp.screenshot('tools/shots/x-focus-check.png')
  return out
}
