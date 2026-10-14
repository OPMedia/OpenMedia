# OpenMedia 二期设计：健壮性整改 + 性能审计 + ffmpeg

> 依据：docs/test-report.md（第二轮终态 + Lead 终态补充）、tests/results/fail-classified.md（83 FAIL 分类）
> 范围：① 健壮性 ② 性能审计（只测量不优化）③ ffmpeg 安装验证 ④ 工程债。B6 插件端点 25 个待拍板，不在本范围。
> 代码证据核验日期：2026-10-14（Architect 实读 ServiceRoutingMiddleware / MediaEncoder / PgItemRepository / schema-deploy.sql）

## 0. 目标与总验收

| 目标 | 内容 | 验收 |
|------|------|------|
| G1 健壮性 | fail-classified ① 类剩余 20 个端点（BUG-16 ×2 已修）从 500/错配 400 转为正确 404/400 | 逐一复测通过；矩阵责任口径 ≥ 95% |
| G2 性能 | 产出 docs/perf-baseline.md（慢查询 Top-20、负载 P95、扫描分解、N+1 清单）+ 优化候选清单 | 报告数字可用 tests/perf/ 脚本复现；优化实施放三期 |
| G3 ffmpeg | 11 个 ② 类端点复测；真实转码端到端打通 | mkv → live.m3u8 可播放，transcoding-temp 有分片且停止后清理 |
| G4 工程债 | master 与 GitHub 远端统一（ahead 43/behind 43） | force push 完成（需 Chief 拍板） |

**总闸门**（四关全过才算交付）：三套件全量回归不劣化（场景 ≥ 22/24 且仅剩已知项、矩阵 ≥ 63.9%、契约 ≥ 16/2）。

---

## 1. 健壮性整改方案

### 1.1 现状（代码证据）

全局异常→状态码映射**已存在**：`src\Emby.Server.HttpServer\Middleware\ServiceRoutingMiddleware.cs:382` `_mapExceptionToStatusCode`：

```
ResourceNotFoundException→404  FileNotFoundException→404  DirectoryNotFoundException→404
ArgumentException→400  SecurityException→401  PaymentRequiredException→402  RemoteServiceUnavailableException→502
```

`GetStatusCode`（:415）兜底 500；`ErrorHandler`（:440）写 `text/html` + `NormalizeExceptionMessage`（剥 ProgramData 路径）。

20 个失败端点的异常类型**落不进这张表**：

| 异常 | 现状 | 应为 | 数量 | 典型端点 |
|------|------|------|------|---------|
| InvalidOperationException（First() 空序列） | 500 | 404 | 7 | LiveTv/ChannelMappings、Dlna/Profiles/{Id}、Plugins/{Id}/*、System/Logs/Log、Subtitles/{Id} |
| NullReferenceException | 500 | 404 | 4 | Dlna/icons ×2、LiveTv/Recordings/Folders、LiveStreamFiles |
| ArgumentNullException('id'/'result') | 400（经 ArgumentException 继承命中）但语义是资源不存在 | 404 | 4 | LiveTv/Channels、Recordings、Timers、SeriesTimers/{Id} |
| FormatException（'test' 解析） | 500 | 400 | 3+ | Playback/BitrateTest、Images/Remote、Videos/index.bif、Views(IncludeExternalContent) |
| InvalidCastException（Movie→Playlist） | 500 | 404 | 2 | Playlists/{Id}/Items、Playlists/{Id}/InstantMix |

根因共性：**按 Id/Name 查找用裸 First() / 直接解引用 / 直接强转**，查不到时抛的异常类型不对，掉进 500 兜底。这是旧 Emby 8 年的代码习惯，不是迁移引入的。

### 1.2 方案取舍

**方案 A：纯全局映射**（把 IOE/NRE/FormatException/InvalidCastException 全映射 400/404）
- ✅ 一处改、全覆盖（含未知端点）、工作量最小（~0.5 天）
- ❌ NRE 和大多数 IOE 是**真代码 bug**，全局洗成 4xx 会掩盖回归——矩阵"500 = 真 bug"这个信号被永久污染，以后这类问题再也测不出来
- ❌ 无法区分 404（查不到）与 400（参数坏）：First() 空该 404，'test' 解析失败该 400，同一张表给不出两种语义
- 结论：**否决为主方案**，只可作兜底且仅限低风险条目

**方案 B：纯逐点修**（20 个端点逐个改）
- ✅ 语义精确、不掩盖任何 bug
- ❌ 20 处手工改；未来新端点还会犯同类错（旧代码就是这么积累的），无防线

**方案 C（选定）：逐点修为主 + 低风险全局兜底**
1. 逐点修 20 个端点：查不到 → 显式 `throw new ResourceNotFoundException(...)`（自动进 404 映射）；参数解析失败 → TryParse/Uri.TryCreate 失败抛 ArgumentException（400）；类型不符 → 先判 `is` 再 404
2. 提共享助手消灭"裸 First()"家族：各 Manager 内统一 `FirstOrDefault + throw ResourceNotFoundException` 模式（LiveTvManager 是重灾区，一次修 8 个端点）
3. 全局兜底**只加 1 条**低风险映射：`FormatException → 400`（几乎必然来自用户输入解析；极少数内部解析误标 400 的风险可接受，记录在案）
4. **IOE / NRE / InvalidCastException 保持 500**——它们是回归探测器，不许洗白
5. 错误体维持 `text/html` + message 现状（兼容现有测试断言与旧客户端），本期不改格式
- 理由：保住"500 = 真 bug"的测试信号，同时消掉已知 20 个 + 长尾兜底（未知端点的 FormatException）

### 1.3 修改点清单（按文件）

| 文件 | 端点 | 修法 |
|------|------|------|
| `Emby.Server.Implementations\LiveTv\LiveTvManager.cs` | Channels/{Id}、Recordings/{Id}、Timers/{Id}、SeriesTimers/{Id}、ChannelMappings、ChannelMappingOptions、Recordings/Folders、LiveStreamFiles/{Id}/stream | First()→FirstOrDefault + ResourceNotFound；ArgumentNullException('id'/'result') 路径改判空后抛 ResourceNotFound；NRE 点补判空 |
| `Emby.Dlna\*`（Profiles/{Id}、icons/{Filename}、{UuId}/icons） | 3 端点 | 同上模式 |
| Plugins/{Id}/Thumb、Plugins/{Id}/Configuration | 2 端点 | First()→404 |
| `MediaBrowser.Api\PackageService.cs` | Packages/{Name} | 现有 :235 的 ResourceNotFound 没被走到（上游 'result' 判空先炸）——包查询空结果先判空再抛 |
| `MediaBrowser.Api\*` System/Logs/Log | 1 端点 | First()→404 |
| `MediaBrowser.Api\Subtitles\SubtitleService.cs` | Subtitles/{Id} | First()→404 |
| Playlists/{Id}/Items、Playlists/{Id}/InstantMix | 2 端点 | `item is IHasPlaylist` 先判，否则 404 |
| Playback/BitrateTest、Images/Remote、Videos/{Id}/index.bif、Users/{UserId}/Views | 4 端点 | int/long/Uri/bool 解析改 TryParse，失败 400 |
| `ServiceRoutingMiddleware.cs:382` | 全局 | 映射表 + `{typeof(FormatException), 400}` |

### 1.4 验收（Tester / Reviewer）

- ① 类 20 端点逐一复测：404/400 正确 + body 有可读 message
- **负路径用例固化进 test-plan.json**：每族至少 1 个"不存在的 Id"用例（否则这次修完下次回归测不出来）
- 全量三套件回归：无现有 PASS 转 FAIL
- Reviewer 阻塞项定义：任何现有 PASS 转 FAIL；404/400 语义错配（查不到返 400、参数坏返 404）；IOE/NRE 被全局映射洗成 4xx

---

## 2. 性能审计计划（先测量，后动手）

### 2.1 原则

- 一切优化前先有基线数字；无 profile 不改代码（用户点名"先测量再动手"）
- 本期只产出测量报告 + 优化候选清单（优先级/预估收益/风险），**优化实施放三期**
- 测量脚本全部入库 `tests\perf\`，Reviewer 可复现

### 2.2 数据规模前提（关键）

当前库仅 4 个条目，任何 EXPLAIN 都是索引假象。先造合成数据：**10k media_items**（含 jsonb data / people / chapters / user_datas 100 用户），用真实 Movie 的序列化模板生成（避免 blob 形态失真）。

⚠️ **条目库跨实例共享**（功能测试断言 TotalRecordCount=4）——合成数据必须放**独立数据库**（如 `openmedia_perf`，用 deploy\schema-deploy.sql 重建），测完即弃，严禁写进共享库。

### 2.3 PG 慢查询分析

1. **pg_stat_statements**：`shared_preload_libraries`（PG 重启）→ `CREATE EXTENSION` → 跑完负载后取 Top-20（按 total_exec_time / mean_exec_time / calls 三口径）
2. **分支覆盖矩阵**：`PgItemRepository.Query.cs`（2504 行）60 个过滤分支逐分支生成 EXPLAIN ANALYZE（10k 数据），标记 seq scan / 高 cost / 索引未命中
3. **重点嫌疑**（代码结构推断，待数据验证）：
   - DISTINCT ON + 外层重排序（旧 GROUP BY PresentationUniqueKey 的 PG 翻译）在大表的代价
   - user_datas join（user_data_key_id 翻译）是否走索引
   - 60 分支动态拼接 SQL 的 plan 复用（Npgsql prepared statements 命中率）
   - **水合路径**：`PgItemRepository.cs:1030` 每行 `_jsonSerializer.DeserializeFromString(data)` 全量反序列化——列表页 N 条 × 全 blob 的 CPU 占比
4. **N+1 检查**：静态 grep 循环内开连接/查询（Ancestors / People / ChaptersStreams 分部类）+ Npgsql 运行时日志（statement + duration）交叉验证
5. **索引盘点**：schema-deploy.sql 已有 58 个索引（media_items 20+，含 GIN/BRIN），对照实际查询模式找"未用 / 缺失"

### 2.4 负载测试（k6）

| 场景 | 内容 | 目的 |
|------|------|------|
| L1 只读热点 | /Items 分页、Ids 批量、/Users/{uid}/Items、图片端点；RPS 阶梯 10→50→100，每档 60s | 查询链路基线 |
| L2 扫描并发 | POST /Library/Refresh 进行中跑 L1 | 扫描对在线查询的影响 |
| L3 静态流式 | stream?static=true + Range，并发 10 | IO 路径 |
| L4 认证 | AuthenticateByName 并发 | 密码哈希成本 |

指标：P50/P95/P99、错误率、服务器 CPU/内存、PG 连接数与 active sessions。
判定：只记数字不设阈值；P95 > 500ms 或错误率 > 1% 的端点进优化候选清单。
口径：本机 16 核单实例 → **相对基线**（改动前后对比），不作容量承诺。

### 2.5 启动扫描与缓存

- 扫描耗时分解：目录枚举 / ffprobe / DB 写入 / 图片处理各段计时（**依赖 ffmpeg 装好后测真实值**，否则 ffprobe 段缺失）
- 缓存验证：同一请求连续两次延迟对比（1st vs 2nd）验证命中；不命中则查缓存 key 策略

### 2.6 交付物

`docs\perf-baseline.md`（慢查询 Top-20 + 分支矩阵结论 + 负载 P95 表 + N+1 清单 + 扫描分解）+ 优化候选清单 + `tests\perf\` 脚本。

---

## 3. ffmpeg 安装与验证

### 3.1 安装（Developer，0.5 天）

- `winget install Gyan.FFmpeg`（或便携版放 deploy\ 并配 EncoderAppPath）；**版本 ≥ 3.0**（`MediaEncoder.ValidateVersion` 硬要求，:340）
- 路径解析链（已核验 `MediaEncoder.cs`）：`Init()` → `InitPaths()` → `ConfigureEncoderPaths()` → `GetEncodingOptions().EncoderAppPath`；PATH 可见即走 system 探测（`IsSystemInstalledPath`）
- 装完**重启服务器进程**（探测只在启动时跑）；日志确认 ffmpeg/ffprobe 路径输出 + decoders/encoders 列表非空
- 若探测失败：检查 `UpdateEncoderPath(path, "system")` 手动触发路径（:292）

### 3.2 验证（Tester，0.5 天）

- ② 类 11 个端点复测（stream / stream.{Container} / live.m3u8 / hls1 / subtitles.m3u8 等）
- 端到端：Movie1 mkv → `live.m3u8` 转码播放：transcoding-temp 出现分片、播放推进、停止后临时文件清理
- PlaybackInfo 的 MediaSources 出现 ffprobe 探测的 MediaStreams（codec/分辨率/码率）
- **预期管理**：转码路径首次真跑可能暴露新 bug——登记进 fail-classified 新分类，不算二期失败，但必须记录

### 3.3 转码并发回归（依赖 3.1）

- 并发 3 路转码 × 60s：服务器存活、无 StackOverflow（BUG-14 修复 `6f9383a0c3` StreamState.Dispose 在**转码路径**的验证——static 直连已验，转码是新激活代码）、句柄与磁盘不泄漏

---

## 4. 工程债

- master 与 GitHub 分叉（ahead 43 / behind 43）：确认 behind 43 全为旧提交者历史、无独有内容后 **force push 统一**。改写远端历史，**需 Chief 拍板**后执行（建议 Developer 执行前先 `git bundle` 备份本地 master）。

---

## 5. 任务清单（可并行拆分）

| 任务 | 内容 | 依赖 | 工作量 | 验收 |
|------|------|------|--------|------|
| T3.1 | ffmpeg 安装 + 重启 + 日志确认探测 | — | 0.5d | 日志见 ffmpeg 路径 + 编解码器列表 |
| T3.2 | ② 类 11 端点复测 + 转码端到端 | T3.1 | 0.5d | m3u8 可播放、分片清理 |
| T1.1 | 中间件 +FormatException→400 映射 | — | 0.5d | BitrateTest 等 400；现有 PASS 不劣化 |
| T1.2 | LiveTv 族 8 端点 | — | 1d | 全部 404 + message 可读 |
| T1.3 | Dlna/Plugins/Packages/Logs/Subtitles 族 7 端点 | — | 1d | 同上 |
| T1.4 | Playlists 强转 2 端点 | — | 0.5d | Movie 当 Playlist → 404 |
| T1.5 | 输入解析 400 化 4 端点 | — | 0.5d | 'test' 类参数 → 400 |
| T1.T | 负路径用例入 test-plan.json | 可与 T1.* 并行 | 0.5d | 每族 ≥1 个不存在 Id 用例 |
| T2.1 | pg_stat_statements + 10k 独立库 fixture | — | 1d | 独立库就绪，共享库零污染 |
| T2.2 | 分支 EXPLAIN ANALYZE 矩阵 | T2.1 | 1d | 60 分支全覆盖出表 |
| T2.3 | k6 四场景负载 | T2.1 | 1d | P95 表 + 脚本入库 |
| T2.4 | 扫描耗时分解 + 缓存验证 | T3.1 | 1d | 分解表 + 命中结论 |
| T4 | 转码并发 3 路 × 60s 回归 | T3.1 | 0.5d | 存活、无 SO、无泄漏 |
| T5 | force push 统一远端 | Chief 拍板 | 0.1d | 远端 = 本地 master |

**依赖图**：T3.1 → {T3.2, T4, T2.4}；T2.1 → {T2.2, T2.3}；T1.* 全相互独立；T1 与 T2/T3 全并行。

**建议执行顺序**：T3.1 最先（半天解锁 11 个端点 + 给 T2.4/T4 铺路）→ T1 主体与 T2 并行 → T4 + 全量回归收尾。

**分工**：Developer 做 T1/T3.1/T5；Tester 做 T3.2/T1.T/回归；T2 测量可由 Developer 搭、Tester 跑（脚本入库后谁跑都行）；Reviewer 按总闸门审查。

---

## 6. 风险登记

| 风险 | 应对 |
|------|------|
| R1 ffmpeg 装上后转码路径首跑出新 bug | 预期内，登记 fail-classified 新分类，不阻断二期 |
| R2 性能 fixture 误写共享条目库 | 独立数据库硬隔离 + Reviewer 检查共享库 TotalRecordCount 仍为 4 |
| R3 FormatException→400 全局映射误标少数内部解析错误 | 接受并记录；逐点修覆盖已知端点后长尾风险低 |
| R4 force push 改写远端历史 | Chief 拍板 + git bundle 备份 |
| R5 10k 合成数据 blob 形态失真导致 EXPLAIN 结论失真 | 用真实 Movie 序列化模板生成 |
| R6 服务器实例在测试期间被重建（一期发生过 4 次） | 二期约定：受测实例 PID 写进每份结果文件，实例切换即作废重跑 |
