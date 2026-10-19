/**
 * MSW handlers:在 HTTP 层模拟 Emby 兼容 API(SPEC/api)。
 * 响应形状照 openapi.json(Emby 4.8);行为按 SPEC 实现:
 *  - 鉴权双通道(§1.1)
 *  - /Users/{id}/Items 查询组合(§1.5)
 *  - 进度阈值 5%/90%/300s(playback §5)
 * 接真后端后:main.ts 不调用 setupWorker 即整体下线。
 */
import { http, HttpResponse, delay } from 'msw'
import type { BaseItemDto, UserDto, UserItemData } from '@/api/types'
import {
  allItems, getUserData, itemById, libraries, mutateUserData,
  genres, users, RESUME_THRESHOLDS, toTicks, TICKS_PER_SECOND,
  scheduledTasks, logFiles, devices, plugins, apiKeys, serverConfig, serverConfigFields,
} from './db'
import { avatarSvg, backdropSvg, posterSvg } from './svg'

const BASE = '/emby'

// ---------- 工具 ----------

function authOk(req: Request): string | null {
  const url = new URL(req.url)
  const apiKey = url.searchParams.get('api_key')
  const m = /Token="([^"]+)"/.exec(req.headers.get('Authorization') ?? '')
  const token = apiKey ?? m?.[1]
  // 无状态 token(可跨 SW 重载):格式 tok-<userId>-<rand>
  if (!token) return null
  const um = /^tok-(\d+)-/.exec(token)
  return um?.[1] ?? null
}

function requireUser(req: Request): { userId: string } | null {
  const userId = authOk(req)
  if (!userId) return null
  // URL 中的 userId 若有,须与 token 一致;管理员端点(新建/删除其他用户)对管理员放行
  const m = new RegExp(`^${BASE}/Users/([^/]+)`).exec(new URL(req.url).pathname)
  if (m && m[1] !== 'Public' && m[1] !== 'AuthenticateByName' && m[1] !== 'New' && m[1] !== userId) {
    const requester = users.find((u) => u.Id === userId)
    if (!requester?.IsAdministrator) return null
  }
  return { userId }
}

/** 递归后代遍历(库 → 剧集 → 季 → 集);防止环用 visited 集合 */
function descendantsOf(rootId: string): BaseItemDto[] {
  const out: BaseItemDto[] = []
  const seen = new Set<string>([rootId])
  const stack = [rootId]
  while (stack.length) {
    const id = stack.pop()!
    for (const kid of allItems) {
      if (kid.ParentId === id && !seen.has(kid.Id)) {
        seen.add(kid.Id)
        out.push(kid)
        stack.push(kid.Id)
      }
    }
  }
  return out
}

function withUserData(item: BaseItemDto, userId: string): BaseItemDto {
  return { ...item, UserData: getUserData(userId, item.Id) }
}

function resumable(ud: UserItemData): boolean {
  // 有播放位置且未看完(>0 且 <90% 由阈值规则保证,见 playback §5)
  return (ud.PlaybackPositionTicks ?? 0) > 0
}

function isPlayed(ud: UserItemData): boolean {
  return !!ud.Played
}

function tvChildren(seriesId: string): BaseItemDto[] {
  return allItems.filter((i) => i.SeriesId === seriesId || i.Id === seriesId || i.ParentId === seriesId)
}

/**
 * /Users/{id}/Items 查询引擎(api.md §1.5 参数组合)
 */
function queryItems(userId: string, url: URL): { items: BaseItemDto[]; total: number } {
  const parentId = url.searchParams.get('ParentId')
  const include = url.searchParams.get('IncludeItemTypes')?.split(',').filter(Boolean)
  const recursive = url.searchParams.get('Recursive') === 'true'
  const sortBy = url.searchParams.get('SortBy') ?? 'SortName'
  const sortOrder = url.searchParams.get('SortOrder') ?? 'Ascending'
  const filters = url.searchParams.get('Filters')?.split(',').filter(Boolean) ?? []
  const genresQ = url.searchParams.get('Genres')
  const personIdsQ = url.searchParams.get('PersonIds')
  const yearsQ = url.searchParams.get('Years')
  const search = url.searchParams.get('SearchTerm')?.toLowerCase()
  const startIndex = Number(url.searchParams.get('StartIndex') ?? 0)
  const limit = Number(url.searchParams.get('Limit') ?? 300)

  let pool: BaseItemDto[]
  if (parentId) {
    const parent = itemById.get(parentId)
    if (!parent) pool = []
    else if (parent.Type === 'CollectionFolder' && parent.CollectionType === 'tvshows' && !recursive) {
      pool = allItems.filter((i) => i.Type === 'Series' && i.ParentId === parent.Id)
    } else if (recursive) {
      // 全量递归后代(库 → 剧集 → 季 → 集;"集"tab 与搜索都依赖它)
      pool = descendantsOf(parent.Id)
    } else {
      pool = allItems.filter((i) => i.ParentId === parent.Id)
    }
  } else {
    pool = allItems.filter((i) => i.Type === 'Movie' || i.Type === 'Series')
  }

  if (include?.length) pool = pool.filter((i) => include.includes(i.Type))

  // Filters(SPEC/api §1.5:IsResumable/IsUnplayed 组合是继续观看横栏的活路)
  if (filters.includes('IsResumable')) pool = pool.filter((i) => resumable(getUserData(userId, i.Id)))
  if (filters.includes('IsUnplayed')) pool = pool.filter((i) => !isPlayed(getUserData(userId, i.Id)))
  if (filters.includes('IsPlayed')) pool = pool.filter((i) => isPlayed(getUserData(userId, i.Id)))
  if (filters.includes('IsFavorite')) pool = pool.filter((i) => !!getUserData(userId, i.Id).IsFavorite)

  if (genresQ) {
    const gs = genresQ.split('|').filter(Boolean)
    if (gs.length) pool = pool.filter((i) => i.Genres?.some((g) => gs.includes(g)))
  }
  const studiosQ = url.searchParams.get('Studios')
  if (studiosQ) {
    const ss = studiosQ.split('|').filter(Boolean)
    if (ss.length) pool = pool.filter((i) => i.Studios?.some((s) => ss.includes(s.Name)))
  }
  if (personIdsQ) {
    const ps = personIdsQ.split(',').filter(Boolean)
    if (ps.length) pool = pool.filter((i) => i.People?.some((p) => ps.includes(p.Id)))
  }
  if (yearsQ) {
    const ys = yearsQ.split(',').filter(Boolean)
    if (ys.length) pool = pool.filter((i) => i.ProductionYear && ys.includes(String(i.ProductionYear)))
  }
  if (search) {
    pool = pool.filter((i) =>
      i.Name.toLowerCase().includes(search) ||
      i.SortName?.toLowerCase().includes(search) ||
      i.SeriesName?.toLowerCase().includes(search) ||
      i.Overview?.toLowerCase().includes(search),
    )
  }

  // 排序(Emby SortBy 家族;DatePlayed 来自 UserData)
  const key = (i: BaseItemDto): string | number => {
    switch (sortBy) {
      case 'DateCreated': return i.DateCreated ?? ''
      case 'DatePlayed': return getUserData(userId, i.Id).LastPlayedDate ?? ''
      case 'CommunityRating': return i.CommunityRating ?? 0
      case 'ProductionYear': return i.ProductionYear ?? 0
      case 'PremiereDate': return i.PremiereDate ?? ''
      default: return i.SortName ?? i.Name
    }
  }
  pool = [...pool].sort((a, b) => {
    const ka = key(a); const kb = key(b)
    const c = ka === kb ? 0 : ka < kb ? -1 : 1
    return sortOrder === 'Descending' ? -c : c
  })

  const total = pool.length
  return { items: pool.slice(startIndex, startIndex + limit), total }
}

/** NextUp:每部剧集按季排序,取第一个未播放的集(SPEC/api §1.6) */
function computeNextUp(userId: string): BaseItemDto[] {
  const out: BaseItemDto[] = []
  for (const s of allItems.filter((i) => i.Type === 'Series')) {
    const eps = tvChildren(s.Id).filter((i) => i.Type === 'Episode')
      .sort((a, b) => (a.SortName ?? '').localeCompare(b.SortName ?? ''))
    // 有播放历史才谈 NextUp;继续中的那一集也算 NextUp
    const firstUn = eps.find((e) => !isPlayed(getUserData(userId, e.Id)) && resumable(getUserData(userId, e.Id)))
      ?? eps.find((e) => !isPlayed(getUserData(userId, e.Id)) && eps.some((x) => isPlayed(getUserData(userId, x.Id))))
    if (firstUn) out.push(firstUn)
  }
  return out
}

// ---------- 鉴权(SPEC/api §1.6) ----------

const auth = [
  http.get(`${BASE}/Users/Public`, () => {
    // 对齐原版:mock 用户无头像(全新安装语境)→ 登录页走 logindefault.png + Metro 色
    return HttpResponse.json(users.map((u) => ({
      Id: u.Id, Name: u.Name, ServerId: 'mock-server', HasPassword: u.HasPassword,
    })))
  }),

  http.post(`${BASE}/Users/AuthenticateByName`, async ({ request: req }) => {
    const body = (await req.json()) as { Username?: string; Pw?: string }
    const user = users.find((u) => u.Name === body.Username && u.Password === (body.Pw ?? ''))
    if (!user) return new HttpResponse(null, { status: 401 })
    const token = `tok-${user.Id}-${Math.random().toString(36).slice(2, 10)}`
    return HttpResponse.json({
      User: { Id: user.Id, Name: user.Name, ServerId: 'mock-server', IsAdministrator: user.IsAdministrator, HasPassword: user.HasPassword, Policy: { IsAdministrator: user.IsAdministrator, EnableAllFolders: true } },
      AccessToken: token,
      ServerId: 'mock-server',
    })
  }),

  http.get(`${BASE}/Users/:userId`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const found = users.find((x) => x.Id === params.userId)
    if (!found) return new HttpResponse(null, { status: 404 })
    const dto: UserDto = { Id: found.Id, Name: found.Name, IsAdministrator: found.IsAdministrator, HasPassword: found.HasPassword, PrimaryImageTag: `u-${found.Id}` }
    return HttpResponse.json(dto)
  }),
]

// ---------- 浏览(SPEC/api §1.6) ----------

const browse = [
  http.get(`${BASE}/Users/:userId/Views`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    return HttpResponse.json({ Items: libraries, TotalRecordCount: libraries.length, StartIndex: 0 })
  }),

  http.get(`${BASE}/Users/:userId/Items/Latest`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const url = new URL(req.url)
    const limit = Number(url.searchParams.get('Limit') ?? 20)
    const parentId = url.searchParams.get('ParentId')
    // 真实站 Latest 含剧集(Series,带未看计数徽章)
    let pool = allItems.filter((i) => i.Type === 'Movie' || i.Type === 'Episode' || i.Type === 'Series')
    if (parentId) {
      pool = pool.filter((i) => i.ParentId === parentId)
    }
    pool = [...pool].sort((a, b) => (b.DateCreated ?? '').localeCompare(a.DateCreated ?? ''))
    return HttpResponse.json(pool.slice(0, limit).map((i) => withUserData(i, u.userId)))
  }),

  http.get(`${BASE}/Users/:userId/Items/:itemId`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const item = itemById.get(String(params.itemId))
    if (!item) return new HttpResponse(null, { status: 404 })
    return HttpResponse.json(withUserData(item, u.userId))
  }),

  http.get(`${BASE}/Users/:userId/Items`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const { items, total } = queryItems(u.userId, new URL(req.url))
    const url = new URL(req.url)
    return HttpResponse.json({
      Items: items.map((i) => withUserData(i, u.userId)),
      TotalRecordCount: total,
      StartIndex: Number(url.searchParams.get('StartIndex') ?? 0),
    })
  }),

  http.get(`${BASE}/Shows/NextUp`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const url = new URL(req.url)
    const limit = Number(url.searchParams.get('Limit') ?? 20)
    const next = computeNextUp(u.userId)
    return HttpResponse.json({ Items: next.slice(0, limit).map((i) => withUserData(i, u.userId)), TotalRecordCount: next.length, StartIndex: 0 })
  }),

  http.get(`${BASE}/Shows/:seriesId/Seasons`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const seasons = allItems.filter((i) => i.Type === 'Season' && i.SeriesId === params.seriesId)
      .sort((a, b) => (a.SortName ?? '').localeCompare(b.SortName ?? ''))
    return HttpResponse.json({ Items: seasons.map((i) => withUserData(i, u.userId)), TotalRecordCount: seasons.length, StartIndex: 0 })
  }),

  http.get(`${BASE}/Shows/:seriesId/Episodes`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const url = new URL(req.url)
    const seasonId = url.searchParams.get('SeasonId')
    let eps = allItems.filter((i) => i.Type === 'Episode' && i.SeriesId === params.seriesId)
    if (seasonId) eps = eps.filter((i) => i.SeasonId === seasonId)
    eps = [...eps].sort((a, b) => (a.SortName ?? '').localeCompare(b.SortName ?? ''))
    return HttpResponse.json({ Items: eps.map((i) => withUserData(i, u.userId)), TotalRecordCount: eps.length, StartIndex: 0 })
  }),

  http.get(`${BASE}/Items/:itemId/Similar`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const item = itemById.get(String(params.itemId))
    const limit = Number(new URL(req.url).searchParams.get('Limit') ?? 12)
    const pool = allItems.filter((i) =>
      i.Id !== item?.Id && i.Type === item?.Type &&
      i.Genres?.some((g) => item?.Genres?.includes(g)),
    )
    return HttpResponse.json({ Items: pool.slice(0, limit).map((i) => withUserData(i, u.userId)), TotalRecordCount: pool.length, StartIndex: 0 })
  }),

  http.get(`${BASE}/Genres`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    // 真实站:类型列表按库内条目计算(ParentId 存在时只看该库;库页"类型"tab 依赖此语义)
    const url = new URL(req.url)
    const parentId = url.searchParams.get('ParentId')
    const pool = parentId ? descendantsOf(parentId) : allItems
    const usePool = parentId ? pool : allItems
    const set = new Set<string>()
    for (const it of usePool) {
      if (it.Type === 'Season' || it.Type === 'Episode') continue
      it.Genres?.forEach((g) => set.add(g))
    }
    const gs = parentId ? [...set].sort() : genres()
    return HttpResponse.json({ Items: gs.map((g, i) => ({ Name: g, Id: `genre-${i}` })), TotalRecordCount: gs.length, StartIndex: 0 })
  }),

  http.get(`${BASE}/Items/Counts`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    return HttpResponse.json({
      MovieCount: allItems.filter((i) => i.Type === 'Movie').length,
      SeriesCount: allItems.filter((i) => i.Type === 'Series').length,
      EpisodeCount: allItems.filter((i) => i.Type === 'Episode').length,
      BoxSetCount: 0,
    })
  }),

  http.get(`${BASE}/System/Info`, () => {
    return HttpResponse.json({ ServerName: 'Serata Mock', Version: '0.1.0-mock', Id: 'mock-server', OperatingSystem: 'Browser' })
  }),
]

// ---------- 条目操作 ----------

const operations = [
  http.post(`${BASE}/Users/:userId/PlayedItems/:itemId`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const item = itemById.get(String(params.itemId))
    mutateUserData(u.userId, String(params.itemId), {
      Played: true, PlayCount: (getUserData(u.userId, String(params.itemId)).PlayCount ?? 0) + 1,
      LastPlayedDate: new Date().toISOString(), PlaybackPositionTicks: 0,
    })
    return HttpResponse.json({ success: !!item })
  }),

  http.delete(`${BASE}/Users/:userId/PlayedItems/:itemId`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    mutateUserData(u.userId, String(params.itemId), { Played: false, PlayCount: 0, PlaybackPositionTicks: 0 })
    return new HttpResponse(null, { status: 204 })
  }),

  http.post(`${BASE}/Users/:userId/FavoriteItems/:itemId`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    mutateUserData(u.userId, String(params.itemId), { IsFavorite: true })
    return HttpResponse.json(withUserData(itemById.get(String(params.itemId))!, u.userId))
  }),

  http.delete(`${BASE}/Users/:userId/FavoriteItems/:itemId`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    mutateUserData(u.userId, String(params.itemId), { IsFavorite: false })
    return HttpResponse.json(withUserData(itemById.get(String(params.itemId))!, u.userId))
  }),

  http.post(`${BASE}/Users/:userId/Items/:itemId/Rating`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const likes = new URL(req.url).searchParams.get('likes') === 'true'
    mutateUserData(u.userId, String(params.itemId), { Likes: likes })
    return HttpResponse.json(withUserData(itemById.get(String(params.itemId))!, u.userId))
  }),

  // 编辑元数据(真实站 formdialog 保存 → POST /Items/{id})
  http.post(`${BASE}/Items/:itemId`, async ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const item = itemById.get(String(params.itemId))
    if (!item) return new HttpResponse(null, { status: 404 })
    const patch = (await req.json()) as Partial<BaseItemDto>
    Object.assign(item, patch)
    return HttpResponse.json(withUserData(item, u.userId))
  }),

  // 人物(真实站人物页:GET /Persons/{id} + /Items?PersonIds=)
  http.get(`${BASE}/Persons/:personId`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const pid = String(params.personId)
    for (const it of allItems) {
      const p = it.People?.find((x) => x.Id === pid)
      if (p) {
        const person: BaseItemDto = {
          Id: pid, Name: p.Name, Type: 'Person', ServerId: 'mock',
          ImageTags: { Primary: `person-${pid}` },
        }
        return HttpResponse.json(person)
      }
    }
    return new HttpResponse(null, { status: 404 })
  }),
]

// ---------- 管理后台(SPEC/api 管理面;真实站 4.9.5 admin 端点形状) ----------

const admin = [
  // 用户(增/查/删)
  http.get(`${BASE}/Users`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    return HttpResponse.json(users.map(({ Password: _pw, ...rest }) => rest))
  }),
  http.post(`${BASE}/Users/New`, async ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const body = (await req.json()) as { Name?: string; Password?: string }
    const id = String(users.length + 1)
    const created = { Id: id, Name: body.Name ?? `user${id}`, Password: body.Password ?? '', IsAdministrator: false, HasPassword: !!body.Password, DateCreated: new Date().toISOString() }
    users.push(created)
    const { Password: _pw, ...rest } = created
    return HttpResponse.json(rest)
  }),
  http.delete(`${BASE}/Users/:userId`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const i = users.findIndex((x) => x.Id === String(params.userId))
    if (i >= 0) users.splice(i, 1)
    return new HttpResponse(null, { status: 204 })
  }),

  // 计划任务
  http.get(`${BASE}/ScheduledTasks`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    return HttpResponse.json(scheduledTasks)
  }),
  http.post(`${BASE}/ScheduledTasks/Running/:taskId`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const t = scheduledTasks.find((x) => x.Id === String(params.taskId))
    if (t) t.LastExecutionResult = { Status: 'Completed', StartTimeUtc: new Date().toISOString(), EndTimeUtc: new Date().toISOString() }
    return new HttpResponse(null, { status: 204 })
  }),

  // 日志
  http.get(`${BASE}/System/Logs`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    return HttpResponse.json(logFiles)
  }),
  http.delete(`${BASE}/System/Logs/:name`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const i = logFiles.findIndex((x) => x.Name === decodeURIComponent(String(params.name)))
    if (i >= 0) logFiles.splice(i, 1)
    return new HttpResponse(null, { status: 204 })
  }),

  // 设备 / 插件 / API 密钥
  http.get(`${BASE}/Devices`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    return HttpResponse.json({ Items: devices, TotalRecordCount: devices.length })
  }),
  http.get(`${BASE}/Plugins`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    return HttpResponse.json(plugins)
  }),
  http.get(`${BASE}/Auth/Keys`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    return HttpResponse.json({ Items: apiKeys, TotalRecordCount: apiKeys.length })
  }),
  http.post(`${BASE}/Auth/Keys`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const created = { Id: `k${apiKeys.length + 1}`, Name: '新建密钥', DateCreated: new Date().toISOString(), Key: Math.random().toString(16).slice(2).padEnd(32, '0') }
    apiKeys.push(created)
    return new HttpResponse(null, { status: 204 })
  }),
  http.delete(`${BASE}/Auth/Keys/:key`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const i = apiKeys.findIndex((x) => x.Key === String(params.key) || x.Id === String(params.key))
    if (i >= 0) apiKeys.splice(i, 1)
    return new HttpResponse(null, { status: 204 })
  }),

  // 服务器配置(管理页表单)
  http.get(`${BASE}/System/Configuration`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    return HttpResponse.json(serverConfig)
  }),
  http.get(`${BASE}/System/Configuration/Fields`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    return HttpResponse.json(serverConfigFields)
  }),
  http.post(`${BASE}/System/Configuration`, async ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const patch = (await req.json()) as Record<string, string | number | boolean>
    Object.assign(serverConfig, patch)
    return new HttpResponse(null, { status: 204 })
  }),

  // 媒体库扫描
  http.post(`${BASE}/Library/Refresh`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    return new HttpResponse(null, { status: 204 })
  }),

  // 新建/删除媒体库(真实站 /Library/VirtualFolders)
  http.post(`${BASE}/Library/VirtualFolders`, async ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const body = (await req.json()) as { Name?: string; CollectionType?: string }
    const id = String(300 + libraries.length)
    libraries.push({
      Name: body.Name ?? `媒体库 ${id}`, Id: id, Type: 'CollectionFolder',
      CollectionType: body.CollectionType ?? 'movies', SortName: body.Name ?? id,
      UserData: { UnplayedItemCount: 0 },
    } as BaseItemDto)
    return new HttpResponse(null, { status: 204 })
  }),
  http.delete(`${BASE}/Library/VirtualFolders`, ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const name = new URL(req.url).searchParams.get('name')
    const i = libraries.findIndex((l) => l.Name === name)
    if (i >= 0) libraries.splice(i, 1)
    return new HttpResponse(null, { status: 204 })
  }),

  // 日志内容下载(真实站 GET /System/Logs/{name}/File)
  http.get(`${BASE}/System/Logs/:name/File`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const name = decodeURIComponent(String(params.name))
    const f = logFiles.find((x) => x.Name === name)
    if (!f) return new HttpResponse(null, { status: 404 })
    const body = [
      `# ${f.Name}`,
      `# generated by Serata mock (${new Date().toISOString()})`,
      '',
      '2026-10-16 17:33:00.000 Info App: Serata mock log start',
      '2026-10-16 17:33:00.100 Info App: Emby-compatible API surface served by MSW',
      '2026-10-16 17:33:00.200 Info Library: scan completed, 0 new items',
      '2026-10-16 17:33:00.300 Info Session: user demo connected from 127.0.0.1',
      '',
    ].join('\n')
    return new HttpResponse(body, { headers: { 'Content-Type': 'text/plain; charset=utf-8' } })
  }),

  // 插件启用/停用(真实站 POST /Plugins/{id}/{version} / DELETE /Plugins/{id})
  http.post(`${BASE}/Plugins/:pluginId/:version`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const p = plugins.find((x) => x.Id === String(params.pluginId))
    if (p) p.Status = 'Active'
    return new HttpResponse(null, { status: 204 })
  }),
  http.delete(`${BASE}/Plugins/:pluginId`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const p = plugins.find((x) => x.Id === String(params.pluginId))
    if (p) p.Status = 'Disabled'
    return new HttpResponse(null, { status: 204 })
  }),
]

// ---------- 播放(SPEC/api §1.2/§1.4 + playback §5) ----------

const playback = [
  http.post(`${BASE}/Items/:itemId/PlaybackInfo`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const item = itemById.get(String(params.itemId))
    const source = item?.MediaSources?.[0]
    if (!item || !source) return new HttpResponse(null, { status: 404 })
    // SPEC/api §1.2:空 TranscodingProfiles 的 DeviceProfile 必须直连
    const playSessionId = `ps-${Math.random().toString(36).slice(2, 10)}`
    return HttpResponse.json({
      MediaSources: item.MediaSources,
      PlaySessionId: playSessionId,
      DirectStreamUrl: `${BASE}/Videos/${item.Id}/stream.mkv?MediaSourceId=${source.Id}&PlaySessionId=${playSessionId}&api_key=${authOk(req) ?? ''}`,
    })
  }),

  // 进度上报:按 playback §5 阈值落 user_data
  http.post(`${BASE}/Sessions/Playing`, async ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const b = (await req.json()) as { ItemId: string }
    return new HttpResponse(null, { status: 204 })
  }),

  http.post(`${BASE}/Sessions/Playing/Progress`, async ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const b = (await req.json()) as { ItemId: string; PositionTicks?: number }
    if (b.PositionTicks !== undefined && itemById.has(b.ItemId)) {
      const item = itemById.get(b.ItemId)!
      const runtimeTicks = item.RunTimeTicks ?? 0
      if (runtimeTicks > 0) {
        const pct = (b.PositionTicks / runtimeTicks) * 100
        mutateUserData(u.userId, b.ItemId, { PlaybackPositionTicks: b.PositionTicks })
        // >90% → Played=true、position 清零(playback §5)
        if (pct >= RESUME_THRESHOLDS.maxResumePct) {
          mutateUserData(u.userId, b.ItemId, { Played: true, PlaybackPositionTicks: 0, PlayCount: (getUserData(u.userId, b.ItemId).PlayCount ?? 0) + 1, LastPlayedDate: new Date().toISOString() })
        }
      }
    }
    return new HttpResponse(null, { status: 204 })
  }),

  http.post(`${BASE}/Sessions/Playing/Stopped`, async ({ request: req }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const b = (await req.json()) as { ItemId: string; PositionTicks?: number }
    const item = itemById.get(b.ItemId)
    if (item) {
      const ud = getUserData(u.userId, b.ItemId)
      const runtimeTicks = item.RunTimeTicks ?? 0
      const runtimeSec = runtimeTicks / TICKS_PER_SECOND
      if (b.PositionTicks === undefined) {
        // Stopped 无 positionTicks → PlayCount++、Played=true(playback §5)
        mutateUserData(u.userId, b.ItemId, { Played: true, PlayCount: (ud.PlayCount ?? 0) + 1, LastPlayedDate: new Date().toISOString() })
      } else {
        const pct = runtimeTicks > 0 ? (b.PositionTicks / runtimeTicks) * 100 : 100
        if (pct >= RESUME_THRESHOLDS.maxResumePct || pct === 100 || runtimeSec < RESUME_THRESHOLDS.minResumeDurationSeconds) {
          mutateUserData(u.userId, b.ItemId, { Played: true, PlaybackPositionTicks: 0, PlayCount: (ud.PlayCount ?? 0) + 1, LastPlayedDate: new Date().toISOString() })
        } else if (pct < RESUME_THRESHOLDS.minResumePct) {
          mutateUserData(u.userId, b.ItemId, { PlaybackPositionTicks: 0 })
        } else {
          mutateUserData(u.userId, b.ItemId, { PlaybackPositionTicks: b.PositionTicks, LastPlayedDate: new Date().toISOString() })
        }
      }
    }
    return new HttpResponse(null, { status: 204 })
  }),
]

// ---------- 图片 ----------

/** 用户上传的图片(内存;key = itemId:type,value = data URL) */
const uploadedImages = new Map<string, string>()

const images = [
  http.get(`${BASE}/Items/:itemId/Images/:type`, ({ request: req, params }) => {
    const itemId = String(params.itemId)
    const type = String(params.type)
    // 用户上传的图片优先(真实站同一端点返回已上传图)
    const uploaded = uploadedImages.get(`${itemId}:${type}`)
    if (uploaded) {
      const [meta, b64] = uploaded.split(',')
      const mime = /:(.*?);/.exec(meta ?? '')?.[1] ?? 'image/png'
      const bin = atob(b64 ?? '')
      const bytes = new Uint8Array(bin.length)
      for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i)
      return new HttpResponse(bytes, { headers: { 'Content-Type': mime, 'Cache-Control': 'no-cache' } })
    }
    const item = itemById.get(itemId)
    const title = item?.Name ?? 'Serata'
    const svg = type === 'Primary'
      ? posterSvg(title, { subtitle: item?.ProductionYear ? String(item.ProductionYear) : undefined })
      : backdropSvg(title)
    return new HttpResponse(svg, {
      headers: { 'Content-Type': 'image/svg+xml', 'Cache-Control': 'no-cache' },
    })
  }),

  // 上传图片(真实站 POST /Items/{id}/Images/{type},multipart;mock 存 base64)
  http.post(`${BASE}/Items/:itemId/Images/:type`, async ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const itemId = String(params.itemId)
    const type = String(params.type)
    const form = await req.formData()
    const file = form.get('file') ?? form.get('data') ?? [...form.values()].find((v) => typeof v !== 'string')
    if (!(file instanceof Blob)) return new HttpResponse(null, { status: 400 })
    const buf = await file.arrayBuffer()
    let bin = ''
    const bytes = new Uint8Array(buf)
    for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]!)
    uploadedImages.set(`${itemId}:${type}`, `data:${file.type || 'image/png'};base64,${btoa(bin)}`)
    const item = itemById.get(itemId)
    if (item) {
      item.ImageTags = { ...item.ImageTags, [type]: `uploaded-${Date.now()}` }
      if (type === 'Backdrop') item.BackdropImageTags = [`uploaded-${Date.now()}`]
    }
    return new HttpResponse(null, { status: 204 })
  }),

  // 删除图片(真实站 DELETE /Items/{id}/Images/{type})
  http.delete(`${BASE}/Items/:itemId/Images/:type`, ({ request: req, params }) => {
    const u = requireUser(req)
    if (!u) return new HttpResponse(null, { status: 401 })
    const key = `${String(params.itemId)}:${String(params.type)}`
    uploadedImages.delete(key)
    const item = itemById.get(String(params.itemId))
    if (item?.ImageTags) {
      const tags = { ...item.ImageTags }
      delete tags[String(params.type)]
      item.ImageTags = tags
      if (String(params.type) === 'Backdrop') item.BackdropImageTags = []
    }
    return new HttpResponse(null, { status: 204 })
  }),

  http.get(`${BASE}/Users/:userId/Images/Primary`, ({ params }) => {
    const user = users.find((x) => x.Id === params.userId)
    return new HttpResponse(avatarSvg(user?.Name ?? '?'), { headers: { 'Content-Type': 'image/svg+xml' } })
  }),
]

function tvChildrenOfLibraries(libId: string): BaseItemDto[] {
  const out: BaseItemDto[] = []
  const walk = (parentId: string) => {
    for (const c of allItems.filter((i) => i.ParentId === parentId)) {
      out.push(c)
      walk(c.Id)
    }
  }
  walk(libId)
  return out
}

export const handlers = [...auth, ...browse, ...operations, ...playback, ...images, ...admin]

// delay 供真实感测试用(未启用:本地 mock 即时返回)
export { delay }