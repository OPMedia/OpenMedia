/** 详情页分集卡悬浮层验证(同款图标 + 交互) */
export default async function (cdp) {
  const out = {}
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/item/301' })
  await cdp.wait(6000)

  const g = await cdp.eval(`
    const cards = [...document.querySelectorAll('main .itemsContainer .card.backdropCard')]
    const card = cards[0]
    if (!card) return { err: 'no episode card', n: cards.length }
    const r = card.getBoundingClientRect()
    const o = card.querySelector('.cardOverlayContainer')
    return { n: cards.length, cx: Math.round(r.x + r.width/2), cy: Math.round(r.y + r.height/2),
             hasOverlay: !!o }
  `)
  out.geom = g
  if (g.err) return out

  await cdp.hover(g.cx, g.cy)
  await cdp.wait(600)
  out.hover = await cdp.eval(`
    const card = [...document.querySelectorAll('main .itemsContainer .card.backdropCard')][0]
    const o = card.querySelector('.cardOverlayContainer')
    const btns = [...o.querySelectorAll('button')].map((b) => {
      const r = b.getBoundingClientRect()
      return { cp: (b.textContent || '').trim().codePointAt(0)?.toString(16), title: b.title, action: b.getAttribute('data-action'),
               cls: b.className.slice(0, 120), x: Math.round(r.x + r.width/2), y: Math.round(r.y + r.height/2), size: Math.round(r.width) + 'x' + Math.round(r.height) }
    })
    return { opacity: getComputedStyle(o).opacity, buttons: btns }
  `)
  out.shotHover = await cdp.screenshot('tools/shots/detail-episode-hover.png')

  // 点已播按钮 → 状态
  const played = out.hover.buttons.find((b) => b.cls.includes('playstatebutton'))
  await cdp.click(played.x, played.y)
  await cdp.wait(900)
  out.playedOn = await cdp.eval(`
    const b = [...document.querySelectorAll('main .itemsContainer .card.backdropCard .cardOverlayButton-br button')].find(x => x.className.includes('playstatebutton'))
    return { cls: b.className.slice(0, 140), color: getComputedStyle(b).color, title: b.title, dataPlayed: b.getAttribute('data-played') }
  `)
  out.shotPlayed = await cdp.screenshot('tools/shots/detail-episode-played.png')

  // 收藏
  const fav = out.hover.buttons.find((b) => b.cls.includes('md-icon fab') && !b.cls.includes('playstatebutton') && b.action === 'none')
  await cdp.click(fav.x, fav.y)
  await cdp.wait(900)
  out.favOn = await cdp.eval(`
    const b = [...document.querySelectorAll('main .itemsContainer .card.backdropCard .cardOverlayButton-br button')].find(x => x.getAttribute('data-isfavorite') !== null)
    return { cls: b.className.slice(0, 140), color: getComputedStyle(b).color, title: b.title, dataFav: b.getAttribute('data-isfavorite') }
  `)
  out.shotFav = await cdp.screenshot('tools/shots/detail-episode-fav.png')
  return out
}
