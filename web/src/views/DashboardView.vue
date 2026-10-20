<script setup lang="ts">
/**
 * 控制台(管理看板)—— 1:1 还原 Emby 4.9.5 dashboard(基准=真实站运行时 dump,
 * 见 reference/dumps/emby495-dashboard.md):
 * html.theme-light;浅色侧栏(5 折叠区,Admin 语境);页面 = 服务器信息卡(infoBanner)
 * + 警告区(圆形红底图标行)+ 正在播放(activeSession 卡)+ 活动日志。
 * v1 mock:警告取 mock 登录审计,正在播放 = 静态示例(无会话时隐藏区)。
 */
import { ref, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()
const { t } = useI18n()

const ICON_MORE_H = 'more_horiz'
const ICON_POWER = 'power_settings_new'
const ICON_WIFI = 'wifi'
const ICON_NOTIF = 'notification_important'
const ICON_INFO = 'info'

interface MockActivity {
  Id: string
  Name: string
  ShortOverview?: string
  Date: string
}

const loading = ref(true)
const version = ref('4.9.5.0')
const serverName = ref('Serata')
const port = ref('8096')
const activities = ref<MockActivity[]>([])
const activeSessions = ref(0)

// 管理侧栏在 App 壳(与真实站一致:侧栏属壳,不属页面)

function fmtActivityTime(iso: string): string {
  const d = new Date(iso)
  return `${d.getFullYear()}/${String(d.getMonth() + 1).padStart(2, '0')}/${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

onMounted(() => {
  // 注意：这里**不再**整体覆盖 <html> 的 className。
  // 旧写法 ['noScrollY','withFullDrawer','theme-light','accent-emby'] 会把主题类硬写成浅色，
  // 深色模式下进后台整页翻白、并抹掉 useTheme 注入的 theme-dark（退出后也不还原）。
  // 结构类(noScrollY/withFullDrawer/withHeaderTabs)由 App.vue 的外壳统一维护，主题类归 useTheme。
  // mock 活动数据(登录审计形态)
  activities.value = [
    { Id: '1', Name: `${serverName.value} 上来自 hinata 的登录尝试失败`, Date: '2026-10-15T23:25:00' },
    { Id: '2', Name: `${serverName.value} 上来自 Noah115 的登录尝试失败`, Date: '2026-10-15T23:21:00' },
    { Id: '3', Name: `${serverName.value} 上来自 雨人 的登录尝试失败`, Date: '2026-10-15T22:53:00' },
  ]
  activeSessions.value = 2
  loading.value = false
})
</script>

<template>
  <main class="view flex flex-direction-column scrollFrameY flex-grow emby-scroller page scrollY focuscontainer-x view-dashboard-dashboard page-withFullDrawer page-withDockedDrawer">
    <div class="scrollSlider flex-grow padded-top-page padded-left padded-left-page padded-right scrollSliderY focuscontainer-y">
      <!-- 顶部推广(真实站形态;v1 显式占位) -->
      <div class="supporterPromotionContainer">
        <div class="supporterPromotion">
          <a class="raised block emby-button emby-button-focusscale">
            <div>{{ t('dash.supportTeam') }}</div>
            <div>{{ t('dash.supportExtra') }}</div>
          </a>
        </div>
      </div>

      <div class="dashboardSections">
        <!-- 服务器信息卡 -->
        <div class="dashboardSection dashboardSection-mini">
          <div class="flex align-items-center">
            <h2 class="serverNameHeader">{{ serverName }}</h2>
            <button type="button" class="btnServerMoreMenu paper-icon-button-light emby-button-focusscale"><i class="md-icon">{{ ICON_MORE_H }}</i></button>
            <button type="button" class="btnRestartMenu paper-icon-button-light emby-button-focusscale"><i class="md-icon">{{ ICON_POWER }}</i></button>
          </div>
          <div class="infoBanner">
            <div class="infoBannerContent">
              <p class="appVersionNumber">{{ t('dash.version') }} <a class="button-link emby-button emby-button-focusscale">{{ version }}</a></p>
              <p class="ports">{{ t('dash.runningPort', { port }) }}</p>
              <a class="raised raised-mini button-submit emby-button emby-button-focusscale">
                <i class="md-icon button-icon button-icon-left">{{ ICON_WIFI }}</i>
                <span>{{ t('dash.connectHelp') }}</span>
              </a>
            </div>
          </div>
        </div>

        <!-- 警告区 -->
        <div class="dashboardSection serverActivitySection activityContainer dashboardSection-mini">
          <div class="sectionTitleContainer">
            <a class="noautofocus button-link button-link-color-inherit sectionTitleTextButton sectionTitleTextButton-link emby-button emby-button-focusscale">
              <h2 class="sectionTitle sectionTitleText-withseeall">{{ t('dash.alerts') }}</h2>
              <i class="md-icon sectionTitleMoreIcon secondaryText">&#xE5D3;</i>
            </a>
          </div>
          <div class="serverActivityItems itemsContainer flex-direction-column flex flex-grow vertical-list">
            <div v-for="a in activities" :key="a.Id" class="listItem listItem-autoactive listItem-hoverable" data-action="none">
              <div class="listItem-content listItem-content-bg listItemContent-touchzoom listItem-border listItem-border-offset listItem-border-offset-square">
                <div class="listItemImageContainer itemAction listItemImageContainer-margin listItemImageContainer-square listItemImageContainer-round defaultCardBackground">
                  <i class="listItemIcon md-icon autortl">{{ ICON_NOTIF }}</i>
                </div>
                <div class="listItemBody itemAction listItemBody-3-lines">
                  <div class="listItemBodyText listItemBodyText-lf">{{ a.Name }}</div>
                  <div class="listItemBodyText listItemBodyText-lf listItemBodyText-secondary secondaryText">{{ fmtActivityTime(a.Date) }}</div>
                </div>
                <button type="button" title="概要" class="listItemButton itemAction md-icon paper-icon-button-light emby-button-focusscale" data-action="overview">
                  <i class="md-icon">{{ ICON_INFO }}</i>
                </button>
              </div>
            </div>
          </div>
        </div>

        <!-- 正在播放(v1 mock:2 会话占位) -->
        <div class="dashboardSection">
          <div class="sectionTitleContainer">
            <h2 class="sectionTitle">{{ t('dash.nowPlaying') }}</h2>
          </div>
          <div class="activeSessionsItems itemsContainer horizontal-wrap">
            <div class="card bannerCard activeSession card-hoverable sideFooterCard card-autoactive">
              <div class="cardBox cardBox-sideFooter visualCardBox cardBox-touchzoom">
                <button type="button" class="itemAction cardContent-button cardContent cardImageContainer cardContent-nobr cardImageContainer-sideFooter">
                  <!--
                    占位卡不该产生真实请求。
                    这里原本是 :src="'/emby/Items/101/Images/Primary?maxWidth=150&quality=90'" —— 写死的 id=101,
                    而本服务器没有这个条目,于是**每进一次后台就一条 404**(2026-10-20 真机逐路由实测记录在案;
                    证据 scratch/lead2/datafix-admin/{before,after}/route-probe.json 的 dashboard 路由)。
                    下面「长风渡口」是 v1 mock 占位文案(没有真实条目可引用),所以不该去请求任何真实图片:
                    换成品牌渐变占位层 —— 复用 EmbyCard 缺图兜底同一个 --s-gradient-dusk 令牌,零网络请求,
                    并且保留 .cardImage 的尺寸约束(position:absolute + inset:0 + 100%/100%),占位观感不变。
                  -->
                  <div class="cardImage dashboardCardPlaceholder" aria-hidden="true"></div>
                </button>
                <div class="innerCardFooter">
                  <div class="itemProgressBar"><div class="itemProgressBarForeground" style="width: 74%"></div></div>
                </div>
                <div class="cardFooterContent cardFooterContent-start itemAction">
                  <div class="cardText text-align-start cardText-first">长风渡口</div>
                  <div class="cardText text-align-start cardText-secondary">影片</div>
                  <div class="cardText text-align-start cardText-secondary">01:50:12 / 02:28:00</div>
                </div>
                <div class="cardSideFooter-bottomsections activeSession-bottomsections">
                  <div class="cardSideFooter-bottomsection activeSession-bottomsection activeSession-deviceSection itemAction">
                    <div class="secondaryText activeSession-deviceimage"><i class="activeSession-deviceimage-icon md-icon autortl">desktop_windows</i></div>
                    <div>
                      <div>Serata Web 0.1.0</div>
                      <div class="secondaryText">Chrome</div>
                      <div class="secondaryText flex align-items-center">127.0.0.1 http/1.1</div>
                    </div>
                  </div>
                  <div class="cardSideFooter-bottomsection activeSession-bottomsection">
                    <div class="secondaryText activeSession-bottomsection-title">流媒体</div>
                    <div>MKV</div>
                    <div><i class="md-icon activeSessionStreamIcon autortl">check_circle</i>直接播放</div>
                  </div>
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
/* 控制台布局细节(真实站 dashboardSections 多列 flex wrap;样式大多在原版 css,此处仅本页特有) */
.activeSessionsItems { display: flex; flex-wrap: wrap; gap: 1em; }
.activeSessionsItems .activeSession { width: 24em; }
/* 占位卡图位:品牌渐变(与 EmbyCard 缺图兜底同一令牌),纯 CSS,不引任何图片资源 → 零网络请求 */
.dashboardCardPlaceholder {
  background: var(--s-gradient-dusk);
  background-size: 100% 100%;
}
</style>