<script setup lang="ts">
/**
 * 播放页(视频 OSD)—— 1:1 还原 Emby 4.9.5 videoosd(基准=静态模板 fetch,dump:emby495-video-osd.md):
 * 骨架 = osdPosterContainer + videoOsdText(标题/副题) + topright 按钮组 + 位置条(时间/滑条/时长)
 * + transport 按钮组(上一/快退/暂停/停止/快进/下一)+ 音量条 + 内容按钮组(章节/歌词/队列/随机/循环)。
 * **零真实播放**:本页不发起任何 Playback 请求;进度由前端计时器模拟(mock 会话),播放/暂停切换仅改本地状态。
 * 进入:详情页"播放"按钮 → 本页(独立全屏路由,不占 view 系统,同真实站 body 直下 overlay 形态)。
 */
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { getItem, backdropUrl } from '@/api/endpoints'
import type { BaseItemDto } from '@/api/types'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const { t } = useI18n()

const item = ref<BaseItemDto | null>(null)
/** 画面占位用的 backdrop 静帧（已处理"剧集的大图在父级系列上"这条回退链） */
const osdBackdrop = computed(() => backdropUrl(item.value, { maxWidth: 1600, quality: 80 }))
const playing = ref(true)
const positionSec = ref(0)
const durationSec = ref(0)
const muted = ref(false)
const volume = ref(80)
const showOSD = ref(true)
const activeSection = ref<'none' | 'chapters' | 'lyrics' | 'playqueue'>('none')

let timer: ReturnType<typeof setInterval> | undefined
const runtimeTicks = computed(() => item.value?.RunTimeTicks ?? 0)

function fmtTime(sec: number): string {
  const s = Math.floor(sec)
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const r = s % 60
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}` : `${m}:${String(r).padStart(2, '0')}`
}

const positionText = computed(() => fmtTime(positionSec.value))
const durationText = computed(() => fmtTime(runtimeTicks.value / 10_000_000))
const positionPct = computed(() => (durationSec.value ? (positionSec.value / durationSec.value) * 100 : 0))

/** 模拟进度(纯前端;mock 会话不落库不播放) */
function startFakePlayback() {
  durationSec.value = runtimeTicks.value / 10_000_000
  positionSec.value = 0
  timer = setInterval(() => {
    if (playing.value && positionSec.value < durationSec.value) positionSec.value += 1
  }, 1000)
}

function togglePlay() { playing.value = !playing.value }
function stop() {
  clearInterval(timer)
  router.back()
}
function seek(e: Event) {
  const v = Number((e.target as HTMLInputElement).value)
  positionSec.value = (v / 100) * durationSec.value
}

function onKey(e: KeyboardEvent) {
  if (e.key === 'Escape' || e.key === ' ') {
    if (e.key === 'Escape') stop()
    else togglePlay()
  }
}

onMounted(async () => {
  // 主题类归 useTheme()：这里只补结构性类。
  // 旧写法 ['noScrollY','theme-dark','accent-emby'] 会在亮色模式下播片时把整站切成深色，
  // 且退出播放后不还原；播放页的深色语境改由根节点的 .s-osd 局部令牌覆盖实现（见 <style>）。
  document.documentElement.classList.add('noScrollY')
  document.body.className = 'mainAnimatedPages skinBody'
  document.addEventListener('keydown', onKey)
  try {
    item.value = await getItem(auth.user!.Id, String(route.params.id))
    startFakePlayback()
  } catch { /* 静默 */ }
})

onUnmounted(() => {
  document.removeEventListener('keydown', onKey)
  clearInterval(timer)
})
</script>

<template>
  <div class="view flex flex-direction-column s-osd" :class="{ osdVisible: showOSD }" @mousemove="showOSD = true" @click="showOSD = true">
    <div class="skipIntroContainer focuscontainer-x hide">
      <!-- v1 mock 无 intro 区间数据 → 隐藏(真实站仅在 IntroStart~End 内显示) -->
      <button type="button" class="raised btnSkipIntro nobackdropfilter osd-raisedbutton">{{ t('osd.skipIntro') }}</button>
    </div>

    <div class="videoOsdBottom flex">
      <div class="videoOsdBottom-maincontrols focuscontainer-down">
        <div class="osdPosterContainer flex flex-direction-column">
          <!-- 视频画面占位(真实站=video 元素;零播放语境用条目 backdrop 静帧)。
               backdrop 可能挂在父级系列上(剧集自己通常是空数组)，统一走 backdropUrl。 -->
          <img
            v-if="item?.Id && osdBackdrop" class="osdVideoPlaceholder" alt=""
            :src="osdBackdrop"
          />
        </div>

        <div class="flex flex-direction-column videoOsdText videoOsd-hideWithOpenTab videoOsd-hideWhenLocked">
          <div class="videoOsdParentTitleContainer flex align-items-center">
            <h3 class="videoOsdParentTitle videoOsdParentTitle-small">{{ item?.SeriesName ?? '' }}</h3>
          </div>
          <div class="flex videoOsdSecondaryText secondaryText flex-wrap-wrap">
            <h3 class="videoOsdTitle">{{ item?.Name }}</h3>
            <h3 v-if="item?.ProductionYear" class="videoOsdThirdTitle">{{ item.ProductionYear }}</h3>
            <div class="flex align-items-center videoOsdSecondaryMediaInfo mediaInfoItems"></div>
          </div>
        </div>

        <!-- 右上按钮组 -->
        <div class="flex flex-shrink-zero justify-content-flex-end flex-wrap-wrap videoOsdBottom-buttons-topright videoOsd-hideWithOpenTab videoOsd-hideWhenLocked focuscontainer-x focusable">
          <button type="button" class="osdIconButton paper-icon-button-light btnSubtitles" :title="t('osd.subtitles')">
            <i class="md-icon osdIconButton-icon subtitleIcon">&#xE04D;</i>
          </button>
          <button type="button" class="osdIconButton paper-icon-button-light btnAudio" :title="t('osd.audio')">
            <i class="md-icon osdIconButton-icon audioIcon">5.1</i>
          </button>
          <button type="button" class="osdIconButton paper-icon-button-light btnVideoOsdSettings" :title="t('osd.settings')">
            <i class="md-icon osdIconButton-icon">&#xE8B8;</i>
          </button>
          <button type="button" class="osdIconButton btnOsdMore btnOsdMore-bottom" :title="t('item.more')">
            <i class="md-icon osdIconButton-icon">more_horiz</i>
          </button>
          <button type="button" class="osdIconButton paper-icon-button-light btnFullscreen" :title="t('osd.fullscreen')">
            <i class="md-icon osdIconButton-icon">&#xE5D0;</i>
          </button>
        </div>

        <!-- 位置条 -->
        <div class="flex flex-direction-row align-items-center justify-content-center videoOsdPositionContainer">
          <div class="flex align-items-center videoOsdPositionText flex-shrink-zero secondaryText">{{ positionText }}</div>
          <div class="sliderContainer videoOsdPositionSliderContainer flex-grow">
            <input type="range" step="0.01" min="0" max="100" :value="positionPct" class="videoOsdPositionSlider" @input="seek" />
          </div>
          <div class="flex align-items-center videoOsdDurationText flex-shrink-zero secondaryText">{{ durationText }}</div>
        </div>

        <!-- transport 按钮组 -->
        <div class="videoOsd-belowtransportbuttons flex focusable focuscontainer-x">
          <div class="videoOsdBottom-buttons flex align-items-flex-start flex-direction-row">
            <button type="button" class="osdIconButton paper-icon-button-light btnPreviousTrack" :title="t('osd.prevItem')">
              <i class="md-icon md-icon-fill osdIconButton-icon">&#xE045;</i>
            </button>
            <div class="flex align-items-center flex-direction-row videoOsd-maintransportbuttons">
              <button type="button" class="osdIconButton paper-icon-button-light btnRewind" :title="t('osd.rewind')">
                <i class="md-icon osdIconButton-icon btnRewindIcon">&#xE042;</i>
              </button>
              <button type="button" class="osdIconButton paper-icon-button-light videoOsd-btnPause" @click="togglePlay">
                <!-- 播放中显示暂停键(真实站语义);暂停态显示播放键 -->
                <i v-if="playing" class="md-icon md-icon-fill osdIconButton-icon autortl">&#xE034;</i>
                <i v-else class="md-icon md-icon-fill osdIconButton-icon autortl">&#xE037;</i>
              </button>
              <button type="button" class="osdIconButton paper-icon-button-light btnVideoOsd-stop" :title="t('osd.stop')" @click="stop">
                <i class="md-icon md-icon-fill osdIconButton-icon">&#xE047;</i>
              </button>
              <button type="button" class="osdIconButton paper-icon-button-light btnOsdFastForward" :title="t('osd.fastForward')">
                <i class="md-icon osdIconButton-icon btnOsdFastForwardIcon">&#xE042;</i>
              </button>
            </div>
            <button type="button" class="osdIconButton paper-icon-button-light btnNextTrack" :title="t('osd.nextItem')">
              <i class="md-icon md-icon-fill osdIconButton-icon">&#xE044;</i>
            </button>
          </div>

          <!-- 音量 -->
          <div class="videoOsdVolumeControls flex flex-direction-row align-items-center">
            <button type="button" class="osdIconButton buttonMute flex-shrink-zero" :title="t('osd.mute')" @click="muted = !muted">
              <i class="md-icon osdIconButton-icon">{{ muted ? '&#xE04F;' : '&#xE050;' }}</i>
            </button>
            <i class="md-icon md-icon-fill osdVolumeDisplayIcon osdVolumeDisplayIcon-left secondaryText">&#xE04D;</i>
            <div class="videoOsdVolumeSliderWrapper flex-grow">
              <div class="sliderContainer flex-grow">
                <input v-model.number="volume" type="range" step="1" min="0" max="100" class="videoOsdVolumeSlider" tabindex="-1" />
              </div>
            </div>
            <i class="md-icon md-icon-fill osdVolumeDisplayIcon osdVolumeDisplayIcon-right secondaryText">&#xE050;</i>
          </div>

          <!-- 内容按钮组 -->
          <div class="videoOsdBottom-contentbuttons flex align-items-center justify-content-center flex-direction-row">
            <button
              type="button" class="osdIconButton btnChapters toggleButton" :class="{ 'toggleButton-active': activeSection === 'chapters' }"
              :title="t('osd.chapters')" @click="activeSection = activeSection === 'chapters' ? 'none' : 'chapters'"
            >
              <i class="md-icon osdIconButton-icon toggleButtonIcon osdContentSectionToggleButtonIcon chaptersIcon">&#xE01B;</i>
            </button>
            <button
              type="button" class="osdIconButton btnPlayQueue toggleButton toggleButton-active" :class="{ 'toggleButton-active': activeSection === 'playqueue' }"
              :title="t('osd.playQueue')" @click="activeSection = activeSection === 'playqueue' ? 'none' : 'playqueue'"
            >
              <i class="md-icon osdIconButton-icon toggleButtonIcon osdContentSectionToggleButtonIcon autortl">&#xE241;</i>
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/**
 * 播放页永远是深色语境（视频画面 + OSD 控制条）。
 * 用根节点上的局部令牌覆盖实现，而不是去改 <html> 的主题类——
 * 这样退出播放后全站主题不受影响，亮色模式下也不会被播片"带黑"。
 */
.s-osd {
  color-scheme: dark;
  /**
   * 关键：老版浅色主题在 .skinBody 上用**具体颜色**声明了文字色（不是变量），
   * 所以只覆盖 HSL 三件套不够——OSD 控件靠继承拿到的是"墨色"，在暗画面里看不见。
   * 这里直接落一个 color，让所有 color:inherit 的子控件回到奶白。
   */
  color: #fff8f1;
  --s-bg: #0a0c18;
  --s-bg-deep: #06070e;
  --s-surface: #11142a;
  --s-raised: #1a1f3b;
  --s-hover: #232a4d;
  --s-line: rgba(255, 248, 241, 0.08);
  --s-line-strong: #5a5f8a;
  --s-text-1: #fff8f1;
  --s-text-2: #c6bfd4;
  --s-text-3: #8e88a4;
  --s-accent: #e0705a;
  --s-accent-hi: #ec8a76;
  --s-accent-text: #e0705a;
  --s-accent-soft: rgba(224, 112, 90, 0.14);
  --s-cta-bg: #fff8f1;
  --s-cta-fg: #171a2e;
  --s-cta2-bg: rgba(255, 248, 241, 0.1);
  --s-cta2-fg: #fff8f1;
  --s-scrim: rgba(6, 7, 14, 0.72);
  --s-scrim-soft: rgba(6, 7, 14, 0.4);
  /* Emby 老变量：OSD 的进度条 / 计数 / 按钮底色仍走它们 */
  --background-hue: 231;
  --background-saturation: 41%;
  --background-lightness: 6.7%;
  --theme-primary-color-hue: 10;
  --theme-primary-color-saturation: 68%;
  --theme-primary-color-lightness: 62%;
  --theme-text-color-hue: 30;
  --theme-text-color-saturation: 100%;
  --theme-text-color-lightness: 97.3%;
  --theme-accent-text-color-darkbg: #e0705a;
  --theme-accent-text-color-darkbg-alt: #ec8a76;
}

.view {
  position: fixed; inset: 0; z-index: 500;
  /* 视频信函框必须是纯黑（不是主题色），这是播放器的物理语义 */
  background: #000;
}
/* 视频画面占位(真实站是 video 元素;占满 poster 容器区) */
.osdVideoPlaceholder {
  position: absolute; inset: 0;
  width: 100%; height: 100%;
  object-fit: contain;
}
</style>