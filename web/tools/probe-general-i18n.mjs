export default async function (cdp) {
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1600, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
  await cdp.wait(2500)
  await cdp.eval(`localStorage.setItem('serata.themeMode','dark'); localStorage.setItem('serata.locale','en'); return 1`)
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/settings/general' })
  await cdp.wait(3000)
  const en = await cdp.eval(`
    const all = [...document.querySelectorAll('main select')].map(s => [...s.options].map(o => o.textContent.trim()))
    const cjk = (document.querySelector('main').innerText.match(/[\\u4e00-\\u9fa5]/g) || []).length
    return { optionSets: all, cjkChars: cjk, htmlLang: document.documentElement.lang }
  `)
  const shotEn = await cdp.screenshot('tools/shots/x-settings-general-en.png')
  await cdp.eval(`localStorage.setItem('serata.locale','zh-CN'); return 1`)
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/settings/general' })
  await cdp.wait(2600)
  const zh = await cdp.eval(`return { htmlLang: document.documentElement.lang, first: [...document.querySelectorAll('main select')].slice(0,3).map(s => [...s.options].map(o => o.textContent.trim())) }`)
  const shotZh = await cdp.screenshot('tools/shots/x-settings-general-zh.png')
  return { en, shotEn, zh, shotZh }
}
