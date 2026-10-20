/**
 * 首页 Hero 数据池 —— **一次** `GET /Users/{userId}/Suggestions`（服务端随机推荐），
 * 客户端只做「这条能不能当横版 banner」的类型归一化、去重与补足。
 *
 * ── 契约来源（本仓后端自己的实现，不是猜的）──
 *
 *   `OpenMedia/MediaBrowser.Api/SuggestionsService.cs`
 *     `[Route("/Users/{UserId}/Suggestions", "GET")]`
 *     参数：`MediaType` / `Type` / `UserId` / `EnableTotalRecordCount` / `StartIndex` / `Limit`
 *     实现：`OrderBy = ItemSortBy.Random`(Descending)、`IncludeItemTypes = Type`、
 *           `MediaTypes = MediaType`、`IsVirtualItem = false`、`Recursive = true`
 *
 *   也就是说 `/Users/{uid}/Suggestions?MediaType=Video&Type=Movie,Series&Limit=6&EnableTotalRecordCount=false`
 *   就是「从库里**随机**挑 N 个影片/剧集」，随机在**服务端**做。
 *   `src/api/endpoints.ts` 里没有这个端点的封装函数，而该文件不在本次改动范围，
 *   所以这里用 `api/client` 的 `request()` 直接拼 URL（鉴权头 / `api_key` 由它统一处理）。
 *
 *   ⚠️ `Type` 是**我们后端**的入参名（映射到 `IncludeItemTypes`）。夹具 Emby 4.9.5 不认它：
 *   实测（scratch/lead3/hero/contract.txt）带 `Type=Movie,Series` 时 Limit=6 仍回来
 *   4 Episode + 1 Folder + 1 Series，Limit=24 回来 18 Episode + 3 Season + 1 Folder + 1 Series + 1 Movie。
 *   所以**防御性过滤不能省**（见下）。
 *
 * ── 为什么必须归一化 + 过滤（真机夹具 4.9.5 实测，不是理论风险）──
 *
 *   · `Movie` / `Series` → 直接用（拿不到横版图时由 HomeHero 走品牌渐变兜底，不去请求必然 5xx 的地址）；
 *   · `Episode` → 自己 `BackdropImageTags: []`，横版大图挂在**父级系列**上
 *     （`SeriesId` + `ParentBackdropImageTags[0]`）。有这两样就**映射成它所属的剧**：
 *     标题用 `SeriesName`、跳转用 `SeriesId`、图用 `/Items/{SeriesId}/Images/Backdrop?tag={父级 tag}`；
 *     两者缺一（实测确实有 `pbd=0` 的集）就**丢弃** —— 不许退化成"必然 404 的地址"或"没有图的怪卡片"。
 *   · `Folder` / `Season` / 其它 → **丢弃**（它们是目录/季，不是作品）。
 *
 * ── 去重（现在仍然需要）──
 *
 * 随机可能一次抽到同一部剧的好几集，映射成剧之后它们的横版图 key 完全相同
 * （`wideArtKey` = `type:id:tag`）、`SeriesName` 也相同 —— 三个圆点点开是"同一张图 + 同一个标题"，
 * 用户会直接判定轮播坏了（上一轮真机实测：6 个圆点里第 3/4/5 个都是
 * `Items/1668375/Images/Backdrop`）。按 Id、横版图 key、系列 key 三条各记一次账。
 *
 * ── 补足：只允许再发一次更大的 Limit ──
 *
 * 过滤后不足 `HERO_POOL_LIMIT` 时，再发一次 `Limit=SUGGEST_REFILL_LIMIT` 并**追加**到池尾
 * （首批随机结果永远优先）。池子里**只允许有随机推荐**：
 * 「继续观看 / 最近播放 / 最新入库」三个来源连同 `hasPlaybackTrace()` 一并删除
 * （用户口径 2026-10-20：「Banner 栏只显示随机推荐的条目」）。
 *
 * ── Fields 必须带（实测）──
 *
 * 夹具实测：同一批 24 条里，不带 `Fields` → **0 条**有非空 `Overview`；
 * 带上 `Fields` → 15 条有（scratch/lead3/hero/fields.txt）。banner 的简介就读 `Overview`，
 * 不带就是"只有标题和按钮"的空壳。我们自己的 SuggestionsService 没实现 `IHasItemFields`，
 * 走默认 `DtoOptions`（allFields）时本来就有 —— 带上是为了对齐夹具/上游行为，不是多余参数。
 */
import { ref, toValue, watch, type MaybeRefOrGetter, type Ref } from 'vue'
import type { BaseItemDto, QueryResult } from '@/api/types'
import { hasWideArt, wideArtOf } from '@/api/endpoints'
import { request } from '@/api/client'

/** 池子最终上限（hero 首屏最多 6 张可切：1 张主图 + 右侧轮转） */
const HERO_POOL_LIMIT = 6
/** 首次随机推荐取几条 */
const SUGGEST_LIMIT = HERO_POOL_LIMIT
/**
 * 补足用的大 Limit。实测夹具 Limit=24 里"能当 banner"的有 16~19 条（67%~79%），
 * Limit=6 的期望可用数只有 4~5 条 —— 所以补足这一发**经常**要打，
 * 但只打一次：打完还不足就接受更短的池子，绝不为了凑数放宽过滤条件。
 */
const SUGGEST_REFILL_LIMIT = 24

/**
 * 列表接口必须显式索要的字段（理由见文件头「Fields 必须带」）。
 * `ParentBackdropItemId` / `ParentBackdropImageTags` 是剧集映射到剧的**唯一**图源，
 * `UserData` 是续播进度条的唯一来源，都不能省。
 */
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
  'ParentBackdropItemId',
  'ParentBackdropImageTags',
  // 判断 Primary 是横图还是竖图（库/少数影片的横版图挂在 Primary 上）
  'PrimaryImageAspectRatio',
  // 剧集显示剧名要用
  'SeriesName',
  'ParentIndexNumber',
  'IndexNumber',
  'ProductionYear',
  'DateCreated',
  // 续播进度条读它
  'UserData',
].join(',')

export interface HomeHeroState {
  items: Ref<BaseItemDto[]>
  loading: Ref<boolean>
  reload: () => Promise<void>
}

/** 能**直接**当 banner 的类型。不含 Episode（要映射）、不含 CollectionFolder（那是媒体库本身） */
const DIRECT_TYPES = new Set(['Movie', 'Series'])

/**
 * 随机推荐接口。`Type` → 服务端 `IncludeItemTypes`，`MediaType=Video` 排除音乐/图片库。
 * `EnableTotalRecordCount=false`：hero 不用总数，让服务端少算一次 count（传**布尔**，不是字符串）。
 */
function getSuggestions(userId: string, limit: number): Promise<QueryResult> {
  return request<QueryResult>(`/Users/${userId}/Suggestions`, {
    query: {
      MediaType: 'Video',
      Type: 'Movie,Series',
      Limit: limit,
      EnableTotalRecordCount: false,
      Fields: HERO_FIELDS,
    },
  })
}

/**
 * 把一条随机推荐**归一化成"可以当 banner 的作品"**；不能当的返回 null（见文件头）。
 *
 * Episode → 所属剧的映射为什么要把 `Id` 换成 `SeriesId`：
 *   · 跳转（`openItem` / `playItem`）与圆点 key 都用 `Id`，换成 SeriesId 就自然落到剧详情/剧播放；
 *   · `wideArtOf()` 解析 `BackdropImageTags[0]` 时会用 `item.Id` 当图片宿主，
 *     所以把**父级 tag 写进 `BackdropImageTags`** 就得到 `/Items/{SeriesId}/Images/Backdrop?tag={父级 tag}`，
 *     正好是任务口径要的那个 URL（同时不再需要 `ParentBackdropItemId`）。
 * 归一化时**主动清掉**三样东西，避免"张冠李戴"：
 *   · `ImageTags`：剧集的 Primary tag 属于**那一集**，Id 换成系列后继续挂着会拼出必然 5xx 的地址；
 *   · `ParentBackdrop*`：图已经落到自己身上，留着会让 `backdropOf` 的优先级判断变模糊；
 *   · `UserData`：进度/已看状态都是**那一集**的，挂到"剧"上会出现"剧名 + 某集进度条"的错配。
 *     （清掉后 `hasProgress=false` → 主按钮回落到 `hero.play`「播放」，与"随机推荐"语义一致。）
 *
 * 保留 `Overview`：接口给的就是这条内容的简介（剧集给的是那一集的剧情简介），
 * 比"没有简介"好；真要显示整部剧的简介得再打一次剧详情接口，hero 不值得为它多等一次往返。
 */
function toHeroItem(raw: BaseItemDto | undefined): BaseItemDto | null {
  if (!raw?.Id) return null
  if (DIRECT_TYPES.has(raw.Type)) return raw
  if (raw.Type !== 'Episode') return null
  const seriesId = raw.SeriesId
  const backdropTag = raw.ParentBackdropImageTags?.[0]
  if (!seriesId || !backdropTag) return null
  return {
    ...raw,
    Id: seriesId,
    Type: 'Series',
    Name: raw.SeriesName || raw.Name,
    SeriesName: raw.SeriesName,
    BackdropImageTags: [backdropTag],
    ImageTags: undefined,
    ParentBackdropItemId: undefined,
    ParentBackdropImageTags: undefined,
    PrimaryImageAspectRatio: undefined,
    UserData: undefined,
  }
}

/**
 * 横版图 key —— 同一个 key 就是**同一张图**。
 * 用归一化后的归属（剧集的图已落到 `SeriesId` 上），所以同一部剧的几集算出同一个 key。
 * 拿不到横版图的条目返回 null（它们进 brand 列表，不参与图的去重）。
 */
function wideArtKey(item: BaseItemDto): string | null {
  const art = wideArtOf(item)
  return art ? `${art.type}:${art.id}:${art.tag ?? ''}` : null
}

/** 系列 key：同一部剧只出一个圆点。电影没有 SeriesName，退回片名 */
function seriesKey(item: BaseItemDto): string {
  return item.SeriesName || item.Name || item.Id || ''
}

/**
 * 按传入批次**顺序**拼池（首批随机结果优先），返回最多 `HERO_POOL_LIMIT` 条。
 *
 * 两条账：
 *   · `art` / `brand` —— **有横版图**的排前面（首屏第一张就是真图），
 *     只有品牌渐变的垫后（HomeHero 对拿不到图的条目本来就渲染品牌渐变，不是空窗）；
 *   · `takenArt` / `takenSeries` —— 图会撞 / 同一部剧只留一条（理由见文件头）。
 */
function pickPool(batches: BaseItemDto[][]): BaseItemDto[] {
  const art: BaseItemDto[] = []
  const brand: BaseItemDto[] = []
  const seenIds = new Set<string>()
  const takenArt = new Set<string>()
  const takenSeries = new Set<string>()
  for (const batch of batches) {
    for (const raw of batch) {
      const item = toHeroItem(raw)
      if (!item || seenIds.has(item.Id)) continue
      seenIds.add(item.Id)
      const withArt = hasWideArt(item)
      const list = withArt ? art : brand
      if (list.length >= HERO_POOL_LIMIT) continue
      const artKey = withArt ? wideArtKey(item) : null
      if (artKey && takenArt.has(artKey)) continue
      const series = seriesKey(item)
      if (takenSeries.has(series)) continue
      if (artKey) takenArt.add(artKey)
      takenSeries.add(series)
      list.push(item)
    }
  }
  return [...art, ...brand].slice(0, HERO_POOL_LIMIT)
}

export function useHomeHero(userId: MaybeRefOrGetter<string | undefined>): HomeHeroState {
  const items = ref<BaseItemDto[]>([])
  const loading = ref(false)

  /** 请求序号：userId 变化/重复 reload 时，只认最后一次结果，避免旧响应覆盖新数据 */
  let requestSeq = 0

  /** 发一次随机推荐；失败不致命（返回空批，交给调用方决定要不要补足/空态） */
  async function suggestBatch(uid: string, limit: number): Promise<BaseItemDto[]> {
    return getSuggestions(uid, limit)
      .then((r) => r.Items ?? [])
      .catch(() => [] as BaseItemDto[])
  }

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
      const first = await suggestBatch(uid, SUGGEST_LIMIT)
      // 期间又发起了新请求（userId 变了）→ 丢弃本次结果
      if (seq !== requestSeq) return

      let pool = pickPool([first])
      // 补足：过滤后不足 6 条时再发一次更大的 Limit（只一次，见文件头）。
      // 首批请求失败时这里也兜一次重试，等于"网络抖一下不会让首屏开天窗"。
      if (pool.length < HERO_POOL_LIMIT) {
        const more = await suggestBatch(uid, SUGGEST_REFILL_LIMIT)
        if (seq !== requestSeq) return
        pool = pickPool([first, more])
      }

      items.value = pool
    } finally {
      if (seq === requestSeq) loading.value = false
    }
  }

  // userId 从 undefined → 有值（登录/刷新用户）时自动重新取数
  watch(() => toValue(userId), () => void load(), { immediate: true })

  return { items, loading, reload: load }
}
