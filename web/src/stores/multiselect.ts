/**
 * 多选模式(1:1 对齐 Emby 4.9.5 multiselect):
 * 真实站进入多选后给 <html> 加 `.multi-select-active`,给选中卡加 `.item-multiselected`;
 * 对应 CSS 在已下载的原版 card_nontv2.css 内(其它悬浮按钮隐藏、选中卡 overlay 背景透明)。
 */
import { computed, ref, watch } from 'vue'

/** 已选条目 id(全局共享:任一卡进入多选,全部卡显示勾选框) */
const selectedIds = ref<string[]>([])

export const multiSelectActive = computed(() => selectedIds.value.length > 0)
export const selectedCount = computed(() => selectedIds.value.length)

watch(
  multiSelectActive,
  (on) => {
    if (typeof document !== 'undefined') {
      document.documentElement.classList.toggle('multi-select-active', on)
    }
  },
  { immediate: true },
)

export function isMultiSelected(id: string): boolean {
  return selectedIds.value.includes(id)
}

/** 真实站:点勾选框 = 加入/移出选择集(移出最后一个即退出多选) */
export function toggleMultiSelected(id: string): void {
  const i = selectedIds.value.indexOf(id)
  if (i >= 0) selectedIds.value = selectedIds.value.filter((x) => x !== id)
  else selectedIds.value = [...selectedIds.value, id]
}

export function clearMultiSelect(): void {
  selectedIds.value = []
}
