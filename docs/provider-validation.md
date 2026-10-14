# 元数据 Provider 验证记录（P3-A 轨道）

> 执行：Developer，2026-10-14 22:45–23:15，受测实例 PID 24980（T1 构建，8096）
> 依据：docs/phase3-design.md §2.4 / §A（P3-A1、P3-A4）
> 方法：API 实测（curl 直打 provider 端点 + 服务器端到端刷新）+ DB 落库核对（psql）

## A1 TMDb 电影链路 ✅ 全通

**前置实测**：`api.themoviedb.org` 可达，硬编码 key `f6bd687…` 有效（configuration / movie / search 三端点 200）。

**端到端验证**（Movie1，删 nfo → FullRefresh+ReplaceAllMetadata）：

| 环节 | 结果 | 证据 |
|------|------|------|
| 搜索识别 | ✅ | `POST /Items/RemoteSearch/Movie` 返回 5+ 结果；刷新日志 `MovieDbProvider: Finding id for item: Movie1` → 命中 TMDb 392617 |
| 元数据落库 | ✅ | Name→"movie 1"、Overview（英文简介）、ProductionYear 2018、CommunityRating 7.1、OfficialRating PG-13、Genres [Crime,Drama,Family] |
| ProviderIds | ✅ | `{"Imdb":"tt2418644","Tmdb":"392617"}`，ExternalUrls 出 IMDb/TheMovieDb/Trakt 三链 |
| 缓存 | ✅ | `cache/tmdb-movies2/392617/all-en.json`（<2 天命中不重拉，22:53 刷新零外连复用） |
| People 写入 | ✅（数据源空，非 bug） | 392617 在 TMDb 是空壳条目（casts/crew/images 全空）→ People [] 正确；**写入链路本身已由 Movie2 验证**：329969（にゃんこ THE MOVIE2）cast 唯一人 Manami Konishi 落库 item_people 1 行，与 TMDb 数据一致 |
| TMDb 海报入库 | ✅ | Movie2 Primary 66KB 真海报（image.tmdb.org 下载）；Movie1 因 TMDb 源 posters 空，本地 poster.jpg 按语义保留（ReplaceAllImages=false） |

**结论**：TMDb 电影链路（搜索→识别→拉取→落库→图片）无代码缺陷，BUG-16 修复后元数据可正常持久化。Movie1 nfo 已恢复（fixture 完整性），库内 TMDb 数据保留。

## A4 TVDB 禁用 + Omdb 验证 ✅

**TVDB 死源实锤**：`api.thetvdb.com` 连接不可达（STATUS=000，DNS/连接层失败）。处置（"死源默认禁用，TMDb 顶上"）：

1. **代码默认值**（ServerConfiguration ctor）：Series/Season/Episode 的 `DisabledMetadataFetchers`/`DisabledImageFetchers` 由 `TheMovieDb` 反转为 `TheTVDB`（Season 保留原 FanArt 禁用项）。TvdbPrescanTask 走 `IsMetadataFetcherEnabled` 过滤，禁用后 post-scan 自动跳过，无需改代码。
2. **部署配置**（D:\OpenMedia\ProgramData-Server\config\system.xml，已持久化的 MetadataOptions）：三处同步反转（备份 system.xml.a4bak）。**需重启窗口生效**——与 Chief 的 7b773d9ca4 部署同窗口执行。

**Omdb 验证**：

| 项 | 结果 |
|----|------|
| 主 API `www.omdbapi.com` | ✅ 活，key `fe53f97e` 有效；i=tt2418644 返回 "Testing Movie1"（2018，Crime/Drama/Family）——与 TMDb 392617 同一部片，交叉印证 A1 识别正确 |
| 图片端点 `img.omdbapi.com` | ❌ 404（死）。**修复**：OmdbImageProvider Movie/Trailer 分支原走该死端点，统一改走主 API 的 `Poster` 字段（m.media-amazon.com，实测可用）——与 Episode 分支一致 |
| 默认禁用状态 | Movie/MusicVideo/Episode 的 Omdb 保持原默认（Episode 禁用列表含 Omdb，维持） |

## 遗留与移交

- system.xml 的 TVDB 禁用**等重启窗口**生效（谁重启谁顺带验证：`GET /emby/Library/MediaFolders` 后 Series 刷新不打 thetvdb.com）
- A2（TMDb 剧集链路）等 D3 剧集 fixture；A3（音乐三件套）等 D2 音频 fixture
- Movie1 刷新后 Name 已变 "movie 1"（TMDb 数据），若测试断言依赖 Name=="Movie1" 需在回归时确认（ItemId 未变，路径派生稳定）
