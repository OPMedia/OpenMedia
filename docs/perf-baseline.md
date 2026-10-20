# OpenMedia 性能基线（二期 T2 交付）

> 测量日期：2026-10-14 23:20　受测实例：PID 6748（D:\OpenMedia 部署实例，8096）
> 环境：本机 16 核 / PostgreSQL 18.6（localhost:5432）/ .NET 10 Debug 构建
> 原则：只测量不优化（design §2.1）；优化实施放三期
> 复现：`tests\perf\` 下脚本全量入库，Reviewer 可重跑（见 §6）

## 1. 数据规模前提（T2.1）

- 独立库 `openmedia_perf`：schema 用 `pg_dump -s` 从实际库克隆（**schema-deploy.sql 与实际库有漂移**：实际库含运行时新增列 type_name/clean_name/media_type/top_parent_id，deploy 脚本没有——工程债登记）
- 合成数据（真实 Movie1 序列化模板 + 1.5KB Overview 模拟真实元数据重量，R5 缓解）：

| 表 | 行数 |
|---|---|
| media_items | 10,502（10k Movie + 500 Person + 2 Folder） |
| item_people | 29,906 |
| item_extradata | 35,000 |
| media_streams | 20,000 |
| ancestor_ids | 10,000 |
| local_users | 100 |
| user_datas | 300,000 |
| data blob 均值 | 1,511 字节 |

- **共享库零污染验证**：`openmedia` 中 `name LIKE 'Movie-0%' OR path LIKE 'D:\perf%'` = 0 行（R2 通过）
- ⚠️ **pg_stat_statements 未启用（阻塞）**：需要管理员重启 PG 服务（`shared_preload_libraries`），当前 shell 无服务控制权限；conf 修改已回滚。待 Chief 安排一次 PG 重启后补 Top-20（步骤：conf 设 `shared_preload_libraries='pg_stat_statements'` → 重启服务 → `CREATE EXTENSION` → 跑 load-test.py → 取 Top-20）

## 2. 查询分支 EXPLAIN ANALYZE 矩阵（T2.2）

50 个分支（覆盖 GetWhereClauses 全部分支族 + 主查询形态），10k 数据，`tests\perf\explain-matrix.sql`，原始输出 `tests\perf\results\explain-matrix.out`。

### 2.1 慢分支 Top-10（>4ms）

| # | 分支 | 耗时 | 根因 |
|---|---|---|---|
| 45 | AncestorWithPresentationUniqueKey | **118.5ms** | IN 子查询基数估计 8 行 vs 实际 10,000 → 嵌套循环逐行回探 media_items（60,068 buffer 命中，112ms 在 Nested Loop） |
| 40 | similarity_score 表达式（Similar 端点 ORDER BY） | **71.5ms** | 每行相关子查询 `count(*) FROM item_extradata WHERE item_id=A.id AND clean_value IN (...)` × 10k 行 |
| 36 | VideoType（A.data::text LIKE） | **65.5ms** | 全分区 seq scan + 每行 1.5KB blob ::text 转换 + LIKE |
| 37 | SeriesStatus（A.data::text LIKE） | **34.7ms** | 同上 |
| 34 | HasNoAudioTrackWithLanguage | **19.3ms** | 每行相关标量子查询探 media_streams |
| 03 | DISTINCT ON 包装（GroupByPresentationUniqueKey） | **17.6ms** | 全量物化+排序后才能 LIMIT（同查询非包装路径 0.14ms，**125×**） |
| 26 | Tags（相关子查询 IN） | 5.8ms | item_extradata 相关子查询 |
| 41 | search_score 布尔过滤 | 5.3ms | 双 LIKE 评分表达式 |
| 30 | PersonName（join+name） | 5.3ms | hash join 全扫 item_people 8 分区（30k 行） |
| 23 | 恢复播放（playback_position>0） | 4.5ms | user_datas join + 排序 |

### 2.2 快分支（健康，索引命中）

基线列表查询 0.14ms（type 过滤+排序+分页走分区+索引）；水合路径 0.12ms；user_datas join 0.09ms；IsPlayed 1.5ms；GenreIds 2.9ms；PersonIds 0.22ms；AncestorId 0.57ms；ItemIds 0.08ms；MediaTypes 0.07ms。

### 2.3 索引盘点结论

- 分区表（item_people/media_streams/ancestor_ids）的分区索引**齐全**（父表 `ON ONLY` 只是目录形态）
- item_extradata 有 (item_id, extradata_type_id) 与 (extradata_type_id, clean_value) 双索引，GenreIds 走索引
- **未命中/低效**：`A.data::text LIKE`（无索引可用，blob 转换逐行）；`similarity_score` 相关子查询（planner 无法用索引消除逐行探查）；~~BR-45 的基数误估~~（**四期更正：合成数据失真**——perf 库全部条目共享一个 CollectionFolder 祖先 → IN 列表 10k 行放大逐行回探；真实场景 PUK 匹配单 Series、IN 列表几十行 <1ms 非瓶颈，JOIN 改写实测无改善，见 §5 P4）

## 3. 负载基线（T2.3，k6 未安装 → Python stdlib 等价实现）

受测实例为在线实例（共享库 64 条目），**测的是 HTTP+查询+水合链路基线**；10k 规模的 DB 侧成本由 §2 EXPLAIN 矩阵覆盖。每档 30s（设计值 60s，在线实例缩减，偏差已记录）。

| 场景 | 吞吐 | P50 | P95 | P99 | 错误率 |
|---|---|---|---|---|---|
| L1 /Users/{uid}/Items 分页 10rps | 99 rps | 40.8ms | 57.3ms | 63.6ms | 0% |
| L1 同 50rps | 267 rps | 118.1ms | 163.2ms | 196.3ms | 0% |
| L1 同 100rps | 227 rps | 138.4ms | 187.8ms | 235.3ms | 0% |
| L1b /Items?Ids= 批量 50rps | 94 rps | **340.3ms** | **657.4ms** | 757.1ms | 0% |
| L2 扫描进行中 50rps | 247 rps | 130.0ms | 166.5ms | 185.8ms | 0% |
| L3 静态流 Range 并发10 | 1,204 rps | 7.6ms | 12.7ms | 22.1ms | 0%（206） |
| L4 AuthenticateByName 并发20 | 177 rps | 97.5ms | 221.3ms | 402.5ms | 0% |

**发现**：
1. **L1b（/Items?Ids= 批量）P95 657ms 是全场景最慢**——比同 RPS 的分页查询慢 4×，进优化候选（Ids 查询路径疑似走全量水合或未走索引，需 Developer 定位）
2. L2 扫描对在线查询**无影响**（130ms vs 118ms 基线，小库扫描快；大库结论待三期）
3. L4 认证 P99 402ms——密码哈希+会话创建成本，20 并发下可接受
4. L3 首跑 100% 失败暴露 **BUG-26**：Range 越界（请求 0-1048575，文件 809,858B）时服务器回 `Content-Length: 1048576` 但实际只发 809,858B（RFC 7233 要求按文件尾截断 Content-Range/Content-Length）→ 严格 HTTP 客户端（Python urllib 等）IncompleteRead；curl 容忍。界内 Range 重跑全绿

## 4. 转码并发回归（T4）

3 路并发动态 HLS 播放 × 60s（`tests\perf\results\t4-concurrent-transcode.json`）：

- master 3/3 200，main 3/3 200；分片 150×200 + 149×500（**500 全部是 BUG-23/24 竞态**，见 fail-classified）
- 服务器存活、无 StackOverflow（BUG-14 修复在转码路径验证通过）、无 ffmpeg 进程残留
- **临时文件泄漏**：3 会话 6 文件 2.6MB，测试结束 70s 后仍在（kill timer 对已退出的 ffmpeg 不生效，BUG-25）

## 5. 优化候选清单（四期实施，按优先级）

> **实施终态更新（2026-10-15 晚，Architect 维护）**：P1/P2/P3/P6 完成（commit `b1fe3d5e40`/`d6967b7a18`/`3e9ba4f0cb`/`081f78d522`），P4 跳过（结论更正），P5 已落地（`0fe4fbed12` 收尾：NULL-pukey 组不折叠是有意改善），**P7 五期提前落地**（partial hydration 三 commit），P8 新增候选（Reviewer 审计发现）。P1 守卫已修（`9c79863ad9`）。

| # | 候选 | 依据 | 预估收益 | 风险 | **终态** |
|---|---|---|---|---|---|
| P1 | /Items?Ids= 批量查询路径排查（L1b P95 657ms） | §3 | 4× | 低（纯查询层） | ✅ 匿名 Recursive 250→35ms（**6-7×**，BoxSet 折叠劫持根因+库树限定）；守卫已修（`9c79863ad9`） |
| P2 | similarity_score 相关子查询改 JOIN/预计算 | BR-40 71.5ms@10k | Similar 端点 10× | 中（Developer 正在改该区域，需协调） | ✅ LEFT JOIN 聚合 87.5→10.4ms（**8.4×**），新旧 top-10 score 逐行一致 |
| P3 | `A.data::text LIKE` 族改 jsonb 运算符或提取列 | BR-36/37 65/35ms@10k | VideoType/SeriesStatus 过滤 10× | 中（需数据迁移） | ✅ jsonb 运算符 40.9→10.1ms（**4×**），真实库 VideoType 8=8 一致（未做数据迁移，纯查询改写） |
| P4 | BR-45 AncestorWithPresentationUniqueKey 改 JOIN 写法消除基数误估 | BR-45 118.5ms@10k | 8× | 低 | ❌ **跳过+结论更正：合成数据失真**——perf 库全部条目共享一个 CollectionFolder 祖先 → IN 列表 10k 行放大逐行回探；真实场景 PUK 匹配单 Series、IN 列表几十行 <1ms 非瓶颈；JOIN 改写实测无改善（128ms）。**基线方法论教训：合成 fixture 的祖先分布必须贴近真实库形态，否则 EXPLAIN 结论失真** |
| P5 | DISTINCT ON 包装跳过条件（键唯一时退化为普通查询） | BR-03 125× | 列表页 10×+ | 中（语义等价性需 Reviewer 确认） | ✅ 已落地：非 NULL 键去重语义不变 + NULL-pukey 组不再折叠是有意改善（旧行为折叠 NULL 键行才是 bug，LiveTvProgram fixture 两行实测从折叠变各自出现） |
| P6 | HasNoAudioTrackWithLanguage 相关子查询改 EXISTS JOIN | BR-34 19.3ms | 2× | 低 | ✅ 五分支 EXISTS 28.8→16.5ms（**1.7×**），502=502 一致 |
| P7 | 水合路径测量（PgItemRepository.cs:1030 逐行全量反序列化） | BR-02 0.12ms@10k（blob 1.5KB） | 10k 规模下非瓶颈；100k+ 复测 | — | ✅ **提前落地**：partial hydration（`36b7858fcc`，列表路径跳过 data JSONB 全量反序列化，L1 82→51ms）+ count-per-folder TTL（`6bf8d59c35`，语句 27→3、L1 32ms）+ review 收尾（`0fe4fbed12`）。**Reviewer 阻塞一项**：RequiresFullHydration switch 漏 `ItemFields.RemoteTrailers`（data JSONB 独有字段，漏了=列表 `Fields=RemoteTrailers` 静默返 null，公开 API 契约回归）——一行修，修完关账。审计结论：Chapters（惰性走表）/MediaSources（运行时算）/ExternalUrls（DTO 现算）均不依赖 data JSONB，partial 下安全 |
| P8 | **新增候选**：skipDataHydration 时把 data 列从 SELECT 摘除 | Reviewer 审计发现：跳过水合但 data 列仍传输 ~1.5KB/行（网络+PG 成本白付） | 列表页再省一笔 | 低 | ⏸ 下个迭代 |

## 6. 复现步骤

```powershell
# T2.1 独立库（幂等，drop+重建）
powershell -ExecutionPolicy Bypass -File tests\perf\setup-perf-db.ps1
# T2.2 EXPLAIN 矩阵（输出 tests\perf\results\explain-matrix.out）
& 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h localhost -U postgres -d openmedia_perf -f tests\perf\explain-matrix.sql
# T2.3 负载（对在线服务器）
python tests\perf\load-test.py        # 结果 tests\perf\results\load-latest.json
# T4 转码并发：见 tests\perf\results\t4-concurrent-transcode.json（脚本逻辑内嵌于本报告运行记录）
```

pg_stat_statements 补测（待 Chief 排期管理员重启 PG）：conf 设 `shared_preload_libraries='pg_stat_statements'` → 重启服务 → `CREATE EXTENSION pg_stat_statements;` → 重跑 load-test.py → `SELECT query, calls, mean_exec_time, total_exec_time FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 20;`

---

## 7. 部署后复测（2026-10-15 16:40，PID 13636，P1–P6 + ByName 404 + T4.8 已部署，PG@15432，库 147 条目）

### 7.1 P1 验收：L1b 达标 ✓

| 场景 | 基线（05:14，64 条目） | 复测 | 结论 |
|---|---|---|---|
| L1b /Items?Ids= 批量 P95 | 657ms | **188ms** | P1 修复生效（Ids 路径 10ms 级） |
| L3 静态流 P95 | 12.7ms | 11.4ms | 持平 |
| L1 用户分页 P50 | 40.8ms | **363ms** | ⚠️ 回归，见 7.2 |

### 7.2 ⚠️ 新回归：无类型列表查询 ~9×（水合路径，待 Developer 根因）

- **现象**：`/Users/{uid}/Items?Recursive`（无类型过滤）P50 40.8→363ms；匿名同款 362ms（P1 承诺的 35ms 未保持——注意 P1 实测时库 34 条，现 147 条）
- **定位**（变量分离实测）：
  - 时间随返回条目数线性：Limit 5/20/50 → 75/155/211ms ⇒ **~4.2ms/行**（基线 ~0.4ms/行，**每行成本 ~10×**）
  - 按类型：Audio **12ms/条**、MusicAlbum **13ms/条**（带 ByName 字段）；Movie/Episode 1–2ms/条
  - PG 侧安静（水合期间单条 `SELECT A.id,…,A.data::text` 活动查询，无逐行 DB 风暴）⇒ 成本在服务器进程内（JSONB 反序列化 + DTO 映射 + ByName 解析）
  - 库条目 64→147（+46 Person、+11 Genre 等，A2 TMDb identify 与 LiveTv fixture 的产物）——条目增长 2.3× 解释不了 9×，每行成本上升是主因
- **嫌疑**（供 Developer）：水合路径 P7（PgItemRepository.cs:1030 逐行全量反序列化）在部署批后每行成本上升；与 ByName 404 批（f1a1ce90d7）的 DTO 解析路径交叉待查
- **负载测试的 21s 停顿/超时为并发污染**（复测时段 D2 的 S5 1M fixture 正在灌 PG）；PG 安静后干净复跑 P95 335ms、零错误、无停顿

### 7.3 负路径新用例（合法不存在 Guid/Name → 404）

新增 10 条（ByName 404 新语义 ×5 + GhostGuid ×5），**9/10 PASS**；唯一 FAIL：`GET /Items/{GhostId}/PlaybackInfo` → 400 "Parameter 'item'"（资源不存在应 404，T1 家族语义缺口，保留作防线）。负路径总计 33/34。

### 7.4 干净轮复测补充（16:52，PG 零活动、D2 灌库未开始）

- **回归确认（非污染）**：L1 P50 488ms@10rps（手动单发最佳口径 316ms vs 基线 40.8ms）——水合每行成本 ~10× 是真回归
- **21s 停顿定性**：连接层连锁——慢响应 × 持续负载 → urllib 每请求新建连接 → TIME_WAIT/临时端口耗尽 → TCP 连接超时（~21s 是 Windows SYN 重传窗口）；主因仍是每请求延迟
- **L2 级联污染**：load-test.py 的 L2 触发 /Library/Refresh 后，扫描+元数据刷新（TMDb/MBZ 网络 + T4.8 最多 ~9s 重试）**在测试结束后仍后台运行数分钟**，污染后续 L3/L4 与背靠背轮次（L4 的 500/5.2s 认证即此因）。**建议 load-test.py 把 L2 挪到最后或独立跑**
- Chief 归因修正：openmedia_scale 库尚不存在（D2 灌库未开始）；16:25 窗口的污染源是 D3 内存审计（8099 共享 PG，672 转码+10 扫描）；持续存在的停顿是服务器侧连锁

### 7.5 水合修复后正式数字（17:45，PID 22796，a05156ce6b 部署后，PG 零活动）

| 指标 | 基线（05:14） | 回归峰值 | **修复后** | 结论 |
|---|---|---|---|---|
| L1 用户分页 P50@10rps | 40.8ms | 488ms | **83ms** | 修复收回 ~83%（残余 2× 见下） |
| L1 用户分页 P95@10rps | 57ms | 3251ms | **109ms** | ✓ |
| L1b Ids P50/P95 | 340/657ms | — | **33.5/62.7ms** | 历史最佳 |
| L3 静态流 P95 | 12.7ms | — | 18ms | ✓ |
| L4 认证 P95 | 221ms | — | 79ms | ✓ |
| 单请求 PG 语句数（N+1 口径） | 未测 | **464** | **~201** | GetArtist TTL 缓存生效；剩余 N+1 见下 |
| 单请求延迟（Limit=30） | ~40ms | 215ms | **60-75ms** | ✓ |

**残余 N+1（每请求 ~201 条语句，给 Developer 下一轮批量化）**：count(DISTINCT) ×21、
string_agg(item_extradata) ×27、ancestor_ids ×18、guid 查找 ×18、series/parent 相关子查询 ×15+15、
水合 SELECT ×9、DISTINCT ON ×6、token ×10。批量化后 L1 预期回到 ~40ms 基线。
**50-100 RPS 档的 21s P99 为负载发生器连接层伪影**（urllib 每请求新建连接 + TIME_WAIT 耗尽），
10 RPS 档干净；后续换 keep-alive 会话池的发生器可消除。

### 7.6 S7 准备（10k 用户压测）

`tests/perf/s7-user-load.py` 已入库：U1 多用户认证阶梯（100/500/2000）+ U2 用户隔离列表查询。
前置：整份复制部署目录起 scale 实例 + `OPENMEDIA_PGCONN=...Database=openmedia_scale`（用户
scale-user-00001..10000，空密码，user_datas 1M 就绪）。跑法：`python tests/perf/s7-user-load.py 8098`。

### 7.7 终态（23:30，PID 25628，P7+count 完整批次，v2 keep-alive 发生器，安静窗口）

| 场景 | 二期基线 | 回归峰值 | **终态** | 备注 |
|---|---|---|---|---|
| L1 分页 P50@10rps | 40.8ms | 488ms | **29.3ms** | 低于基线，水合链路 363→29ms（**12.5×**） |
| L1 P95@10rps | 57ms | 3251ms | **40.6ms** | 零错误 |
| L1 P95@50rps | 163ms | 21017ms | **142.4ms** | 零错误，21s 伪影消失（keep-alive） |
| L1 P95@100rps | 188ms | 21042ms | **126.4ms** | 零错误 |
| L1b P50/P95 | 340/657ms | — | **30.6/49.8ms**（994 RPS） | 历史最佳 |
| L2 扫描并发 P95 | 167ms | — | 125.2ms | 零错误 |
| L3 静态流 P95 | 12.7ms | — | 9.0ms（1610 RPS） | 脚本 206 计数显示 bug，实际零错误 |
| L4 认证 P95 | 221ms | — | 154.5ms | 零错误 |
| 单请求暖延迟 | ~40ms | 215ms | **21ms**（P50，10 次） | |
| 单请求语句数 | 未测 | 464 行 | **~14 条**（6 视图文件夹水合+3 count+5 认证/token） | Developer 的 3=条目路径口径（其实例视图文件夹更少），分歧为范围非对错 |

**规模化轨道性能侧全部收口**。U1 高并发复跑待 S8 拉起 scale 实例（8098 已被 D2 收工带走）。
---

## 8. Release 口径复测（2026-10-20，Architect / Codex）

> 本章替代 §1-§7 作为**唯一可引用的口径**。历史 Debug 数字保留作审计轨迹，
> 但**不再作为任何容量承诺的依据**。完整决策论证见 `docs/adr/0017-perf-calibration-ci-docker.md`。

### 8.1 历史口径作废原因（三条，缺一不可）

| # | 问题 | 证据 | 后果 |
|---|---|---|---|
| 1 | 测于 Debug 构建 | §0 自证 `.NET 10 Debug 构建` | 无 ReadyToRun、分层编译未预热，JIT 成本全额计入请求延迟 |
| 2 | 库规模口径断裂 | §7 基于 147 条目；现在共享库 `openmedia` 仅 1 条目 | §7.2 已栽过：库 64→147（+2.3×）就让 L1 P50 40.8→363ms（9×），规模是主导变量 |
| 3 | `pg_stat_statements` 未启用 | §1 自证"阻塞"、§6 挂"待排期" | §1/§6 承诺的慢查询 Top-20 从未补做，缺 DB 侧取证 |

### 8.2 本轮已修的口径问题

- **端口漂移**：`tests/perf/` 下 6 个脚本硬编码 `15432` → 改为 `-PgPort` 参数（默认 `5432`），本机 PG 已在 5432
- **`pg_stat_statements` 已启用**：`postgresql.conf:814` 改 `shared_preload_libraries = 'pg_stat_statements'`，重启 `postgresql-x64-18`，`postgres` 与 `openmedia_perf` 两库 `CREATE EXTENSION`。Top-20 见 §8.5
- **树形 fixture**：`tests/perf/fixture-100k-tree.ps1` 根治 BR-45 祖先分布失真，fixture 尾部自带"每库子孙数"断言
- **独立对比库**：`tests/perf/setup-compare-db.ps1` 建 `openmedia_compare`（库名白名单硬断言，共享库零污染）

**PG 重启影响（用户要求记录）**：重启前确认 `pg_stat_activity` 无外部连接。
2026-10-20 10:53-10:54 首次重启后服务因 UTF-8 BOM 混入 conf 起不来（详见 ADR D3），
服务不可用窗口约 8 分钟（10:54-11:02），期间 `openmedia_perf`（195,251 条目）与
共享库 `openmedia`（1 条目）均无写入，数据无损。11:02 去 BOM 后恢复 Running。

### 8.3 Debug vs Release 同口径对比（471 条目树形库，**这是本节核心**）

口径：库 `openmedia_compare`（471 条目：150 电影 + 15 系列 + 45 季 + 225 集 + 5 库 + 30 人物 + 1 根），
同一用户 `ab-user-001`（Id `412dba26bbfe4509aa8bbb144cf4c7c8`），同一负载脚本
`tests/perf/load-ab.py`，每档 15s，各 2 轮干净数据（已剔除 warmup 轮）。
**唯一变量是构建配置。**

| 场景 | Debug 轮1 | Debug 轮2 | Release 轮1 | Release 轮2 | 结论 |
|---|---|---|---|---|---|
| L1 `/Users/{uid}/Items` 分页 P50 @10rps | 22.7ms | 23.1ms | 27.5ms | 26.4ms | Release 慢 3-4ms（噪声量级） |
| L1 同 P95 @10rps | 30.9ms | 115.4ms | 38.4ms | 51.1ms | Debug 轮2 的 115ms 是离群点；Release 更稳 |
| L1 分页 P50 @50rps | 82.1ms | 79.8ms | 79.3ms | 66.0ms | 持平，Release 略优 |
| L1 分页 P95 @50rps | 147.1ms | 163.1ms | 133.7ms | 177.5ms | 持平（方差重叠） |
| L1b `/Items?Recursive` P50 @50rps | 25.4ms | 32.1ms | 25.8ms | 25.0ms | 持平 |
| L1b P95 @50rps | 51.4ms | 71.1ms | 49.0ms | 52.7ms | Release 略优 |
| L3 静态流 P95 并发10 ⚠️ | 13.3ms | 28.0ms | 16.0ms | 18.2ms | **两侧均 100% 500**（对比库无真实视频文件），测的是错误路径开销，非流式吞吐 |
| L4 认证 P50 并发20 | 49.1ms | 49.2ms | 48.8ms | 44.5ms | 持平 |
| L4 认证 P95 并发20 | 243.2ms | 268.9ms | 154.6ms | 198.6ms | Release 优 20-45% |

**结论：471 条目规模下该服务不是 JIT/R2R 敏感的**——耗时主要花在等 PG 与 I/O，
不在 CPU 密集的 JIT 代码上。老 Debug 口径的"失真"主要体现在大规模 + 高并发档。

### 8.4 全量 195k 口径（Release，`openmedia_perf` 树形 fixture）

fixture 规模：195,251 条目（150 库 + 60,000 电影 + 3,000 系列 + 12,000 季 + 120,000 集 +
100 人物 + 1 根），`ancestor_ids` 447,000，每库子孙数均 1300（祖先分布真实），
`user_datas` 5,400,000，avg data blob 1,038B。灌库耗时 5.58 分钟。

| 场景 | 471 条目库（同上） | 195k 条目库 | 规模效应 |
|---|---|---|---|
| L1 `/Users/{uid}/Items` P50 @10rps | 26.4ms | **2,780.5ms** | **105×** |
| L1 同 P95 @10rps | 38.4ms | **3,339.8ms** | **87×** |
| L1 分页 P50 @50rps | 66.0ms | **7,089.1ms** | **107×** |
| L1 分页 P95 @50rps | 133.7ms | **9,524.4ms** | **71×** |
| L1b `/Items?Recursive` P50 @50rps | 25.0ms | 42.8ms | 1.7× |
| L1b P95 @50rps | 52.7ms | 85.6ms | 1.6× |
| L3 静态流 P95 并发10 ⚠️ | 16.0ms（500） | 24.1ms（500） | 均为错误路径 |
| L4 认证 P50 并发20 | 44.5ms | 63.8ms | 1.4× |
| L4 认证 P95 并发20 | 198.6ms | 280.1ms | 1.4× |

**这是本节最重要的发现**：`/Users/{uid}/Items?Recursive=true`（无类型过滤的用户列表）
随库规模**超线性劣化**（105×），而同 RPS 的 `/Items?Recursive&IncludeItemTypes=Movie` 只 1.7×。
差异指向"用户视图解析 + 无类型全库水合"这条路径，不是 PG 单条查询的问题
（PG 侧证据见 §8.5 第 1 名）。这条路径是下一个迭代的首要优化对象。

### 8.5 pg_stat_statements Top-20（欠账 4 天，已补）

取证环境：`openmedia_perf`（195,251 条目树形），Release 构建，跑 §8.4 那轮负载后取数。
总量口径：**74 条被跟踪语句 / 408,179 calls / 2,266,288.3ms 总执行时间**。
原始输出：`tests/perf/results/pgss-{total,top20-total,top20-mean,top20-calls,top1-explain}.out`。
复现脚本：`tests/perf/pg-stat-statements-top20.ps1`（含扩展前置断言）。

#### 8.5.1 Top-10 by `total_exec_time`（"谁最烧"）

| # | 查询 | calls | mean(ms) | total(ms) | 占比 |
|---|---|---|---|---|---|
| 1 | `SELECT count(DISTINCT A.presentation_unique_key) FROM media_items A` | 236 | **5,485.98** | **1,294,690.1** | **57.1%** |
| 2 | `INSERT INTO activity_log_types (name) VALUES ($1) ON CONFLICT ... RETURNING type_id` | 14,750 | 46.89 | 691,692.9 | 30.5% |
| 3 | `SELECT count(*) FROM media_items A WHERE A.type IN ($1)` | 16,948 | 11.42 | 193,612.0 | 8.5% |
| 4 | `SELECT A.id,A.guid,A.type,... (列表水合)` | 16,948 | 3.44 | 58,376.0 | 2.6% |
| 5 | `UPDATE local_users SET data = $1 WHERE id = $2` | 7,376 | 0.81 | 5,996.2 | 0.3% |
| 6 | `INSERT INTO activity_log (...) VALUES (...)` | 14,750 | 0.25 | 3,651.5 | 0.2% |
| 7 | `SELECT EXISTS (SELECT $1 FROM media_items WHERE presentation_unique_key IS NOT NULL GROUP BY ... HAVING count(*) > $2 ...)` | 10 | 344.65 | 3,446.5 | 0.2% |
| 8 | `SELECT id FROM local_users WHERE guid = $1` | 22,123 | 0.11 | 2,471.7 | 0.1% |
| 9 | `SELECT t.id, t.access_token, d.reported_device_id, ... (token 认证)` | 36,752 | 0.07 | 2,468.1 | 0.1% |
| 10 | `SELECT A.id,A.guid,...,A.data::text,... (全量水合)` | 19,612 | 0.12 | 2,421.3 | 0.1% |

#### 8.5.2 Top-5 by `mean_exec_time`（"单次最慢"）

| # | 查询 | calls | mean(ms) |
|---|---|---|---|
| 1 | `SELECT count(DISTINCT A.presentation_unique_key) FROM media_items A` | 236 | **5,485.98** |
| 2 | `SELECT EXISTS (SELECT $1 FROM media_items WHERE presentation_unique_key IS NOT NULL GROUP BY presentation_unique_key HAVING count(*) > $2 LIMIT $3)` | 10 | 344.65 |
| 3 | `INSERT INTO activity_log_types (name) VALUES ($1) ON CONFLICT ...` | 14,750 | 46.89 |
| 4 | `SELECT count(*) FROM media_items A WHERE A.type IN ($1)` | 16,948 | 11.42 |
| 5 | `INSERT INTO media_items (guid, type, type_name, data, parent_id, ...)` | 1 | 5.48 |

#### 8.5.3 Top-5 by `calls`（"调用最频"，抓 N+1 与连接层）

| # | 查询 | calls | total(ms) |
|---|---|---|---|
| 1 | `DISCARD ALL` | 125,430 | 1,947.8 |
| 2 | `SELECT t.id, t.access_token, d.reported_device_id, ... (token 认证)` | 36,752 | 2,468.1 |
| 3 | `SELECT count(*) FROM tokens t LEFT JOIN devices d ON d.id = t.device_id WHERE t.access_token = $1` | 36,752 | 869.8 |
| 4 | `BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED` | 22,129 | 96.8 |
| 5 | `SELECT id FROM local_users WHERE guid = $1` | 22,123 | 2,471.7 |

#### 8.5.4 头号慢查询执行计划（`pgss-top1-explain.out` 要点）

```
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF, TIMING OFF)
SELECT count(DISTINCT A.presentation_unique_key) FROM media_items A;

 Aggregate (actual rows=1.00 loops=1)
   Buffers: shared hit=14190 read=47339, temp read=406 written=407
   ->  Sort (actual rows=195252.00 loops=1)
         Sort Key: a.presentation_unique_key
         Sort Method: external merge  Disk: 3248kB     <-- 落盘排序
         ->  Append (actual rows=195252.00 loops=1)
               ->  Seq Scan on media_items_folders  a_1 (actual rows=150.00)
               ->  Seq Scan on media_items_movies   a_2 (actual rows=60000.00)
               ->  Seq Scan on media_items_series   a_3 (actual rows=3000.00)
               ->  Seq Scan on media_items_seasons  a_4 (actual rows=12000.00)
               ->  Seq Scan on media_items_episodes a_5 (actual rows=120000.00)
               ->  Seq Scan on media_items_audio    a_6 (actual rows=0.00)
               ->  Seq Scan on media_items_books    a_7 (actual rows=0.00)
               ->  Seq Scan on media_items_other    a_8 (actual rows=102.00)
 Planning Time: 12.301 ms
 Execution Time: 1174.540 ms
```

**8 个分区全 Seq Scan + 195,252 行 external merge 落盘排序**，只为了数一遍不同的
presentation_unique_key。这条查询 §2 的 EXPLAIN 矩阵（50 分支 / 10k 数据）没覆盖到，
量级远超当年任何分支（当年最慢 118.5ms，且后来证明是 fixture 假象）。

#### 8.5.5 新优化候选（超出本任务范围，登记待办）

| # | 候选 | 依据 | 备注 |
|---|---|---|---|
| N1 | `count(DISTINCT presentation_unique_key)` 消除全表扫+落盘排序 | §8.5.4 | 236 calls 吃掉 57% PG 时间。若能缓存或改用近似/索引，收益最大 |
| N2 | `activity_log_types` INSERT..ON CONFLICT 高频化 | §8.5.1 #2 | 14,750 calls / 691ms。看着像每条日志都重查一次类型表，应缓存 |
| N3 | `count(*) WHERE A.type IN` 调用 16,948 次 | §8.5.1 #3 | 与 §7.5 的 count-per-folder TTL 是同一族，TTL 可能没覆盖到这个调用点 |
| N4 | `DISCARD ALL` 125,430 次 | §8.5.3 #1 | 连接层开销，绝对时间小（1.9s）但次数异常，值得查是否有连接未复用 |

### 8.6 复现步骤（Release 口径）

```powershell
# 0. 前置（一次性）
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
# PG 18 已在 localhost:5432，pg_stat_statements 已启用

# 1. 小规模 A/B 对比库（471 条目，秒级）
powershell -ExecutionPolicy Bypass -File tests\perf\setup-compare-db.ps1
powershell -ExecutionPolicy Bypass -File tests\perf\run-ab-load.ps1 -Tag debug   -Seconds 15
powershell -ExecutionPolicy Bypass -File tests\perf\run-ab-load.ps1 -Tag release -Seconds 15

# 2. 全量口径库（195k 条目，灌库 ~5.6 分钟）
powershell -ExecutionPolicy Bypass -File tests\perf\setup-perf-db.ps1 -Fixture tree-100k
powershell -ExecutionPolicy Bypass -File tests\perf\run-ab-load.ps1 -Tag release -Db openmedia_perf -User perf-user-001 -Seconds 20

# 3. 慢查询 Top-20（三口径 + 头号 EXPLAIN）
powershell -ExecutionPolicy Bypass -File tests\perf\pg-stat-statements-top20.ps1
```

### 8.7 CI 与容器化（首次建立）

- `.github/workflows/ci.yml`：`build(Release, 含 VSTHRD 门禁)` → `test(unit + 3 PG 集成)` → `web(pnpm type-check + build-only)` → `perf-smoke(471 条目 fixture + 冒烟负载, **无硬阈值门禁**, P95 上 job summary)`
- `.github/workflows/ci.yml` 内独立 `docker` workflow：`workflow_dispatch` 手动触发，buildx amd64+arm64
- `Dockerfile`：四阶段（node:24 构建 web/dist → 锁版本静态 ffmpeg → dotnet sdk:10.0 publish → aspnet:10.0 运行时）
- ⚠️ **web/dist 托管缺口**：`MediaBrowser.Server.csproj` 无任何引用 `web/dist` 逻辑（已 grep），
  镜像里 web 只放备用路径 `/opt/openmedia/web` 并标注"待人接线"，根路径仍是旧 dashboard-ui。
  详见 ADR D5
- 本机无 docker，所以镜像构建 job 本机不可跑；CI 其余 3 个 job 本机可等价复现（清单见 ci.yml 末尾）