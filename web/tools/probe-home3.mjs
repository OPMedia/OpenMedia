/** 首页探针:先登录 demo/demo,再悬浮首页卡片 */
export default async function (cdp) {
  const out = {}
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
  await cdp.wait(5000)

  if (await cdp.eval(`return location.pathname === '/login'`)) {
    await cdp.eval(`
      const u = document.querySelector('#txtManualName'), p = document.querySelector('#txtManualPassword')
      u.value = 'demo'; u.dispatchEvent(new Event('input', { bubbles: true }))
      p.value = 'demo'; p.dispatchEvent(new Event('input', { bubbles: true }))
      await new Promise(r => setTimeout(r, 300))
      document.querySelector('form.manualLoginForm')?.requestSubmit()
      return 1
    `)
    await cdp.wait(5000)
    await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
    await cdp.wait(6000)
  }

  out.page = await cdp.eval(`
    const cards = [...document.querySelectorAll('.card')]
    return { path: location.pathname, nCards: cards.length, appLen: (document.querySelector('#app')?.innerHTML || '').length }
  `)

  const g = await cdp.eval(`
    const card = [...document.querySelectorAll('.card')].find(c => c.querySelector('.cardOverlayContainer .cardOverlayFab-primary'))
    if (!card) return { err: 'no card with play' }
    card.scrollIntoView({ block: 'center' })
    await new Promise(r => setTimeout(r, 600))
    const r = card.getBoundingClientRect()
    return { cx: Math.round(r.x + r.width/2), cy: Math.round(r.y + r.height/2), cls: card.className,
             name: card.querySelector('.cardText-first')?.textContent?.trim() }
  `)
  out.pick = g
  if (g.err) { out.shot = await cdp.screenshot('tools/shots/final-home-hover.png'); return out }

  await cdp.hover(g.cx, g.cy)
  await cdp.wait(800)
  out.hover = await cdp.eval(`
    const card = [...document.querySelectorAll('.card')].find(c => c.querySelector('.cardOverlayContainer .cardOverlayFab-primary'))
    const o = card.querySelector('.cardOverlayContainer')
    return { opacity: getComputedStyle(o).opacity, bg: getComputedStyle(o).backgroundColor,
             buttons: [...o.querySelectorAll('button')].map(b => ({ cp: (b.textContent||'').trim().codePointAt(0)?.toString(16), title: b.title, cls: b.className.slice(0, 95) })),
             checkbox: !!o.querySelector('.chkCardSelectContainer'),
             overlayCls: o.className }
  `)
  out.shotHover = await cdp.screenshot('tools/shots/final-home-hover.png')
  return out
}
