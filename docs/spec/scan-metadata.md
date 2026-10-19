# 扫描与元数据（SPEC/scan-metadata）

> 行为基准出处：Emby 3.5.2.0 源码 + 闭源二进制反射实测（Emby.Naming 1.1.7-beta，
> 与 3.5.2.0 服务端实际引用版本一致）。本册所有"照 Emby"均附出处；实测输入
> 输出表直接作为 Go 解析器的验收基准集。
> 架构前提：解析器唯一（ADR-0008：扫描/重绑定共用同一段代码）。

## 1. 扫描触发 〔定稿〕

定时扫描（间隔可配，默认 12h）+ 手动触发（页面按钮/任务面板/API）+
inotify 实时监视器（加速器而非正确性依赖，网络盘失效时定时扫描兜底）+
**扫描 webhook**（API Key 鉴权，供 MoviePilot 等外部程序整理完毕后秒级刷库）。
扫描产出 = 事件流（itemAdded/itemUpdated/itemRemoved）入进程内 eventbus，
刮削管线订阅（core.md §3）。

## 2. 文件/目录解析规则（照 Emby 完整展开）

### 2.1 季目录判定（SeasonPathParser，实测基准）
| 输入 | SeasonNumber | IsSeasonFolder |
|---|---|---|
| `Season 1` / `Season 01` / `Season1` / `S1` | 1 | ✔ |
| `Season 2009` | 2009 | ✔ |
| `Specials` | **0** | ✔（季 0 显示名默认 "Specials"，`SeasonZeroDisplayName`） |
| `saison 2` / `temporada 3` / `staffel 4` | 2/3/4 | ✔（多语言季词内置） |
| `Season 1 (2010)` | 1 | **✘**（带括号年份视为单集目录候选） |
| `Season 2 Extra` | 2 | ✘ |

剧集目录判定 = 子目录可解析出季号，或子文件可解析出集号
（`SeriesResolver.cs:213-220`）。

### 2.2 集文件名表达式（18 条 EpisodeExpressions，按序）
出处：反射 `MediaBrowser.Naming.1.1.7-beta` NamingOptions；Go 实现以本清单为
规格，验收以 2.5 实测表为准。要点：

- 标准：`S01E02`（1）、`EP12`（2）
- 日期集：`yyyy.MM.dd` / `dd.MM.yyyy` 及 `_`/`-` 变体（3、4；日期集清空
  IndexNumber、置 PremiereDate，`LibraryManager.cs:2441-2477`）
- `1x03` / `5x03` 及带剧名变体（5、8-11）
- 目录风格 `Show.2.05`（6）
- `part 1/II`（7）
- **多集**：`05-06.mkv`（12、13）、`Show - 05 - 标题.mkv`（14）、
  `S02E05-E06` 范围变体 10 条（注：3.5.2 所带 beta 库实测部分变体未生效，
  Go 版按表达式清单完整实现，不复制 beta 的缺陷）
- 目录内裸集号：`Season 1\5.mkv`（17）、`Show - 05.mkv`（16、18）
- 无季表达式：`[/\._ -]()(N)(-M)?`；集内分段（multipart）：`^[-_ex]+(N[a-i])?`
  （`cd1/cd2` 归堆叠管，见 2.3）

### 2.3 堆叠与多版本
- **堆叠（单 MediaSource 多文件）**：`cd|dvd|part|disc` + 数字/字母，及
  `[a-d]` 变体（VideoFileStackingExpressions，实测 `cd1+cd2`、`part1+part2`
  合并；`MovieResolver.cs:141-142`；多碟目录 + VIDEO_TS/BDMV 走
  `StackResolver`，`MovieResolver.cs:508-531`）。
- **多版本（同一 Item 多 MediaSource）**：同目录多个视频文件 = 版本
  （`MovieResolver.cs:425 supportsMultiVersion`）。警示：3.5.2 所带 beta 库
  实测 `- 1080p / - 720p` 同名变体**未**成功合并——Go 版按"同目录即版本"
  完整实现，验收表单独列。
- 版本显示名取文件名清洗后的修饰词（见 2.4）。

### 2.4 名字清洗
- CleanStrings 词表（反射）：移除 `cd[1-9]`、`480p…2160p`、
  `x264|h264|xvid`、`3d|sbs|tab|hsbs|htab|mvc` 等 release 修饰；
  实测：`movie (2010) 1080p BluRay x264.mkv` → Name=`movie`、Year=`2010`。
- 单视频+目录场景名取目录名（`MovieResolver.cs:437-441`）。
- Provider ID 前缀：`tmdbid=`、`imdbid=`（`MovieResolver.cs:333-362`）、
  `tvdbid=`（`SeriesResolver.cs:239-249`）。
- 3D 词表：`fsbs/ftab/hsbs/htab/sbs/sbs3d/tab/mvc`（`BaseVideoResolver.cs:208-245`）。

### 2.5 实测验收基准集（解析器单测直接引用）
| 输入 | 期望 |
|---|---|
| `\Show\Season 1\Show S01E02.mkv` | S=1 E=2 |
| `\Show\Season 1\05.mkv` | E=5（**季号来自目录，不来自文件名**） |
| `Show.5x03.mkv` / `Show EP12.mkv` | S5E3 / E12 |
| `Show 2014.06.12.mkv` | 日期集 ByDate |
| `\Show\Season 1\Show 01-02.mkv` | E=1 End=2（多集） |
| `movie (2010) cd1.mkv + cd2.mkv` | 合并单视频 |

### 2.6 排除与识别辅助
- **Extras/Sample**：文件名（`. - _ !` → 空格后）含 ` sample ` 判 Extras；
  词表：trailer/sample/theme/scene/clip/interview/behindthescenes/deleted/
  featurette/short（`MovieResolver.cs:179-193` + VideoExtraRules 反射）。
  **v1 不收 Extras，直接跳过**（定稿）。
- 样品大小阈值（<50MB 视为样品）为 **Serata 自定扩展**（Emby 无此机制，
  出处已注明），可配。
- Stub：`.disc` 占位；DVD=`video_ts` 含 .vob/ifo；BD=`bdmv` 目录
  （`BaseVideoResolver.cs:265-305`）。
- 视频扩展名白名单 60 个（反射清单入库，`.strm` 在内）。
- **只有集数无季目录 → 默认季 1**（`EpisodeResolver.cs:64-68`，定稿；
  解析不出来视为用户未整理，不发明新规则）。
- 绝对编号：`Series.DisplayOrder == "absolute"` 时按绝对集号
  （`LibraryManager.cs:2419-2424`）；动漫官方顺序纠偏由 AI 仲裁写入
  airs_before/after 字段（ADR-0006），解析器不背这个锅。

## 3. 元数据管线（优先级链）〔定稿〕

**NFO 锁定字段（人工事实）> Provider（TMDB/Bangumi）> AI 补缺**（定稿）。
- 读：Kodi NFO 兼容（Emby XbmcMetadata 形状）；库级写回开关默认关，写回直写
  （定稿，无额外覆盖保护）。
- 多源仲裁（ADR-0006）：Provider 原始报文存 `provider_data`；规则仲裁默认开，
  AI 仲裁（OpenAI 兼容 endpoint，可本地 ollama/vLLM）配置即用、默认关；锁定
  字段宿主层拦截。
- 刮削触发：条目创建后自动 + 手动刷新（Emby `MetadataRefreshMode` 参数
  全集照抄，api.md §1.6）；图片下载缓存本地（远程源同样）。

## 4. 文件生命周期〔定稿〕

洗版 → Unbound（数据保留、不进浏览列表）→ 命名匹配 → 自动 Rebind；真删除仅
用户 UI 弹窗二选一；扫描器无权删数据（ADR-0008）。Unbound 匹配与初次扫描
共用 2.x 解析器。

## 5. 验收

- 2.5 实测表全部为解析器单测用例；18 表达式逐条正例 + 反例。
- 多版本"同目录合并"专列验收（beta 缺陷不复制）。
- 扫描事件流：模拟 1 万文件目录扫描，断言事件完备性与顺序。
- NFO 读写往返（fixture 4.8 Emby 库）。