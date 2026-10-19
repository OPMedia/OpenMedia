/**
 * 图片占位生成器:名称哈希 → 双色渐变 + 首字。
 * 供 MSW handlers 直接以 image/svg+xml 返回,替代真实海报图。
 */
const PALETTES: [string, string][] = [
  ['#0f2027', '#2c5364'], ['#141e30', '#243b55'], ['#1a2a6c', '#b21f1f'],
  ['#2b5876', '#4e4376'], ['#355c7d', '#6c5b7b'], ['#41295a', '#2f0743'],
  ['#134e5e', '#71b280'], ['#3e5151', '#decba4'], ['#232526', '#414345'],
  ['#1f4037', '#99f2c8'], ['#0b0e1a', '#3b4371'], ['#3a1c71', '#d76d77'],
]

function hash(str: string): number {
  let h = 0
  for (let i = 0; i < str.length; i++) h = (h * 31 + str.charCodeAt(i)) >>> 0
  return h
}

export function posterSvg(title: string, opts: { width?: number; height?: number; subtitle?: string } = {}): string {
  const w = opts.width ?? 300
  const h = opts.height ?? 450
  const [c1, c2] = PALETTES[hash(title) % PALETTES.length] ?? ['#232526', '#414345']
  const char = title[0] ?? '?'
  const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  const fs = Math.round(w * 0.42)
  const sub = opts.subtitle
  const subSvg = sub
    ? `<text x="50%" y="${h - Math.round(h * 0.08)}" text-anchor="middle" fill="rgba(255,255,255,.72)" font-size="${Math.round(w * 0.09)}" font-family="system-ui, sans-serif">${esc(sub)}</text>`
    : ''
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">
<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
<stop offset="0" stop-color="${c1}"/><stop offset="1" stop-color="${c2}"/>
</linearGradient></defs>
<rect width="${w}" height="${h}" fill="url(#g)"/>
<circle cx="${w * 0.85}" cy="${h * 0.16}" r="${w * 0.18}" fill="rgba(255,255,255,.06)"/>
<circle cx="${w * 0.15}" cy="${h * 0.78}" r="${w * 0.22}" fill="rgba(0,0,0,.08)"/>
<text x="50%" y="50%" dy="${Math.round(fs * 0.35)}" text-anchor="middle" fill="rgba(255,255,255,.92)"
 font-family="system-ui, sans-serif" font-weight="700" font-size="${fs}">${esc(char)}</text>
${subSvg}
</svg>`
}

/** 宽幅背景图(详情页/首页用) */
export function backdropSvg(title: string, width = 1280, height = 720): string {
  const [c1, c2] = PALETTES[hash(title) % PALETTES.length] ?? ['#232526', '#414345']
  const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}">
<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
<stop offset="0" stop-color="${c1}"/><stop offset="1" stop-color="${c2}"/>
</linearGradient></defs>
<rect width="${width}" height="${height}" fill="url(#g)"/>
<circle cx="${width * 0.8}" cy="${height * 0.2}" r="${width * 0.16}" fill="rgba(255,255,255,.05)"/>
<circle cx="${width * 0.2}" cy="${height * 0.85}" r="${width * 0.12}" fill="rgba(0,0,0,.10)"/>
<text x="8%" y="88%" fill="rgba(255,255,255,.16)" font-size="${Math.round(width * 0.05)}"
 font-family="system-ui, sans-serif" font-weight="700">${esc(title)}</text>
</svg>`
}

/** 用户头像 */
export function avatarSvg(name: string, size = 160): string {
  const [c1, c2] = PALETTES[hash(name) % PALETTES.length] ?? ['#232526', '#414345']
  const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  const fs = Math.round(size * 0.42)
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">
<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
<stop offset="0" stop-color="${c1}"/><stop offset="1" stop-color="${c2}"/>
</linearGradient></defs>
<rect width="${size}" height="${size}" fill="url(#g)"/>
<text x="50%" y="50%" dy="${Math.round(fs * 0.35)}" text-anchor="middle" fill="rgba(255,255,255,.95)"
 font-family="system-ui, sans-serif" font-weight="700" font-size="${fs}">${esc(name[0] ?? '?').toUpperCase()}</text>
</svg>`
}