/**
 * Emby 兼容 HTTP 客户端(SPEC/api §1.1 三件铁律):
 * 1. 双通道鉴权:Authorization: MediaBrowser ... 头 + 所有请求 ?api_key=
 * 2. CORS 只放行 Authorization 头(后端实现项)
 * 3. Expose-Headers: Accept-Ranges/Content-Length/Content-Range(后端实现项)
 * 统一前缀常量(SPEC/web §6):构建时 VITE_API_PREFIX 与后端 base_path 一致。
 */
import type { QueryResult } from './types'

export const API_PREFIX = import.meta.env.VITE_API_PREFIX ?? '/emby'

export const CLIENT_NAME = 'Serata Web'
export const CLIENT_VERSION = '0.1.0'

/** 每设备一个稳定 DeviceId(localStorage 持久化) */
export function getDeviceId(): string {
  const key = 'serata.deviceId'
  let id = localStorage.getItem(key)
  if (!id) {
    id = crypto.randomUUID().replace(/-/g, '')
    localStorage.setItem(key, id)
  }
  return id
}

const TOKEN_KEY = 'serata.token'

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

export function setToken(token: string | null): void {
  if (token) localStorage.setItem(TOKEN_KEY, token)
  else localStorage.removeItem(TOKEN_KEY)
}

/** MediaBrowser 鉴权头(SPEC/api §1.1) */
export function authHeader(token: string | null = getToken()): string {
  const parts = [
    `Client="${CLIENT_NAME}"`,
    `Device="Web"`,
    `DeviceId="${getDeviceId()}"`,
    `Version="${CLIENT_VERSION}"`,
  ]
  if (token) parts.push(`Token="${token}"`)
  return `MediaBrowser ${parts.join(', ')}`
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly body?: unknown,
  ) {
    super(`API ${status}`)
  }
}

/** 组装 Emby API 绝对路径(相对同源;可被 MSW 或真后端消费) */
export function apiUrl(path: string, query?: Record<string, string | number | boolean | undefined>): string {
  const url = new URL(`${API_PREFIX}${path}`, window.location.origin)
  if (query) {
    for (const [k, v] of Object.entries(query)) {
      if (v !== undefined && v !== '') url.searchParams.set(k, String(v))
    }
  }
  const token = getToken()
  if (token) url.searchParams.set('api_key', token) // 铁律:所有请求带 api_key
  return url.pathname + url.search
}

export async function request<T>(
  path: string,
  opts: {
    method?: string
    query?: Record<string, string | number | boolean | undefined>
    body?: unknown
    noAuth?: boolean
  } = {},
): Promise<T> {
  const headers: Record<string, string> = {
    Authorization: authHeader(opts.noAuth ? null : getToken()),
  }
  if (opts.body !== undefined) headers['Content-Type'] = 'application/json'
  const resp = await fetch(apiUrl(path, opts.query), {
    method: opts.method ?? 'GET',
    headers,
    body: opts.body === undefined ? undefined : JSON.stringify(opts.body),
  })
  if (!resp.ok) throw new ApiError(resp.status)
  if (resp.status === 204) return undefined as T
  return (await resp.json()) as T
}

export function queryResult<T>(data: unknown): QueryResult<T> {
  return data as QueryResult<T>
}
