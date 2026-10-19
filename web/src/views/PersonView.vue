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
import { getPerson, getItems, imageUrl } from '@/api/endpoints'
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

const posterSrc = computed(() =>
  person.value ? imageUrl(person.value.Id, 'Primary', person.value.ImageTags?.Primary, { maxWidth: 375, maxHeight: 563, quality: 90 }) : '')

onMounted(async () => {
  try {
    const userId = auth.user!.Id
    const id = String(route.params.id)
    person.value = await getPerson(id, userId)
    document.title = `${person.value.Name} · ${t('app.name')}`
    items.value = (await getItems(userId, {
      PersonIds: id, Recursive: true, IncludeItemTypes: 'Movie,Series',
      Fields: 'PrimaryImageAspectRatio,ProductionYear', Limit: 60,
    })).Items
  } catch { /* 静默 */ } finally {
    loading.value = false
  }
})

function goHome() { router.push('/') }
</script>

<template>
  <div>
    <div v-if="loading" class="page-loading">{{ t('common.loading') }}</div>
    <template v-else-if="person">
      <main class="itemView darkContentContainer-item view flex flex-direction-column scrollFrameY flex-grow emby-scroller page scrollY focuscontainer-x">
        <div class="scrollSlider flex-grow flex-direction-column padded-top-page scrollSliderY focuscontainer-y navout-up">
          <div class="topDetailsContainer flex flex-direction-row verticalSections-item">
            <div class="padded-left padded-left-page padded-right verticalSection detailMainContainer flex flex-direction-row align-items-flex-start">
              <div class="detailImageContainer detailImageContainer-main flex flex-direction-column justify-content-center itemsContainer vertical-wrap">
                <div class="card portraitCard detailImageContainerCard card-hoverable">
                  <div class="detailImageContainerCard-cardBox cardBox">
                    <div class="itemAction cardContent cardImageContainer cardContent-background detailImage cardContent-bxsborder-fv coveredImage coveredImage-noScale cardPadder-portrait detailImage-transparent">
                      <img draggable="false" alt=" " class="cardImage cardImage-bxsborder-fv coveredImage coveredImage-noScale" :src="posterSrc" loading="lazy" decoding="async" />
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
    <div v-else class="page-error">{{ t('item.notFound') }}</div>
  </div>
</template>