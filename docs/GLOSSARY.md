# Serata

一个对标 Emby 的高性能自研视频媒体服务器（Go 后端 + Vue 3 前端 + LinWeb 播放核心）。
本文是全项目的唯一术语权威：SPEC、代码、PR 讨论中出现这些词时，含义以此为准。

## Language

### 媒体与内容

**Item**:
媒体库中的一个可展示条目：电影、剧集、季、集、音乐视频或合集。
_Avoid_: 媒体对象、Media、Asset

**Library**:
一组受扫描规则约束的 Item 集合，含类型（电影/剧集）与路径列表。
_Avoid_: 媒体库目录、文件夹

**MediaSource**:
一个 Item 的具体可播放来源（单个视频文件或一条 .strm 直链）；一个 Item 可有多个。
_Avoid_: 文件、Source

**MediaStream**:
MediaSource 内的一条轨道（视频/音频/文本），来自 ffprobe 的探测结果。
_Avoid_: 轨道以外的叫法（track 仅在代码内部使用）

**Strm**:
内容为一个 URL 的 `.strm` 文本文件，展开后成为指向远程源的 MediaSource。
_Avoid_: 直链文件

**NFO**:
Kodi/Emby 格式的 XML 元数据侧车文件；读取兼容，但**数据库永远是唯一真相源**。
_Avoid_: 元数据文件（含义过宽）

**Provider**:
一个在线元数据来源。v1 内核三个：TMDB、Bangumi、AI 仲裁器；其余（AniDB、TVDB、豆瓣等）以 Plugin 形式接入。
_Avoid_: 刮削器、Scraper

**Canonical**:
本地库的最终元数据事实。刮削与仲裁的产物，与任何 Provider 的原始数据分开存放。
_Avoid_: 权威数据、最终元数据

**Arbitration**:
把多个 Provider 的原始数据裁决为一条 Canonical 的过程；裁决者可以是规则引擎或 AI，参与者与理由留档。
_Avoid_: 合并、去重

**RawProvider**:
某个 Provider 对某次请求返回的原始报文存档，Arbitration 的输入，永久保留。
_Avoid_: 缓存（它不参与响应，只参与再加工）

### 播放

**DirectPlay**:
客户端拿到原容器原编码字节直接解码，服务器零参与处理。
_Avoid_: 直连、原盘直播

**DirectStream**:
服务器只换容器不转码流（remux）后交给客户端。
_Avoid_: 重封装播放

**Transcode**:
服务器用 ffmpeg 重新编码视频或音频后交给客户端。
_Avoid_: 软解、硬解（那是实现手段，不是模式）

**PlaybackSession**:
一次播放的完整生命周期记录：哪个用户、哪个设备、哪个 Item、什么模式、进度如何演进。
_Avoid_: 会话（与登录会话冲突时必须写全称）

**Progress**:
用户在某个 Item 上的播放位置与已看状态，双向同步的事实来源。
_Avoid_: 续看、Resume（那是 UI 功能名）

### 弹幕与房间

**Danmaku**:
叠加在画面时间轴上的弹幕评论，归属某个 Item；来源为导入文件或用户实时发送。
_Avoid_: 评论（Comment 特指房间文字）

**DanmakuExport**:
按条目聚合的弹幕定时导出，弹弹play JSON 兼容格式，用于向弹幕社区共享。
_Avoid_: 备份（备份语义是自用恢复）

**Room**:
服务器上的私密观影房间：有主人、可选密码、共享播放状态，成员间以评论区交流；无弹幕、评论不长期保存（滚动清理）。
_Avoid_: 一起看（LinWeb 旧实现的名称）、直播厅

**RoomComment**:
Room 内的滚动文字消息，容量上限 500 条，超限按时间滚动清除。
_Avoid_: 聊天记录、弹幕（严禁混用，Danmaku 有时间轴语义）

### 系统

**Plugin**:
以 goja（内嵌 JS）为唯一机制的服务器扩展，可开放面：Provider、元数据后处理、事件钩子。
_Avoid_: 扩展、Addon

**Task**:
扫描、刮削、图片抓取、转码等后台工作单元；有队列、有进度、可取消。
_Avoid_: Job（保留给转码的 ffmpeg 进程）

**Anchor**:
Room 内共享播放状态的唯一事实：最后按下操作的时间戳 + 播放头位置。由服务器持有，客户端只做本地微调。
_Avoid_: 状态同步、时钟

**Unbound**:
Item 失去其全部 MediaSource 后的状态：数据全保留、默认不进浏览列表、等待按命名规范重新绑定。
_Avoid_: missing（Emby 的词汇，语义含删除暗示，禁用）
