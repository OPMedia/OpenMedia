/** tick/时间/时长格式化工具 */
import type { BaseItemDto } from '@/api/types'

/** 100ns ticks → "1:23:45"(进度条显示用) */
export function ticksToTime(ticks: number | undefined): string {
  if (!ticks) return '0:00'
  const total = Math.round(ticks / 10_000_000)
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
  return `${m}:${String(s).padStart(2, '0')}`
}

/** ticks → "148 分钟"(元数据行) */
export function ticksToMinutes(ticks: number | undefined): number {
  return Math.round((ticks ?? 0) / 10_000_000 / 60)
}

/** 详情页元数据行片段:年份 / 时长 / 评分 */
export function metaBits(item: BaseItemDto): string[] {
  const bits: string[] = []
  if (item.ProductionYear) bits.push(String(item.ProductionYear))
  if (item.RunTimeTicks) bits.push(`${ticksToMinutes(item.RunTimeTicks)} min`)
  return bits
}

export function formatRate(rating: number | undefined): string {
  return rating ? rating.toFixed(1) : ''
}