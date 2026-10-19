export default async function (cdp) {
  const errs = []
  cdp.on && cdp.on('Runtime.exceptionThrown', e => errs.push(e))
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
  await cdp.wait(3000)
  const home = await cdp.eval(`
    const hero = document.querySelector('.hero')
    const rows = [...document.querySelectorAll('.s-home-inner > *')]
    return {
      heroTop: hero ? Math.round(hero.getBoundingClientRect().top) : -1,
      heroRatio: hero ? +(hero.getBoundingClientRect().height / innerHeight).toFixed(3) : -1,
      heroW: hero ? Math.round(hero.getBoundingClientRect().width) : -1,
      rows: rows.map(r => (r.querySelector('.s-row-title') || {}).textContent || 'HERO'),
      navCount: document.querySelectorAll('#s-nav-drawer .s-nav__row').length,
      premiere: !!document.querySelector('.btnHeaderPremiere'),
      theme: document.documentElement.className,
    }
  `)
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/settings/display' })
  await cdp.wait(2500)
  const disp = await cdp.eval(`return { selects: document.querySelectorAll('main select').length, toggles: document.querySelectorAll('main input[type=checkbox]').length, text: document.querySelector('main').innerText.replace(/\s+/g,' ').trim().slice(0,80) }`)
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/settings/homescreen' })
  await cdp.wait(2500)
  const hs = await cdp.eval(`return { selects: document.querySelectorAll('main select').length }`)
  const shot = await cdp.screenshot('tools/shots/x-after-move.png')
  return { home, disp, hs, shot, errs }
}
