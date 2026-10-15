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

> **四期实施终态（2026-10-15，Developer）**：P1/P2/P3/P6 完成（commit `b1fe3d5e40`/`d6967b7a18`/`3e9ba4f0cb`/`081f78d522`），P4 跳过（结论更正），P5 留下轮（语义等价性需 Reviewer 参与），P7 不做（10k 非瓶颈）。P1 有一处 Reviewer 阻塞项（Folder.cs:553 守卫）修复中。

| # | 候选 | 依据 | 预估收益 | 风险 | **四期终态** |
|---|---|---|---|---|---|
| P1 | /Items?Ids= 批量查询路径排查（L1b P95 657ms） | §3 | 4× | 低（纯查询层） | ✅ 匿名 Recursive 250→35ms（**6-7×**，BoxSet 折叠劫持根因+库树限定）；守卫修复中 |
| P2 | similarity_score 相关子查询改 JOIN/预计算 | BR-40 71.5ms@10k | Similar 端点 10× | 中（Developer 正在改该区域，需协调） | ✅ LEFT JOIN 聚合 87.5→10.4ms（**8.4×**），新旧 top-10 score 逐行一致 |
| P3 | `A.data::text LIKE` 族改 jsonb 运算符或提取列 | BR-36/37 65/35ms@10k | VideoType/SeriesStatus 过滤 10× | 中（需数据迁移） | ✅ jsonb 运算符 40.9→10.1ms（**4×**），真实库 VideoType 8=8 一致（未做数据迁移，纯查询改写） |
| P4 | BR-45 AncestorWithPresentationUniqueKey 改 JOIN 写法消除基数误估 | BR-45 118.5ms@10k | 8× | 低 | ❌ **跳过+结论更正：合成数据失真**——perf 库全部条目共享一个 CollectionFolder 祖先 → IN 列表 10k 行放大逐行回探；真实场景 PUK 匹配单 Series、IN 列表几十行 <1ms 非瓶颈；JOIN 改写实测无改善（128ms）。**基线方法论教训：合成 fixture 的祖先分布必须贴近真实库形态，否则 EXPLAIN 结论失真** |
| P5 | DISTINCT ON 包装跳过条件（键唯一时退化为普通查询） | BR-03 125× | 列表页 10×+ | 中（语义等价性需 Reviewer 确认） | ⏸ 留下轮：真实库规模下收益未显现 + 等价性确认流程未走 |
| P6 | HasNoAudioTrackWithLanguage 相关子查询改 EXISTS JOIN | BR-34 19.3ms | 2× | 低 | ✅ 五分支 EXISTS 28.8→16.5ms（**1.7×**），502=502 一致 |
| P7 | 水合路径测量（PgItemRepository.cs:1030 逐行全量反序列化） | BR-02 0.12ms@10k（blob 1.5KB） | 10k 规模下非瓶颈；**100k+ 或大 blob 时复测** | — | ⏸ 100k+ 复测留五期 |

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
