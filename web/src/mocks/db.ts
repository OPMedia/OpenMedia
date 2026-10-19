/**
 * Mock 种子数据库 —— 形状严格对齐 SPEC/data-model。
 * Id 全部为 Emby 数字字符串形态;时间 UTC ISO;时长 ticks(100ns)。
 * 仅 mock 使用;接真后端后整个 mocks/ 目录可整体摘除。
 */
import type { BaseItemDto, UserItemData, UserDto } from '@/api/types'

export const TICKS_PER_SECOND = 10_000_000

export function toTicks(seconds: number): number {
  return Math.round(seconds * TICKS_PER_SECOND)
}

// ---------- 用户(SPEC/data-model:user) ----------

export interface MockUser extends UserDto {
  Password: string
}

export const users: MockUser[] = [
  // demo 为管理员(与真实站 zzz123 一致:管理后台/用户管理可用)
  { Id: '1', Name: 'demo', Password: 'demo', IsAdministrator: true, HasPassword: true, DateCreated: '2026-10-01T00:00:00Z' },
  { Id: '2', Name: 'admin', Password: '', IsAdministrator: true, HasPassword: false, DateCreated: '2026-10-01T00:00:00Z' },
]

// ---------- 管理后台数据(SPEC/api 管理面:计划任务/日志/设备/插件/密钥/服务器配置) ----------

export interface MockTask {
  Id: string; Name: string; Description: string; Category: string
  LastExecutionResult?: { Status: string; StartTimeUtc: string; EndTimeUtc: string }
  State: string
}

export const scheduledTasks: MockTask[] = [
  { Id: 'rotate-log', Name: 'Rotate log file', Description: 'Moves logging to a new file and keeps the old ones for a period of time.', Category: 'Application', State: 'Idle' },
  { Id: 'vacuum-db', Name: 'Vacuum Database', Description: 'Schedules a database vacuum to optimize performance.', Category: 'Application', State: 'Idle' },
  { Id: 'convert-media', Name: 'Convert media', Description: 'Converts media to a standard format for optimized streaming.', Category: 'Application', State: 'Idle' },
  { Id: 'scan-lib', Name: 'Scan media library', Description: 'Scans the media library for new and updated files.', Category: 'Library', State: 'Idle' },
  { Id: 'refresh-meta', Name: 'Refresh metadata', Description: 'Refreshes metadata for all items.', Category: 'Library', State: 'Idle' },
]

export interface MockLogFile { Name: string; DateCreated: string; Size: number }
export const logFiles: MockLogFile[] = [
  { Name: 'embyserver.txt', DateCreated: '2026-10-16T17:33:00Z', Size: 1_258_291 },
  { Name: 'hardware_detection-63896.txt', DateCreated: '2026-10-16T05:00:00Z', Size: 88_064 },
  { Name: 'embyserver-639277236.txt', DateCreated: '2026-10-16T05:00:00Z', Size: 1_003_520 },
  { Name: 'embyserver-639277056.txt', DateCreated: '2026-10-15T23:59:00Z', Size: 1_150_976 },
]

export interface MockDevice { Id: string; Name: string; AppName: string; LastUserName: string; DateLastActivity: string; IpAddress: string }
export const devices: MockDevice[] = [
  { Id: 'd1', Name: 'Serata Web', AppName: 'Serata Web', LastUserName: 'demo', DateLastActivity: '2026-10-16T18:00:00Z', IpAddress: '127.0.0.1' },
  { Id: 'd2', Name: 'VidHub', AppName: 'VidHub 3.0.2', LastUserName: 'zzz123', DateLastActivity: '2026-10-15T23:25:00Z', IpAddress: '222.247.210.38' },
  { Id: 'd3', Name: 'Emby for Android TV', AppName: 'Emby for Android TV', LastUserName: 'hinata', DateLastActivity: '2026-10-14T20:10:00Z', IpAddress: '10.0.0.24' },
]

export interface MockPlugin { Id: string; Name: string; Version: string; Status: string; Description: string }
export const plugins: MockPlugin[] = [
  { Id: 'p1', Name: 'Open Subtitles', Version: '1.0.0.0', Status: 'Active', Description: '字幕下载插件。' },
  { Id: 'p2', Name: 'Subtitle Options', Version: '1.0.0.0', Status: 'Active', Description: '字幕样式与选项。' },
  { Id: 'p3', Name: 'Backup & Restore', Version: '1.0.0.0', Status: 'Active', Description: '服务器配置备份与恢复。' },
]

export interface MockApiKey { Id: string; Name: string; DateCreated: string; Key: string }
export const apiKeys: MockApiKey[] = [
  { Id: 'k1', Name: 'Serata Web', DateCreated: '2026-10-16T12:00:00Z', Key: 'a1b2c3d4e5f60718293a4b5c6d7e8f90' },
]

/** 服务器配置(管理页表单持久化;GET/POST /System/Configuration) */
export const serverConfig: Record<string, string | number | boolean> = {
  serverName: 'Serata',
  preferredMetadataLanguage: 'zh-CN',
  cachePath: '/config/cache',
  libraryScanFanout: 4,
  enableCaseSensitiveItemIds: false,
  saveMetadataHidden: false,
  hardwareDecoder: '自动',
  hardwareEncoder: '自动',
  softwareEncoder: 'H.264 (AVC)',
  enableThrottling: true,
  throttleDelaySeconds: 180,
  transcodingTempPath: '',
  downMixAudioBoost: 2,
  enableAudioVbrEncoding: true,
  publicHttpPort: 8096,
  publicHttpsPort: 8920,
  enableUpnp: true,
  enableRemoteAccess: true,
  localNetworkAddresses: '192.168.1.0/24',
  requireHttps: false,
  certificatePath: '',
  enableAutomaticPortMapping: false,
  igmpHomeGroup: true,
  baseUrl: '',
  enableIpv6: true,
  httpServerTimeout: 120,
  databasePath: '/config/data/library.db',
  databaseCacheSize: 128,
  enableAutoVacuum: true,
  convertTempPath: '',
  syncPath: '',
  enableSync: false,
  guideDays: 14,
  enableGuide: true,
  recordingPath: '',
  postProcessPath: '',
  premiereKey: '',
  enableHardwareTranscoding: false,
  enableCinemaMode: true,
  enableCoverArt: true,
}

/** 服务器设置类管理页的字段清单(键与真实站语义一致;供 GET /System/Configuration/Fields) */
export const serverConfigFields: { section: string; legend: string; fields: { key: string; label: string; kind: 'select' | 'toggle' | 'text' | 'number'; options?: string[]; desc?: string }[] }[] = [
  {
    section: 'general',
    legend: '服务器',
    fields: [
      { key: 'serverName', label: '服务器名称', kind: 'text' },
      { key: 'preferredMetadataLanguage', label: '首选元数据语言', kind: 'select', options: ['中文', 'English', '日本語'] },
      { key: 'cachePath', label: '缓存路径', kind: 'text' },
      { key: 'libraryScanFanout', label: '媒体库扫描并发数', kind: 'number' },
      { key: 'enableCaseSensitiveItemIds', label: '启用大小写敏感条目 ID', kind: 'toggle' },
      { key: 'saveMetadataHidden', label: '将元数据保存到媒体文件夹', kind: 'toggle' },
    ],
  },
  {
    section: 'transcoding',
    legend: '首选硬件解码器',
    fields: [{ key: 'hardwareDecoder', label: '首选硬件解码器', kind: 'select', options: ['自动', 'VAAPI', 'NVIDIA NVENC', 'Intel QuickSync', '无'] }],
  },
  {
    section: 'transcoding',
    legend: '首选硬件编码器',
    fields: [{ key: 'hardwareEncoder', label: '首选硬件编码器', kind: 'select', options: ['自动', 'VAAPI', 'NVIDIA NVENC', 'Intel QuickSync', '无'] }],
  },
  {
    section: 'transcoding',
    legend: '软件编码器',
    fields: [
      { key: 'softwareEncoder', label: 'H.264 编码器', kind: 'select', options: ['H.264 (AVC)', 'H.265 (HEVC)'] },
      { key: 'enableThrottling', label: '启用转码限速', kind: 'toggle', desc: '限制转码速度以降低 CPU 占用。' },
      { key: 'throttleDelaySeconds', label: '限速延时（秒）', kind: 'number' },
    ],
  },
  {
    section: 'transcoding',
    legend: '高级',
    fields: [
      { key: 'transcodingTempPath', label: '转码临时目录', kind: 'text' },
      { key: 'downMixAudioBoost', label: '音频下混增益', kind: 'number' },
      { key: 'enableAudioVbrEncoding', label: '启用音频 VBR 编码', kind: 'toggle' },
    ],
  },
  {
    section: 'network',
    legend: '网络',
    fields: [
      { key: 'publicHttpPort', label: '公共 HTTP 端口', kind: 'number' },
      { key: 'publicHttpsPort', label: '公共 HTTPS 端口', kind: 'number' },
      { key: 'enableUpnp', label: '启用 UPnP', kind: 'toggle' },
      { key: 'enableRemoteAccess', label: '允许远程连接', kind: 'toggle' },
      { key: 'localNetworkAddresses', label: '局域网地址', kind: 'text' },
      { key: 'requireHttps', label: '要求 HTTPS', kind: 'toggle' },
      { key: 'certificatePath', label: '证书路径', kind: 'text' },
      { key: 'enableAutomaticPortMapping', label: '自动端口映射', kind: 'toggle' },
      { key: 'igmpHomeGroup', label: 'IGMP 组播', kind: 'toggle' },
      { key: 'baseUrl', label: '基础 URL', kind: 'text' },
      { key: 'enableIpv6', label: '启用 IPv6', kind: 'toggle' },
      { key: 'httpServerTimeout', label: 'HTTP 超时（秒）', kind: 'number' },
    ],
  },
  {
    section: 'database',
    legend: '数据库',
    fields: [
      { key: 'databasePath', label: '数据库位置', kind: 'text' },
      { key: 'databaseCacheSize', label: '数据库缓存大小（MB）', kind: 'number' },
      { key: 'enableAutoVacuum', label: '自动清理数据库', kind: 'toggle' },
    ],
  },
  {
    section: 'convert',
    legend: '转换',
    fields: [
      { key: 'convertTempPath', label: '转换临时目录', kind: 'text' },
      { key: 'syncPath', label: '同步目录', kind: 'text' },
      { key: 'enableSync', label: '启用云同步', kind: 'toggle' },
    ],
  },
  {
    section: 'livetv',
    legend: '电视直播',
    fields: [
      { key: 'guideDays', label: '节目指南天数', kind: 'number' },
      { key: 'enableGuide', label: '启用节目指南', kind: 'toggle' },
      { key: 'recordingPath', label: '录制路径', kind: 'text' },
      { key: 'postProcessPath', label: '录制后处理脚本', kind: 'text' },
    ],
  },
  {
    section: 'premiere',
    legend: 'Emby Premiere',
    fields: [
      { key: 'premiereKey', label: 'Premiere 密钥', kind: 'text' },
      { key: 'enableHardwareTranscoding', label: '硬件转码', kind: 'toggle' },
      { key: 'enableCinemaMode', label: '影院模式', kind: 'toggle' },
      { key: 'enableCoverArt', label: '封面艺术', kind: 'toggle' },
    ],
  },
]

/** 已签发 token 由 handlers 无状态解析(tok-<userId>-<rand>),无需内存表 */

// ---------- 库(SPEC/data-model:library) ----------

export const libraries: BaseItemDto[] = [
  {
    Name: '电影', Id: '100', Type: 'CollectionFolder', CollectionType: 'movies', IsFolder: true,
    SortName: '电影', UserData: { UnplayedItemCount: 6 },
  },
  {
    Name: '剧集', Id: '200', Type: 'CollectionFolder', CollectionType: 'tvshows', IsFolder: true,
    SortName: '剧集', UserData: { UnplayedItemCount: 18 },
  },
]

// ---------- 条目(SPEC/data-model:item / media_source / media_stream) ----------

interface SeedMovie {
  id: string; title: string; year: number; rating: number; genres: string[]
  runtimeMin: number; overview: string; studio: string; dateCreated: string
  officialRating?: string
}

const movieSeeds: SeedMovie[] = [
  { id: '101', title: '星际织网者', year: 2024, rating: 8.6, genres: ['科幻', '冒险'], runtimeMin: 148, overview: '当人类第一次编织跨越星系的网络,回声里藏着文明的另一种可能。一场跨越三代的信号追踪,让沉默深空开口说话。', studio: '星桥影业', dateCreated: '2026-10-10T08:00:00Z', officialRating: 'PG-13' },
  { id: '102', title: '雨夜快递', year: 2023, rating: 7.9, genres: ['剧情', '悬疑'], runtimeMin: 112, overview: '台风夜的最后三单快递,把七个陌生人的命运串成一条线。送达的到底是包裹,还是各自没说出口的道歉。', studio: '南光制片', dateCreated: '2026-10-09T08:00:00Z' },
  { id: '103', title: '雪山之息', year: 2025, rating: 8.2, genres: ['纪录', '冒险'], runtimeMin: 96, overview: '海拔七千米的呼吸是奢侈的。摄制组跟随三支登山队穿越风季,记录人在极限处如何与山谈判。', studio: '远山影像', dateCreated: '2026-10-12T08:00:00Z' },
  { id: '104', title: '代码之下', year: 2022, rating: 7.4, genres: ['剧情'], runtimeMin: 128, overview: '一个维护遗留在役系统的年轻工程师,在注释里发现前任程序员留下的二十年前未寄出的信。', studio: '白盒工作室', dateCreated: '2026-10-08T08:00:00Z' },
  { id: '105', title: '长风渡口', year: 2024, rating: 8.8, genres: ['剧情', '历史'], runtimeMin: 156, overview: '渡口老船工一家三代,守着一条越来越安静的河。水面之下,是这个时代所有离开与归来。', studio: '南光制片', dateCreated: '2026-10-11T08:00:00Z' },
  { id: '106', title: '霓虹迷宫', year: 2023, rating: 6.9, genres: ['科幻', '动作'], runtimeMin: 119, overview: '复制人侦探在霓虹街头追查一桩不存在的凶案,每条线索都指向他自己的记忆编号。', studio: '环宇娱乐', dateCreated: '2026-10-07T08:00:00Z', officialRating: 'R' },
  { id: '107', title: '厨房里的四季', year: 2025, rating: 8.4, genres: ['纪录'], runtimeMin: 88, overview: '一口铁锅,四位母亲,二十四道菜。食材来自菜市场,故事来自饭桌。', studio: '远方纪录', dateCreated: '2026-10-13T08:00:00Z' },
  { id: '108', title: '量子旅馆', year: 2024, rating: 7.6, genres: ['喜剧', '科幻'], runtimeMin: 104, overview: '一家只在宇宙膨胀间隙营业的旅馆,前台接待每一位迷路的平行自我。今晚,住进了两个同一人的版本。', studio: '白盒工作室', dateCreated: '2026-10-06T08:00:00Z' },
  { id: '109', title: '深海邮局', year: 2022, rating: 7.1, genres: ['动画', '奇幻'], runtimeMin: 101, overview: '海底三千米有一间替人投递思念的邮局。守局的小灯笼鱼有一天收到了一封寄给自己的信。', studio: '拾光动画', dateCreated: '2026-10-05T08:00:00Z' },
  { id: '110', title: '北纬四十度的黄昏', year: 2021, rating: 8.0, genres: ['剧情', '爱情'], runtimeMin: 134, overview: '两个在黄昏城市里擦肩十二年的人,终于在同一盏路灯下停了下来。', studio: '南光制片', dateCreated: '2026-10-04T08:00:00Z' },
  { id: '111', title: '赤霄', year: 2025, rating: 7.3, genres: ['动作', '历史'], runtimeMin: 141, overview: '铸剑师的最后一炉火,烧的不只是剑。乱世之下,一个门派用六十年等待一次出手。', studio: '环宇娱乐', dateCreated: '2026-10-14T08:00:00Z' },
  { id: '112', title: '纸飞机航线', year: 2023, rating: 6.8, genres: ['家庭', '动画'], runtimeMin: 92, overview: '一群孩子在拆迁前的旧城放起一千架纸飞机,每架都写着想对世界说的一句悄悄话。', studio: '拾光动画', dateCreated: '2026-10-03T08:00:00Z' },
]

interface SeedSeries {
  id: string; title: string; year: number; rating: number; genres: string[]
  overview: string; studio: string; dateCreated: string
  seasons: { id: string; number: number; name: string; episodes: { id: string; title: string; runtimeMin: number; overview: string }[] }[]
}

const seriesSeeds: SeedSeries[] = [
  {
    id: '301', title: '风起长夜', year: 2024, rating: 8.7, genres: ['剧情', '历史'],
    overview: '北境烽燧的守夜人世代单传。当最后一座烽火台被撤编,守了一辈子的火种究竟该传给谁。',
    studio: '星桥影业', dateCreated: '2026-10-12T08:00:00Z',
    seasons: [
      { id: '401', number: 1, name: '第 1 季', episodes: [
        { id: '501', title: '初雪', runtimeMin: 52, overview: '新任守夜人接过火种的第一夜,雪比往年来得早。' },
        { id: '502', title: '烽燧图', runtimeMin: 48, overview: '一张绘制了三十年的烽燧图,少画了最关键的一座。' },
        { id: '503', title: '驿站', runtimeMin: 51, overview: '南来的驿使带来了撤编的诏令,火种第一次没了名分。' },
        { id: '504', title: '夜行', runtimeMin: 55, overview: '为了给火种续柴,守夜人走了三天夜路。' },
        { id: '505', title: '雪盲', runtimeMin: 49, overview: '风雪里的失明老守夜人,反而看得最清楚。' },
        { id: '506', title: '火种', runtimeMin: 54, overview: '撤编令生效前最后一夜,火种烧得比任何时候都亮。' },
        { id: '507', title: '北归', runtimeMin: 50, overview: '有人向北去了,要把火种还给山上。' },
        { id: '508', title: '长夜尽头', runtimeMin: 58, overview: '春天来的时候,烽燧改成了灯塔。' },
      ] },
      { id: '402', number: 2, name: '第 2 季', episodes: [
        { id: '509', title: '灯塔', runtimeMin: 53, overview: '灯塔的第一束光,照见了海上来客。' },
        { id: '510', title: '旧部', runtimeMin: 49, overview: '二十年前的戍边旧部路过灯塔,带了半坛烈酒。' },
        { id: '511', title: '海雾', runtimeMin: 51, overview: '大雾封海七天,灯塔第一次成了唯一的坐标。' },
        { id: '512', title: '夜航', runtimeMin: 52, overview: '雾夜里的那艘船,载着谁也没想到的东西。' },
        { id: '513', title: '薪火', runtimeMin: 50, overview: '新一代守塔人开始学着给火种换柴。' },
        { id: '514', title: '季风', runtimeMin: 56, overview: '季风转向的那天,长夜终于到了尽头。' },
      ] },
    ],
  },
  {
    id: '302', title: 'Starfall', year: 2025, rating: 8.1, genres: ['科幻', '悬疑'],
    overview: '流星雨之后,全球每座天文台都收到了同一段无法破译的信号。破译它的人,先收到了自己的回信。',
    studio: '环宇娱乐', dateCreated: '2026-10-11T08:00:00Z',
    seasons: [
      { id: '421', number: 1, name: 'Season 1', episodes: [
        { id: '521', title: 'Rain of Iron', runtimeMin: 47, overview: 'The meteor shower was beautiful until every radio telescope went silent at once.' },
        { id: '522', title: 'Signal', runtimeMin: 45, overview: 'The first message decodes into coordinates that do not exist.' },
        { id: '523', title: 'Echo', runtimeMin: 46, overview: 'Dr. Lin sends a reply. It is answered before she presses send.' },
        { id: '524', title: 'Quarantine', runtimeMin: 48, overview: 'The observatory becomes its own country with one law: no transmission.' },
        { id: '525', title: 'Light Curve', runtimeMin: 44, overview: 'The star dims by exactly one percent. Every time someone lies.' },
        { id: '526', title: 'Transit', runtimeMin: 47, overview: 'Something crosses the sun. It is in no archive, ever.' },
        { id: '527', title: 'Parallax', runtimeMin: 46, overview: 'Two stations see the signal arrive from opposite directions.' },
        { id: '528', title: 'Redshift', runtimeMin: 52, overview: 'The message is old. Unbearably, impossibly old.' },
      ] },
    ],
  },
  {
    id: '303', title: '美食漫游记', year: 2024, rating: 7.8, genres: ['纪录', '美食'],
    overview: '沿着三条铁路,吃遍沿途小城的一百道家常菜。火车慢,锅气快。',
    studio: '远方纪录', dateCreated: '2026-10-09T08:00:00Z',
    seasons: [
      { id: '441', number: 1, name: '第 1 季', episodes: [
        { id: '541', title: '第一碗粉', runtimeMin: 30, overview: '凌晨四点的米粉店,汤头是一天的心事。' },
        { id: '542', title: '夜市两条街', runtimeMin: 31, overview: '烧烤摊主夫妻的二十年,烤炉换了五次,火候没变。' },
        { id: '543', title: '早点之战', runtimeMin: 29, overview: '一条街上的三家包子铺,各有各的死忠。' },
        { id: '544', title: '灶台传人', runtimeMin: 32, overview: '老灶台要拆了,儿子第一次掌勺全家的年夜饭。' },
        { id: '545', title: '站台味道', runtimeMin: 30, overview: '绿皮车上的盒饭,是几代人的味觉记忆。' },
        { id: '546', title: '终点站', runtimeMin: 34, overview: '旅途的最后一顿,回到了起点那家店。' },
      ] },
    ],
  },
]

/** db.item 的运行时形态:种子条目 + Emby DTO 投影 */
const items: BaseItemDto[] = []

function poster(id: string, seed: { title: string; year: number }): BaseItemDto['ImageTags'] {
  return { Primary: `p-${id}` }
}

for (const m of movieSeeds) {
  items.push({
    Name: m.title, Id: m.id, Type: 'Movie', ParentId: '100',
    SortName: m.title, Overview: m.overview, Genres: m.genres,
    ProductionYear: m.year, PremiereDate: `${m.year}-01-01T00:00:00Z`,
    DateCreated: m.dateCreated, CommunityRating: m.rating, OfficialRating: m.officialRating,
    RunTimeTicks: toTicks(m.runtimeMin * 60),
    Studios: [{ Name: m.studio, Id: `s-${m.studio}` }],
    People: [
      { Name: '林见川', Id: 'person-1', Role: '导演', Type: 'Director' },
      { Name: '沈知微', Id: 'person-2', Role: '主演', Type: 'Actor' },
      { Name: '阿禾', Id: 'person-3', Role: '主演', Type: 'Actor' },
    ],
    ImageTags: poster(m.id, m),
    PrimaryImageAspectRatio: 0.6667,
    BackdropImageTags: [`b-${m.id}`],
    MediaSources: [
      {
        Id: `ms-${m.id}`, Path: `/media/movies/${m.title}.mkv`, Protocol: 'Local',
        Container: 'mkv', Size: 8_589_934_592, IsRemote: false, Name: '4K HDR 原盘',
        Bitrate: 52_000_000,
        MediaStreams: [
          { Type: 'Video', Codec: 'hevc', Index: 0, DisplayTitle: '4K HEVC HDR10', Width: 3840, Height: 2160, BitRate: 50_000_000 },
          { Type: 'Audio', Codec: 'flac', Index: 1, Language: 'chi', DisplayTitle: '中文 5.1 (FLAC)', Channels: 6, SampleRate: 48_000, IsDefault: true },
          { Type: 'Audio', Codec: 'ac3', Index: 4, Language: 'chi', DisplayTitle: '中文 2.0 (AC3)', Channels: 2, SampleRate: 48_000, IsDefault: false },
          { Type: 'Audio', Codec: 'aac', Index: 5, Language: 'eng', DisplayTitle: 'English 5.1 (AAC)', Channels: 6, SampleRate: 48_000, IsDefault: false },
          { Type: 'Subtitle', Codec: 'ass', Index: 2, Language: 'chi', DisplayTitle: '中文 (ASS)', IsDefault: true, DeliveryMethod: 'External', DeliveryUrl: `/emby/Videos/${m.id}/${`ms-${m.id}`}/Subtitles/2/0/Stream.ass` },
          { Type: 'Subtitle', Codec: 'srt', Index: 3, Language: 'eng', DisplayTitle: 'English (SRT)', DeliveryMethod: 'External', DeliveryUrl: `/emby/Videos/${m.id}/${`ms-${m.id}`}/Subtitles/3/0/Stream.srt` },
          { Type: 'Subtitle', Codec: 'srt', Index: 6, Language: 'chi', DisplayTitle: '简中 (SRT 简日)', DeliveryMethod: 'External', DeliveryUrl: `/emby/Videos/${m.id}/${`ms-${m.id}`}/Subtitles/6/0/Stream.srt` },
        ],
      },
      {
        Id: `ms-${m.id}-1080p`, Path: `/media/movies/${m.title} (1080p).mkv`, Protocol: 'Local',
        Container: 'mkv', Size: 2_147_483_648, IsRemote: false, Name: '1080p',
        Bitrate: 12_000_000,
        MediaStreams: [
          { Type: 'Video', Codec: 'h264', Index: 0, DisplayTitle: '1080p H264', Width: 1920, Height: 1080, BitRate: 10_000_000 },
          { Type: 'Audio', Codec: 'aac', Index: 1, Language: 'chi', DisplayTitle: '中文 2.0 (AAC)', Channels: 2, SampleRate: 44_100, IsDefault: true },
        ],
      },
    ],
    MediaSourceCount: 2,
  })
}

for (const s of seriesSeeds) {
  // Series 条目
  items.push({
    Name: s.title, Id: s.id, Type: 'Series', ParentId: '200', IsFolder: true,
    SortName: s.title, Overview: s.overview, Genres: s.genres,
    ProductionYear: s.year, PremiereDate: `${s.year}-01-01T00:00:00Z`,
    DateCreated: s.dateCreated, CommunityRating: s.rating,
    Studios: [{ Name: s.studio, Id: `s-${s.studio}` }],
    People: [
      { Name: '林见川', Id: 'person-1', Role: '导演', Type: 'Director' },
      { Name: '沈知微', Id: 'person-2', Role: '主演', Type: 'Actor' },
    ],
    ImageTags: poster(s.id, s),
    PrimaryImageAspectRatio: 0.6667,
    BackdropImageTags: [`b-${s.id}`],
    ChildCount: s.seasons.length,
  })
  for (const season of s.seasons) {
    items.push({
      Name: season.name, Id: season.id, Type: 'Season', ParentId: s.id, IsFolder: true,
      SeriesId: s.id, SeriesName: s.title, SortName: `S${season.number}`,
      ProductionYear: s.year, ImageTags: poster(season.id, s),
      PrimaryImageAspectRatio: 0.6667, ChildCount: season.episodes.length,
    })
    for (const [i, e] of season.episodes.entries()) {
      items.push({
        Name: e.title, Id: e.id, Type: 'Episode', ParentId: season.id, LocationType: 'FileSystem',
        SeriesId: s.id, SeriesName: s.title, SeasonId: season.id,
        SeasonName: season.name, SeasonNumber: season.number, EpisodeNumber: i + 1,
        SortName: `S${season.number}E${String(i + 1).padStart(2, '0')}`,
        Overview: e.overview, ProductionYear: s.year,
        PremiereDate: `${s.year}-0${((i % 9) + 1)}-0${((i % 9) + 1)}T00:00:00Z`,
        DateCreated: s.dateCreated,
        RunTimeTicks: toTicks(e.runtimeMin * 60),
        ImageTags: poster(e.id, s),
        PrimaryImageAspectRatio: 1.7778,
        MediaSources: [{
          Id: `ms-${e.id}`, Path: `/media/tv/${s.title}/S${season.number}E${i + 1}.mkv`,
          Protocol: 'Local', Container: 'mkv', Size: 2_147_483_648, IsRemote: false,
          Name: '1080p', Bitrate: 12_000_000,
          MediaStreams: [
            { Type: 'Video', Codec: 'h264', Index: 0, DisplayTitle: '1080P H264', Width: 1920, Height: 1080, BitRate: 11_000_000 },
            { Type: 'Audio', Codec: 'aac', Index: 1, Language: 'chi', DisplayTitle: '中文 2.0 (AAC)', Channels: 2, SampleRate: 44_100, IsDefault: true },
            { Type: 'Subtitle', Codec: 'srt', Index: 2, Language: 'chi', DisplayTitle: '中文 (SRT)', DeliveryMethod: 'External', DeliveryUrl: `/emby/Videos/${e.id}/ms-${e.id}/Subtitles/2/0/Stream.srt` },
          ],
        }],
      })
    }
  }
}

// 标签(真实站"标签"tab 数据源):按类型分配,让标签 tab 有内容
for (const it of items) {
  if (!it.Tags && it.Type === 'Movie') it.Tags = ['4K', it.Genres?.[0] ?? '电影'].filter(Boolean)
  else if (!it.Tags && it.Type === 'Series') it.Tags = ['连载', it.Genres?.[0] ?? '剧集'].filter(Boolean)
}

// ---------- 预告 / 合集 / 文件夹(真实站 /tv 页这三个 tab 的容器数据) ----------

/** 预告片(Trailer):挂在剧集下,真实站"预告"tab 按 Trailer 类型查询 */
const trailerSeeds: { id: string; parentId: string; title: string }[] = [
  { id: 'tr-301', parentId: '301', title: '风起长夜 正式预告' },
  { id: 'tr-302', parentId: '302', title: '沉默的证词 先导预告' },
  { id: 'tr-101', parentId: '100', title: '星际织网者 预告片' },
]
for (const t0 of trailerSeeds) {
  items.push({
    Name: t0.title, Id: t0.id, Type: 'Trailer', ParentId: t0.parentId,
    SortName: t0.title, ProductionYear: 2025, DateCreated: '2025-01-01T00:00:00Z',
    RunTimeTicks: toTicks(120), ImageTags: { Primary: `tr-${t0.id}` },
    PrimaryImageAspectRatio: 1.7778,
  })
}

/** 合集(BoxSet):真实站"合集"tab 按 BoxSet 类型查询 */
items.push({
  Name: '科幻经典合集', Id: 'box-1', Type: 'BoxSet', ParentId: '100', IsFolder: true,
  SortName: '科幻经典合集', ProductionYear: 2025, DateCreated: '2025-02-01T00:00:00Z',
  ImageTags: { Primary: 'box-1' }, PrimaryImageAspectRatio: 1.7778,
  ChildCount: 3,
})
items.push({
  Name: '2026 冬季剧集合集', Id: 'box-2', Type: 'BoxSet', ParentId: '200', IsFolder: true,
  SortName: '2026 冬季剧集合集', ProductionYear: 2026, DateCreated: '2026-01-05T00:00:00Z',
  ImageTags: { Primary: 'box-2' }, PrimaryImageAspectRatio: 1.7778,
  ChildCount: 2,
})

/** 文件夹(Folder):真实站"文件夹"tab 列出库内文件夹型条目 */
items.push({
  Name: '未分类影片', Id: 'folder-1', Type: 'Folder', ParentId: '100', IsFolder: true,
  SortName: '未分类影片', DateCreated: '2025-03-01T00:00:00Z', ChildCount: 0,
})

export const allItems = items
export const itemById = new Map<string, BaseItemDto>([
  ...libraries.map((l) => [l.Id, l] as const),
  ...items.map((i) => [i.Id, i] as const),
])

// ---------- 用户数据(SPEC/data-model:user_data,写热点独立表) ----------

/** userId → itemId → UserData */
export const userDataByUser = new Map<string, Map<string, UserItemData>>([
  ['1', new Map(Object.entries({
    '101': { Played: true, PlayCount: 2, LastPlayedDate: '2026-10-10T20:00:00Z', IsFavorite: true, Likes: true, Rating: 9 },
    '102': { Played: true, PlayCount: 1, LastPlayedDate: '2026-10-11T21:30:00Z' },
    '105': { Played: false, PlaybackPositionTicks: toTicks(156 * 60 * 0.35), PlayCount: 1, LastPlayedDate: '2026-10-13T19:00:00Z' },
    '108': { Played: false, PlaybackPositionTicks: toTicks(104 * 60 * 0.6), PlayCount: 1, LastPlayedDate: '2026-10-12T22:10:00Z', IsFavorite: true },
    '109': { Played: false, PlaybackPositionTicks: toTicks(101 * 60 * 0.04) },
    '110': { Played: true, PlayCount: 1, LastPlayedDate: '2026-10-06T20:00:00Z', Likes: true },
    '301': { Played: false, UnplayedItemCount: 14, IsFavorite: true },
    '302': { Played: false, UnplayedItemCount: 8 },
    '303': { Played: false, UnplayedItemCount: 6 },
    '501': { Played: true, PlayCount: 1, LastPlayedDate: '2026-10-12T20:00:00Z' },
    '502': { Played: true, PlayCount: 1, LastPlayedDate: '2026-10-12T21:00:00Z' },
    '503': { Played: true, PlayCount: 1, LastPlayedDate: '2026-10-13T20:00:00Z' },
    '504': { Played: true, PlayCount: 1, LastPlayedDate: '2026-10-13T21:00:00Z' },
    '505': { Played: false, PlaybackPositionTicks: toTicks(49 * 60 * 0.7), PlayCount: 1, LastPlayedDate: '2026-10-14T20:00:00Z' },
    '541': { Played: true, PlayCount: 1, LastPlayedDate: '2026-10-09T20:00:00Z' },
  }))],
  ['2', new Map()],
])

export function getUserData(userId: string, itemId: string): UserItemData {
  return userDataByUser.get(userId)?.get(itemId) ?? {}
}

export function mutateUserData(userId: string, itemId: string, patch: Partial<UserItemData>): UserItemData {
  let m = userDataByUser.get(userId)
  if (!m) { m = new Map(); userDataByUser.set(userId, m) }
  const next = { ...m.get(itemId), ...patch }
  m.set(itemId, next)
  return next
}

// ---------- 派生 ----------

export function genres(): string[] {
  const set = new Set<string>()
  for (const i of items) {
    if ((i.Type === 'Movie' || i.Type === 'Series') && i.Genres) i.Genres.forEach((g) => set.add(g))
  }
  return [...set].sort((a, b) => a.localeCompare(b, 'zh'))
}

/** 播放状态阈值照 Emby(SPEC/playback §5) */
export const RESUME_THRESHOLDS = {
  minResumePct: 5,
  maxResumePct: 90,
  minResumeDurationSeconds: 300,
}