export default async function (cdp) {
  const out = {}
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
  await cdp.wait(3500)
  await cdp.eval(`localStorage.setItem('serata.themeMode','light'); return 1`)
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/library/100' })
  await cdp.wait(4500)
  out.tabs = await cdp.eval(`
    const nodes = [...document.querySelectorAll('[class*="tab"] a, [class*="tab"] button, [class*="tab"] span')]
    const labels = nodes.map(n => (n.textContent || '').trim()).filter(Boolean)
    return { labels: [...new Set(labels)].slice(0, 12), path: location.pathname }
  `)
  out.usermenu = await cdp.eval(`
    const btn = document.querySelector('[aria-label="用户菜单"]')
    if (!btn) return { err: 'no btn' }
    btn.click()
    await new Promise(r => setTimeout(r, 700))
    const scrim = document.querySelector('.sheetScrim')
    return { scrimBg: scrim ? getComputedStyle(scrim).backgroundColor : null }
  `)
  out.shot = await cdp.screenshot('tools/shots/x-light-usermenu.png')
  return out
}
