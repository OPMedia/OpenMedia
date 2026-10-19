<script setup lang="ts">
/**
 * 搜索页 —— 1:1 还原 Emby 4.9.5 list.html?type=search(基准=真实站运行时 dump,
 * 见 reference/dumps/emby495-search.md):
 * - 桌面(docked drawer)语境搜索输入由侧栏承担;本页 = "推荐"列表(suggestionsTitle + listItem 行)
 * - listItem 行形态:64px 海报缩略 + 名/类型两行 + 右侧更多钮(与卡片不同)
 * - 输入过滤:本地过滤推荐列表(真实站走 /Items?SearchTerm;有词时实时建议面板,后续轮补)
 */
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { BaseItemDto } from '@/api/types'
import { getItems, getLatestItems, imageUrl } from '@/api/endpoints'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
const { t } = useI18n()

const ICON_MORE = '\uE5D3'

const loading = ref(true)
const suggestions = ref<BaseItemDto[]>([])
/** 搜索词:来源 = 侧栏搜索框(?q=);真实站搜索页内输入框为隐藏元素(实测 0×0) */
const query = ref(typeof route.query.q === 'string' ? route.query.q : '')
const results = ref<BaseItemDto[]>([])
const searching = ref(false)

async function runSearch() {
  const q = query.value.trim()
  if (!q) { results.value = []; return }
  searching.value = true
  try {
    results.value = (await getItems(auth.user!.Id, {
      SearchTerm: q, Recursive: true, IncludeItemTypes: 'Movie,Series,Episode',
      Limit: 40, Fields: 'PrimaryImageAspectRatio,ProductionYear',
    })).Items
  } catch { results.value = [] } finally { searching.value = false }
}

const shown = computed(() => (query.value.trim() ? results.value : suggestions.value))

// 侧栏搜索框写入 ?q= 后,本页跟随查询
watch(() => route.query.q, (v) => {
  query.value = typeof v === 'string' ? v : ''
  if (query.value.trim()) void runSearch()
})

onMounted(async () => {
  try {
    const userId = auth.user!.Id
    // 真实站"推荐"区 = 最近条目混合(影片/剧集)
    const [movies, series] = await Promise.all([
      getLatestItems(userId, { Limit: 10, ParentId: '100' }),
      getLatestItems(userId, { Limit: 10, ParentId: '200' }),
    ])
    suggestions.value = [...movies, ...series]
    if (query.value.trim()) await runSearch()
  } catch { /* 失败静默 */ } finally {
    loading.value = false
  }
})

const itemTypeName = (item: BaseItemDto): string => {
  switch (item.Type) {
    case 'Movie': return t('search.typeMovie')
    case 'Series': return t('search.typeSeries')
    case 'Episode': return t('search.typeEpisode')
    default: return item.Type
  }
}

function go(item: BaseItemDto) {
  router.push({ name: 'item', params: { id: item.Id } })
}
</script>

<template>
  <main class="viewScroller scrollFrameY view flex flex-direction-row page page-withFullDrawer page-withDockedDrawer focuscontainer-x">
    <div class="scrollSlider flex-grow padded-top-page padded-left padded-left-page padded-right scrollSliderY focuscontainer-y navout-up">
      <div class="searchFields padded-bottom">
        <div class="searchFieldsInner flex align-items-center justify-content-center flex-direction-column">
          <!-- 搜索输入:真实站本页输入框为隐藏元素(实测 h/w=0),搜索入口在侧栏搜索框 -->
          <div class="inputContainer flex-grow flex align-items-center focuscontainer-x searchInputContainer">
            <i class="md-icon searchFieldsSearchIcon hide secondaryText">search</i>
            <label class="inputLabel" for="searchPageInput"></label>
            <input
              id="searchPageInput" v-model="query" type="text" maxlength="60"
              class="searchfields-txtSearch flex-grow emby-input" autocomplete="off"
            />
          </div>

          <div class="verticalSection searchSuggestions padded-top">
            <div>
              <h2 class="sectionTitle suggestionsTitle">{{ query.trim() ? t('search.results') : t('search.suggestions') }}</h2>
            </div>
            <div v-if="searching" class="page-loading">{{ t('common.loading') }}</div>
            <div v-else-if="query.trim() && !results.length" class="secondaryText padded-left padded-right">{{ t('search.noResults') }}</div>
            <div class="itemsContainer suggestionsItemsContainer vertical-list">
              <div
                v-for="item in shown" :key="item.Id"
                class="listItem listItem-autoactive itemAction listItemCursor listItem-hoverable"
                data-action="link"
                @click="go(item)"
              >
                <div class="listItem-content listItem-content-margin listItem-content-bg listItemContent-touchzoom listItem-border listItem-border-offset listItem-border-offset-portrait listItem-border-offset-smaller listItem-border-offset-portrait-smaller">
                  <div class="listItemImageContainer listItemImageContainer-smaller itemAction listItemImageContainer-portrait">
                    <img draggable="false" class="listItemImage listItemImage-smaller listItemImage-portrait coveredImage coveredImage-noScale" :src="imageUrl(item.Id, 'Primary', item.ImageTags?.Primary, { maxWidth: 120, maxHeight: 180, quality: 90 })" loading="lazy" decoding="async" />
                  </div>
                  <div class="listItemBody itemAction listItemBody-reduceypadding listItemBody-3-lines">
                    <div class="listItemBodyText listItemBodyText-lf">{{ item.Name }}</div>
                    <div class="listItemBodyText listItemBodyText-lf listItemBodyText-secondary secondaryText">{{ itemTypeName(item) }}</div>
                  </div>
                  <button type="button" title="更多" class="listItemButton listItemContextMenuButton itemAction md-icon paper-icon-button-light emby-button-focusscale" data-action="menu" @click.stop>
                    <i class="md-icon">{{ ICON_MORE }}</i>
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </main>
</template>

<style scoped>
/* 本页输入框:真实站为隐藏元素(实测 0×0),搜索入口在侧栏 */
.searchInputContainer { display: none; }
/* 推荐标题(真实站实测:fs 21.64 / fw 600 / margin 10.82 0) */
.suggestionsTitle { font-size: 21.636px; font-weight: 600; margin: 10.818px 0; }
/* 行尺寸(真实站实测:listItem 105.3 / content 96.6 br 8.65 margin 4.33 0 / 图 64.4×96.6 br 6.06) */
.suggestionsItemsContainer .listItem-content { border-radius: 8.6544px; margin: 4.3272px 0; }
.suggestionsItemsContainer .listItemImageContainer,
.suggestionsItemsContainer .listItemImage { border-radius: 6.05808px; }
.suggestionsItemsContainer .listItemBody { padding: 3.46176px 10.818px 3.46176px 14.424px; }
</style>