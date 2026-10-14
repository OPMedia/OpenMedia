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
- **未命中/低效**：`A.data::text LIKE`（无索引可用，blob 转换逐行）；`similarity_score` 相关子查询（planner 无法用索引消除逐行探查）；BR-45 的基数误估（统计信息对相关子查询无能为力，需改写 SQL）

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

## 5. 优化候选清单（三期实施，按优先级）

| # | 候选 | 依据 | 预估收益 | 风险 |
|---|---|---|---|---|
| P1 | /Items?Ids= 批量查询路径排查（L1b P95 657ms） | §3 | 4× | 低（纯查询层） |
| P2 | similarity_score 相关子查询改 JOIN/预计算 | BR-40 71.5ms@10k | Similar 端点 10× | 中（Developer 正在改该区域，需协调） |
| P3 | `A.data::text LIKE` 族改 jsonb 运算符或提取列 | BR-36/37 65/35ms@10k | VideoType/SeriesStatus 过滤 10× | 中（需数据迁移） |
| P4 | BR-45 AncestorWithPresentationUniqueKey 改 JOIN 写法消除基数误估 | BR-45 118.5ms@10k | 8× | 低 |
| P5 | DISTINCT ON 包装跳过条件（键唯一时退化为普通查询） | BR-03 125× | 列表页 10×+ | 中（语义等价性需 Reviewer 确认） |
| P6 | HasNoAudioTrackWithLanguage 相关子查询改 EXISTS JOIN | BR-34 19.3ms | 2× | 低 |
| P7 | 水合路径测量（PgItemRepository.cs:1030 逐行全量反序列化） | BR-02 0.12ms@10k（blob 1.5KB） | 10k 规模下非瓶颈；**100k+ 或大 blob 时复测** | — |

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
