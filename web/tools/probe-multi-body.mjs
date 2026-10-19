/** 多选态下点卡片图片空白区(右上,避开所有按钮)→ 应取消选中且不跳转 */
export default async function (cdp) {
  const out = {}
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/library/100' })
  await cdp.wait(5000)
  const g = await cdp.eval(`
    const card = document.querySelector('main .itemsContainer .card')
    const r = card.getBoundingClientRect()
    const lr = card.querySelector('.chkCardSelectContainer').getBoundingClientRect()
    return { cx: Math.round(r.x + r.width/2), cy: Math.round(r.y + r.height/2),
             bx: Math.round(lr.x + lr.width/2), by: Math.round(lr.y + lr.height/2),
             blankX: Math.round(r.x + r.width - 45), blankY: Math.round(r.y + 45) }
  `)
  out.geom = g
  await cdp.hover(g.cx, g.cy); await cdp.wait(400)
  out.hitBlank = await cdp.eval(`
    const el = document.elementFromPoint(${g.blankX}, ${g.blankY})
    return el?.tagName + '.' + (el?.className?.toString?.() || '').slice(0, 70)
  `)
  await cdp.click(g.bx, g.by); await cdp.wait(700)
  out.selected = await cdp.eval(`return { html: document.documentElement.className.includes('multi-select-active'),
    cardSel: document.querySelector('main .itemsContainer .card').className.includes('item-multiselected') }`)
  out.shotOn = await cdp.screenshot('tools/shots/final-multi-selected.png')

  await cdp.click(g.blankX, g.blankY); await cdp.wait(1000)
  out.afterBlankClick = await cdp.eval(`
    const card = document.querySelector('main .itemsContainer .card')
    return { path: location.pathname, html: document.documentElement.className.includes('multi-select-active'),
             cardSel: card ? card.className.includes('item-multiselected') : null }`)
  out.shotOff = await cdp.screenshot('tools/shots/final-multi-deselected.png')
  return out
}
