/**
 * probe-motion.mjs —— 「交互与动效优化」独立验收探针（task-11，对抗式）
 *
 * 用法（两种都行）：
 *   cd web && node tools/probe-motion.mjs                 # 自驱动：内部用 tools/cdp.mjs 跑，退出码 0 = 全通过
 *   cd web && CDP_PORT=9228 node tools/cdp.mjs tools/probe-motion.mjs   # 只跑断言，打印 JSON
 *
 * 覆盖：弹层（EmbySelect / ItemMenuSheet / SheetMenu / 库页三个 / 后台新增用户）
 *       toast（数量/堆叠/视口几何/底色/自动消失）、路由转场、reduced-motion、
 *       深浅双主题、console 零报错、库页芯片对比度、rows 自述的 viewport.amount 复核。
 *
 * 证据落盘：scratch/verify/motion-raw.json + scratch/verify/motion-shots/*.png
 * 本文件只做「测量与报告」，不改任何产品代码。
 */
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const HERE = dirname(fileURLToPath(import.meta.url))
/** web/tools → web → OpenMedia → <workspace> */
const WORKSPACE = resolve(HERE, '../../..')
const VERIFY = process.env.VERIFY_DIR || join(WORKSPACE, 'scratch/verify')
const SHOTS = join(VERIFY, 'motion-shots')

/** 本轮验收覆盖的产品文件：跑前/跑后各算一次哈希，任何中途改动都会让验收失效（发生过一次：lead 中途改了 EmbySelect.vue） */
const PRODUCT_FILES = [
  'OpenMedia/web/src/components/ui/SSheet.vue',
  'OpenMedia/web/src/components/EmbySelect.vue',
  'OpenMedia/web/src/components/ItemMenuSheet.vue',
  'OpenMedia/web/src/components/SheetMenu.vue',
  'OpenMedia/web/src/components/home/HomeHero.vue',
  'OpenMedia/web/src/components/EmbyCard.vue',
  'OpenMedia/web/src/api/toast.ts',
  'OpenMedia/web/src/lib/motion.ts',
  'OpenMedia/web/src/styles/serata/motion.css',
  'OpenMedia/web/src/styles/serata/tokens.css',
  'OpenMedia/web/src/App.vue',
  'OpenMedia/web/src/views/HomeView.vue',
  'OpenMedia/web/src/views/ItemDetailView.vue',
  'OpenMedia/web/src/views/LibraryView.vue',
  'OpenMedia/web/src/views/admin/AdminUsersView.vue',
]
function revision() {
  const out = {}
  for (const rel of PRODUCT_FILES) {
    try {
      const b = readFileSync(join(WORKSPACE, rel))
      out[rel.replace('OpenMedia/web/src/', '')] = createHash('sha256').update(b).digest('hex').slice(0, 16)
    } catch (e) {
      out[rel.replace('OpenMedia/web/src/', '')] = 'ERR'
    }
  }
  return out
}
const RAW = join(VERIFY, 'motion-raw.json')
const BASE = 'http://localhost:5173'

/* ============================================================
   0. 页面侧注入（document-start，跨导航存活）
   ============================================================ */
const PRELUDE = [
  'window.__probe = { errs: [], warns: [], anim: [], io: [], focus: [] }',
  'try {',
  '  var P = window.__probe',
  '  var of = HTMLElement.prototype.focus',
  '  HTMLElement.prototype.focus = function () { try { P.focus.push({ t: Math.round(performance.now()), sel: this.tagName.toLowerCase() + "." + String(this.className || "").split(" ")[0] }) } catch (e) {} return of.apply(this, arguments) }',
  '  var oe = console.error, ow = console.warn',
  '  console.error = function () { try { P.errs.push(Array.prototype.map.call(arguments, function (a) { return String(a && a.message ? a.message : a) }).join(" ").slice(0, 300)) } catch (e) {} return oe.apply(console, arguments) }',
  '  console.warn = function () { try { P.warns.push(Array.prototype.map.call(arguments, function (a) { return String(a && a.message ? a.message : a) }).join(" ").slice(0, 300)) } catch (e) {} return ow.apply(console, arguments) }',
  '  window.addEventListener("error", function (e) { try { P.errs.push("window.error: " + (e.message || "")) } catch (_) {} })',
  '  window.addEventListener("unhandledrejection", function (e) { try { P.errs.push("unhandledrejection: " + String((e.reason && e.reason.message) || e.reason)) } catch (_) {} })',
  '  var oa = Element.prototype.animate',
  '  Element.prototype.animate = function (kf, opts) {',
  '    try {',
  '      var r = this.getBoundingClientRect(), vh = window.innerHeight',
  '      var vis = Math.max(0, Math.min(r.bottom, vh) - Math.max(r.top, 0))',
  '      var keys = Array.isArray(kf) ? kf.map(function (k) { return Object.keys(k).join("+") }).join("|") : Object.keys(kf || {}).join("+")',
  '      var n = this, idx = -1, rows = document.querySelectorAll(".s-row")',
  '      while (n) { if (n.classList && n.classList.contains("s-row")) { idx = Array.prototype.indexOf.call(rows, n); break } n = n.parentElement }',
  '      var st = String((new Error()).stack || "").split("\\n").slice(1, 4).map(function (s) { return s.replace(/^\\s*at\\s*/, "").replace(/\\?[^)]*/, "").replace(/https?:\\/\\/localhost:5173/, "") }).join(" <- ")',
  '      P.anim.push({ t: Math.round(performance.now()), cls: String(this.className || "").split(" ")[0].slice(0, 24), rowIdx: idx, frac: r.height > 0 ? Math.round(vis / r.height * 1000) / 1000 : 0, top: Math.round(r.top), h: Math.round(r.height), keys: keys.slice(0, 60), d: (opts && opts.delay) || 0, dur: Math.round((opts && opts.duration) || 0), st: st.slice(0, 130) })',
  '    } catch (e) {}',
  '    return oa.apply(this, arguments)',
  '  }',
  '  var OIO = window.IntersectionObserver',
  '  window.IntersectionObserver = function (cb, opts) {',
  '    var st = String((new Error()).stack || "").split("\\n").slice(1, 4).map(function (s) { return s.replace(/^\\s*at\\s*/, "").replace(/\\?[^)]*/, "").replace(/https?:\\/\\/localhost:5173/, "") }).join(" <- ")',
  '    return new OIO(function (entries, obs) {',
  '      try { for (var i = 0; i < entries.length; i++) { var e = entries[i]; P.io.push({ t: Math.round(performance.now()), ratio: Math.round(e.intersectionRatio * 1000) / 1000, cls: String(e.target.className || "").split(" ")[0].slice(0, 30), isIn: e.isIntersecting, th: JSON.stringify(opts && opts.threshold), rm: (opts && opts.rootMargin) || "", st: st.slice(0, 130) }) } } catch (er) {}',
  '      return cb(entries, obs)',
  '    }, opts)',
  '  }',
  '  window.IntersectionObserver.prototype = OIO.prototype',
  '} catch (e) { window.__probe.errs.push("prelude: " + String(e && e.message)) }',
].join('\n')

/* ============================================================
   1. 通用页面侧片段
   ============================================================ */
const PANEL_STATE = [
  'const norm = (v) => { const d = document.createElement("div"); d.style.color = v; document.body.appendChild(d); const c = getComputedStyle(d).color; d.remove(); return c }',
  'const panels = [...document.querySelectorAll(".s-sheet__panel")]',
  'const open = panels.filter(p => p.getAttribute("data-state") === "open")',
  'const scrim = document.querySelector(".s-sheet__scrim")',
  'const scrimOpen = scrim && scrim.getAttribute("data-state") === "open"',
  'const p0 = open[0]',
  'const labelledby = p0 ? p0.getAttribute("aria-labelledby") : null',
  'const labelled = labelledby ? labelledby.split(" ").map(id => { const el = document.getElementById(id); return el ? (el.innerText || el.textContent || "").trim() : "" }).filter(Boolean).join(" ") : null',
  'return {',
  '  theme: document.documentElement.dataset.serataTheme,',
  '  total: panels.length,',
  '  openCount: open.length,',
  '  role: p0 ? p0.getAttribute("role") : null,',
  '  ariaModal: p0 ? p0.getAttribute("aria-modal") : null,',
  '  accessibleName: labelled,',
  '  scrim: !!scrimOpen,',
  '  scrimBg: scrim ? getComputedStyle(scrim).backgroundColor : null,',
  '  raised: norm("var(--s-raised)"),',
  '  bodyOverflow: getComputedStyle(document.body).overflow,',
  '  active: document.activeElement ? (document.activeElement.tagName.toLowerCase() + "." + String(document.activeElement.className || "").split(" ")[0]) : null,',
  '  panels: open.map(p => { const cs = getComputedStyle(p); const r = p.getBoundingClientRect(); return {',
  '    cls: String(p.className), state: p.getAttribute("data-state"), anim: cs.animationName, pos: cs.position, z: cs.zIndex,',
  '    bg: cs.backgroundColor, color: cs.color, op: cs.opacity, origin: cs.transformOrigin,',
  '    rect: { x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) },',
  '    text: (p.innerText || "").trim().replace(/\\s+/g, " ").slice(0, 30) } })',
  '}',
].join('\n')

const TOAST_STATE = [
  'const norm = (v) => { const d = document.createElement("div"); d.style.color = v; document.body.appendChild(d); const c = getComputedStyle(d).color; d.remove(); return c }',
  'const items = [...document.querySelectorAll("[data-toast-id]")]',
  'const vp = document.querySelector(".s-toast__viewport")',
  'const vs = vp ? getComputedStyle(vp) : null',
  'const vr = vp ? vp.getBoundingClientRect() : null',
  'return {',
  '  count: items.length,',
  '  ids: items.map(i => i.getAttribute("data-toast-id")),',
  '  raised: norm("var(--s-raised)"),',
  '  viewport: vp ? { pos: vs.position, z: vs.zIndex, w: Math.round(vr.width), right: Math.round(innerWidth - vr.right), bottom: Math.round(innerHeight - vr.bottom), pe: vs.pointerEvents, display: vs.display } : null,',
  '  items: items.map(i => { const cs = getComputedStyle(i); const r = i.getBoundingClientRect(); return {',
  '    pos: cs.position, bg: cs.backgroundColor, color: cs.color, op: cs.opacity, display: cs.display, anim: cs.animationName,',
  '    w: Math.round(r.width), h: Math.round(r.height), x: Math.round(r.left), y: Math.round(r.top),',
  '    text: (i.innerText || "").trim().replace(/\\s+/g, " ").slice(0, 40), limited: i.hasAttribute("data-limited") } })',
  '}',
].join('\n')

const CSSOM_RULE = (sel) => [
  'const want = ' + JSON.stringify(sel),
  'const out = []',
  'for (const sh of document.styleSheets) {',
  '  let rules = null',
  '  try { rules = sh.cssRules } catch (e) { continue }',
  '  if (!rules) continue',
  '  const walk = (list) => { for (const r of list) { if (r.selectorText && r.selectorText.indexOf(want) !== -1) out.push({ sel: r.selectorText, css: r.style.cssText }); if (r.cssRules && r.cssRules.length) walk(r.cssRules) } }',
  '  walk(rules)',
  '}',
  'return out',
].join('\n')

/** 通用对比度：sel 的前景/背景（背景半透明时按 overSel 底色合成） */
const CONTRAST = (sel, overSel) => [
  'const el = document.querySelector(' + JSON.stringify(sel) + ')',
  'if (!el) return null',
  'const over = document.querySelector(' + JSON.stringify(overSel) + ')',
  'const parse = (s) => { const m = String(s).match(/rgba?\\(([^)]+)\\)/); if (!m) return null; const p = m[1].split(",").map(x => parseFloat(x)); return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 } }',
  'const comp = (fg, bg) => ({ r: fg.r * fg.a + bg.r * (1 - fg.a), g: fg.g * fg.a + bg.g * (1 - fg.a), b: fg.b * fg.a + bg.b * (1 - fg.a), a: 1 })',
  'const lum = (c) => { const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4) }; return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b) }',
  'const ratio = (a, b) => { const l1 = lum(a), l2 = lum(b); const hi = Math.max(l1, l2), lo = Math.min(l1, l2); return Math.round(((hi + 0.05) / (lo + 0.05)) * 100) / 100 }',
  'const cs = getComputedStyle(el)',
  'const bgRaw = parse(cs.backgroundColor)',
  'const base = over ? parse(getComputedStyle(over).backgroundColor) : { r: 255, g: 255, b: 255, a: 1 }',
  'const bg = bgRaw.a < 1 ? comp(bgRaw, base) : bgRaw',
  'const fg = parse(cs.color)',
  'return { text: (el.innerText || "").trim().replace(/\\s+/g, " ").slice(0, 20), bgRaw: cs.backgroundColor, colorRaw: cs.color, bg, fg, ratio: ratio(fg, bg) }',
].join('\n')

/* ============================================================
   2. 结果收集
   ============================================================ */
const checks = []
const raw = { startedAt: new Date().toISOString(), sections: {} }
function check(id, name, pass, expected, actual, note) {
  checks.push({ id, name, pass: !!pass, expected, actual, ...(note ? { note } : {}) })
  return !!pass
}
function section(key, obj) {
  raw.sections[key] = obj
}

/* ============================================================
   3. 驱动助手
   ============================================================ */
async function ev(cdp, src) {
  return cdp.eval(src)
}
async function evr(cdp, src, tries = 8) {
  let last
  for (let i = 0; i < tries; i++) {
    try {
      return await cdp.eval(src)
    } catch (e) {
      last = e
      await cdp.wait(300)
    }
  }
  throw new Error('eval failed after retries: ' + String(last && last.message).slice(0, 160))
}
async function waitReady(cdp, ms = 9000) {
  const t0 = Date.now()
  while (Date.now() - t0 < ms) {
    try {
      const s = await cdp.eval('return document.readyState')
      if (s === 'complete') return true
    } catch (e) {
      /* 导航中 */
    }
    await cdp.wait(150)
  }
  return false
}
async function nav(cdp, url, settle = 1100) {
  await cdp.send('Page.navigate', { url: BASE + url })
  await waitReady(cdp)
  await cdp.wait(settle)
}
async function reload(cdp, settle = 1600) {
  await cdp.send('Page.reload')
  await waitReady(cdp)
  await cdp.wait(settle)
}
async function shot(cdp, name) {
  const p = join(SHOTS, name)
  try {
    await cdp.screenshot(p)
    return p
  } catch (e) {
    return 'SHOT_FAILED: ' + String(e && e.message).slice(0, 80)
  }
}
/** 等元素出现（异步数据渲染用），最多 ms 毫秒 */
async function waitFor(cdp, sel, ms = 4000) {
  const t0 = Date.now()
  while (Date.now() - t0 < ms) {
    const ok = await evr(cdp, 'return !!document.querySelector(' + JSON.stringify(sel) + ')')
    if (ok) return true
    await cdp.wait(250)
  }
  return false
}
async function ensureLogin(cdp) {
  await nav(cdp, '/', 1200)
  const st = await evr(cdp, 'return { path: location.pathname, form: !!document.querySelector("#txtManualName") }')
  if (st.path === '/login' || st.form) {
    await evr(cdp, [
      'const n = document.querySelector("#txtManualName"), p = document.querySelector("#txtManualPassword")',
      'if (!n || !p) return "noform"',
      'const set = (el, v) => { el.focus(); el.value = v; el.dispatchEvent(new Event("input", { bubbles: true })); el.dispatchEvent(new Event("change", { bubbles: true })) }',
      'set(n, "demo"); set(p, "demo")',
      'const b = document.querySelector("button.paperSubmit")',
      'if (b) b.click()',
      'return "submitted"',
    ].join('\n'))
    await cdp.wait(2600)
  }
  return evr(cdp, 'return { path: location.pathname, cls: document.documentElement.className, ls: localStorage.getItem("serata.themeMode") }')
}
/** 切主题：**只点顶栏真实按钮**（禁止 import useTheme）。
 *  注意：/login 与 /play 是裸分支、没有顶栏按钮 —— 此时必须先回到壳内路由再切，否则会静默切不动。 */
async function setTheme(cdp, want) {
  let clicks = 0
  let navBack = 0
  for (let i = 0; i < 6; i++) {
    const cur = await evr(cdp, 'return document.documentElement.dataset.serataTheme')
    if (cur === want) break
    const r = await evr(cdp, 'const b = document.querySelector("button[aria-label=\\"切换深浅模式\\"]"); if (!b) return null; const r = b.getBoundingClientRect(); return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }')
    if (!r) {
      // 裸分支（login/play）没有顶栏：先回壳内
      await nav(cdp, '/', 1600)
      navBack++
      continue
    }
    await cdp.click(r.x, r.y)
    clicks++
    await cdp.wait(800)
  }
  const fin = await evr(cdp, 'return { resolved: document.documentElement.dataset.serataTheme, cls: document.documentElement.className, ls: localStorage.getItem("serata.themeMode") }')
  return { ok: fin.resolved === want, clicks, navBack, ...fin }
}
async function clickSel(cdp, sel) {
  const r = await evr(cdp, 'const e = document.querySelector(' + JSON.stringify(sel) + '); if (!e) return null; const b = e.getBoundingClientRect(); return { x: Math.round(b.left + b.width / 2), y: Math.round(b.top + b.height / 2), w: Math.round(b.width), h: Math.round(b.height), top: Math.round(b.top) }')
  if (!r) return null
  await cdp.click(r.x, r.y)
  return r
}
async function esc(cdp) {
  await cdp.send('Input.dispatchKeyEvent', { type: 'rawKeyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27, nativeVirtualKeyCode: 27 })
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27, nativeVirtualKeyCode: 27 })
}
/** 找一个「面板外、且该点确实不在面板内、且不落在可交互控件上」的坐标；同时报告该点上是什么元素 */
async function outsidePoint(cdp) {
  return evr(cdp, [
    'const p = document.querySelector(".s-sheet__panel[data-state=\\"open\\"]")',
    'if (!p) return null',
    'const r = p.getBoundingClientRect(), vw = innerWidth, vh = innerHeight',
    'const cand = [',
    '  { x: r.left - 260, y: r.top + Math.min(120, r.height / 2) },',
    '  { x: r.right + 260, y: r.top + Math.min(120, r.height / 2) },',
    '  { x: Math.round(vw / 2), y: vh - 60 },',
    '  { x: 30, y: vh - 60 },',
    '  { x: Math.round(vw / 2), y: 20 }',
    ']',
    'const ok = []',
    'for (const c of cand) {',
    '  if (!(c.x > 2 && c.x < vw - 2 && c.y > 2 && c.y < vh - 2)) continue',
    '  const el = document.elementFromPoint(c.x, c.y)',
    '  if (!el) continue',
    '  if (el.closest && el.closest(".s-sheet__panel")) continue',
    '  const hit = { x: c.x, y: c.y, top: el.tagName.toLowerCase() + "." + String(el.className || "").split(" ")[0], isScrim: !!(el.classList && el.classList.contains("s-sheet__scrim")), interactive: !!(el.closest && el.closest("button, a, input, select, textarea, [role=button], [role=link]")) }',
    '  ok.push(hit)',
    '}',
    'return ok.find(x => !x.interactive) || ok[0] || null',
  ].join('\n'))
}
/** 关闭过程采样：能否观测到退场动画（data-state=closed / s-pop-out / s-up-out / s-fade-out） */
async function sampleExit(cdp, action) {
  await evr(cdp, [
    'window.__exitSamples = []',
    'window.__exitT0 = performance.now()',
    'const tick = () => {',
    '  const nodes = [...document.querySelectorAll(".s-sheet__panel, .s-sheet__scrim")]',
    '  window.__exitSamples.push({ t: Math.round(performance.now() - window.__exitT0), n: nodes.map(x => ({ st: x.getAttribute("data-state"), an: getComputedStyle(x).animationName })) })',
    '  if (performance.now() - window.__exitT0 < 900) requestAnimationFrame(tick)',
    '}',
    'requestAnimationFrame(tick)',
    'return 1',
  ].join('\n'))
  await action()
  await cdp.wait(650)
  const s = await evr(cdp, 'return window.__exitSamples || []')
  const anims = [...new Set(s.flatMap((x) => x.n.map((n) => n.an)).filter((a) => a && a !== 'none'))]
  return { sampleCount: s.length, exitAnims: anims, closedSeen: s.some((x) => x.n.some((n) => n.st === 'closed')) }
}

/* ============================================================
   4. 弹层通用验收（一个触发器一套）
   ============================================================ */
async function sheetSuite(cdp, cfg) {
  const res = { id: cfg.id, label: cfg.label, theme: cfg.theme }
  // 打开
  const trig = await clickSel(cdp, cfg.trigger)
  await cdp.wait(450)
  res.triggerFound = !!trig
  res.triggerRect = trig
  let st = await evr(cdp, PANEL_STATE)
  res.opened = st
  const panel = st.panels[0]
  res.panelCls = panel ? panel.cls : null
  res.themeOnOpen = st.theme
  check(cfg.id + '.theme', cfg.label + ' 打开时页面主题确实是 ' + cfg.theme, st.theme === cfg.theme, cfg.theme, st.theme)
  check(cfg.id + '.open', cfg.label + ' 能打开', st.openCount === 1, 'openCount=1', 'openCount=' + st.openCount + ' cls=' + (panel ? panel.cls : '-'))
  if (panel) {
    check(cfg.id + '.anim', cfg.label + ' 进场有 animation（≠none）', panel.anim !== 'none', 'animationName≠none', panel.anim)
    check(cfg.id + '.fixed', cfg.label + ' 面板 position:fixed', panel.pos === 'fixed', 'fixed', panel.pos + ' z=' + panel.z)
    check(cfg.id + '.z', cfg.label + ' 面板 z-index ≥ 1000', parseInt(panel.z, 10) >= 1000, '>=1000', panel.z)
    check(cfg.id + '.bgNotBlack', cfg.label + ' 面板底色 ≠ 硬编码 rgb(40,40,43)（浅色黑方块 bug）', panel.bg !== 'rgb(40, 40, 43)', '≠ rgb(40, 40, 43)', panel.bg)
    check(cfg.id + '.bgToken', cfg.label + ' 面板底色 = 当前主题 --s-raised', panel.bg === st.raised, 'bg=' + st.raised, panel.bg)
    if (cfg.inViewport) {
      // 不变式（而不是写死 maxWidth 数值）：面板必须完整落在视口内 —— 固定定位元素溢出时不会产生横向滚动条，会被静默裁掉
      const vp = await evr(cdp, 'return { vw: window.innerWidth, vh: window.innerHeight }')
      const maxW = await evr(cdp, 'const p = document.querySelector(".s-sheet__panel[data-state=\\"open\\"]"); return p ? { maxWidth: getComputedStyle(p).maxWidth, width: getComputedStyle(p).width, left: getComputedStyle(p).left } : null')
      res.inViewport = { rect: panel.rect, ...vp, ...maxW }
      check(cfg.id + '.inViewport', cfg.label + ' 面板完整落在视口内（right ≤ 视口宽 且 left ≥ 0）', panel.rect.x >= 0 && panel.rect.x + panel.rect.w <= vp.vw, 'left≥0 且 right≤' + vp.vw, 'left=' + panel.rect.x + ' right=' + (panel.rect.x + panel.rect.w) + ' w=' + panel.rect.w + ' maxWidth=' + (maxW ? maxW.maxWidth : '-'))
    }
  }
  if (cfg.expectScrim === true) {
    const m = String(st.scrimBg || '').match(/rgba?\(([^)]+)\)/)
    const parts = m ? m[1].split(',').map((x) => parseFloat(x)) : []
    const dark = parts.length >= 3 ? parts[0] + parts[1] + parts[2] < 300 : false
    const alpha = parts.length > 3 ? parts[3] : 1
    check(cfg.id + '.scrim', cfg.label + ' 有遮罩', st.scrim === true, 'scrim=true', 'scrim=' + st.scrim + ' bg=' + st.scrimBg)
    check(cfg.id + '.scrimDims', cfg.label + ' 遮罩是「压暗」的（浅色下也变暗，不是提亮）', dark && alpha >= 0.3, 'rgb 和 < 300 且 alpha ≥ 0.3', st.scrimBg)
  } else if (cfg.expectScrim === false) {
    check(cfg.id + '.scrim', cfg.label + ' 无遮罩（modal=false 轻量浮层）', st.scrim === false, 'scrim=false', 'scrim=' + st.scrim)
  }
  if (cfg.shot) res.shot = await shot(cdp, cfg.shot)

  // Esc 关闭 + 退场动画 + 焦点归还
  const ex = await sampleExit(cdp, () => esc(cdp))
  await cdp.wait(450)
  const afterEsc = await evr(cdp, 'return { open: [...document.querySelectorAll(".s-sheet__panel")].filter(p => p.getAttribute("data-state") === "open").length, focusIsTrigger: document.activeElement === document.querySelector(' + JSON.stringify(cfg.trigger) + '), active: document.activeElement ? (document.activeElement.tagName.toLowerCase() + "." + String(document.activeElement.className || "").split(" ")[0]) : null }')
  res.escClose = afterEsc
  res.escExit = ex
  check(cfg.id + '.esc', cfg.label + ' Esc 能关', afterEsc.open === 0, 'openCount=0', 'openCount=' + afterEsc.open)
  check(cfg.id + '.escFocus', cfg.label + ' Esc 关闭后焦点回到触发器', afterEsc.focusIsTrigger === true, 'activeElement=trigger', 'active=' + afterEsc.active)
  check(cfg.id + '.exitAnim', cfg.label + ' 关闭过程能观测到退场动画', ex.exitAnims.length > 0 || ex.closedSeen === true, 'exitAnims 非空 或 data-state=closed', JSON.stringify({ anims: ex.exitAnims, closedSeen: ex.closedSeen }))

  // 点外/点遮罩关闭
  if (cfg.outside) {
    const re = await clickSel(cdp, cfg.trigger)
    await cdp.wait(450)
    const pt = await outsidePoint(cdp)
    res.outsidePoint = pt
    if (!pt) {
      check(cfg.id + '.outside', cfg.label + ' 点外关闭', false, '找到面板外坐标', 'null')
    } else {
      await evr(cdp, 'window.__probe.focus.length = 0; return 1')
      await cdp.click(pt.x, pt.y)
      await cdp.wait(500)
      const after = await evr(cdp, 'return { open: [...document.querySelectorAll(".s-sheet__panel")].filter(p => p.getAttribute("data-state") === "open").length, focusIsTrigger: document.activeElement === document.querySelector(' + JSON.stringify(cfg.trigger) + '), active: document.activeElement ? (document.activeElement.tagName.toLowerCase() + "." + String(document.activeElement.className || "").split(" ")[0]) : null, focusCalls: (window.__probe.focus || []).map(x => x.sel) }')
      res.outsideClose = { ...after, pt }
      const label = cfg.expectScrim === true ? '点遮罩能关' : '点页面空白能关'
      check(cfg.id + '.outside', cfg.label + ' ' + label, after.open === 0, 'openCount=0', 'openCount=' + after.open + ' 点(' + Math.round(pt.x) + ',' + Math.round(pt.y) + ')命中 ' + pt.top + ' isScrim=' + pt.isScrim)
      check(cfg.id + '.outsideFocus', cfg.label + ' 点外关闭后焦点回到触发器', after.focusIsTrigger === true, 'activeElement=trigger', 'active=' + after.active + '，关闭后 focus() 调用=' + JSON.stringify(after.focusCalls))
    }
    await esc(cdp)
    await cdp.wait(300)
  }
  return res
}

/* ============================================================
   5. 主流程
   ============================================================ */
export default async function run(cdp) {
  mkdirSync(SHOTS, { recursive: true })
  await cdp.send('Page.enable')
  await cdp.send('Runtime.enable')
  await cdp.send('Page.bringToFront')
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1600, height: 900, deviceScaleFactor: 1, mobile: false })
  await cdp.send('Page.addScriptToEvaluateOnNewDocument', { source: PRELUDE })
  const revBefore = revision()
  section('revision.before', revBefore)

  /* ---------- 登录 + 主题 ---------- */
  const login = await ensureLogin(cdp)
  section('login', login)
  if (login.path === '/login') {
    check('setup.login', '登录成功', false, '/', login.path)
  } else {
    check('setup.login', '登录成功', true, '/', login.path)
  }

  /* ---------- rows 自述复核：viewport.amount / once 是否真的生效（对抗式，带归因） ---------- */
  await setTheme(cdp, 'dark')
  await nav(cdp, '/', 2600)
  const scrollInfo = await evr(cdp, [
    'const sc = (() => { const all = [...document.querySelectorAll("*")]; let b = null; for (const el of all) { if (el.scrollHeight > el.clientHeight + 20 && /(auto|scroll)/.test(getComputedStyle(el).overflowY)) { if (!b || el.scrollHeight > b.scrollHeight) b = el } } return b })()',
    'window.__sc = sc',
    'return { scroller: sc ? sc.tagName + "." + String(sc.className).split(" ")[0] : null, sh: sc ? sc.scrollHeight : 0, ch: sc ? sc.clientHeight : 0, rows: document.querySelectorAll(".s-row").length, items: document.querySelectorAll(".s-row-item").length, vh: innerHeight }',
  ].join('\n'))

  const reveal = await evr(cdp, [
    'const sc = window.__sc, P = window.__probe',
    'const rows = [...document.querySelectorAll(".s-row")]',
    'if (!sc || !rows.length) return { err: "no scroller or rows" }',
    'const lastIdx = rows.length - 1',
    'const last = rows[lastIdx]',
    'const base = last.getBoundingClientRect().top - sc.getBoundingClientRect().top + sc.scrollTop',
    'const only = (l) => l.filter(a => a.rowIdx === lastIdx)',
    'P.anim.length = 0',
    'await new Promise((res) => { const t0 = performance.now(), dur = 1300, start = sc.scrollTop, target = Math.max(0, base - 380); const step = () => { const p = Math.min(1, (performance.now() - t0) / dur); sc.scrollTop = start + (target - start) * p; if (p < 1) requestAnimationFrame(step); else res(1) }; requestAnimationFrame(step) })',
    'await new Promise(r => setTimeout(r, 900))',
    'const enter = only(P.anim.slice())',
    'const enterOpacity = [...last.querySelectorAll(".s-row-item")].slice(0, 2).map(i => getComputedStyle(i).opacity)',
    'P.anim.length = 0',
    'sc.scrollTop = 0',
    'await new Promise(r => setTimeout(r, 1100))',
    'const whileOut = only(P.anim.slice())',
    'const outOpacity = [...last.querySelectorAll(".s-row-item")].slice(0, 2).map(i => getComputedStyle(i).opacity + "|" + getComputedStyle(i).transform)',
    'P.anim.length = 0',
    'sc.scrollTop = Math.max(0, base - 380)',
    'await new Promise(r => setTimeout(r, 1200))',
    'const reEnter = only(P.anim.slice())',
    'const ioRow = P.io.filter(x => x.cls === "s-row")',
    'const thr = [...new Set(ioRow.map(x => x.th + "|rm:" + (x.rm || "-")))].sort()',
    'const motionIO = ioRow.filter(x => !x.rm)',
    'return { lastIdx, base: Math.round(base), thresholds: thr, ioEnterRatios: motionIO.filter(x => x.isIn).map(x => x.ratio).slice(0, 14), ioRowCount: ioRow.length, enter, enterOpacity, whileOut, outOpacity, reEnter }',
  ].join('\n'))
  const enterRow = (reveal.enter || []).filter((a) => a.cls === 's-row')
  const enterItems = (reveal.enter || []).filter((a) => a.cls === 's-row-item')
  const minRowFrac = enterRow.length ? Math.min(...enterRow.map((a) => a.frac)) : null
  const maxItemFrac = enterItems.length ? Math.max(...enterItems.map((a) => a.frac)) : null
  // motion-v 的 whileInView IO 是唯一 rootMargin 为空的那个（其余非空 rootMargin 的 threshold=1 是 auto-animate 的）。
  // 注意：**不要**再按 threshold 的具体数值去匹配 —— 曾经写成 indexOf('0|')===0，
  // 那等于把「修复前的 bug 值 0」钉成期望值，导致 F1 修好后这条断言反而红了。
  const motionThr = (reveal.thresholds || []).filter((t) => t.endsWith('|rm:-'))
  const motionThrNum = motionThr.length === 1 ? parseFloat(motionThr[0]) : null
  const ioRatios = (reveal.ioEnterRatios || []).filter((r) => r < 1)
  const minIoRatio = ioRatios.length ? Math.min(...ioRatios) : null
  const outOfViewAnims = (reveal.whileOut || []).filter((a) => a.frac === 0)
  // 根因证据：motion-v 2.8.0 里 whileInView 读的是 inViewOptions（props.mjs 只声明 inViewOptions/onViewportEnter/onViewportLeave），
  // 写 :viewport 会作为「未知属性」落到 DOM 上，既不设阈值也不吃 once。
  const rowProps = await evr(cdp, [
    'const r = document.querySelector(".s-row")',
    'return r ? { viewportAttr: r.getAttribute("viewport"), whileInView: r.getAttribute("while-in-view"), motionAttrs: [...r.attributes].map(a => a.name).filter(n => /view|motion|once/i.test(n)) } : null',
  ].join('\n'))
  section('reveal', { scrollInfo, rowProps, ...reveal })
  section('revealSummary', {
    minRowFracAtAnimStart: minRowFrac,
    maxItemFracAtAnimStart: maxItemFrac,
    motionIOThresholds: motionThr,
    minIoRatioAtEnter: minIoRatio,
    animsWhileRowOutOfView: outOfViewAnims.length,
    animsOnReEnter: (reveal.reEnter || []).length,
  })

  check('reveal.rowsRendered', '首页行渲染出来了', scrollInfo.rows >= 3, 'rows>=3', 'rows=' + scrollInfo.rows)
  check('reveal.triggered', '滚动后行确实触发了进场动画', enterRow.length > 0, 'enterRow>0', 'enterRow=' + enterRow.length + ' enterItems=' + enterItems.length)
  check(
    'reveal.viewportPropConsumed',
    '行入场用的是被 motion-v 真正消费的 prop（DOM 上不应残留 `viewport` 属性）',
    rowProps !== null && rowProps.viewportAttr === null,
    'DOM 上不应残留 viewport 属性（残留 = 写成了未声明的 `:viewport`，整个对象落到 $attrs）',
    'DOM 属性=' + JSON.stringify(rowProps && rowProps.motionAttrs) + ' viewport=' + JSON.stringify(rowProps && rowProps.viewportAttr),
    'motion-v dist/es/components/motion/props.mjs:110 只声明 inViewOptions，没有 viewport；' +
      '正确写法是 `:in-view-options`',
  )
  check(
    'reveal.amountHonored',
    'HomeView 的 `:in-view-options` amount 被消费成 IO 阈值（空 rootMargin 的那个 motion-v 观察器）',
    motionThr.length === 1 && motionThrNum !== null && motionThrNum >= 0.2,
    '恰好一个空 rootMargin 的 s-row IO，且 threshold >= 0.2',
    '空 rootMargin 的阈值=' + JSON.stringify(motionThr) + ' 全部阈值=' + JSON.stringify(reveal.thresholds),
    'threshold 0 = 只要露出 1px 就开播（`:viewport` 未被消费时的症状）；' +
      '0.15 也不够：实测卡片顶部在行内占比 0.169~0.208，0.15 触发时卡片仍在视口外',
  )
  check(
    'reveal.cardsVisibleAtStart',
    '卡片开始播放时至少部分可见（可见比例 > 0）',
    maxItemFrac !== null && maxItemFrac > 0,
    'maxItemFrac > 0',
    '卡片动画触发时可见比例 = ' + maxItemFrac + '（行=' + minRowFrac + '，IO 进入时最小 ratio=' + minIoRatio + '）',
    'frac=0 表示整张卡片还在视口下方就开始播',
  )
  check(
    'reveal.noAnimWhileOutOfView',
    '行完全离开视口后不再播放动画',
    outOfViewAnims.length === 0,
    '0 条',
    outOfViewAnims.length + ' 条（示例 top=' + (outOfViewAnims[0] ? outOfViewAnims[0].top : '-') + ' frac=' + (outOfViewAnims[0] ? outOfViewAnims[0].frac : '-') + '）→ 离开视口时被动画回 hidden（卡片 opacity=' + JSON.stringify(reveal.outOpacity) + '）',
  )
  check(
    'reveal.noReplayOnReEnter',
    '行滚出再滚回不重播（once: true）',
    (reveal.reEnter || []).length === 0,
    '0 条',
    (reveal.reEnter || []).length + ' 条重新播放（含 stagger delay=' + JSON.stringify([...new Set((reveal.reEnter || []).map((a) => a.d))]) + '）',
  )
  await evr(cdp, 'if (window.__sc) window.__sc.scrollTop = 0; return 1')

  await evr(cdp, 'if (window.__sc) window.__sc.scrollTop = 0; return 1')

  /* ---------- 主题：dark 全套 ---------- */
  const themeResults = {}
  for (const theme of ['dark', 'light']) {
    // 先回到壳内路由：/play、/login 是裸分支，没有顶栏主题按钮（否则切不动且会静默用旧主题跑完整套断言）
    await nav(cdp, '/', 1800)
    const th = await setTheme(cdp, theme)
    themeResults[theme] = { switch: th }
    check('theme.' + theme, '切到 ' + theme + '（点顶栏真实按钮）', th.ok === true, 'dataset.serataTheme=' + theme, JSON.stringify({ resolved: th.resolved, ls: th.ls, clicks: th.clicks, navBack: th.navBack }))

    /* --- hero CTA 对比度（lead 自述：深色 18.5:1 / 浅色 14.2:1，主题反转、无 coral+white） --- */
    await waitFor(cdp, '.hero-cta--primary', 5000)
    const heroCta = await evr(cdp, CONTRAST('.hero-cta--primary', '.hero'))
    themeResults[theme].heroCta = heroCta
    check('hero.ctaContrast.' + theme, '[' + theme + '] hero 主 CTA 对比度 ≥ 4.5:1', !!heroCta && heroCta.ratio >= 4.5, '>=4.5', heroCta ? heroCta.ratio + ':1 (' + heroCta.bgRaw + ' / ' + heroCta.colorRaw + ')' : 'no .hero-cta--primary')

    /* --- 弹层：/item/101 --- */
    await nav(cdp, '/item/101', 2600)
    const selectCount = await evr(cdp, [
      'const fs = [...document.querySelectorAll(".emby-select-facade")]',
      'const lab = (f) => String(f.getAttribute("aria-label") || f.innerText || "").trim().replace(/\\s+/g, " ").slice(0, 14)',
      'return { facades: fs.length, enabled: fs.filter(f => !f.disabled).length, disabled: fs.filter(f => f.disabled).length, labels: fs.map(f => lab(f) + (f.disabled ? "[disabled]" : "")) }',
    ].join('\n'))
    themeResults[theme].embySelectCount = selectCount
    check('sheet.embySelectCount.' + theme, '[' + theme + '] /item/101 EmbySelect 实例数 = 4', selectCount.facades === 4, '4', JSON.stringify(selectCount))
    // 「视频」只有一条轨 → disabled:true，点了不开面板（lead 已说明，属预期，不报缺陷）；探针只允许点非 disabled 的
    const disClick = await evr(cdp, [
      'const d = document.querySelector(".emby-select-facade[disabled]")',
      'if (!d) return { skip: true }',
      'const before = [...document.querySelectorAll(".s-sheet__panel")].filter(p => p.getAttribute("data-state") === "open").length',
      'd.click()',
      'return { skip: false, before, after: [...document.querySelectorAll(".s-sheet__panel")].filter(p => p.getAttribute("data-state") === "open").length }',
    ].join('\n'))
    themeResults[theme].disabledSelectClick = disClick
    if (!disClick.skip) {
      check('sheet.embySelectDisabled.' + theme, '[' + theme + '] disabled 的「视频」下拉点了不开面板（预期行为，非缺陷）', disClick.after === 0, 'openCount=0', 'openCount=' + disClick.after)
    }

    themeResults[theme].embySelect = await sheetSuite(cdp, {
      id: 'sheet.embySelect.' + theme,
      label: '[' + theme + '] EmbySelect',
      theme,
      trigger: '.emby-select-facade:not([disabled])',
      expectScrim: false,
      outside: true,
      inViewport: true,
      shot: theme + '-item101-embyselect.png',
    })

    themeResults[theme].itemMenu = await sheetSuite(cdp, {
      id: 'sheet.itemMenu.' + theme,
      label: '[' + theme + '] ItemMenuSheet（更多）',
      theme,
      trigger: '.btnMoreCommands',
      expectScrim: true,
      outside: true,
      shot: theme + '-item101-itemmenu.png',
    })

    themeResults[theme].userMenu = await sheetSuite(cdp, {
      id: 'sheet.userMenu.' + theme,
      label: '[' + theme + '] SheetMenu（用户菜单）',
      theme,
      trigger: 'button.s-avatar',
      expectScrim: true,
      outside: true,
      shot: theme + '-usermenu.png',
    })
    // SheetMenu 的 aria：role=dialog + 可读名（VisuallyHidden DialogTitle）
    await clickSel(cdp, 'button.s-avatar')
    await cdp.wait(500)
    const umAria = await evr(cdp, PANEL_STATE)
    themeResults[theme].userMenuAria = { role: umAria.role, ariaModal: umAria.ariaModal, accessibleName: umAria.accessibleName, bodyOverflow: umAria.bodyOverflow }
    check('sheet.userMenu.aria.' + theme, '[' + theme + '] 用户菜单 role=dialog 且有可读名（用户菜单）', umAria.role === 'dialog' && /用户菜单/.test(String(umAria.accessibleName)), 'role=dialog + name 含「用户菜单」', JSON.stringify({ role: umAria.role, name: umAria.accessibleName }))
    await esc(cdp)
    await cdp.wait(400)

    /* --- 弹层：/library/100 三个 --- */
    await nav(cdp, '/library/100', 2600)
    themeResults[theme].library = {}
    themeResults[theme].library.sort = await sheetSuite(cdp, {
      id: 'sheet.libSort.' + theme,
      label: '[' + theme + '] 库页排序',
      theme,
      trigger: '.btnSort',
      expectScrim: true,
      outside: true,
      shot: theme + '-library-sort.png',
    })
    themeResults[theme].library.filter = await sheetSuite(cdp, {
      id: 'sheet.libFilter.' + theme,
      label: '[' + theme + '] 库页筛选',
      theme,
      trigger: '.btnFilter',
      expectScrim: true,
      outside: true,
      shot: theme + '-library-filter.png',
    })
    themeResults[theme].library.view = await sheetSuite(cdp, {
      id: 'sheet.libView.' + theme,
      label: '[' + theme + '] 库页视图设置',
      theme,
      trigger: '.btnViewSettings',
      expectScrim: true,
      outside: true,
      shot: theme + '-library-viewsettings.png',
    })

    /* --- 芯片对比度（筛选弹层内） --- */
    await clickSel(cdp, '.btnFilter')
    await cdp.wait(500)
    const chipCount = await evr(cdp, 'return { chips: document.querySelectorAll(".filterChip").length, on: document.querySelectorAll(".filterChip-on").length, pager: !!document.querySelector(".pager") }')
    // 主动选中第一枚芯片，保证有 .filterChip-on
    await evr(cdp, 'const c = document.querySelector(".filterChip"); if (c) c.click(); return 1')
    await cdp.wait(350)
    const chips = await evr(cdp, [
      'const norm = (v) => { const d = document.createElement("div"); d.style.color = v; document.body.appendChild(d); const c = getComputedStyle(d).color; d.remove(); return c }',
      'const panelBg = (() => { const p = document.querySelector(".s-sheet__panel[data-state=\\"open\\"]"); return p ? getComputedStyle(p).backgroundColor : "rgb(255,255,255)" })()',
      'const parse = (s) => { const m = String(s).match(/rgba?\\(([^)]+)\\)/); if (!m) return null; const p = m[1].split(",").map(x => parseFloat(x)); return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 } }',
      'const over = (fg, bg) => ({ r: fg.r * fg.a + bg.r * (1 - fg.a), g: fg.g * fg.a + bg.g * (1 - fg.a), b: fg.b * fg.a + bg.b * (1 - fg.a), a: 1 })',
      'const lum = (c) => { const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4) }; return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b) }',
      'const ratio = (a, b) => { const l1 = lum(a), l2 = lum(b); const hi = Math.max(l1, l2), lo = Math.min(l1, l2); return Math.round(((hi + 0.05) / (lo + 0.05)) * 100) / 100 }',
      'const base = parse(panelBg)',
      'const info = (el) => { if (!el) return null; const cs = getComputedStyle(el); const bg = parse(cs.backgroundColor); const fg = parse(cs.color); return { text: (el.innerText || "").trim().slice(0, 16), bgRaw: cs.backgroundColor, colorRaw: cs.color, bgOver: over(bg, base), fgOver: over(fg, bg.a < 1 ? over(bg, base) : bg), ratio: ratio(over(fg, bg.a < 1 ? over(bg, base) : bg), bg.a < 1 ? over(bg, base) : bg) } }',
      'return { panelBg, on: info(document.querySelector(".filterChip-on")), off: info([...document.querySelectorAll(".filterChip")].find(c => !c.classList.contains("filterChip-on"))) }',
    ].join('\n'))
    themeResults[theme].chips = { ...chipCount, ...chips }
    themeResults[theme].chipsShot = await shot(cdp, theme + '-library-chips.png')
    check('chips.onExists.' + theme, '[' + theme + '] 筛选弹层里有选中芯片（.filterChip-on）', !!chips.on, '存在', JSON.stringify(chips.on))
    check('chips.onContrast.' + theme, '[' + theme + '] 选中芯片对比度 ≥ 4.5:1', chips.on && chips.on.ratio >= 4.5, '>=4.5', chips.on ? chips.on.ratio + ':1 (' + chips.on.bgRaw + ' / ' + chips.on.colorRaw + ')' : 'no chip')
    if (chips.off) {
      check('chips.offContrast.' + theme, '[' + theme + '] 未选中芯片对比度 ≥ 4.5:1', chips.off.ratio >= 4.5, '>=4.5', chips.off.ratio + ':1')
    }
    check('chips.count.' + theme, '[' + theme + '] 筛选弹层芯片数 ≥ 20（lead 称 24）', chipCount.chips >= 20, '>=20', 'chips=' + chipCount.chips)
    // .pager：mock 数据只有一页 → pageCount=1 → v-if 不渲染，只能做源码级（CSSOM）核对，写「无法验证」而非伪造通过
    const pagerRules = await evr(cdp, CSSOM_RULE('.pager'))
    const pagerBtnCss = (pagerRules.find((r) => r.sel.indexOf('.pager button') !== -1) || {}).css || ''
    const pagerCurCss = (pagerRules.find((r) => r.sel.indexOf('.pager .cur') !== -1) || {}).css || ''
    themeResults[theme].pager = { present: chipCount.pager, rules: pagerRules.length, btn: pagerBtnCss, cur: pagerCurCss }
    section('pager.' + theme, {
      present: chipCount.pager,
      verdict: chipCount.pager ? '运行时已渲染（可实测）' : '无法验证：mock 数据只有一页 → pageCount=1 → v-if="pageCount > 1 && !loading" 不渲染 .pager',
      cssom: { '.pager button': pagerBtnCss, '.pager .cur': pagerCurCss },
      sourceLevelCheck: /--s-raised/.test(pagerBtnCss) && /--s-text-2/.test(pagerCurCss),
    })
    if (!chipCount.pager) {
      check('pager.ruleSource', '库页 .pager 浅色修复（源码级 CSSOM 核对，非运行时渲染；运行时无法验证）', /--s-raised/.test(pagerBtnCss) && /--s-text-2/.test(pagerCurCss), 'button 用 var(--s-raised) / .cur 用 var(--s-text-2)', JSON.stringify({ btn: pagerBtnCss.slice(0, 130), cur: pagerCurCss.slice(0, 130) }))
    }
    await esc(cdp)
    await cdp.wait(300)

    /* --- 弹层：/dashboard/users 新增用户 --- */
    await nav(cdp, '/dashboard/users', 2600)
    themeResults[theme].addUser = await sheetSuite(cdp, {
      id: 'sheet.addUser.' + theme,
      label: '[' + theme + '] 后台新增用户',
      theme,
      trigger: '.btnAddUser',
      expectScrim: true,
      outside: true,
      shot: theme + '-adminusers-add.png',
    })

    /* --- toast --- */
    await nav(cdp, '/', 2200)
    const tMod = await evr(cdp, 'const m = await import("/src/api/toast.ts"); window.__toastMod = m; return { ok: !!m.showToast, keys: Object.keys(m) }')
    const t0 = await evr(cdp, TOAST_STATE)
    await evr(cdp, 'window.__toastMod.showToast("探针单条"); return 1')
    await cdp.wait(400)
    const t1 = await evr(cdp, TOAST_STATE)
    // 清空已有 toast，保证「连发 3 条」的堆叠数是干净的 3（否则会混进上面那条）
    await evr(cdp, [
      'const ids = [...document.querySelectorAll("[data-toast-id]")].map(i => i.getAttribute("data-toast-id"))',
      'ids.forEach(id => { try { window.__toastMod.closeToast(id) } catch (e) {} })',
      'return ids.length',
    ].join('\n'))
    await cdp.wait(600)
    const tClear = await evr(cdp, TOAST_STATE)
    await evr(cdp, 'window.__toastMod.showToast("堆叠 A"); window.__toastMod.showToast("堆叠 B"); window.__toastMod.showToast("堆叠 C"); return 1')
    await cdp.wait(500)
    const t3 = await evr(cdp, TOAST_STATE)
    themeResults[theme].toast = { mod: tMod, before: t0, one: t1, cleared: tClear, three: t3 }
    check('toast.module.' + theme, '[' + theme + '] 能通过 /src/api/toast.ts 单例弹 toast', tMod.ok && t1.count === t0.count + 1, 'count +1', 'before=' + t0.count + ' after=' + t1.count)
    check('toast.clear.' + theme, '[' + theme + '] closeToast 能清空', tClear.count === 0, 'count=0', 'count=' + tClear.count)
    check('toast.stack.' + theme, '[' + theme + '] 连发 3 条能堆叠（count=3）', t3.count === 3, '3', 'count=' + t3.count + ' ids=' + JSON.stringify(t3.ids))
    const vp = t3.viewport
    check('toast.viewportGeom.' + theme, '[' + theme + '] toast 视口 fixed / z=1200 / 宽 380 / 右下 20', !!vp && vp.pos === 'fixed' && vp.z === '1200' && vp.w === 380 && vp.right === 20 && vp.bottom === 20, 'fixed/z1200/w380/right20/bottom20', JSON.stringify(vp))
    const it = t3.items[0]
    check('toast.itemPosition.' + theme, '[' + theme + '] toast 本体 position ≠ static（旧 bug：文档流顶部 1400px 宽）', !!it && it.pos === 'relative' && it.w < 420, 'relative & w<420', it ? it.pos + ' w=' + it.w : 'none')
    check('toast.itemBg.' + theme, '[' + theme + '] toast 底色 = --s-raised 且不透明（' + theme + '）', !!it && it.bg === t3.raised && !/rgba\(.*,\s*0\)/.test(it.bg), 'bg=' + t3.raised, it ? it.bg : 'none')
    if (it) {
      themeResults[theme].toastShot = await shot(cdp, theme + '-toast.png')
    }
    await cdp.wait(4200)
    const tAfter = await evr(cdp, TOAST_STATE)
    themeResults[theme].toastAfter = tAfter
    check('toast.autoDismiss.' + theme, '[' + theme + '] toast 3.6s 后自动消失', tAfter.count === 0, 'count=0', 'count=' + tAfter.count)

    /* --- 路由转场 --- */
    await nav(cdp, '/', 2200)
    const before = await evr(cdp, 'return location.pathname')
    await evr(cdp, [
      'window.__tr = []',
      'window.__trT0 = performance.now()',
      'const tick = () => {',
      '  const els = [...document.querySelectorAll("[class*=s-page-]")]',
      '  window.__tr.push({ t: Math.round(performance.now() - window.__trT0), els: els.map(e => ({ cls: String(e.className).split(" ").filter(c => c.indexOf("s-page-") === 0).join(","), an: getComputedStyle(e).animationName, op: getComputedStyle(e).opacity })) })',
      '  if (performance.now() - window.__trT0 < 1300) requestAnimationFrame(tick)',
      '}',
      'requestAnimationFrame(tick)',
      'return 1',
    ].join('\n'))
    const searchRect = await clickSel(cdp, 'button.s-iconbtn[aria-label="搜索"]')
    await cdp.wait(1400)
    const after = await evr(cdp, 'return { path: location.pathname, samples: (window.__tr || []).filter(s => s.els.length > 0) }')
    const enterSeen = after.samples.some((s) => s.els.some((e) => e.cls.indexOf('enter-active') !== -1 && e.an === 's-page-in'))
    const leaveSeen = after.samples.some((s) => s.els.some((e) => e.cls.indexOf('leave-active') !== -1 && e.an === 's-fade-out'))
    themeResults[theme].transition = { before, after: after.path, searchRect, sampleCount: after.samples.length, enterSeen, leaveSeen, firstSamples: after.samples.slice(0, 4) }
    check('transition.navigated.' + theme, '[' + theme + '] 点顶栏搜索真的跳转了', after.path === '/search', '/search', after.path)
    check('transition.enter.' + theme, '[' + theme + '] 转场进场类 s-page-enter-active + animation s-page-in 可观测', enterSeen, 'true', 'samples=' + after.samples.length)
    check('transition.leave.' + theme, '[' + theme + '] 转场退场类 s-page-leave-active + animation s-fade-out 可观测', leaveSeen, 'true', 'samples=' + after.samples.length)

    /* --- 裸分支：login / play 故意不套转场（记录，不判失败） --- */
    await nav(cdp, '/play/105', 1800)
    const playTr = await evr(cdp, 'return { cls: document.body.className, pageClasses: document.querySelectorAll("[class*=s-page-]").length }')
    themeResults[theme].bareBranch = playTr
  }
  section('themeResults', themeResults)

  /* ---------- EmbySelect「面板必在视口内」不变式：窄视口 + 超长选项名（lead 中途改过 popupStyle，这里独立复验） ---------- */
  const mob = []
  for (const w of [280, 390]) {
    await cdp.send('Emulation.setDeviceMetricsOverride', { width: w, height: 900, deviceScaleFactor: 1, mobile: false })
    await nav(cdp, '/item/101', 2400)
    const n = await evr(cdp, 'return document.querySelectorAll(".emby-select-facade:not([disabled])").length')
    const rows = []
    for (let i = 0; i < n; i++) {
      const pt = await evr(cdp, 'const e = document.querySelectorAll(".emby-select-facade:not([disabled])")[' + i + ']; const r = e.getBoundingClientRect(); return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }')
      await cdp.click(pt.x, pt.y)
      await cdp.wait(450)
      const injected = await evr(cdp, [
        'const p = document.querySelector(".s-sheet__panel[data-state=\\"open\\"]")',
        'if (!p) return 0',
        'const os = [...p.querySelectorAll("[role=option]")]',
        'os.forEach(o => { o.textContent = "超长音频轨名 Audio Track Name 7.1 TrueHD Atmos".repeat(3) })',
        'return os.length',
      ].join('\n'))
      await cdp.wait(350)
      const m = await evr(cdp, [
        'const p = document.querySelector(".s-sheet__panel[data-state=\\"open\\"]")',
        'if (!p) return null',
        'const r = p.getBoundingClientRect(), cs = getComputedStyle(p)',
        'return { left: Math.round(r.left), right: Math.round(r.right), w: Math.round(r.width), vw: innerWidth, maxWidth: cs.maxWidth, injected: ' + injected + ', overflow: r.right > innerWidth }',
      ].join('\n'))
      rows.push(m)
      await esc(cdp)
      await cdp.wait(350)
    }
    mob.push({ width: w, rows })
    const bad = rows.filter((r) => !r || r.right > r.vw)
    check('embySelect.viewportContain.' + w, 'EmbySelect 在 ' + w + 'px 视口下（选项名已注入超长文本）面板右边缘 ≤ 视口宽', rows.length >= 3 && bad.length === 0, '全部 right ≤ vw', JSON.stringify(rows))
    await shot(cdp, 'embyselect-' + w + 'px.png')
  }
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1600, height: 900, deviceScaleFactor: 1, mobile: false })
  section('embySelect.mobile', mob)

  /* ---------- 截图：首页深浅各一张 ---------- */
  await nav(cdp, '/', 2000)
  const dth = await setTheme(cdp, 'dark')
  section('shotTheme.dark', dth)
  await nav(cdp, '/', 2400)
  await shot(cdp, 'dark-home.png')
  const lth = await setTheme(cdp, 'light')
  section('shotTheme.light', lth)
  await cdp.wait(700)
  check('shot.lightThemeApplied', '截图前确实切到了 light（防止 light 截图其实是 dark）', lth.ok === true, 'resolved=light', JSON.stringify(lth))
  await shot(cdp, 'light-home.png')
  try {
    const dHome = readFileSync(join(SHOTS, 'dark-home.png'))
    const lHome = readFileSync(join(SHOTS, 'light-home.png'))
    check('shot.darkLightDiffer', '深/浅两张首页截图字节不同（证明确实换了主题）', !dHome.equals(lHome), '内容不同', 'dark=' + dHome.length + 'B light=' + lHome.length + 'B identical=' + dHome.equals(lHome))
  } catch (e) {
    check('shot.darkLightDiffer', '深/浅两张首页截图字节不同', false, '内容不同', String(e && e.message).slice(0, 80))
  }

  /* ---------- reduced-motion ---------- */
  await setTheme(cdp, 'dark')
  await cdp.send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: 'reduce' }] })
  await reload(cdp, 3000)
  const rm = await evr(cdp, [
    'const all = document.getAnimations().map(a => { let kf = []; try { kf = a.effect.getKeyframes() } catch (e) {} ; const props = [...new Set(kf.flatMap(k => Object.keys(k)))]; return { name: a.animationName || (a.constructor && a.constructor.name) || "?", play: a.playState, props: props.join(","), transform: kf.some(k => k.transform && k.transform !== "none") } })',
    'const rows = [...document.querySelectorAll(".s-row")]',
    'const items = [...document.querySelectorAll(".s-row-item")]',
    'const hero = document.querySelector(".hero")',
    'const tf = (el) => el ? getComputedStyle(el).transform : null',
    'const idx = (() => { const d = [...document.querySelectorAll(".hero-dot")]; return d.findIndex(x => x.getAttribute("aria-current") === "true") })()',
    'const tok = (n) => getComputedStyle(document.documentElement).getPropertyValue(n).trim()',
    'return {',
    '  total: all.length, transformAnims: all.filter(a => a.transform).length, runningTransform: all.filter(a => a.transform && a.play === "running").length,',
    '  transformList: all.filter(a => a.transform),',
    '  rows: rows.length, items: items.length,',
    '  rowOpacity: rows.map(r => getComputedStyle(r).opacity), rowTransform: rows.map(r => tf(r)),',
    '  itemTransform: items.slice(0, 6).map(i => tf(i)), heroTransform: tf(hero),',
    '  heroIdx: idx, dotFillState: (() => { const f = document.querySelector(".hero-dot-fill"); if (!f) return null; const cs = getComputedStyle(f); return { anim: cs.animationName, play: cs.animationPlayState } })(),',
    '  durToken: tok("--s-dur-slow"), reducedFlag: window.matchMedia("(prefers-reduced-motion: reduce)").matches',
    '}',
  ].join('\n'))
  const rmShot = await shot(cdp, 'reduced-motion-home.png')
  await cdp.wait(9500)
  const rmAfter = await evr(cdp, 'const d = [...document.querySelectorAll(".hero-dot")]; return { heroIdx: d.findIndex(x => x.getAttribute("aria-current") === "true"), running: document.getAnimations().filter(a => a.playState === "running").length }')
  section('reducedMotion', { initial: rm, after9500: rmAfter, shot: rmShot })
  check('rm.detected', 'reduced-motion 被识别（matchMedia + 时长令牌压缩）', rm.reducedFlag === true, 'true', JSON.stringify({ flag: rm.reducedFlag, dur: rm.durToken }))
  check('rm.noRunningTransform', 'reduced 下没有「在跑」的 transform 动画（行/卡片/hero）', rm.runningTransform === 0, 'runningTransform=0', 'running=' + rm.runningTransform + ' total transform-keyframe anims=' + rm.transformAnims + ' ' + JSON.stringify(rm.transformList))
  check('rm.rowsVisible', 'reduced 下行直接是终态（opacity=1，不留在 hidden）', rm.rows > 0 && rm.rowOpacity.every((o) => o === '1'), 'opacity 全 1', JSON.stringify(rm.rowOpacity))
  check('rm.noShift', 'reduced 下卡片/hero 没有位移（transform 为 none/单位矩阵）', [...rm.itemTransform, rm.heroTransform].every((t) => t === 'none' || t === 'matrix(1, 0, 0, 1, 0, 0)'), 'none', JSON.stringify({ items: rm.itemTransform, hero: rm.heroTransform }))
  check('rm.heroPaused', 'reduced 下 hero 不自动轮转（>9.5s 索引不变）', rm.heroIdx === rmAfter.heroIdx, '索引不变', 'before=' + rm.heroIdx + ' after=' + rmAfter.heroIdx + ' dotFill=' + JSON.stringify(rm.dotFillState))
  await cdp.send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: '' }] })

  /* ---------- console ---------- */
  const con = await evr(cdp, 'return { errs: (window.__probe.errs || []).slice(0, 20), warns: (window.__probe.warns || []).slice(0, 20), vueWarns: (window.__probe.warns || []).filter(w => w.indexOf("Vue warn") !== -1).length }')
  section('console', con)
  check('console.errors', '全流程 console 无 error', con.errs.length === 0, '0', JSON.stringify(con.errs))
  check('console.warns', '全流程 console 无 warning（含 Vue warn）', con.warns.length === 0, '0', JSON.stringify(con.warns))
  check('console.vueWarns', 'Vue warn 计数 = 0', con.vueWarns === 0, '0', con.vueWarns)

  /* ---------- 验收对象版本锁定：跑前/跑后哈希一致，否则本次验收对不上代码 ---------- */
  const revAfter = revision()
  section('revision.after', revAfter)
  const revStable = JSON.stringify(revBefore) === JSON.stringify(revAfter)
  const revDiff = Object.keys(revAfter).filter((k) => revAfter[k] !== revBefore[k])
  check('revision.stableDuringRun', '验收期间产品代码零改动（跑前/跑后文件哈希一致）', revStable, '哈希一致', revStable ? '全部一致（' + Object.keys(revBefore).length + ' 个文件）' : '跑动中被改：' + JSON.stringify(revDiff.map((k) => k + ': ' + revBefore[k] + '→' + revAfter[k])))

  /* ---------- 汇总 ---------- */
  const failed = checks.filter((c) => !c.pass)
  const result = {
    probe: 'probe-motion',
    theme: 'task-11 交互与动效优化 独立验收',
    base: BASE,
    startedAt: raw.startedAt,
    finishedAt: new Date().toISOString(),
    total: checks.length,
    passed: checks.length - failed.length,
    failed: failed.length,
    failedIds: failed.map((c) => c.id),
    checks,
    sections: raw.sections,
    shots: SHOTS,
  }
  mkdirSync(VERIFY, { recursive: true })
  writeFileSync(RAW, JSON.stringify(result, null, 2), 'utf8')
  return result
}

/* ============================================================
   6. 自驱动模式：node tools/probe-motion.mjs
   ============================================================ */
const isMain = process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url
if (isMain) {
  const cdpPath = join(HERE, 'cdp.mjs')
  const r = spawnSync(process.execPath, [cdpPath, fileURLToPath(import.meta.url)], { stdio: 'inherit', env: process.env })
  let res = null
  try {
    res = JSON.parse(readFileSync(RAW, 'utf8'))
  } catch (e) {
    console.error('\n[probe-motion] 没能读到结果文件: ' + RAW + ' — ' + String(e && e.message))
    process.exit(2)
  }
  const fails = res.checks.filter((c) => !c.pass)
  console.log('\n================ probe-motion 汇总 ================')
  console.log('断言总数 ' + res.total + ' / 通过 ' + res.passed + ' / 失败 ' + res.failed)
  if (fails.length) {
    for (const f of fails) console.log('  ✗ ' + f.id + ' — ' + f.name + '\n      期望: ' + f.expected + '\n      实测: ' + f.actual)
  } else {
    console.log('  全部通过 ✓')
  }
  console.log('原始数据: ' + RAW)
  console.log('截图目录: ' + SHOTS)
  console.log('==================================================')
  process.exit(fails.length ? 1 : 0)
}
