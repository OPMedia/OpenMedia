/**
 * 构建期把后端 ServerConfiguration.BaseUrl 映射到前端 base。
 *
 * 后端 BaseUrl=/media 时，API 前缀设为 VITE_API_PREFIX=/media/emby，
 * 于是路由 base / Vite 资源 base 都自动得到 /media/app/；
 * VITE_BASE_PATH 显式设置时优先（值与后端 BaseUrl + /app/ 保持一致）。
 * 两个变量都不带子路径时返回 '/'，与原有默认行为一致。
 */
export function resolveWebBasePath(configuredBasePath?: string, apiPrefix?: string): string {
  const explicit = configuredBasePath?.trim()
  if (explicit) {
    return explicit.endsWith('/') ? explicit : `${explicit}/`
  }

  const prefix = apiPrefix?.trim()
  if (!prefix) {
    return '/'
  }

  const match = /^(.+)\/emby\/?$/.exec(prefix)
  return match ? `${match[1]}/app/` : '/'
}
