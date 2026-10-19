export default async function (cdp) {
  const shots = {}
  for (const theme of ['dark', 'light']) {
    await cdp.send('Page.navigate', { url: 'http://localhost:5173/settings/display' })
    await cdp.wait(2200)
    await cdp.eval(`localStorage.setItem('serata.themeMode','${theme}'); return 1`)
    await cdp.send('Page.navigate', { url: 'http://localhost:5173/settings/display' })
    await cdp.wait(2600)
    shots[theme] = await cdp.screenshot(`tools/shots/x-settings-display-${theme}.png`)
  }
  const info = await cdp.eval(`return { theme: document.documentElement.className, h1: (document.querySelector('h1')||{}).textContent || '', rows: [...document.querySelectorAll('#s-nav-drawer .s-nav__row')].length }`)
  await cdp.eval(`localStorage.setItem('serata.themeMode','dark'); return 1`)
  return { shots, info }
}
