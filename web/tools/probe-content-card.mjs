/** 内容卡(电影库)悬浮:悬浮层 4 按钮 + 逐个真点击验证交互 */
export default async function (cdp) {
  const out = { steps: [] }
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/library/100' })
  await cdp.wait(5000)

  const cardInfo = await cdp.eval(`
    const card = document.querySelector('main .itemsContainer .card')
    if (!card) return { err: 'no card', html: document.body.className }
    const r = card.getBoundingClientRect()
    return { x: r.x + r.width / 2, y: r.y + r.height / 2, cls: card.className, name: card.querySelector('.cardText-first')?.textContent?.trim() }
  `)
  out.card = cardInfo
  if (cardInfo.err) return out

  await cdp.hover(cardInfo.x, cardInfo.y)
  await cdp.wait(500)

  const hover = await cdp.eval(`
    const card = document.querySelector('main .itemsContainer .card')
    const o = card.querySelector('.cardOverlayContainer')
    const btns = [...o.querySelectorAll('button')].map((b, i) => {
      const r = b.getBoundingClientRect(); const s = getComputedStyle(b)
      return { i, cls: b.className, title: b.title, action: b.getAttribute('data-action'),
               cp: (b.textContent || '').trim().codePointAt(0)?.toString(16),
               x: Math.round(r.x + r.width/2), y: Math.round(r.y + r.height/2),
               w: Math.round(r.width), h: Math.round(r.height), fs: s.fontSize, color: s.color, bg: s.backgroundColor }
    })
    const lbl = o.querySelector('.chkCardSelectContainer')
    const lr = lbl.getBoundingClientRect()
    return { opacity: getComputedStyle(o).opacity, buttons: btns,
             checkbox: { x: Math.round(lr.x + lr.width/2), y: Math.round(lr.y + lr.height/2), w: Math.round(lr.width), h: Math.round(lr.height), cls: lbl.className } }
  `)
  out.hover = hover
  out.shotHover = await cdp.screenshot('tools/shots/content-card-hover.png')

  const byTitle = (t) => hover.buttons.find((b) => b.title === t)

  // ---- 1) 已播/未播切换 ----
  const playedBtn = hover.buttons.find((b) => b.action === 'none' && b.cls.includes('playstatebutton'))
  await cdp.click(playedBtn.x, playedBtn.y)
  await cdp.wait(900)
  out.afterPlayed = await cdp.eval(`
    const b = document.querySelector('main .itemsContainer .card .playstatebutton-icon')
    const s = getComputedStyle(b)
    return { cls: b.className, dataPlayed: b.getAttribute('data-played'), title: b.title, color: s.color }
  `)
  out.shotPlayed = await cdp.screenshot('tools/shots/content-card-played.png')
  await cdp.click(playedBtn.x, playedBtn.y) // 还原
  await cdp.wait(700)
  out.afterPlayedRestore = await cdp.eval(`
    const b = document.querySelector('main .itemsContainer .card .playstatebutton-icon')
    return { cls: b.className, dataPlayed: b.getAttribute('data-played') }
  `)

  // ---- 2) 收藏切换 ----
  const favBtn = hover.buttons.find((b) => b.cls.includes('ratingbutton') === false && b.action === 'none' && !b.cls.includes('playstatebutton'))
  await cdp.click(favBtn.x, favBtn.y)
  await cdp.wait(900)
  out.afterFav = await cdp.eval(`
    const card = document.querySelector('main .itemsContainer .card')
    const b = [...card.querySelectorAll('.cardOverlayButton-br button')].find(x => !x.classList.contains('playstatebutton-icon'))
    const s = getComputedStyle(b)
    return { cls: b.className, dataFav: b.getAttribute('data-isfavorite'), title: b.title, color: s.color }
  `)
  out.shotFav = await cdp.screenshot('tools/shots/content-card-fav.png')
  await cdp.click(favBtn.x, favBtn.y) // 还原
  await cdp.wait(700)
  out.afterFavRestore = await cdp.eval(`
    const card = document.querySelector('main .itemsContainer .card')
    const b = [...card.querySelectorAll('.cardOverlayButton-br button')].find(x => !x.classList.contains('playstatebutton-icon'))
    return { cls: b.className, dataFav: b.getAttribute('data-isfavorite') }
  `)

  // ---- 3) 更多 → 菜单 ----
  const moreBtn = hover.buttons.find((b) => b.action === 'menu')
  await cdp.click(moreBtn.x, moreBtn.y)
  await cdp.wait(900)
  out.afterMore = await cdp.eval(`
    const sheet = document.querySelector('.actionSheet, .actionSheetContent, .sheetMenu, .actionSheetMenuItem')
    return { open: !!sheet, firstItems: [...document.querySelectorAll('.actionSheetMenuItem')].slice(0,5).map(x => x.textContent.trim()) }
  `)
  out.shotMore = await cdp.screenshot('tools/shots/content-card-more.png')
  await cdp.eval(`document.querySelector('.actionSheetScrim, .dialogBackdrop, .actionSheet')?.click(); document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })); return 1`)
  await cdp.wait(700)

  // ---- 4) 多选 ----
  await cdp.hover(cardInfo.x, cardInfo.y)
  await cdp.wait(300)
  await cdp.click(hover.checkbox.x, hover.checkbox.y)
  await cdp.wait(900)
  out.afterMulti = await cdp.eval(`
    const card = document.querySelector('main .itemsContainer .card')
    const o = card.querySelector('.cardOverlayContainer')
    const brBtns = [...card.querySelectorAll('.cardOverlayButton-br button')]
    return {
      htmlCls: document.documentElement.className,
      cardCls: card.className,
      overlayOpacity: getComputedStyle(o).opacity,
      overlayBg: getComputedStyle(o).backgroundColor,
      visibleBrButtons: brBtns.filter(b => getComputedStyle(b).display !== 'none').length,
      totalBrButtons: brBtns.length,
    }
  `)
  out.shotMulti = await cdp.screenshot('tools/shots/content-card-multiselect.png')
  await cdp.eval(`
    const cb = document.querySelector('main .itemsContainer .card .chkCardSelect')
    cb.click()
    return 1
  `)
  await cdp.wait(700)
  out.afterMultiRestore = await cdp.eval(`return { htmlCls: document.documentElement.className, cardCls: document.querySelector('main .itemsContainer .card').className }`)

  // ---- 5) 居中播放按钮 → 播放 ----
  await cdp.hover(cardInfo.x, cardInfo.y)
  await cdp.wait(400)
  const playBtn = hover.buttons.find((b) => b.cls.includes('cardOverlayFab-primary'))
  await cdp.click(playBtn.x, playBtn.y)
  await cdp.wait(1800)
  out.afterPlay = await cdp.eval(`
    return { path: location.pathname + location.search, hasOsd: !!document.querySelector('.videoOsdBottom, .videoOsdTop, .osdControls'), title: document.querySelector('.videoOsdTitle, .osdTitle')?.textContent?.trim() }
  `)
  out.shotPlay = await cdp.screenshot('tools/shots/content-card-play.png')

  return out
}
