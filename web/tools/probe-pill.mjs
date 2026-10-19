export default async function (cdp) {
  const out = {}
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
  await cdp.wait(5000)
  out.hit = await cdp.eval(`
    const sc = document.querySelector('main.s-home') || document.scrollingElement
    if (sc && sc.scrollTop === 0) { sc.scrollTop = 1000; await new Promise(r => setTimeout(r, 900)) }
    const hits = []
    for (const el of document.querySelectorAll('body *')) {
      const r = el.getBoundingClientRect()
      if (r.width < 20 || r.height < 10) continue
      if (r.left > 600 && r.left < 860 && r.top > 830 && r.top < 900) {
        hits.push({ tag: el.tagName, cls: (el.className||'').toString().slice(0,90),
                    rect: [Math.round(r.x),Math.round(r.y),Math.round(r.width),Math.round(r.height)],
                    pos: getComputedStyle(el).position, z: getComputedStyle(el).zIndex,
                    html: el.outerHTML.slice(0, 300) })
      }
    }
    return hits.slice(-6)
  `)
  out.scrollBtns = await cdp.eval(`
    const sel = '.scrollButtons, .paging, .btnScrollLeft, .btnScrollRight, [class*="paging"], [class*="scrollButton"]'
    return [...document.querySelectorAll(sel)].slice(0, 10).map(e => ({ cls: e.className.toString().slice(0,90), rect: (() => { const r = e.getBoundingClientRect(); return [Math.round(r.x),Math.round(r.y),Math.round(r.width),Math.round(r.height)] })(), disp: getComputedStyle(e).display, op: getComputedStyle(e).opacity }))
  `)
  out.scrollbarCSS = await cdp.eval(`
    const sc = document.querySelector('main.s-home')
    return { cls: sc.className, overflowY: getComputedStyle(sc).overflowY, scrollbarWidth: getComputedStyle(sc).scrollbarWidth, width: sc.offsetWidth - sc.clientWidth }
  `)
  return out
}
