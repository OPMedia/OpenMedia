export default async function (cdp) {
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/play/105' })
  await cdp.wait(4000)
  await cdp.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: 700, y: 500 })
  await cdp.wait(900)
  const st = await cdp.eval(`
    const bar = document.querySelector('.videoOsdBottom')
    const cs = bar ? getComputedStyle(bar) : null
    return { cls: document.documentElement.className, osdVisible: document.querySelector('.osdVisible') !== null,
             barOpacity: cs ? cs.opacity : null, barBg: cs ? cs.backgroundColor : null }
  `)
  st.shot = await cdp.screenshot('tools/shots/x-light-osd-visible.png')
  return st
}
