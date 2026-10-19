/**
 * Serata 跨页主题一致性探针（Lead 集成验证）
 * 目标：
 *  1) 深色模式进 /dashboard 不再被强制翻白（theme 类不被覆盖）
 *  2) 浅色模式播片 /play/:id 不再把整站切成深色，且 OSD 自身仍是深色语境
 *  3) 浅色模式下用户菜单（SheetMenu）不再是硬编码深色方块
 *  4) 库页 tabs 文案（库名/标签/文件夹）
 */
const BASE = process.env.SERATA_URL || 'http://localhost:5173'

const htmlClass = `
  return { cls: document.documentElement.className, path: location.pathname + location.search,
           bg: getComputedStyle(document.body).backgroundColor,
           htmlBg: getComputedStyle(document.documentElement).backgroundColor }
`

export default async function (cdp) {
  const out = {}
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false })

  // ---------- 深色：后台 ----------
  await cdp.send('Page.navigate', { url: `${BASE}/` })
  await cdp.wait(3500)
  await cdp.eval(`localStorage.setItem('serata.themeMode','dark'); return 1`)
  await cdp.send('Page.navigate', { url: `${BASE}/dashboard` })
  await cdp.wait(4000)
  out.darkDashboard = await cdp.eval(htmlClass)
  out.darkDashboard.forcedLight = /theme-light/.test(out.darkDashboard.cls)
  out.darkDashboardShot = await cdp.screenshot('tools/shots/x-dark-dashboard.png')

  // ---------- 浅色：设置页（后台语境） ----------
  await cdp.eval(`localStorage.setItem('serata.themeMode','light'); return 1`)
  await cdp.send('Page.navigate', { url: `${BASE}/settings/display` })
  await cdp.wait(4000)
  out.lightSettings = await cdp.eval(htmlClass)
  out.lightSettings.forcedDark = /theme-dark/.test(out.lightSettings.cls)

  // ---------- 浅色：用户菜单 ----------
  out.lightUserMenu = await cdp.eval(`
    const btn = document.querySelector('[aria-label="用户菜单"], [aria-label="User menu"]')
    if (!btn) return { err: 'no user menu button' }
    btn.click()
    await new Promise(r => setTimeout(r, 700))
    const sheet = document.querySelector('.dialog.actionSheet')
    const scrim = document.querySelector('.sheetScrim')
    return {
      opened: !!sheet,
      sheetBg: sheet ? getComputedStyle(sheet).backgroundColor : null,
      sheetColor: sheet ? getComputedStyle(sheet).color : null,
      scrimBg: scrim ? getComputedStyle(scrim).backgroundColor : null,
    }
  `)
  out.lightUserMenuShot = await cdp.screenshot('tools/shots/x-light-usermenu.png')
  await cdp.eval(`const s = document.querySelector('.sheetScrim'); if (s) s.click(); return 1`)
  await cdp.wait(500)

  // ---------- 浅色：播片（OSD） ----------
  await cdp.send('Page.navigate', { url: `${BASE}/play/105` })
  await cdp.wait(4000)
  out.lightOsd = await cdp.eval(`
    const root = document.querySelector('.s-osd')
    const html = { cls: document.documentElement.className, path: location.pathname }
    return {
      ...html,
      osdRoot: !!root,
      osdAccentVar: root ? getComputedStyle(root).getPropertyValue('--s-accent').trim() : null,
      osdTextVar: root ? getComputedStyle(root).getPropertyValue('--s-text-1').trim() : null,
      forcedDark: /theme-dark/.test(document.documentElement.className),
    }
  `)
  out.lightOsdShot = await cdp.screenshot('tools/shots/x-light-osd.png')

  // ---------- 库页 tabs 文案 ----------
  await cdp.send('Page.navigate', { url: `${BASE}/library/1` })
  await cdp.wait(4000)
  out.libTabs = await cdp.eval(`
    const nodes = [...document.querySelectorAll('.s-header__tab, [class*="tab"] a, [class*="tab"] button')]
    const labels = nodes.map(n => (n.textContent || '').trim()).filter(Boolean)
    return { labels: labels.slice(0, 14), libId: location.pathname }
  `)
  return out
}
