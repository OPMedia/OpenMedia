/**
 * 首页分步行懒加载（task-13 C）。
 *
 * 真机（22 个媒体库）首页一次性渲染 23 行 / 267 张卡 / 5500+ DOM 节点、scrollHeight 9778px，
 * 首屏实际只看得到 hero + 1 行，其余全在白屏之外挂着。这里把「挂载」和「行序」拆开：
 *
 * - 行序仍然由 HomeView 的 `rows`（= 设置页 homeSection* 槽位顺序，铁律 12）决定，
 *   本 composable **只决定某一行此刻挂不挂载**，不重排、不筛选、不丢行；
 * - 首屏直出前 `initialRows` 条非常驻行（默认 4 条）+ 全部常驻行（hero）；
 * - 其余行在模板里渲染成**同尺寸骨架占位**（占位行不是 `.s-row`，所以 .s-row 数量会随滚动增长），
 *   等它顶部进入「滚动容器视口底部 + preloadPx」之内，再换成真行；
 * - 占位与真行同宽同高 → CLS≈0，滚动条不跳；滚到底后全部收敛为真行。
 *
 * 铁律 12：hero 是 homeSection* 里的一项，**永远不能被卸载**，所以调用方把 hero 标成
 * `persistent`；它既不进占位池，也不占「首屏 N 行」的名额。
 * 铁律 16：本 composable 不做任何动画（只做挂载/不挂载），骨架与真行尺寸一致，
 * 因此不引入位移，也就不需要再过 reduced-motion —— reduce 时行为完全相同。
 */
import { computed, nextTick, onBeforeUnmount, ref, type ComputedRef, type Ref } from 'vue'

/** 行在本 composable 眼里只需要这两个字段（HomeRow 结构兼容） */
export interface ProgressiveRow {
  key: string
  /** true = 常驻行（首页 hero），永不进占位池、不占首屏名额 */
  persistent?: boolean
}

export interface UseProgressiveRowsOptions {
  /** 行序（含 hero 占位行），顺序即渲染顺序 */
  rows: ComputedRef<ProgressiveRow[]>
  /** 滚动容器（HomeView 的 .s-home） */
  scroller: Ref<HTMLElement | null>
  /** 行容器（HomeView 的 .s-home-inner）：占位行的直接父级 */
  container: Ref<HTMLElement | null>
  /** 首屏直出的行数（不含常驻行），默认 4 */
  initialRows?: number
  /** 提前量：占位行顶部进入「视口底部 + preloadPx」内就挂载，默认 600px（≈1.5 行） */
  preloadPx?: number
  /** 占位行类名，默认 s-row-ph */
  placeholderClass?: string
}

/** 首屏直出行数（可配置常量，验收按这个数核对） */
export const HOME_INITIAL_ROWS = 4
/** 滚动提前量：一行 356px，600px ≈ 1.7 行，用户看不到换行那一刻 */
export const HOME_ROW_PRELOAD_PX = 600

/** 单轮 sync 最多补几趟（每趟至少挂一行，防死循环） */
const MAX_PASSES = 8

export function useProgressiveRows(opts: UseProgressiveRowsOptions) {
  const initialRows = opts.initialRows ?? HOME_INITIAL_ROWS
  const preloadPx = opts.preloadPx ?? HOME_ROW_PRELOAD_PX
  const placeholderClass = opts.placeholderClass ?? 's-row-ph'

  /** 首屏直出的 key：按行序取前 N 条非常驻行 */
  const eagerKeys = computed(() => {
    const keys = new Set<string>()
    let taken = 0
    for (const row of opts.rows.value) {
      if (row.persistent) continue
      if (taken >= initialRows) break
      keys.add(row.key)
      taken++
    }
    return keys
  })

  /**
   * 按需挂载过的 key。只增不减：数据刷新（切 tab / 重新 load）时已挂载的行不退回占位，
   * 否则用户会看到自己刚看过的行变回骨架。
   */
  const lazyKeys = ref<Set<string>>(new Set())

  function isMounted(row: ProgressiveRow): boolean {
    return Boolean(row.persistent) || eagerKeys.value.has(row.key) || lazyKeys.value.has(row.key)
  }

  /** 还没挂载的行数（0 = 全部挂载，滚动回调可以直接短路） */
  const pendingCount = computed(() => opts.rows.value.reduce((n, row) => (isMounted(row) ? n : n + 1), 0))

  let raf = 0
  let scheduled = false
  let settleTimer = 0

  /**
   * 挂载完一批之后补一次「落地复查」。
   *
   * 为什么必须有：一次 mountPass 之后 DOM 要重建（占位 → motion.section + N 张卡），
   * 期间行内自动动画 / 图片回流会让布局短暂处于中间态；如果用户此时刚好滚到底并停手，
   * 就不会再有新的 scroll 事件来触发第二轮，最后几行会卡在占位上。
   * 实测复现过：滚动到底后 12 行挂载、13 行仍是占位，且 scrollHeight 已经稳定（高度对所以没有事件）。
   * 这里在挂载过后 260ms（> 自动动画 250ms）无条件复查一次；复查若挂载 0 行就自然停止，不会自旋。
   */
  function scheduleSettle(): void {
    if (settleTimer) return
    settleTimer = window.setTimeout(() => {
      settleTimer = 0
      void sync()
    }, 260)
  }
  /**
   * 一趟：把「已经进入提前量」的占位行标成已挂载。
   * 用 getBoundingClientRect 而不是 offsetTop —— 行内 cardBox 的外边距、
   * auto-animate 临时加的 transform 都不会让换算错位。
   */
  function mountPass(): number {
    if (pendingCount.value === 0) return 0
    const scroller = opts.scroller.value
    const container = opts.container.value
    if (!scroller || !container) return 0
    const view = scroller.getBoundingClientRect()
    const line = view.bottom + preloadPx
    const next = new Set(lazyKeys.value)
    let added = 0
    for (const el of Array.from(container.children)) {
      if (!(el instanceof HTMLElement) || !el.classList.contains(placeholderClass)) continue
      const key = el.dataset.rowKey
      if (!key || next.has(key)) continue
      if (el.getBoundingClientRect().top <= line) {
        next.add(key)
        added++
      }
    }
    if (added) lazyKeys.value = next
    return added
  }

  /**
   * 补到「提前量之内没有剩余占位行」为止。
   * 一趟挂上来的真行会把后面的行顶上来（也可能更矮），所以要等 DOM 落地再量一遍。
   * 结束后再排一次落地复查（见 scheduleSettle）：挂载过才排，避免空转。
   */
  async function sync(): Promise<number> {
    let total = 0
    for (let pass = 0; pass < MAX_PASSES; pass++) {
      const added = mountPass()
      if (!added) break
      total += added
      await nextTick()
    }
    if (total > 0) scheduleSettle()
    return total
  }

  /** 滚动回调：rAF 合并，一帧最多算一次 */
  function onScroll(): void {
    if (pendingCount.value === 0 || scheduled) return
    scheduled = true
    raf = requestAnimationFrame(() => {
      scheduled = false
      raf = 0
      void sync()
    })
  }

  onBeforeUnmount(() => {
    if (raf) cancelAnimationFrame(raf)
    if (settleTimer) clearTimeout(settleTimer)
  })

  return { isMounted, eagerKeys, lazyKeys, pendingCount, onScroll, sync }
}
