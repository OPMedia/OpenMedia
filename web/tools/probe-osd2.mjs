export default async function (cdp) {
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/play/105' })
  await cdp.wait(4000)
  await cdp.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: 700, y: 500 })
  await cdp.wait(700)
  return await cdp.eval(`
    const btns = [...document.querySelectorAll('.videoOsdBottom button, .videoOsdBottom .md-icon')].slice(0, 6)
    const info = btns.map(b => {
      const cs = getComputedStyle(b)
      return { tag: b.tagName, cls: (b.className||'').toString().slice(0,40), color: cs.color, op: cs.opacity, fill: cs.fill }
    })
    const bar = document.querySelector('.videoOsdBottom')
    const bcs = bar ? getComputedStyle(bar) : null
    return { info, bar: bcs ? { op: bcs.opacity, color: bcs.color, bgImg: bcs.backgroundImage.slice(0,80) } : null,
             textVar: getComputedStyle(document.querySelector('.s-osd')).getPropertyValue('--theme-text-color-hue') }
  `)
}
