export default async function (cdp) {
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
  await cdp.wait(3000)
  await cdp.eval(`localStorage.setItem('serata.themeMode','dark'); return 1`)
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/settings/display' })
  await cdp.wait(4000)
  const a = await cdp.screenshot('tools/shots/serata-settings-display-dark.png')
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/library/100' })
  await cdp.wait(4000)
  const b = await cdp.screenshot('tools/shots/serata-library-dark.png')
  return { settings: a, library: b }
}
