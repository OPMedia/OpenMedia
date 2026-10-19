/**
 * 首页 Hero 数据池 —— 三条查询合并成 6 条候选。
 *
 * 产品决策（顺序不可调）：
 *   ① 继续观看前 2 条（Filters=IsResumable、SortBy=DatePlayed、Descending）
 *   ② 最新未看过的电影前 3 条（Filters=IsUnplayed、SortBy=DateCreated、Descending）
 *   ③ 最新剧集前 2 条（IncludeItemTypes=Series、SortBy=DateCreated、Descending）
 * 合并后按 Id 去重，最终上限 6 条；三条请求 Promise.allSettled，任一失败不致命。
 *
 * 必须带 Fields：Emby 列表接口默认不返回 Overview，不带就没有简介可显示。
 */
import { ref, toValue, watch, type MaybeRefOrGetter, type Ref } from 'vue'
import type { BaseItemDto } from '@/api/types'
import { getItems } from '@/api/endpoints'

/** 池子最终上限（hero 首屏最多 6 张可切） */
const HERO_POOL_LIMIT = 6

/** 列表接口必须显式索要的字段（Overview 不在这份清单里就不会返回） */
const HERO_FIELDS = [
  'Overview',
  'Genres',
  'Taglines',
  'OfficialRating',
  'CommunityRating',
  'RunTimeTicks',
  'ImageTags',
  'BackdropImageTags',
  'PrimaryImageAspectRatio',
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
        // ① 继续观看前 2 条
        getItems(uid, {
          Filters: 'IsResumable',
          SortBy: 'DatePlayed',
          SortOrder: 'Descending',
          Limit: 2,
          Recursive: true,
          IncludeItemTypes: 'Movie,Episode',
          Fields: HERO_FIELDS,
        }),
        // ② 最新未看过的电影前 3 条
        getItems(uid, {
          IncludeItemTypes: 'Movie',
          Filters: 'IsUnplayed',
          SortBy: 'DateCreated',
          SortOrder: 'Descending',
          Limit: 3,
          Fields: HERO_FIELDS,
        }),
        // ③ 最新剧集前 2 条
        getItems(uid, {
          IncludeItemTypes: 'Series',
          SortBy: 'DateCreated',
          SortOrder: 'Descending',
          Limit: 2,
          Fields: HERO_FIELDS,
        }),
      ])

      // 期间又发起了新请求（userId 变了）→ 丢弃本次结果
      if (seq !== requestSeq) return

      // 严格按 ①②③ 的优先级拼池 + 按 Id 去重 + 截到 6 条
      const merged: BaseItemDto[] = []
      const seen = new Set<string>()
      for (const result of settled) {
        if (result.status !== 'fulfilled') continue
        const batch = result.value.Items ?? []
        for (const item of batch) {
          if (!item?.Id || seen.has(item.Id)) continue
          seen.add(item.Id)
          merged.push(item)
          if (merged.length >= HERO_POOL_LIMIT) break
        }
        if (merged.length >= HERO_POOL_LIMIT) break
      }
      items.value = merged
    } finally {
      if (seq === requestSeq) loading.value = false
    }
  }

  // userId 从 undefined → 有值（登录/刷新用户）时自动重新取数
  watch(() => toValue(userId), () => void load(), { immediate: true })

  return { items, loading, reload: load }
}
