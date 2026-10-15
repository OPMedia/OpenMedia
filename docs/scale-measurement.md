# S6：1M 规模化测量报告（EXPLAIN 矩阵 + 水合 + 缓存）

> 测量日期：2026-10-15　|　库：openmedia_scale（S5 生成，PG 15432）
> 条目：**1,101,001 行**（媒体 1,000,000 = Episode 500k / Movie 200k / Audio 150k / Season 50k / MusicAlbum 50k / Series 20k / Video 20k / Book 5k / Photo 5k；+ Person 100k + 库 1000 + 根 1）
> 注：此前口头汇报的"1,001,001"为加总笔误（漏加 Person 100k），媒体条目数 1M 准确。
> 关联表：keys 880k / item_people 2.25M / extradata 2.25M / ancestor_ids 2.2M / streams 1.59M / users 10k / user_datas 1M

## 1. EXPLAIN 矩阵（60 分支，tests/perf/explain-matrix-1m.sql，结果 explain-matrix-1m.out）

### 1.1 头号问题：用户作用域 DISTINCT ON（真实 UI 路径）

| 形态 | 耗时 | 说明 |
|---|---|---|
| 用户全库（TopParentIds=1000 库）+ DISTINCT ON + type 过滤 + LIMIT 50 | **6,936 ms** | InternalItemsQuery 默认 GroupByPresentationUniqueKey=true 的真实形态 |
| 用户单库 + DISTINCT ON + LIMIT 50 | 4.2 ms | 单库 800 条，排序量小 |
| 匿名（无 DISTINCT ON）+ LIMIT 50 | 0.5 ms | BRANCH-01 |

**根因**：DISTINCT ON (presentation_unique_key) 要求先按 (PUK, sort_name) 排序全部匹配行（20 万电影全排）再取 LIMIT——排序量与库规模线性。**这是 1M 规模列表页的第一阻断项**（用户浏览全库视图时每页 ~7s）。

### 1.2 其余 >1s 分支（10k 矩阵未显形或已修复形态的 1M 表现）

| 分支 | 1M 耗时 | 10k 基线 | 备注 |
|---|---|---|---|
| BRANCH-03 DISTINCT ON 包装 | 5,434 ms | ~40ms | 同上，P5 项 |
| BRANCH-10 IsMovie/IsFolder/IsVirtualItem | 5,076 ms | — | 三布尔组合无索引，全分区扫描 |
| BRANCH-09 MinIndexNumber/ParentIndexNumber | 4,065 ms | — | 剧集分集过滤 |
| BRANCH-04 COUNT-DISTINCT（TotalRecordCount） | 3,589 ms | — | PUK 去重计数（注：实测 API 路径的 count 走了快形态，32ms 内含 count——见 §2） |
| BRANCH-40 similarity_score | 1,489 ms | 71ms | P2 改写后仍 1.5s（Similar 端点） |
| BRANCH-34 HasNoAudioTrackWithLanguage | 1,306 ms | — | 相关标量子查询 |

### 1.3 快分支（索引命中，1M 无劣化）

ParentId 1.6ms / AncestorId 8.2ms / TopParentId 0.29ms / user_datas join 0.36ms / GenreIds 10.7ms / People 3.7ms / similarity JOIN 形态 25ms（P2 改写收益保持）。

## 2. 服务器侧水合测量（8098 实例 = 最新构建 + scale 库，匿名路径）

| 查询 | 冷 | 暖均值 | DB 侧（EXPLAIN） | 服务器侧（暖-DB） | 每条 |
|---|---|---|---|---|---|
| Movie 50 | 37.6ms | 32.4ms | 0.55ms | ~32ms | 0.64ms |
| Movie 100 | 30.3ms | 38.0ms | ~0.6ms | ~37ms | 0.37ms |
| Episode 50 | 124.5ms | 60.4ms | 0.24ms | ~60ms | 1.2ms |
| Episode 100 | 109.2ms | 156.5ms | ~0.3ms | ~156ms | 1.56ms |
| Audio 50（艺人缓存暖） | 219.8ms | 24.1ms | ~0.3ms | ~24ms | 0.48ms |
| Audio 50（新排序=艺人缓存冷） | 968ms | 23.6ms | — | N+1 显形 | — |
| 搜索 movie-123 | 1,225ms | 1,164ms | 679ms | ~485ms | — |
| Genres=Genre1234 过滤 | 3,075ms | 3,877ms | 612ms | ~3,265ms | — |

**水合结论（P7 决策数据）**：
- 列表页成本 **~100% 在服务器侧**（DB 取数 <1ms）——水合（JSONB→BaseItem）+ DTO 序列化 + GetArtist 等 ByName 补查
- 每条 0.37-1.56ms（Episode 最重：多字段 + series/season 关联）
- **绝对值判断**：Movie/Audio 列表页 24-38ms 可接受；Episode 100 条 156ms 边缘；**P7（partial hydration）不是 1M 的第一阻断项**——查询形态问题（§1.1/§2 的 Genres/搜索）优先级更高
- GetArtist TTL 缓存（a05156ce6b）实测有效：暖 24ms，但冷缓存/新排序集仍 N+1（968ms/50 条）——艺人基数大时缓存命中率是关键

**Genres 过滤 3.9s 甄别**：DB 侧仅 612ms（BRANCH-25 相关子查询），服务器侧 ~3.3s——TotalRecordCount 的 COUNT 查询带 Genres 相关子查询无 LIMIT 全量执行是主嫌疑（列表查询本身 LIMIT 50 快，count 慢）。

## 3. 缓存效果（冷 vs 暖差值）

- LibraryItemsCache：Episode 50 冷 124.5→暖 60.4ms（条目对象缓存命中省一半）
- GetArtist TTL：Audio 50 冷 220→暖 24ms；换排序集（新艺人名）冷 968ms
- 结论：两级缓存都在干活，但都是**首次/换集冷启动贵**——S1 的 LRU 上限不会恶化这些数字

## 4. 测量中发现的 fixture 缺口（已修）

1. **top_parent_id 全 NULL**：生成器直插不经过服务器保存路径，TopParentIds 过滤零匹配（BRANCH-51/52 假快 0.1ms）。已回填（UPDATE join ancestor_ids，100 万行）+ 生成器补第 16 步。修正后用户全库形态实测 6.9s（§1.1）。
2. **用户 Policy 缺失**：fixture 的 local_users data 无 Policy 字段 → 用户视图为空。已 UPDATE 10k 用户补 EnableAllFolders。
3. **用户视图管道**：root\default 虚拟文件夹结构未建（1000 库的 .mblink），用户作用域端到端未走通——DB 侧形态已用 EXPLAIN 直接测量（§1.1），服务器侧水合用匿名路径等价覆盖（同一水合代码）。S7 用户压测前需补虚拟文件夹结构。

## 5. P7 实施决策（S6 验收项）

| 优先级 | 项 | 依据 |
|---|---|---|
| **P0** | DISTINCT ON 用户全库路径（P5 等价性改写或条件跳过） | 6.9s/页，1M 列表页不可用 |
| **P0** | Genres/搜索的 COUNT 放大 | 3.9s/1.2s |
| P1 | IsMovie/MinIndexNumber 布尔组合索引 | 4-5s（低频路径） |
| **P2** | P7 partial hydration | 列表页 24-156ms 可接受，Episode 100 边缘——在 P0 修完后做收益 ~2-4× |
| 不做 | similarity 1.5s | Similar 端点低频，P2 改写已 8×，1M 下可容忍 |

## 6. 环境备注

- 8098 实例已停、部署副本已删；**D:\scale\ 1000 库目录保留**（含 .collection 文件，服务器侧复测需要）
- scale 库当前状态 = 生成器产物 + top_parent_id 回填 + 10k 用户 Policy + 1 行 Playlists（实例注册）；复跑 setup-scale-db.ps1 可完全重建（6.7 分钟）
- EXPLAIN 矩阵/结果文件：tests/perf/explain-matrix-1m.sql + results/explain-matrix-1m.out
