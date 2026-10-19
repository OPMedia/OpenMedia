export default async function (cdp) {
  const out = {}
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.navigate', { url: 'http://localhost:5173/' })
  await cdp.wait(3500)
  await cdp.eval(`localStorage.setItem('serata.themeMode','dark'); return 1`)

  await cdp.send('Page.navigate', { url: 'http://localhost:5173/settings/display' })
  await cdp.wait(3000)
  out.display = await cdp.eval(`
    return [...document.querySelectorAll('select')].map(s => ({
      value: s.value,
      options: [...s.options].map(o => o.textContent.trim()),
    }))
  `)

  await cdp.send('Page.navigate', { url: 'http://localhost:5173/settings/homescreen' })
  await cdp.wait(3000)
  out.homescreen = await cdp.eval(`
    const sel = document.querySelector('select')
    return sel ? { first: [...sel.options].map(o => o.textContent.trim()), count: document.querySelectorAll('select').length } : { err: 'no select' }
  `)

  // 骨架屏伪元素动画（只应动 transform）
  out.skeleton = await cdp.eval(`
    const d = document.createElement('div')
    d.className = 's-skeleton'
    d.style.width = '120px'; d.style.height = '60px'
    document.body.appendChild(d)
    const cs = getComputedStyle(d)
    const af = getComputedStyle(d, '::after')
    const r = {
      bg: cs.backgroundColor,
      radius: cs.borderRadius,
      animName: af.animationName,
      animProps: af.animationDuration,
      afterBg: af.backgroundImage.slice(0, 90),
      afterTransform: af.transform,
      prefersReduce: matchMedia('(prefers-reduced-motion: reduce)').matches,
    }
    d.remove()
    return r
  `)
  return out
}
