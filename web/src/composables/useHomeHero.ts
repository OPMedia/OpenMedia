/**
 * 首页 Hero 数据池 —— 两条查询合并成候选，再按「能不能当横版 banner」筛。
 *
 * 产品决策（顺序不可调）：
 *   ① 继续观看前 4 条（Filters=IsResumable、SortBy=DatePlayed、Descending）
 *   ② 最新入库前 40 条（Movie,Series、SortBy=DateCreated、Descending），从这里补足
 * 合并后按 Id 去重、**只保留拿得到 ≈16:9 大图的条目**，最终上限 6 条。
 * 两条请求 Promise.allSettled，任一失败不致命。
 *
 * ── 为什么原来的第 ③ 条「最新剧集」被删掉了（真机实测，不是口味问题）──
 *
 * 实测真机 4.9.5.0 的 `IncludeItemTypes=Series&SortBy=DateCreated&Limit=12`：
 *   **12/12 条 `BackdropImageTags: []`、`PrimaryImageAspectRatio: 0.667`** ——
 *   这批新剧只有竖版海报，一条横版剧照都没有。也就是说「最新剧集」这条查询
 *   在结构上**永远产不出 banner 主图**，只会把 hero 拖到降级分支上去。
 *   而且 Emby 的 `ImageTypes=Backdrop` 过滤参数实测被**静默忽略**（带上与不带返回逐条相同），
 *   没法让服务端帮忙筛，只能在客户端筛。
 *   所以改成一条更宽的「最新入库」查询（同时含 Movie 和 Series），再客户端筛横版图。
 *
 * ── `Recursive` 是硬契约，别省 ──
 *
 * 实测同一个查询：
 *   `IncludeItemTypes=Movie&Filters=IsUnplayed&Limit=6`                 → total=21，类型全是 CollectionFolder
 *   `IncludeItemTypes=Movie&Filters=IsUnplayed&Limit=6&Recursive=true`  → total=24619，类型全是 Movie
 * 非递归时 Emby 把 `IncludeItemTypes` **整个忽略**，返回的是用户根下的 21 个媒体库本身。
 * 这条曾经让首页 hero 的 6 条候选里有 3 条是「追新剧集 / 追新电影 / 国产剧」这些**库**，
 * 渲染成没有任何简介、眉标写着裸 `COLLECTIONFOLDER` 的怪卡片。两条查询都必须带 `Recursive=true`。
 *
 * ── 必须带 Fields ──
 * Emby 列表接口默认不返回 Overview，不带就没有简介可显示；
 * 不带 `PrimaryImageAspectRatio` 就没法判断 Primary 是横图还是竖图（见 endpoints.wideArtOf）。
 */
import { ref, toValue, watch, type MaybeRefOrGetter, type Ref } from 'vue'
import type { BaseItemDto } from '@/api/types'
import { getItems, hasWideArt } from '@/api/endpoints'

/** 池子最终上限（hero 首屏最多 6 张可切：1 张主图 + 右侧 2×3 轮转缩略图栏） */
const HERO_POOL_LIMIT = 6
/** ① 继续观看取几条 */
const RESUME_LIMIT = 4
/** ② 最新入库取几条（客户端要按"有没有横版图"筛，所以多取一些） */
const DISCOVER_LIMIT = 40

/** 能进 hero 的条目类型。**不含 CollectionFolder**：那是媒体库本身，不是内容。 */
const HERO_TYPES = new Set(['Movie', 'Series', 'Episode'])

/** 列表接口必须显式索要的字段 */
const HERO_FIELDS = [
  'Overview',
  'Genres',
  'Taglines',
  'OfficialRating',
  'CommunityRating',
  'RunTimeTicks',
  'ImageTags',
  'BackdropImageTags',
  // 剧集/季自己不带 backdrop（BackdropImageTags 为空数组），大图在父级系列上。
  // 不显式要这两个字段，hero 抽到剧集时就没有背景图。
  'ParentBackdropItemId',
  'ParentBackdropImageTags',
  // 判断 Primary 是横图还是竖图（库/少数影片的横版图挂在 Primary 上）
  'PrimaryImageAspectRatio',
  // 剧集显示剧名 + S/E 定位要用
  'SeriesName',
  'ParentIndexNumber',
  'IndexNumber',
  'ProductionYear',
  'DateCreated',
].join(',')

export interface HomeHeroState {
  items: Ref<BaseItemDto[]>
  loading: Ref<boolean>
  reload: () => Promise<void>
}

export function useHomeHero(userId: MaybeRefOrGetter<string | undefined>): HomeHeroState {
  const items = ref<BaseItemDto[]>([])
  const loading = ref(false)

  /** 请求序号：userId 变化/重复 reload 时，只认最后一次结果，避免旧响应覆盖新数据 */
  let requestSeq = 0

  async function load(): Promise<void> {
    const uid = toValue(userId)
    // userId 未就绪（登录态未落地）→ 不发请求，直接空态
    if (!uid) {
      requestSeq += 1
      items.value = []
      loading.value = false
      return
    }

    const seq = ++requestSeq
    loading.value = true
    try {
      const settled = await Promise.allSettled([
        // ① 继续观看前 4 条
        getItems(uid, {
          Filters: 'IsResumable',
          SortBy: 'DatePlayed',
          SortOrder: 'Descending',
          Limit: RESUME_LIMIT,
          Recursive: true,
          IncludeItemTypes: 'Movie,Episode',
          Fields: HERO_FIELDS,
        }),
        // ② 最新入库前 40 条（Recursive 不能省，见文件头）
        getItems(uid, {
          IncludeItemTypes: 'Movie,Series',
          SortBy: 'DateCreated',
          SortOrder: 'Descending',
          Limit: DISCOVER_LIMIT,
          Recursive: true,
          Fields: HERO_FIELDS,
        }),
      ])

      // 期间又发起了新请求（userId 变了）→ 丢弃本次结果
      if (seq !== requestSeq) return

      const batchOf = (i: number): BaseItemDto[] =>
        settled[i]?.status === 'fulfilled' ? (settled[i].value.Items ?? []) : []

      // 严格按 ①② 的优先级拼池 + 按 Id 去重 + 截到 6 条。
      // `usable` = 类型是内容 且 拿得到横版大图；这是 hero 能好看的前提。
      const merged: BaseItemDto[] = []
      const fallback: BaseItemDto[] = []
      const seen = new Set<string>()
      for (const batch of [batchOf(0), batchOf(1)]) {
        for (const item of batch) {
          if (!item?.Id || seen.has(item.Id)) continue
          if (!HERO_TYPES.has(item.Type)) continue
          seen.add(item.Id)
          if (hasWideArt(item)) merged.push(item)
          else fallback.push(item)
          if (merged.length >= HERO_POOL_LIMIT) break
        }
        if (merged.length >= HERO_POOL_LIMIT) break
      }

      // 兜底：整池都挑不出横版大图（服务器还没刮到剧照时会发生）→ 退回不过滤的池子，
      // hero 会走品牌渐变主图。宁可难看一点，也不能让首屏开天窗。
      items.value = merged.length ? merged : fallback.slice(0, HERO_POOL_LIMIT)
    } finally {
      if (seq === requestSeq) loading.value = false
    }
  }

  // userId 从 undefined → 有值（登录/刷新用户）时自动重新取数
  watch(() => toValue(userId), () => void load(), { immediate: true })

  return { items, loading, reload: load }
}
