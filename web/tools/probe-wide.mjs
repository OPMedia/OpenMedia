export default async function (cdp) {
  const out = {}
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
  await cdp.wait(4500)
  out.wide = await cdp.eval(`
    const hero = document.querySelector('.hero')
    const inner = document.querySelector('.hero-inner')
    const frame = document.querySelector('.hero-frame-wrap')
    const r = hero.getBoundingClientRect()
    return { heroW: Math.round(r.width), heroH: Math.round(r.height), ratio: +(r.height / innerHeight).toFixed(3),
             innerW: Math.round(inner.getBoundingClientRect().width), innerLeft: Math.round(inner.getBoundingClientRect().left),
             frameW: frame ? Math.round(frame.getBoundingClientRect().width) : null,
             docScrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth }
  `)
  out.shot = await cdp.screenshot('tools/shots/serata-desktop-1920-dark.png')
  return out
}
