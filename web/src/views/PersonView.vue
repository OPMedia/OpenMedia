<script setup lang="ts">
/**
 * 人物页 —— 1:1 还原 Emby 4.9.5 人物详情(person.html):
 * itemView 同族布局:人物头像卡 + 姓名 + 类型标签 + "出演作品"分节(按 PersonIds 查条目)。
 * 真实站人物页 view 类与 item 页同族(detailImageContainer-person + itemName-primary)。
 */
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { BaseItemDto } from '@/api/types'
import { getPerson, getItems, cardArtUrl } from '@/api/endpoints'
import { useAuthStore } from '@/stores/auth'
import EmbyCard from '@/components/EmbyCard.vue'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const { t } = useI18n()

const loading = ref(true)
const person = ref<BaseItemDto | null>(null)
const items = ref<BaseItemDto[]>([])
/** 人物在作品中的角色(从来源条目带入;真实站人物页显示 演员/导演 等) */
const role = computed(() => (typeof route.query.role === 'string' ? route.query.role : ''))

/**
 * 人物头像。
 *
 * 原实现无条件拼 `imageUrl(person.Id, 'Primary', person.ImageTags?.Primary)` —— tag 是 undefined 也照拼，
 * 真机 Emby 4.9.5.0 对没有 Primary 的条目 `/Items/{id}/Images/Primary` 返回 **HTTP 500**（不是 404）。
 * 改走 `cardArtUrl`：没有 tag 返回空串 → 模板用 v-if 不渲染 <img>，不发这次必然 5xx 的请求。
 */
const posterSrc = computed(() =>
  cardArtUrl(person.value, 'portrait', { maxWidth: 375, maxHeight: 563, quality: 90 }))

/** 加载失败原因(非空时页面走错误态,不再退化成"查无此人") */
const loadError = ref('')

onMounted(async () => {
  const id = String(route.params.id)
  try {
    const userId = auth.user!.Id
    // getPerson 内部已经把路由里的 id 换成 Name 再查（真机 /Persons/{id} 恒 500，只认 Name）
    person.value = await getPerson(id, userId)
    document.title = `${person.value.Name} · ${t('app.name')}`
    items.value = (await getItems(userId, {
      PersonIds: id, Recursive: true, IncludeItemTypes: 'Movie,Series',
      Fields: 'PrimaryImageAspectRatio,ProductionYear', Limit: 60,
    })).Items
  } catch (e) {
    // 这里原来是 `catch { /* 静默 */ }`：真机 /Persons/{id} 恒 500 时错误被吃掉，
    // person 停在 null → 下面的"该人物作品"查询**根本不发** → 整页退化成 item.notFound，
    // 看起来像"这个人物不存在"，实际是接口 500。现在把原因暴露到页面错误态 + 控制台。
    loadError.value = e instanceof Error ? e.message : String(e)
    console.error('[PersonView] 人物页加载失败', id, e)
  } finally {
    loading.value = false
  }
})

function goHome() { router.push('/') }
</script>

<template>
  <div>
    <!-- 加载态 / 错误态也必须带 `<main>` 滚动壳。
         改前这三个状态都是裸 <div>，加载期间整页没有滚动容器（与 ItemDetailView 同一类缺陷，
         见 docs/adr/0012-web-home-hero.md 第六轮）。壳上的 class 与正常态逐字相同，
         三种状态互切时布局零位移。 -->
    <main
      v-if="loading"
      class="itemView darkContentContainer-item view flex flex-direction-column scrollFrameY flex-grow emby-scroller page scrollY focuscontainer-x"
      aria-busy="true"
    >
      <div class="scrollSlider flex-grow flex-direction-column padded-top-page scrollSliderY focuscontainer-y navout-up">
        <div class="page-loading">{{ t('common.loading') }}</div>
      </div>
    </main>
    <template v-else-if="person">
      <main class="itemView darkContentContainer-item view flex flex-direction-column scrollFrameY flex-grow emby-scroller page scrollY focuscontainer-x">
        <div class="scrollSlider flex-grow flex-direction-column padded-top-page scrollSliderY focuscontainer-y navout-up">
          <div class="topDetailsContainer flex flex-direction-row verticalSections-item">
            <div class="padded-left padded-left-page padded-right verticalSection detailMainContainer flex flex-direction-row align-items-flex-start">
              <div class="detailImageContainer detailImageContainer-main flex flex-direction-column justify-content-center itemsContainer vertical-wrap">
                <div class="card portraitCard detailImageContainerCard card-hoverable">
                  <div class="detailImageContainerCard-cardBox cardBox">
                    <div class="itemAction cardContent cardImageContainer cardContent-background detailImage cardContent-bxsborder-fv coveredImage coveredImage-noScale cardPadder-portrait detailImage-transparent">
                      <!-- 没有图源时 posterSrc 为空串，v-if 整个不渲染：避免 src="" 让浏览器去请求页面本身 -->
                      <img v-if="posterSrc" draggable="false" alt=" " class="cardImage cardImage-bxsborder-fv coveredImage coveredImage-noScale" :src="posterSrc" loading="lazy" decoding="async" />
                    </div>
                  </div>
                </div>
              </div>

              <div class="flex-grow detailTextContainer verticalFieldItems details-largefont">
                <div class="detailNameContainer focuscontainer-x verticalFieldItem verticalFieldItems detail-lineItem focusable">
                  <div class="verticalFieldItem flex align-items-center flex-wrap-wrap itemPrimaryNameContainer">
                    <h1 class="itemName-primary">{{ person.Name }}</h1>
                  </div>
                </div>
                <div v-if="role" class="mediaInfo verticalFieldItem detail-mediaInfoPrimary flex align-items-center flex-wrap-wrap mediaInfoItems detail-lineItem">
                  <div class="mediaInfoItem">{{ role }}</div>
                </div>
                <div class="verticalFieldItem detailButtons mainDetailButtons flex align-items-flex-start flex-wrap-wrap focuscontainer-x detail-lineItem">
                  <button type="button" class="raised detailButton emby-button emby-button-focusscale" title="首页" @click="goHome">
                    <i class="md-icon button-icon button-icon-left">&#xE88A;</i>
                    <span>{{ t('app.home') }}</span>
                  </button>
                </div>
              </div>
            </div>
          </div>

          <div class="details-additionalContent padded-bottom-page verticalSections verticalSections-item">
            <div v-if="items.length" class="verticalSection verticalSection-cards emby-scrollbuttons-scroller">
              <div class="sectionTitleContainer sectionTitleContainer-cards padded-left padded-left-page padded-right">
                <h2 class="sectionTitle sectionTitle-cards">{{ t('person.appearsIn') }}</h2>
              </div>
              <div class="emby-scroller padded-top-focusscale padded-bottom-focusscale padded-left padded-left-page padded-right scrollX hiddenScrollX scrollFrameX flex-direction-row">
                <div class="scrollSlider focusable itemsContainer focuscontainer-x scrollSliderX emby-scrollbuttons-scrollSlider">
                  <EmbyCard v-for="it in items" :key="it.Id" :item="it" shape="portrait" />
                </div>
              </div>
            </div>
            <div v-else class="padded-left padded-left-page padded-right secondaryText">{{ t('person.noItems') }}</div>
          </div>
        </div>
      </main>
    </template>
    <!-- 两种错误态合并：只有 loadError 时多一行详情。都要有滚动壳 -->
    <main
      v-else
      class="itemView darkContentContainer-item view flex flex-direction-column scrollFrameY flex-grow emby-scroller page scrollY focuscontainer-x"
    >
      <div class="scrollSlider flex-grow flex-direction-column padded-top-page scrollSliderY focuscontainer-y navout-up">
        <div class="page-error">
          {{ t('item.notFound') }}
          <div v-if="loadError" class="secondaryText">{{ loadError }}</div>
        </div>
      </div>
    </main>
  </div>
</template>