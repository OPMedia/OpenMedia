/** 首页"继续观看"内容卡悬浮(完整 class 不截断) */
export default async function (cdp) {
  const out = {}
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
  await cdp.wait(6000)
  const g = await cdp.eval(`
    const rows = [...document.querySelectorAll('.card')]
    const card = rows.find(c => c.className.includes('backdropCard') && c.querySelector('.playstatebutton-icon'))
    if (!card) return { err: 'none' }
    card.scrollIntoView({ block: 'center' })
    await new Promise(r => setTimeout(r, 500))
    const r = card.getBoundingClientRect()
    return { cx: Math.round(r.x + r.width/2), cy: Math.round(r.y + r.height/2),
             name: card.querySelector('.cardText-first')?.textContent?.trim(), cls: card.className }
  `)
  out.pick = g
  if (g.err) return out
  await cdp.hover(g.cx, g.cy)
  await cdp.wait(800)
  out.hover = await cdp.eval(`
    const card = [...document.querySelectorAll('.card')].find(c => c.className.includes('backdropCard') && c.querySelector('.playstatebutton-icon'))
    const o = card.querySelector('.cardOverlayContainer')
    const btns = [...o.querySelectorAll('button')].map(b => { const r = b.getBoundingClientRect()
      return { cp: (b.textContent||'').trim().codePointAt(0)?.toString(16), title: b.title, action: b.getAttribute('data-action'),
               cls: b.className, size: Math.round(r.width)+'x'+Math.round(r.height) } })
    return { opacity: getComputedStyle(o).opacity, buttons: btns, checkbox: !!o.querySelector('.chkCardSelectContainer') }
  `)
  out.shot = await cdp.screenshot('tools/shots/final-home-row-hover.png')
  return out
}
