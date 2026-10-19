export default async function (cdp) {
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  const out = {}
  for (const [k, p] of [['home','/'], ['settings','/settings/display'], ['admin','/dashboard/general']]) {
    await cdp.send('Page.navigate', { url: 'http://localhost:5173' + p })
    await cdp.wait(2200)
    out[k] = await cdp.eval(`
      const rows = [...document.querySelectorAll('#s-nav-drawer .s-nav__row')]
      const groups = [...document.querySelectorAll('#s-nav-drawer .s-nav__group')]
      return { rows: rows.length, labels: rows.map(r => r.textContent.trim()), groups: groups.length }
    `)
  }
  const shot = await cdp.screenshot('tools/shots/x-nav-home.png')
  return { out, shot }
}
