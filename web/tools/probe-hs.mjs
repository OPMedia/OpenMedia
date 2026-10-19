export default async function (cdp) {
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/settings/homescreen' })
  await cdp.wait(3500)
  return await cdp.eval(`
    return [...document.querySelectorAll('select')].map((s, i) => ({ i, value: s.value, opts: [...s.options].map(o => o.textContent.trim()) }))
      .filter(s => s.opts.length > 2)
  `)
}
