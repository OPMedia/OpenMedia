/** Serata 诊断探针：滚动容器 / 强调色计数 / 行区截图 / 移动端固定层与 hero 画框。 */
const BASE = process.env.SERATA_URL || 'http://localhost:5173'

export default async function (cdp) {
  const out = {}
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(5000)

  out.scrollers = await cdp.eval(`
    const cands = [...document.querySelectorAll('*')].filter(e => {
      const cs = getComputedStyle(e)
      return /(auto|scroll)/.test(cs.overflowY) && e.scrollHeight > e.clientHeight + 20
    })
    return cands.slice(0, 8).map(e => ({ tag: e.tagName, cls: e.className.toString().slice(0, 140), sh: e.scrollHeight, ch: e.clientHeight }))
  `)

  out.accent = await cdp.eval(`
    const raw = getComputedStyle(document.documentElement).getPropertyValue('--s-accent').trim()
    const h = raw.replace('#', '')
    const want = 'rgb(' + parseInt(h.slice(0,2),16) + ', ' + parseInt(h.slice(2,4),16) + ', ' + parseInt(h.slice(4,6),16) + ')'
    const colorHits = [], bgHits = [], borderHits = []
    for (const el of document.querySelectorAll('body *')) {
      const cs = getComputedStyle(el)
      if (cs.display === 'none' || cs.visibility === 'hidden' || +cs.opacity === 0) continue
      const r = el.getBoundingClientRect()
      if (r.width < 2 || r.height < 2) continue
      const name = (el.className && el.className.toString().slice(0, 50)) || el.tagName
      if (cs.color === want) colorHits.push(name)
      if (cs.backgroundColor === want) bgHits.push(name)
      if (cs.borderTopColor === want || cs.borderLeftColor === want) borderHits.push(name)
    }
    return { raw, want, color: colorHits.slice(0, 12), bg: bgHits.slice(0, 12), border: borderHits.slice(0, 12),
             counts: { color: colorHits.length, bg: bgHits.length, border: borderHits.length } }
  `)

  out.scrolled = await cdp.eval(`
    const sc = [...document.querySelectorAll('*')].find(e => {
      const cs = getComputedStyle(e)
      return /(auto|scroll)/.test(cs.overflowY) && e.scrollHeight > e.clientHeight + 20
    })
    if (!sc) return { err: 'no scroller' }
    sc.scrollTop = 1000
    await new Promise(r => setTimeout(r, 1000))
    const heads = [...document.querySelectorAll('h1,h2,h3,[class*="rowTitle"],[class*="s-row-title"]')].map(h => h.textContent.trim()).filter(Boolean)
    return { scrollTop: sc.scrollTop, cls: sc.className.toString().slice(0, 90), heads }
  `)
  out.rowShot = await cdp.screenshot('tools/shots/serata-desktop-rows.png')

  // ---- 移动端 ----
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true })
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(4500)

  out.mobileFixed = await cdp.eval(`
    const vh = innerHeight
    const list = [...document.querySelectorAll('body *')].filter(e => {
      const cs = getComputedStyle(e)
      if (cs.position !== 'fixed' && cs.position !== 'sticky') return false
      const r = e.getBoundingClientRect()
      return r.height > 8 && r.width > 8 && r.bottom > vh - 220 && r.top > vh * 0.55
    })
    return list.map(e => {
      const r = e.getBoundingClientRect()
      return { tag: e.tagName, cls: (e.className && e.className.toString().slice(0, 100)) || '',
               rect: [Math.round(r.x), Math.round(r.y), Math.round(r.width), Math.round(r.height)],
               z: getComputedStyle(e).zIndex, txt: (e.textContent || '').trim().slice(0, 40) }
    })
  `)

  out.mobileHero = await cdp.eval(`
    const hero = document.querySelector('.s-hero, [class*="s-hero"]')
    if (!hero) return { err: 'no hero' }
    const hr = hero.getBoundingClientRect()
    const imgs = [...hero.querySelectorAll('img')].map(i => {
      const r = i.getBoundingClientRect()
      return { tail: i.currentSrc.slice(-36), rect: [Math.round(r.x), Math.round(r.y), Math.round(r.width), Math.round(r.height)], op: getComputedStyle(i).opacity }
    })
    const frame = hero.querySelector('[class*="frame"], [class*="rotator"], [class*="stage"], [class*="media"]')
    let frameRect = null
    if (frame) { const r = frame.getBoundingClientRect(); frameRect = [Math.round(r.x), Math.round(r.y), Math.round(r.width), Math.round(r.height)] }
    return { heroRect: [Math.round(hr.x), Math.round(hr.y), Math.round(hr.width), Math.round(hr.height)],
             frameCls: frame ? frame.className.toString().slice(0, 90) : null, frameRect, imgs }
  `)
  out.mobileShot = await cdp.screenshot('tools/shots/serata-mobile-diag.png')
  return out
}
