/**
 * 静态审计：每个视图/页面组件的「模板根节点数量」。
 *
 * 为什么必须查：App.vue 里的 <RouterView v-slot> + <Transition mode="out-in"> 要求
 * 被包住的组件**在运行时有且只有一个根元素**。多根组件会让 Vue 直接告警
 * "Transition expects exactly one child element or component" 并且转场静默失效。
 *
 * 判定规则（比"数标签"严谨）：
 *   - 注释节点、纯空白文本节点不计。
 *   - 根级 <template v-if>/<v-else-if>/<v-else> 链只算「1 个可选根」：
 *     它们互斥，运行时只出现一个，Vue 允许这种写法。
 *   - 非空白文本根单独标出，因为 <Transition> 不接受文本根。
 *
 * 用法：cd web && node tools/audit-roots.mjs [目录，默认 .]
 * 退出码 1 = 有模板解析失败（说明代码当前是坏的）。
 */
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { parse } from 'vue/compiler-sfc'

const ROOT = process.argv[2] || process.cwd()
const files = []

function walk(dir) {
  let entries
  try {
    entries = readdirSync(dir)
  } catch {
    return
  }
  for (const name of entries) {
    const p = join(dir, name)
    const st = statSync(p)
    if (st.isDirectory()) walk(p)
    else if (name.endsWith('.vue')) files.push(p)
  }
}
walk(join(ROOT, 'src/views'))
walk(join(ROOT, 'src/components'))

const COND = new Set(['if', 'else-if', 'else'])

function dirName(node) {
  const d = (node.props || []).find((x) => x.type === 7 && (x.name === 'if' || x.name === 'else'))
  return d ? d.name : null
}

function describe(node) {
  if (node.type === 1) {
    const k = dirName(node)
    return { kind: 'element', tag: node.tag, conditional: COND.has(k || '') ? k : null }
  }
  if (node.type === 12) {
    const k = dirName(node)
    return { kind: 'template', tag: 'template', conditional: COND.has(k || '') ? k : null }
  }
  if (node.type === 2) {
    const t = String(node.content || '').trim()
    return { kind: t ? 'text' : 'blank', tag: '#text', text: t.slice(0, 40) }
  }
  if (node.type === 3) return { kind: 'comment', tag: '#comment' }
  return { kind: 'other:' + node.type, tag: node.tag || '?' }
}

const rows = []
for (const f of files) {
  const { descriptor, errors } = parse(readFileSync(f, 'utf8'), { filename: f })
  const tpl = descriptor.template
  if (!tpl) {
    rows.push({ file: relative(ROOT, f).replace(/\\/g, '/'), roots: 'NO TEMPLATE', detail: [] })
    continue
  }
  const roots = (tpl.ast.children || [])
    .map(describe)
    .filter((r) => r.kind !== 'blank' && r.kind !== 'comment')

  let groups = 0
  let inChain = false
  const detail = []
  for (const r of roots) {
    if (r.conditional === 'if') {
      groups++
      inChain = true
      detail.push(`${r.tag}[v-if]`)
    } else if (r.conditional === 'else-if' || r.conditional === 'else') {
      if (!inChain) groups++
      inChain = false
      detail.push(`${r.tag}[${r.conditional}]`)
    } else {
      groups++
      inChain = false
      detail.push(`${r.tag}${r.kind === 'text' ? '(文本根!)' : ''}`)
    }
  }
  rows.push({
    file: relative(ROOT, f).replace(/\\/g, '/'),
    roots: groups,
    detail,
    parseErrors: errors?.length ? errors.map((e) => String(e.message || e).slice(0, 90)) : undefined,
  })
}

const bad = rows.filter((r) => typeof r.roots === 'number' && r.roots !== 1)
const textRoots = rows.filter((r) => (r.detail || []).some((d) => d.includes('文本根!')))
const broken = rows.filter((r) => r.parseErrors)

console.log('=== 单根检查（roots !== 1 的都要处理） ===')
console.log(`共 ${rows.length} 个 .vue（views + components）`)
console.log(`单根 ${rows.filter((r) => r.roots === 1).length} / 多根或零根 ${bad.length}`)
if (bad.length) {
  console.log('')
  for (const r of bad) console.log(`  x roots=${r.roots}  ${r.file}  ->  ${(r.detail || []).join(' + ')}`)
}
if (textRoots.length) {
  console.log('\n=== 含文本根的组件（<Transition> 不接受文本根） ===')
  for (const r of textRoots) console.log(`  ! ${r.file}  ->  ${r.detail.join(' + ')}`)
}
if (broken.length) {
  console.log('\n=== 模板解析失败（代码当前是坏的） ===')
  for (const r of broken) console.log(`  x ${r.file}  ->  ${r.parseErrors.join(' | ')}`)
}
console.log('\n=== 全量 ===')
for (const r of rows) console.log(`  ${String(r.roots).padStart(2)}  ${r.file}  ${(r.detail || []).join(' + ')}`)

process.exit(broken.length ? 1 : 0)
