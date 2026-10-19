/** 审计：设置/后台侧栏每一条都点一遍，找出「点不进去」和「点进去是空壳」的条目。 */
const BASE = 'http://localhost:5173'

export default async function (cdp) {
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(2500)
  await cdp.eval(`localStorage.setItem('serata.themeMode','dark'); return 1`)

  // 登录
  await cdp.send('Page.navigate', { url: `${BASE}/login` })
  await cdp.wait(2500)
  const logged = await cdp.eval(`
    const ins = [...document.querySelectorAll('input')]
    const u = ins.find(i => /user|name/i.test(i.name + i.id + (i.autocomplete||'')))
    const p = ins.find(i => /pass/i.test(i.name + i.id + (i.autocomplete||'')))
    if (!u || !p) return { ok: false, inputs: ins.length }
    const set = (el, v) => { const d = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value'); d.set.call(el, v); el.dispatchEvent(new Event('input', { bubbles: true })) }
    set(u, 'demo'); set(p, 'demo')
    const btn = document.querySelector('button[type=submit]') || [...document.querySelectorAll('button')].find(b => b.offsetParent)
    if (btn) btn.click()
    return { ok: true }
  `)
  await cdp.wait(3500)

  await cdp.send('Page.navigate', { url: `${BASE}/settings/display` })
  await cdp.wait(3000)

  const nav = await cdp.eval(`
    return [...document.querySelectorAll('#s-nav-drawer .s-nav__row')].map((el, i) => ({
      i, label: el.querySelector('.s-nav__label')?.textContent.trim() || '',
      active: el.classList.contains('is-active'),
    }))
  `)

  const results = []
  for (const item of nav) {
    const r = await cdp.eval(`
      const rows = [...document.querySelectorAll('#s-nav-drawer .s-nav__row')]
      const el = rows[${item.i}]
      if (!el) return { err: 'no row' }
      const before = location.pathname
      el.click()
      return { before }
    `)
    await cdp.wait(1200)
    const after = await cdp.eval(`
      const main = document.querySelector('main, .view, .skinBody .page')
      const txt = (main ? main.innerText : document.body.innerText) || ''
      const heads = [...document.querySelectorAll('h1,h2,h3')].map(h => h.textContent.trim()).filter(Boolean).slice(0, 4)
      return { path: location.pathname, len: txt.replace(/\\s+/g, ' ').trim().length, heads, sample: txt.replace(/\\s+/g, ' ').trim().slice(0, 120) }
    `)
    const dead = after.path === r.before
    const empty = after.len < 80
    results.push({ label: item.label, from: r.before, to: after.path, dead, empty, len: after.len, heads: after.heads, sample: after.sample })
    // 点完回到起点，保证每一条都在同一语境下点
    if (!dead) {
      await cdp.send('Page.navigate', { url: `${BASE}${r.before}` })
      await cdp.wait(1200)
    }
  }

  return {
    login: logged,
    navCount: nav.length,
    dead: results.filter((r) => r.dead).map((r) => r.label),
    empty: results.filter((r) => !r.dead && r.empty).map((r) => `${r.to} (${r.len})`),
    results,
  }
}
