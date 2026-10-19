export default async function (cdp) {
  const out = {}
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
  await cdp.wait(3500)
  await cdp.eval(`localStorage.setItem('serata.themeMode','light'); return 1`)
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
  await cdp.wait(5000)
  out.glow = await cdp.eval(`
    const g = document.querySelector('.hero-glow')
    const gi = document.querySelector('.hero-glow-img')
    const root = document.querySelector('.s-hero') || document.querySelector('[class*=hero]')
    const layers = [...(root ? root.children : [])].slice(0, 12).map(e => {
      const cs = getComputedStyle(e); const r = e.getBoundingClientRect()
      return { cls: (e.className||'').toString().slice(0,50), rect: [Math.round(r.x),Math.round(r.y),Math.round(r.width),Math.round(r.height)],
               bg: cs.backgroundColor, bgImg: cs.backgroundImage.slice(0,50), op: cs.opacity, filt: cs.filter, z: cs.zIndex, pos: cs.position }
    })
    return {
      theme: document.documentElement.className,
      glowExists: !!g,
      glow: g ? { bg: getComputedStyle(g).backgroundImage.slice(0,120), op: getComputedStyle(g).opacity, filt: getComputedStyle(g).filter, rect: (() => { const r = g.getBoundingClientRect(); return [Math.round(r.x),Math.round(r.y),Math.round(r.width),Math.round(r.height)] })() } : null,
      glowImgExists: !!gi,
      glowImg: gi ? { op: getComputedStyle(gi).opacity, bgImg: getComputedStyle(gi).backgroundImage.slice(0,140), rect: (() => { const r = gi.getBoundingClientRect(); return [Math.round(r.x),Math.round(r.y),Math.round(r.width),Math.round(r.height)] })() } : null,
      layers,
    }
  `)
  out.shotBase = await cdp.screenshot('tools/shots/x-light-base.png')
  await cdp.eval(`document.querySelectorAll('.hero-glow,.hero-glow-img').forEach(e => e.remove()); return 1`)
  await cdp.wait(500)
  out.shotNoGlow = await cdp.screenshot('tools/shots/x-light-noglow.png')
  return out
}
