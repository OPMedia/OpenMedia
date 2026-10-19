/**
 * Emby 端点函数(SPEC/api §1.6 LinWeb 依赖全集)。
 * 每个 URL/参数形状以 openapi.json(Emby 4.8)为锚;被砍端点不在此层出现。
 */
import { apiUrl, request } from './client'
import type {
  AuthenticationResult,
  BaseItemDto,
  ItemCounts,
  PlaybackInfoResponse,
  QueryResult,
  SystemInfo,
  UserDto,
} from './types'

/** GET /Users/Public(登录页快捷选择) */
export function getPublicUsers(): Promise<UserDto[]> {
  return request<UserDto[]>('/Users/Public', { noAuth: true })
}

/** POST /Users/AuthenticateByName */
export function authenticateByName(username: string, password: string): Promise<AuthenticationResult> {
  return request<AuthenticationResult>('/Users/AuthenticateByName', {
    method: 'POST',
    noAuth: true,
    body: { Username: username, Pw: password },
  })
}

/** GET /Users/{id} */
export function getUser(userId: string): Promise<UserDto> {
  return request<UserDto>(`/Users/${userId}`)
}

/** GET /Users/{id}/Views(首页各库入口) */
export function getUserViews(userId: string): Promise<QueryResult> {
  return request<QueryResult>(`/Users/${userId}/Views`)
}

export interface GetItemsQuery {
  ParentId?: string
  IncludeItemTypes?: string
  Recursive?: boolean
  SortBy?: string
  SortOrder?: 'Ascending' | 'Descending'
  Filters?: string // 逗号分隔:IsResumable,IsUnplayed,IsPlayed,IsFavorite
  Genres?: string
  Studios?: string
  PersonIds?: string
  Years?: string
  SearchTerm?: string
  StartIndex?: number
  Limit?: number
  Fields?: string
  SortBy2?: string
}

/** GET /Users/{userId}/Items(浏览/筛选/排序/分页,api.md §1.5 组合) */
export function getItems(userId: string, q: GetItemsQuery): Promise<QueryResult> {
  return request<QueryResult>(`/Users/${userId}/Items`, { query: q as Record<string, string | number | boolean | undefined> })
}

/** 收藏 tab:各类型收藏(真实站 favorites tab 分区,emby495-favorites-tab.md) */
export function getFavorites(userId: string, type: 'Movie' | 'Series', limit = 20): Promise<QueryResult> {
  return request<QueryResult>(`/Users/${userId}/Items`, {
    query: {
      Filters: 'IsFavorite',
      IncludeItemTypes: type,
      Recursive: true,
      Limit: limit,
      Fields: 'PrimaryImageAspectRatio,ProductionYear',
    },
  })
}

/** GET /Users/{id}/Items/Latest(最新入库) */
export function getLatestItems(userId: string, q: { Limit?: number; ParentId?: string } = {}): Promise<BaseItemDto[]> {
  return request<BaseItemDto[]>(`/Users/${userId}/Items/Latest`, { query: q })
}

/** GET /Users/{id}/Items/{id}(详情) */
export function getItem(userId: string, itemId: string): Promise<BaseItemDto> {
  return request<BaseItemDto>(`/Users/${userId}/Items/${itemId}`)
}

/** GET /Shows/NextUp */
export function getNextUp(userId: string, q: { ParentId?: string; Limit?: number } = {}): Promise<QueryResult> {
  return request<QueryResult>('/Shows/NextUp', { query: { UserId: userId, ...q } })
}

/** GET /Shows/{seriesId}/Seasons */
export function getSeasons(seriesId: string, userId: string): Promise<QueryResult> {
  return request<QueryResult>(`/Shows/${seriesId}/Seasons`, { query: { UserId: userId } })
}

/** GET /Shows/{seriesId}/Episodes */
export function getEpisodes(seriesId: string, userId: string, seasonId?: string): Promise<QueryResult> {
  return request<QueryResult>(`/Shows/${seriesId}/Episodes`, {
    query: { UserId: userId, SeasonId: seasonId, Fields: 'Overview' },
  })
}

/** GET /Persons/{id}(人物页;真实站人物详情) */
export function getPerson(personId: string, userId: string): Promise<BaseItemDto> {
  return request<BaseItemDto>(`/Persons/${personId}`, { query: { UserId: userId } })
}

/** POST /Items/{id}(编辑元数据;真实站 formdialog 保存动作) */
export function updateItem(itemId: string, patch: Partial<BaseItemDto>): Promise<BaseItemDto> {
  return request<BaseItemDto>(`/Items/${itemId}`, { method: 'POST', body: patch })
}

/** GET /Items/{id}/Similar */
export function getSimilarItems(itemId: string, userId: string, limit = 12): Promise<QueryResult> {
  return request<QueryResult>(`/Items/${itemId}/Similar`, { query: { UserId: userId, Limit: limit } })
}

/** GET /Genres */
export function getGenres(userId: string, parentId?: string): Promise<QueryResult<{ Name: string; Id: string }>> {
  return request<QueryResult<{ Name: string; Id: string }>>('/Genres', { query: { UserId: userId, ParentId: parentId } })
}

/** GET /Items/Counts(管理/看板) */
export function getItemCounts(): Promise<ItemCounts> {
  return request<ItemCounts>('/Items/Counts')
}

/** GET /System/Info */
export function getSystemInfo(): Promise<SystemInfo> {
  return request<SystemInfo>('/System/Info')
}

// ---- 条目操作(SPEC/api §1.6 条目操作) ----

/** POST/DELETE /Users/{userId}/PlayedItems/{itemId} */
export function markPlayed(userId: string, itemId: string, played: boolean): Promise<void> {
  return request(`/Users/${userId}/PlayedItems/${itemId}`, { method: played ? 'POST' : 'DELETE' })
}

/** POST/DELETE /Users/{userId}/FavoriteItems/{itemId} */
export function markFavorite(userId: string, itemId: string, favorite: boolean): Promise<void> {
  return request(`/Users/${userId}/FavoriteItems/${itemId}`, { method: favorite ? 'POST' : 'DELETE' })
}

/** POST/DELETE /Users/{userId}/Items/{itemId}/Rating */
export function rateItem(userId: string, itemId: string, likes: boolean | null): Promise<void> {
  if (likes === null) return request(`/Users/${userId}/Items/${itemId}/Rating`, { method: 'DELETE' })
  return request(`/Users/${userId}/Items/${itemId}/Rating`, { method: 'POST', query: { likes } })
}

/** POST /Items/{id}/PlaybackInfo(空 TranscodingProfiles 逼迫直连,api.md §1.2) */
export function getPlaybackInfo(itemId: string, userId: string): Promise<PlaybackInfoResponse> {
  return request<PlaybackInfoResponse>(`/Items/${itemId}/PlaybackInfo`, {
    method: 'POST',
    query: { UserId: userId },
    body: { DeviceProfile: { MaxStreamingBitrate: 0, TranscodingProfiles: [] } },
  })
}

// ---- 进度上报(SPEC/api §1.4) ----

export function reportPlaybackStart(itemId: string, mediaSourceId: string, playSessionId: string): Promise<void> {
  return request('/Sessions/Playing', { method: 'POST', body: { ItemId: itemId, MediaSourceId: mediaSourceId, PlaySessionId: playSessionId } })
}

export function reportPlaybackProgress(itemId: string, playSessionId: string, positionTicks: number, isPaused: boolean, playMethod = 'DirectPlay'): Promise<void> {
  return request('/Sessions/Playing/Progress', {
    method: 'POST',
    body: { ItemId: itemId, PlaySessionId: playSessionId, PositionTicks: positionTicks, IsPaused: isPaused, PlayMethod: playMethod, EventName: 'progress' },
  })
}

export function reportPlaybackStopped(itemId: string, playSessionId: string, positionTicks?: number): Promise<void> {
  return request('/Sessions/Playing/Stopped', {
    method: 'POST',
    body: { ItemId: itemId, PlaySessionId: playSessionId, PositionTicks: positionTicks },
  })
}

// ---- 图片 URL(SPEC/api §1.6 图片) ----

/** GET /Items/{id}/Images/{type}:纯 URL 构造(经 apiUrl 统一附加 api_key) */
export function imageUrl(
  itemId: string,
  type: string,
  tag?: string,
  opts: { maxWidth?: number; maxHeight?: number; quality?: number } = {},
): string {
  return apiUrl(`/Items/${itemId}/Images/${type}`, {
    tag,
    maxWidth: opts.maxWidth,
    maxHeight: opts.maxHeight,
    quality: opts.quality,
  })
}

/** GET /Users/{id}/Images/Primary */
export function userImageUrl(userId: string, tag?: string): string {
  return apiUrl(`/Users/${userId}/Images/Primary`, { tag })
}

/**
 * 背景大图的真实归属。
 *
 * ⚠️ 真实 Emby 里**剧集/季往往没有自己的 backdrop**：`BackdropImageTags` 是空数组 `[]`，
 * 大图挂在父级系列上，字段是 `ParentBackdropItemId` / `ParentBackdropImageTags`。
 * 实测一台真实服务器的 `继续观看`：
 *   `第 13 集` → `BackdropImageTags: []`、`ParentBackdropItemId: "1332203"`、
 *                `ParentBackdropImageTags: ["46af0034…"]`
 *   `情书`（Movie）→ `BackdropImageTags: ["68377be4…"]`（自己就有）
 *
 * 而 mock 给每个条目都塞了 `BackdropImageTags: [b-id]`，
 * 所以「条目自己就有大图」这个假设在本地永远成立 —— 一接真机，首页 hero 抽到剧集就没有背景了。
 *
 * 返回 `null` 表示真的没有大图，调用方走品牌渐变兜底。
 */
export function backdropOf(item: BaseItemDto | null | undefined): { id: string; tag?: string } | null {
  if (!item) return null
  const own = item.BackdropImageTags?.[0] ?? item.ImageTags?.Backdrop
  if (own) return { id: item.Id, tag: own }
  const parentTag = item.ParentBackdropImageTags?.[0]
  if (parentTag && item.ParentBackdropItemId) return { id: item.ParentBackdropItemId, tag: parentTag }
  return null
}

/** 背景大图 URL；没有大图时返回空串（调用方据此走渐变兜底，不要请求一个必然 404 的地址） */
export function backdropUrl(
  item: BaseItemDto | null | undefined,
  opts: { maxWidth?: number; maxHeight?: number; quality?: number } = {},
): string {
  const b = backdropOf(item)
  return b ? imageUrl(b.id, 'Backdrop', b.tag, opts) : ''
}

/**
 * 横版大图（≈16:9）的真实归属。
 *
 * 这是「hero banner / 首页首屏主图」唯一认可的图源，比 backdropOf 多一条来源：
 * **库（CollectionFolder）和少数影片的横版图挂在 Primary 上**，不在 Backdrop 上。
 *
 * 实测（真机 4.9.5.0）：`CollectionFolder` 的 `ImageTags.Primary` 存在且
 * `PrimaryImageAspectRatio = 1.7778 / 1.7792` —— 是横图。而剧集/影片的海报
 * `PrimaryImageAspectRatio = 0.667` —— 是竖图。
 * 只按"不是 Backdrop 就当竖版"来判断会把这些横版 Primary 当成竖版渲染成 2:3 画框，
 * 于是首页出现一张又高又窄、边缘错位的怪卡片。所以判断必须看真实宽高比。
 *
 * 返回 null 表示**这张条目拿不到任何横版大图**，调用方要么换条目，要么走品牌渐变兜底。
 */
export interface WideArt {
  /** 图片宿主条目 Id（剧集的大图挂在父级系列上，不等于 item.Id） */
  id: string
  type: 'Backdrop' | 'Primary'
  tag?: string
}

/** 判定横图的容差：16:9 = 1.7778，1.85:1 也算横图（1.85 与 1.7778 差 0.072） */
const WIDE_ART_TOLERANCE = 0.08
const WIDE_ART_RATIO = 16 / 9

export function wideArtOf(item: BaseItemDto | null | undefined): WideArt | null {
  if (!item) return null
  const b = backdropOf(item)
  if (b) return { id: b.id, type: 'Backdrop', tag: b.tag }
  const aspect = item.PrimaryImageAspectRatio
  const tag = item.ImageTags?.Primary
  if (tag && typeof aspect === 'number' && Math.abs(aspect - WIDE_ART_RATIO) <= WIDE_ART_TOLERANCE) {
    return { id: item.Id, type: 'Primary', tag }
  }
  return null
}

/** 这张条目能不能当横版 banner 用（hero 入池的硬条件） */
export function hasWideArt(item: BaseItemDto | null | undefined): boolean {
  return wideArtOf(item) !== null
}

/** 横版大图 URL；拿不到时返回空串（调用方据此走渐变兜底，不要请求必然 404 的地址） */
export function wideArtUrl(
  item: BaseItemDto | null | undefined,
  opts: { maxWidth?: number; maxHeight?: number; quality?: number } = {},
): string {
  const a = wideArtOf(item)
  return a ? imageUrl(a.id, a.type, a.tag, opts) : ''
}
// ---------- 管理后台(SPEC/api 管理面) ----------

export interface AdminTask {
  Id: string; Name: string; Description: string; Category: string; State: string
  LastExecutionResult?: { Status: string; StartTimeUtc: string; EndTimeUtc: string }
}
export interface AdminLogFile { Name: string; DateCreated: string; Size: number }
export interface AdminDevice { Id: string; Name: string; AppName: string; LastUserName: string; DateLastActivity: string; IpAddress: string }
export interface AdminPlugin { Id: string; Name: string; Version: string; Status: string; Description: string }
export interface AdminApiKey { Id: string; Name: string; DateCreated: string; Key: string }
export interface AdminConfigField { key: string; label: string; kind: 'select' | 'toggle' | 'text' | 'number'; options?: string[]; desc?: string }
export interface AdminConfigGroup { section: string; legend: string; fields: AdminConfigField[] }

/** GET /Users(管理:全部用户) */
export function getUsers(): Promise<UserDto[]> {
  return request<UserDto[]>('/Users')
}
/** POST /Users/New */
export function createUser(body: { Name: string; Password?: string }): Promise<UserDto> {
  return request<UserDto>('/Users/New', { method: 'POST', body })
}
/** DELETE /Users/{id} */
export function deleteUser(userId: string): Promise<void> {
  return request<void>(`/Users/${userId}`, { method: 'DELETE' })
}
/** GET /ScheduledTasks */
export function getScheduledTasks(): Promise<AdminTask[]> {
  return request<AdminTask[]>('/ScheduledTasks')
}
/** POST /ScheduledTasks/Running/{id} */
export function runScheduledTask(taskId: string): Promise<void> {
  return request<void>(`/ScheduledTasks/Running/${taskId}`, { method: 'POST' })
}
/** GET /System/Logs */
export function getLogFiles(): Promise<AdminLogFile[]> {
  return request<AdminLogFile[]>('/System/Logs')
}
/** DELETE /System/Logs/{name} */
export function deleteLogFile(name: string): Promise<void> {
  return request<void>(`/System/Logs/${encodeURIComponent(name)}`, { method: 'DELETE' })
}
/** GET /Devices */
export function getDevices(): Promise<QueryResult<AdminDevice>> {
  return request<QueryResult<AdminDevice>>('/Devices')
}
/** GET /Plugins */
export function getPlugins(): Promise<AdminPlugin[]> {
  return request<AdminPlugin[]>('/Plugins')
}
/** GET /Auth/Keys */
export function getApiKeys(): Promise<QueryResult<AdminApiKey>> {
  return request<QueryResult<AdminApiKey>>('/Auth/Keys')
}
/** POST /Auth/Keys */
export function createApiKey(app: string): Promise<void> {
  return request<void>('/Auth/Keys', { method: 'POST', body: { App: app } })
}
/** DELETE /Auth/Keys/{key} */
export function deleteApiKey(key: string): Promise<void> {
  return request<void>(`/Auth/Keys/${key}`, { method: 'DELETE' })
}
/** GET /System/Configuration */
export function getServerConfig(): Promise<Record<string, string | number | boolean>> {
  return request<Record<string, string | number | boolean>>('/System/Configuration')
}
/** GET /System/Configuration/Fields */
export function getServerConfigFields(): Promise<AdminConfigGroup[]> {
  return request<AdminConfigGroup[]>('/System/Configuration/Fields')
}
/** POST /System/Configuration */
export function saveServerConfig(patch: Record<string, string | number | boolean>): Promise<void> {
  return request<void>('/System/Configuration', { method: 'POST', body: patch })
}
/** POST /Library/Refresh */
export function refreshLibrary(): Promise<void> {
  return request<void>('/Library/Refresh', { method: 'POST' })
}
/** POST /Library/VirtualFolders(新建媒体库) */
export function createLibrary(body: { Name: string; CollectionType?: string }): Promise<void> {
  return request<void>('/Library/VirtualFolders', { method: 'POST', body })
}
/** DELETE /Library/VirtualFolders?name= */
export function deleteLibrary(name: string): Promise<void> {
  return request<void>(`/Library/VirtualFolders?name=${encodeURIComponent(name)}`, { method: 'DELETE' })
}
/** GET /System/Logs/{name}/File(日志内容;前端触发下载) */
export async function downloadLogFile(name: string): Promise<string> {
  const url = apiUrl(`/System/Logs/${encodeURIComponent(name)}/File`)
  const token = typeof localStorage !== 'undefined' ? localStorage.getItem('serata.token') : null
  const res = await fetch(url, { headers: token ? { Authorization: `MediaBrowser Token="${token}"` } : {} })
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return res.text()
}
/** POST /Plugins/{id}/{version}(启用) */
export function enablePlugin(pluginId: string, version: string): Promise<void> {
  return request<void>(`/Plugins/${pluginId}/${version}`, { method: 'POST' })
}
/** DELETE /Plugins/{id}(停用) */
export function disablePlugin(pluginId: string): Promise<void> {
  return request<void>(`/Plugins/${pluginId}`, { method: 'DELETE' })
}
/** POST /Items/{id}/Images/{type}(multipart 上传;真实站图像编辑器) */
export async function uploadItemImage(itemId: string, type: string, file: File): Promise<void> {
  const token = typeof localStorage !== 'undefined' ? localStorage.getItem('serata.token') : null
  const fd = new FormData()
  fd.append('file', file, file.name)
  const res = await fetch(apiUrl(`/Items/${itemId}/Images/${type}`), {
    method: 'POST',
    headers: token ? { Authorization: `MediaBrowser Token="${token}"` } : {},
    body: fd,
  })
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
}

/** DELETE /Items/{id}/Images/{type} */
export function deleteItemImage(itemId: string, type: string): Promise<void> {
  return request<void>(`/Items/${itemId}/Images/${type}`, { method: 'DELETE' })
}