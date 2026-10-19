/**
 * 极简 CDP 客户端(Node 24 自带全局 WebSocket,零依赖)。
 * 用法:node tools/cdp.mjs <script-file.mjs>
 * 脚本文件里可用:cdp.send(method, params)、cdp.eval(expr)、cdp.hover(x,y)、cdp.click(x,y)、
 *               cdp.screenshot(path)、cdp.wait(ms)、console.log 结果由 JSON 输出。
 */
import { readFileSync } from 'node:fs'
import { pathToFileURL } from 'node:url'

const PORT = process.env.CDP_PORT || '9222'
const BASE = `http://127.0.0.1:${PORT}`

async function createTarget(url) {
  const r = await fetch(`${BASE}/json/new?${encodeURIComponent(url)}`, { method: 'PUT' })
  if (!r.ok) throw new Error(`/json/new failed: ${r.status}`)
  return r.json()
}

class Cdp {
  constructor(ws) {
    this.ws = ws
    this.id = 0
    this.pending = new Map()
    this.events = []
    ws.addEventListener('message', (ev) => {
      const msg = JSON.parse(ev.data)
      if (msg.id && this.pending.has(msg.id)) {
        const { resolve, reject } = this.pending.get(msg.id)
        this.pending.delete(msg.id)
        msg.error ? reject(new Error(JSON.stringify(msg.error))) : resolve(msg.result)
      } else if (msg.method) {
        this.events.push(msg)
      }
    })
  }
  send(method, params = {}) {
    const id = ++this.id
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject })
      this.ws.send(JSON.stringify({ id, method, params }))
      setTimeout(() => {
        if (this.pending.has(id)) {
          this.pending.delete(id)
          reject(new Error(`timeout ${method}`))
        }
      }, 45000)
    })
  }
  async eval(expr) {
    const wrapped = `(async () => { ${expr} })()`
    const r = await this.send('Runtime.evaluate', {
      expression: wrapped, awaitPromise: true, returnByValue: true,
    })
    if (r.exceptionDetails) throw new Error('eval exception: ' + JSON.stringify(r.exceptionDetails).slice(0, 600))
    return r.result.value
  }
  async hover(x, y) {
    await this.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y, buttons: 0 })
  }
  async click(x, y) {
    await this.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y, buttons: 0 })
    await this.send('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', clickCount: 1, buttons: 1 })
    await this.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', clickCount: 1, buttons: 0 })
  }
  async screenshot(path) {
    const r = await this.send('Page.captureScreenshot', { format: 'png' })
    const { writeFileSync } = await import('node:fs')
    writeFileSync(path, Buffer.from(r.data, 'base64'))
    return path
  }
  wait(ms) {
    return new Promise((r) => setTimeout(r, ms))
  }
}

const scriptFile = process.argv[2]
if (!scriptFile) {
  console.error('usage: node tools/cdp.mjs <script.mjs>')
  process.exit(2)
}

const target = await createTarget('about:blank')
const ws = new WebSocket(target.webSocketDebuggerUrl)
await new Promise((res, rej) => {
  ws.addEventListener('open', res, { once: true })
  ws.addEventListener('error', rej, { once: true })
})
const cdp = new Cdp(ws)
await cdp.send('Page.enable')
await cdp.send('Runtime.enable')
await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1600, height: 900, deviceScaleFactor: 1, mobile: false })

const mod = await import(pathToFileURL(scriptFile).href)
const out = await mod.default(cdp)
console.log(JSON.stringify(out, null, 2))
ws.close()
process.exit(0)
