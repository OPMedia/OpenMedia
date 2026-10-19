export default async function (cdp) {
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/library/100' })
  await cdp.wait(4500)
  return await cdp.eval(`
    const nav = document.querySelector('.s-header__tabs')
    const header = document.querySelector('.s-header')
    return {
      tabCount: nav ? nav.querySelectorAll('button').length : -1,
      tabs: nav ? [...nav.querySelectorAll('button')].map(b => ({ label: b.textContent.trim(), active: b.classList.contains('is-active'), aria: b.getAttribute('aria-current') })) : [],
      headerLeftHTML: header ? header.querySelector('.s-header__left') ? header.querySelector('.s-header__left').outerHTML.slice(0, 900) : 'no left' : 'no header',
      anySvgInTabs: nav ? nav.querySelectorAll('svg').length : -1,
    }
  `)
}
