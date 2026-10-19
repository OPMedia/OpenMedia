/** 首页卡片悬浮验证:登录 → 定位首页第一张内容卡 → 真鼠标悬浮 → 截图 + DOM/样式取证 */
export default async function (cdp) {
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
  await cdp.wait(4000)

  // 登录(demo/demo)
  const loggedIn = await cdp.eval(`
    const u = document.querySelector('#txtManualName')
    if (!u) return 'already'
    u.value = 'demo'; u.dispatchEvent(new Event('input', { bubbles: true }))
    const p = document.querySelector('#txtManualPassword')
    p.value = 'demo'; p.dispatchEvent(new Event('input', { bubbles: true }))
    document.querySelector('form.manualLoginForm').requestSubmit()
    return 'submitted'
  `)
  await cdp.wait(4500)

  const info = await cdp.eval(`
    const card = document.querySelector('main .card')
    if (!card) return { err: 'no card', body: document.body.className }
    const r = card.getBoundingClientRect()
    return { x: r.x + r.width / 2, y: r.y + r.height / 2, w: r.width, h: r.height, cls: card.className }
  `)
  if (info.err) return { loggedIn, info }

  // 悬浮前:overlay 透明度
  const before = await cdp.eval(`
    const o = document.querySelector('main .card .cardOverlayContainer')
    return { opacity: getComputedStyle(o).opacity, display: getComputedStyle(o).display,
             bg: getComputedStyle(o).backgroundColor, radius: getComputedStyle(o).borderRadius,
             buttons: o.querySelectorAll('button').length }
  `)

  await cdp.hover(info.x, info.y)
  await cdp.wait(600)

  const after = await cdp.eval(`
    const card = document.querySelector('main .card')
    const o = card.querySelector('.cardOverlayContainer')
    const cs = getComputedStyle(o)
    const btns = [...o.querySelectorAll('button')].map(b => {
      const s = getComputedStyle(b)
      const r = b.getBoundingClientRect()
      return {
        cls: b.className,
        icon: (b.textContent || '').trim(),
        title: b.title,
        action: b.getAttribute('data-action'),
        w: Math.round(r.width), h: Math.round(r.height),
        x: Math.round(r.x), y: Math.round(r.y),
        fs: s.fontSize, color: s.color, bg: s.backgroundColor,
      }
    })
    const label = o.querySelector('.chkCardSelectContainer')
    return {
      opacity: cs.opacity, display: cs.display, bg: cs.backgroundColor,
      cardRect: (() => { const r = card.getBoundingClientRect(); return { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height) } })(),
      labelCls: label && label.className, labelRect: label && (() => { const r = label.getBoundingClientRect(); return { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height) } })(),
      buttons: btns,
    }
  `)

  const shot = await cdp.screenshot('tools/shots/card-hover.png')

  return { loggedIn, cardRect: info, before, after, shot }
}
