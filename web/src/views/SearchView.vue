<script setup lang="ts">
/**
 * 搜索页 —— 一个真正的搜索页（用户 2026-10-20 口径：
 * "搜索页就是搜索页 简简单单的一个搜索页 可以看到搜索历史的搜索页"）。
 *
 * 与上一版的区别（上一版的问题：本页输入框被 scoped 样式 display:none 干掉，
 * 于是"搜索页"退化成一个"推荐列表页" —— 标题「推荐」，内容是 /Items/Latest 的最新条目）：
 * - 本页顶部一个**可见**的搜索输入框，进页面自动聚焦；输入即搜（debounce 300ms），回车立即搜；
 * - 无查询词时只显示**搜索历史**（可点击重搜 / 单条删除 / 清空）；
 * - 有查询词时显示**海报卡网格**（用户 2026-10-20 第二轮："搜索出来的候选结果和结果都要用卡片式
 *   不要用条式 很难看 标题过长就省略"）—— 复用 @/components/EmbyCard.vue，与库页/首页同一套卡片语言；
 * - 「推荐」列表与它的 getLatestItems 请求整条删除 —— 用户要的是"简简单单的一个搜索页"。
 *
 * 历史存储：localStorage['serata.searchHistory'] = string[]，最多 10 条、最近的在前、
 * 大小写不敏感去重（同一份数据在 App.vue 之外没有别的读写方）。
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { BaseItemDto } from '@/api/types'
import { getItems } from '@/api/endpoints'
import { useAuthStore } from '@/stores/auth'
import EmbyCard from '@/components/EmbyCard.vue'

const HISTORY_KEY = 'serata.searchHistory'
const HISTORY_MAX = 10
/** 输入即搜的防抖窗口（毫秒） */
const DEBOUNCE_MS = 300

const auth = useAuthStore()
const route = useRoute()
const { t } = useI18n()

const inputEl = ref<HTMLInputElement | null>(null)
/** 搜索词：来源 = 本页输入框 / 侧栏搜索框写入的 ?q= / 点击历史条目 */
const query = ref(typeof route.query.q === 'string' ? route.query.q : '')
const results = ref<BaseItemDto[]>([])
const searching = ref(false)
const hasQuery = computed(() => query.value.trim().length > 0)

// ---------- 搜索历史（localStorage） ----------

const history = ref<string[]>(loadHistory())

/** 读历史：脏数据一律丢弃（非数组 / 非字符串 / 空串），并按"最近的在前 + 大小写不敏感去重"规范化 */
function loadHistory(): string[] {
  try {
    const raw: unknown = JSON.parse(localStorage.getItem(HISTORY_KEY) ?? '[]')
    if (!Array.isArray(raw)) return []
    const out: string[] = []
    for (const v of raw) {
      if (typeof v !== 'string') continue
      const s = v.trim()
      if (!s) continue
      if (out.some((x) => x.toLowerCase() === s.toLowerCase())) continue
      out.push(s)
      if (out.length >= HISTORY_MAX) break
    }
    return out
  } catch {
    return []
  }
}

function persistHistory(): void {
  try {
    localStorage.setItem(HISTORY_KEY, JSON.stringify(history.value))
  } catch {
    /* 隐私模式 / 配额写满：历史存不下不影响搜索本身，不吞掉搜索功能 */
  }
}

/**
 * 写入一条历史。
 * 除了"大小写不敏感去重"（需求原话），这里额外丢掉新词的**严格前缀**条目：
 * 输入即搜（debounce）会把"星际穿越"依次搜成"星"→"星际"→…，
 * 每个前缀都进历史的话，历史里就全是半截词 —— 同一个输入过程只留最终那一条。
 */
function remember(term: string): void {
  const s = term.trim()
  if (!s) return
  const lower = s.toLowerCase()
  const rest = history.value.filter((x) => {
    const xl = x.toLowerCase()
    if (xl === lower) return false
    return !(xl.length < lower.length && lower.startsWith(xl))
  })
  history.value = [s, ...rest].slice(0, HISTORY_MAX)
  persistHistory()
}

function removeHistory(term: string): void {
  history.value = history.value.filter((x) => x !== term)
  persistHistory()
}

function clearHistory(): void {
  history.value = []
  persistHistory()
}

/** 删除按钮的 aria-label：带上词条本身，读屏时能分清删的是哪一条 */
function removeAriaLabel(term: string): string {
  return `${t('search.historyRemove')}：${term}`
}

// ---------- 搜索 ----------

async function runSearch(term: string): Promise<void> {
  const q = term.trim()
  if (!q) {
    results.value = []
    return
  }
  searching.value = true
  try {
    results.value = (await getItems(auth.user!.Id, {
      SearchTerm: q, Recursive: true, IncludeItemTypes: 'Movie,Series,Episode',
      Limit: 40, Fields: 'PrimaryImageAspectRatio,ProductionYear',
    })).Items
    // 成功搜索后才写历史（请求失败不留痕）
    remember(q)
  } catch {
    results.value = []
  } finally {
    searching.value = false
  }
}

let debounceTimer: ReturnType<typeof setTimeout> | undefined

function onInput(): void {
  clearTimeout(debounceTimer)
  const term = query.value.trim()
  if (!term) {
    results.value = []
    return
  }
  debounceTimer = setTimeout(() => {
    void runSearch(term)
  }, DEBOUNCE_MS)
}

function onEnter(): void {
  clearTimeout(debounceTimer)
  const term = query.value.trim()
  if (term) void runSearch(term)
}

/** 点历史条目 = 回填 + 立刻重搜 */
function searchHistory(term: string): void {
  clearTimeout(debounceTimer)
  query.value = term
  inputEl.value?.focus()
  void runSearch(term)
}

// 侧栏搜索框写入 ?q= 后，本页跟随查询（保留上一版行为）
watch(() => route.query.q, (v) => {
  query.value = typeof v === 'string' ? v : ''
  if (query.value.trim()) void runSearch(query.value)
})

onMounted(() => {
  // 读取时已按"最多 10 条 / 最近的在前 / 大小写不敏感去重"规范化；原始串与规范化结果不一致时
  // 回写一次，保证 localStorage 里存的就是页面看到的那份（脏数据不在本地留存）。
  try {
    if (localStorage.getItem(HISTORY_KEY) !== JSON.stringify(history.value)) persistHistory()
  } catch {
    /* 读不到就跳过，不影响搜索 */
  }
  // 进页面自动聚焦（<Transition mode="out-in"> 下要等这一帧挂完）
  void nextTick(() => inputEl.value?.focus())
  if (query.value.trim()) void runSearch(query.value)
})

onBeforeUnmount(() => {
  clearTimeout(debounceTimer)
})

// 结果行时代的 itemTypeName() / go() / rowArt() / ICON_MORE 已随 listItem 行一起删除：
// 卡片自己负责跳转（EmbyCard 的 .cardContent-button @click=open）、图源（cardArtUrl 内建）与副标题。
// 这里不再持有任何"行"相关的渲染逻辑，避免死代码被 noUnusedLocals 抓出来。
</script>

<template>
  <!--
    本页 `<main>` 只挂 `viewScroller scrollFrameY`，这两个类在 vendor CSS 里 0 定义 ——
    它们**能不能滚由 styles/serata/emby-bridge.css 的 `.scrollFrameY:not(.scrollY):not(.overflowYScroll)` 统一兜底**
    （该规则同时修掉 ItemDetailView / AdminUsersView 两处同源缺陷，详见那里的注释）。
    这里不再加 page-scoped 的 `scrollY`：有全局定义就不留局部 hack，否则同一缺陷会被抄到第四个页面。
    实测（真实滚轮 deltaY=400 ×10 @1600）：main 是唯一滚动容器，scrollTop 0→776 = maxScroll，
    最后一张结果卡完整进视口（见 scratch/lead5/scroll-audit-after.json）。
  -->
  <main class="viewScroller scrollFrameY view flex flex-direction-row page page-withFullDrawer page-withDockedDrawer focuscontainer-x">
    <div class="scrollSlider flex-grow padded-top-page padded-left padded-left-page padded-right scrollSliderY focuscontainer-y navout-up">
      <div class="searchFields padded-bottom">
        <div class="searchFieldsInner flex align-items-center justify-content-center flex-direction-column">
          <!-- 本页自己的搜索输入框（可见；上一版这里是 display:none） -->
          <div class="inputContainer flex align-items-center focuscontainer-x searchInputContainer">
            <i class="md-icon searchFieldsSearchIcon hide secondaryText" aria-hidden="true">search</i>
            <label class="inputLabel s-sr" for="searchPageInput">{{ t('search.title') }}</label>
            <input
              id="searchPageInput"
              ref="inputEl"
              v-model="query"
              type="text"
              maxlength="60"
              class="searchfields-txtSearch flex-grow emby-input"
              autocomplete="off"
              enterkeyhint="search"
              :placeholder="t('search.placeholder')"
              @input="onInput"
              @keydown.enter="onEnter"
            />
          </div>

          <!-- 无查询词：只有搜索历史 -->
          <div v-if="!hasQuery" class="verticalSection searchSuggestions searchHistory padded-top">
            <div class="searchHistory__head">
              <h2 class="sectionTitle suggestionsTitle">{{ t('search.history') }}</h2>
              <button
                v-if="history.length"
                type="button"
                class="searchHistory__clear"
                :aria-label="t('search.historyClear')"
                @click="clearHistory"
              >{{ t('search.historyClear') }}</button>
            </div>

            <p v-if="!history.length" class="secondaryText searchHistory__empty">{{ t('search.historyEmpty') }}</p>

            <ul v-else class="searchHistory__list">
              <li v-for="h in history" :key="h" class="searchHistory__item">
                <button type="button" class="searchHistory__term" @click="searchHistory(h)">
                  <span class="searchHistory__text">{{ h }}</span>
                </button>
                <button
                  type="button"
                  class="searchHistory__remove"
                  :aria-label="removeAriaLabel(h)"
                  @click="removeHistory(h)"
                >
                  <svg class="searchHistory__x" viewBox="0 0 24 24" aria-hidden="true">
                    <path d="M6 6l12 12M18 6 6 18" />
                  </svg>
                </button>
              </li>
            </ul>
          </div>

          <!-- 有查询词：结果卡片网格（用户："候选结果和结果都要用卡片式 不要用条式"） -->
          <div v-else class="verticalSection searchSuggestions padded-top">
            <div>
              <h2 class="sectionTitle suggestionsTitle">{{ t('search.results') }}</h2>
            </div>
            <div v-if="searching" class="page-loading">{{ t('common.loading') }}</div>
            <div v-else-if="!results.length" class="secondaryText padded-left padded-right">{{ t('search.noResults') }}</div>
            <!--
              网格容器 = 库页同款 `itemsContainer vertical-wrap padded-bottom-page`（LibraryView 网格分支），
              卡片尺寸由 vendor 的 .portraitCard（100% / max(--portrait-cards, 3)）给出：
              1600 视口下容器 1265px、--portrait-cards=8 → 单卡 ≈158px（库页实测 156.3px，同一套语言）。
              searchResultsGrid 只是本页的 scoped 钩子（撑满宽度），不写任何卡片内部样式。
            -->
            <div class="itemsContainer vertical-wrap padded-bottom-page searchResultsGrid">
              <EmbyCard v-for="item in results" :key="item.Id" :item="item" shape="portrait" />
            </div>
          </div>
        </div>
      </div>
    </div>
  </main>
</template>

<style scoped>
/*
 * 本页输入框：可见（此前是 display:none，那正是"搜索页变成推荐页"的原因）。
 * 只用 --s-* 令牌。
 *
 * 不要方框（用户 2026-10-20："选中搜索框之后为什么会有方框框着 看着好丑不需要那个方框"）。
 * 实测：聚焦前后计算样式**零差异**（输入框 outline:none，容器 outline-style:none）——
 * 那个"方框"不是 focus ring，是这里常驻的 1px 边框 + 填充底色 + 圆角。
 * 所以把盒子整个拆掉：无边框、无底色、无圆角，只留一条底线当"这里能打字"的提示。
 * 内边距归零，底线宽度就等于文字宽度，不再多出左右各 12px 的空框。
 */
.searchInputContainer {
  display: flex;
  align-items: center;
  gap: var(--s-sp-2);
  width: 100%;
  max-width: 640px;
  /*
   * 去掉方框后必须靠左：父级 .searchFieldsInner 是 align-items:center 的居中列，
   * 有边框时居中像个"搜索框"，没边框就只剩一根悬在中间的线 —— 和下方左对齐的
   * 「搜索历史」两条左边缘对不上。≤768px 时容器本来就占满宽度，这条不改变移动端。
   */
  align-self: flex-start;
  margin-bottom: var(--s-sp-4);
  padding: 0;
  border: 0;
  border-bottom: 1px solid var(--s-line);
  border-radius: 0;
  background: transparent;
}

.searchInputContainer .searchfields-txtSearch {
  height: 44px;
  padding: 0;
  border: 0;
  border-radius: 0;
  background: transparent;
  color: var(--s-text-1);
  font-family: var(--s-font);
  font-size: var(--s-fs-body);
  font-weight: var(--s-fw-regular);
  outline: none;
}

.searchInputContainer .searchfields-txtSearch::placeholder {
  color: var(--s-text-3);
}

/* 标题（真实站实测：fs 21.636 / fw 600 / margin 10.818 0）—— 沿用上一版实测值，不动 */
.suggestionsTitle { font-size: 21.636px; font-weight: 600; margin: 10.818px 0; }

/* ---------- 搜索历史 ---------- */
.searchHistory {
  width: 100%;
}

.searchHistory__head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--s-sp-3);
}

.searchHistory__clear {
  flex: none;
  padding: var(--s-sp-1) var(--s-sp-2);
  border: 0;
  border-radius: var(--s-r-sm);
  background: transparent;
  color: var(--s-text-3);
  font-family: var(--s-font);
  font-size: var(--s-fs-sm);
  cursor: pointer;
  transition:
    color var(--s-dur-fast) var(--s-ease-std),
    background-color var(--s-dur-fast) var(--s-ease-std);
}

.searchHistory__clear:hover {
  color: var(--s-text-1);
  background: var(--s-hover);
}

.searchHistory__empty {
  margin: 0;
  padding: var(--s-sp-2) 0;
}

.searchHistory__list {
  display: flex;
  flex-direction: column;
  gap: 2px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.searchHistory__item {
  display: flex;
  align-items: center;
  gap: var(--s-sp-1);
}

.searchHistory__term {
  flex: 1 1 auto;
  min-width: 0;
  display: flex;
  align-items: center;
  height: 40px;
  padding: 0 var(--s-sp-3);
  border: 0;
  border-radius: var(--s-r-sm);
  background: transparent;
  color: var(--s-text-2);
  font-family: var(--s-font);
  font-size: var(--s-fs-body);
  font-weight: var(--s-fw-medium);
  text-align: start;
  cursor: pointer;
  transition:
    color var(--s-dur-fast) var(--s-ease-std),
    background-color var(--s-dur-fast) var(--s-ease-std);
}

.searchHistory__term:hover {
  color: var(--s-text-1);
  background: var(--s-hover);
}

.searchHistory__text {
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.searchHistory__remove {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  padding: 0;
  border: 0;
  border-radius: var(--s-r-sm);
  background: transparent;
  color: var(--s-text-3);
  cursor: pointer;
  transition:
    color var(--s-dur-fast) var(--s-ease-std),
    background-color var(--s-dur-fast) var(--s-ease-std);
}

.searchHistory__remove:hover {
  color: var(--s-text-1);
  background: var(--s-hover);
}

.searchHistory__x {
  width: 16px;
  height: 16px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.7;
  stroke-linecap: round;
  stroke-linejoin: round;
}

/* ---------- 搜索结果网格 ----------
   不写任何卡片内部样式（片名单行/省略号、海报圆角、悬停位移全归 EmbyCard）；
   这里只负责"让网格铺满可用宽度"，卡片宽高由 vendor 的 .portraitCard 按 --portrait-cards 计算。
   width:100% 是必须的：父级 .searchFieldsInner 是 align-items:center 的纵向 flex，
   不写就会收缩成 fit-content，卡片宽度算出来是一堆碎值。 */
.searchResultsGrid {
  width: 100%;
}
</style>
