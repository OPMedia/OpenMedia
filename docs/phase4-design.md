# OpenMedia 四期设计：矩阵收尾 + 性能落地 + 架构隐患处置

> 依据：docs/test-report.md §6（83 FAIL 分类）、docs/perf-baseline.md（P1–P7 优化候选）、docs/phase3-design.md §6（三期终态）、D3 事故报告（根 GUID 同构）、Developer-3 postscan 疑点
> 前置：三期 A/B/C 已落地、D 已否决；8096 跑全量最新构建（PID 26456）
> 代码证据核验日期：2026-10-15（Architect 实读 LibraryManager GUID 链 / postscan 注册链 / MusicBrainz 节流）

## 0. 目标与总验收

| 目标 | 内容 | 验收 |
|------|------|------|
| G1 矩阵收尾 | ⑤ fixture 32 点 + ④ 测试基建 12 点 + ③ 必填参数 6 点清零 | 矩阵 ≥ 90%（230 口径）；残差全部归类记账（B6 22 用户跳过 + 已知接受项）；test-report v3 |
| G2 性能落地 | P1–P7 优化实施（perf-baseline.md §5） | 每项前后对比数字 + k6 复跑 + 三套件回归不劣化 |
| G3 架构隐患 | 根 GUID 同构守卫、ByName 写副作用/缓存掩蔽/键分歧三根因修复、MBZ 503 重试 | D3 复现场景共享库零损失；ByName 读不建行；清库他实例免重启可见；跨实例同名 GUID 唯一；MBZ 间歇 503 不断链 |
| G4 口径 | 矩阵记账口径统一（用户决策跳过 vs 预期 404 vs 已知接受） | test-report v3 附残差分类表 |

---

## 1. 矩阵收尾轨道（⑤ 32 + ④ 12 + ③ 6）

### 1.1 ⑤ fixture 限制 32 点处置表

| 子类 | 数量 | 处置 | 状态 |
|------|------|------|------|
| 按名实体图片（Artists/Genres/Persons/Studios…/{Name}/Images） | 12 | fixture 补图（TMDb 链路已通可借力拉图入库） | Developer-2 进行中 |
| 用户无图 | 2 | 同上 | Developer-2 进行中 |
| InstantMix ×7 / Audio universal ×2 | 9 | **纯复测**：音频库就绪（9 Audio），test-plan 换真实 Audio Id 即转绿 | 待复测 |
| Shows/Seasons/Episodes（Movie 非 Series） | 2 | **纯复测**：剧集库就绪，换真实 Series Id | 待复测 |
| LiveTv 数据类（Lineups/LiveRecordings/Recordings/Groups） | ~4 | **纯复测**：B 轨道后 LiveTv 有数据 | 待复测 |
| web/ConfigurationPage | 1 | 维持 404（无插件=用户决策跳过，记账） | 记账 |
| 服务器图库 Images/General/Ratings/MediaInfo | 3 | branding 图 fixture（ProgramData 放图）或判定 404 合理记账 | 待定 |

### 1.2 ④ 测试基建 12 点

- PS 5.1 单元素数组解包 ×2：ConvertFrom-JsonSafe 用 `@()` 包裹（test-report §8.5 建议）
- 合成参数 404 ×4：重分类 SKIP-EXPECTED（合成 Id 本就查不到）
- {Id} 解析为 UserId ×3：harness 修正（/Users/{UserId}/Items/{Id} 的 {Id} 不应复用 UserId 解析）
- 计划缺参数 ×1：test-plan.json 补 `GET /Channels/{Id}/Items` 参数定义
- Views FormatException ×1：T1.5 已修 400，复测转绿
- POST 无 body ×1：AuthenticateByName 补 body 或重分类（场景 S2 已验证 200）

### 1.3 ③ 预期差异 6 点

UserId 必填 ×3、MediaSourceId 必填 ×2、path 必填 ×1——加参后 200 已验证，test-plan.json 补必填参数即转 PASS。

---

## 2. 性能优化轨道（P1–P7，Developer 进行中）

按 docs/perf-baseline.md §5 顺序实施，每项独立 commit + 前后对比：

| # | 项 | 预期收益 | 风险控制 |
|---|----|---------|---------|
| P1 | /Items?Ids= 批量路径（L1b P95 657ms，4× 慢） | 4× | 纯查询层，低风险 |
| P2 | similarity_score 子查询 JOIN 化 | Similar 10× | 与 Query.cs 改动协调 |
| P3 | `A.data::text LIKE` 族改 jsonb 运算符 | 过滤 10× | **需数据迁移**：先备份、可回滚 |
| P4 | AncestorWithPresentationUniqueKey JOIN | 8× | 低 |
| P5 | DISTINCT ON 跳过条件 | 列表页 10×+ | **语义等价性 Reviewer 确认后再合** |
| P6 | HasNoAudioTrackWithLanguage EXISTS | 2× | 低 |
| P7 | 水合复测 | 10k 非瓶颈 | 100k+ 规模复测留五期 |

验收：每项 perf-baseline 对应指标复测对比 + k6 L1 场景复跑 + 三套件回归不劣化。

---

## 3. 架构隐患轨道

### 3.1 跨实例根 GUID 同构（D3 事故根因）——评估结论：不改 GUID 公式，加扫描侧守卫

**机制**（LibraryManager.cs:515 实读）：`GetNewItemIdInternal` 对 ProgramData 前缀路径**剥离前缀**（老 Emby 的"portability"设计——迁移 ProgramData 时 GUID 保持稳定）→ 根 CollectionFolder 的 key 变相对路径（如 `root\OpenMediaTests`）→ **任何实例算出同一 GUID**。共享 DB 只有一套根行，虚拟文件夹目录却是各实例 ProgramData 私有——B 实例扫描时把 A 实例的库目录判"不存在"→级联清库（10-15 事故实证）。

**三选项**：

- **A 只靠纪律**（群规红线已立）：零成本，但软约束已实证会踩（D3）
- **B 改 GUID 公式**（加实例判别/不剥离）：破坏既有部署 GUID 稳定性（升级=全量重扫+重复行）、破坏 portability、需数据迁移——**为测试拓扑付生产代价，否决**
- **C 扫描侧守卫（选定）**：全量扫描时，DB 根行若不在**本实例 ProgramData 虚拟文件夹配置**内 → 跳过 + 醒目警告，不级联删除。语义："实例只管理自己知道的库"。用户删库走 API（RemoveVirtualFolder 已清 DB 行，D3 修复）；"手动删磁盘文件夹"场景降级为警告+条目保留

**验收**：8097 复现场景（全新 ProgramData + 添加库 + RefreshLibrary=true）→ 共享库 TotalRecordCount 不变；正常 API 删库流不受影响；幽灵库修复回归通过。

### 3.2 postscan ArtistsValidator 疑点（✅ 已结案：postscan 没坏，D3 只读诊断 2026-10-15）

**诊断结论**（证据链闭环，四选项之争作废）：

1. **ArtistsPostScanTask 每次全量扫描都跑**（PerformLibraryValidation → RunPostScanTasks 无条件执行）。"没日志"≠"没跑"：任务日志 Debug 级被过滤，艺人已存在且不脏时 RefreshMetadata 空转零输出
2. **清库后不重建的真根因是 LibraryItemsCache 掩蔽**：内存缓存无逐出、无跨实例失效，8097 清库后仍持陈旧艺人条目 → GetItemById 缓存命中 → 跳过 DB 重建；铁证=06:03 回写行 date_created 仍是 05:29（陈旧条目带原时间戳回写）
3. **新发现：ByName 读端点有写副作用**——GET /Artists/{Name}（Genres/Persons/Studios/MusicGenres 同族）对不存在的名字直接创建条目（DtoService 序列化带 Artists 的条目也会顺手建行）。矩阵每轮播种幽灵行（08:18 实证 35ms 内创建 "Movie1" 艺人）
4. **重复 GUID 机制实锤**：`EnableNormalizedItemByNameIds` 默认 false（ServerConfiguration.cs:24 无初始化），ServerConfigurationManager save 时才强制 true（:220）——8096（Norm=true 小写键）与 bin\Debug 实例（Norm=false 原大小写键）对同路径哈希出双 GUID

**修复批（Chief 已派 D3）**：① ByName 读端点去写副作用（不存在→404）；② postscan 日志升 Info；③ 幽灵数据清理（Movie1 幽灵行 ×4 + Person/Genre 重复对，删前零引用验证）。

**新增设计项（本节升级为"多实例隔离"族，与 §3.1 根 GUID 守卫同族）**：

- **T4.9 LibraryItemsCache 失效策略**：现状无逐出无失效，共享 DB 多实例拓扑下掩蔽外部写（清库/他实例写）。选项：(a) TTL 兜底（简单，秒级陈旧窗口可接受）；(b) PG NOTIFY/LISTEN 跨实例失效（彻底，加依赖）；(c) 扫描/写后主动逐出（覆盖主路径）。**建议 a+c 组合**：写路径（SaveItems/DeleteItem）后逐出本实例 + TTL 兜底他实例写；b 留五期。验收：8097 清库后 8096 不重启也能看到库空（当前必须重启）
- **T4.10 ByName 键大小写与配置解耦（✅ 定案见 §3.4）**：最小改法=ServerConfiguration 默认值改 true（一行，与 8096 现行一致）+ 重复行清理；彻底改法=删配置开关固定小写（forceCaseInsensitive 恒真）。**建议先最小改法**（前向兼容：小写键=现行 8096 行为，仅原大小写异常行重哈希），彻底解耦随五期配置清理。验收：跨实例同名同路径 GUID 唯一；重复对清零

### 3.4 T4.10 定案：ByName 键解耦 + 存量清理（四期收尾，2026-10-15）

**复发根因（Comedy 案例定案）**：TMDb 图片链刷新时对 Genre/Person 走建行路径，GUID 计算读当前配置——`EnableNormalizedItemByNameIds` **默认 false**（ServerConfiguration.cs:24 无初始化），仅 ServerConfigurationManager save 时强制 true（:220-223）。配置为 false 的窗口期（新实例首启/配置未保存）算出**原大小写键 GUID**，与 8096 既有小写键行 divergent → 同名双行 → 图片落在新建行、查询命中旧行 → "Comedy 坏路径/图片 404" 症状族复发。两处计算点同源：LibraryManager.cs:1002（GetNewItemIdInternal）与 PgItemRepository.cs:1705（GetItemByNameId，注释自述"复刻 LibraryManager 公式"）。

**定案两层**：

1. **四期落地（一行）**：`ServerConfiguration.EnableNormalizedItemByNameIds` 默认值改 `true`。效果：任何新实例/新 ProgramData 从第一秒即小写键口径，与 8096 已保存配置一致；save 强制迁移保留（幂等兜底）。前向兼容：小写键=现行行为，既有行 GUID 不变；仅 divergent 行下次建行时重哈希——正是清理对象。
2. **五期彻底解耦（登记不混入四期）**：删配置开关、两处计算点 forceCaseInsensitive 恒真——动 LibraryManager/PgData 双项目语义，随五期配置清理批。

**存量清理方案（DB 手术，D3 流程：查引用→迁移→删→复核）**：

- 检测：`SELECT type, name, COUNT(*) FROM media_items GROUP BY type, name HAVING COUNT(*) > 1`（限 ByName 类型：Genre/Person/Studio/MusicArtist/MusicGenre/GameGenre）→ 同名对清单
- 保留规则：**保留小写键 GUID 行**（8096 口径、承重图片 fixture 所在行）；divergent 行资产迁移——图片文件（metadata 目录合并到保留行）、user_data、item_extradata 引用
- 删除：零引用验证后删 divergent 行；验证=同名对清零、Comedy 图片点 200、TotalRecordCount 与基线一致；**脚本幂等可重跑，兼作回归工具**

**复发防线**：默认 true 后新实例不再产 divergent 行（配置文档标注"勿关"）；ByName 404 化已挡读路径建行；残留建行路径（postscan validator、DtoService 自愈链）走同一公式，配置统一后不再 divergent。

### 3.3 MusicBrainz 503 重试

**现状**（MusicBrainzAlbumProvider.cs 实读）：已有 1000ms 节流 + UA 修复（含空格 UA 触发 503 已修）；**无重试**——间歇 503 直接断艺人/专辑刷新链（D3 恢复期实证）。

**方案**：503/429 指数退避重试 ×3（1s/2s/4s，尊重 Retry-After 头）；仅 GET 元数据请求；三次失败跳过该 provider 本轮（不阻断扫描，下轮再试）。

**验收**：间歇 503 场景刷新成功率提升（日志统计 before/after）。

---

## 4. 任务清单

| 任务 | 内容 | 依赖 | 工作量 | 验收 |
|------|------|------|--------|------|
| T4.1 | ⑤ 图片 fixture（按名实体 ×12 + 用户 ×2） | — | 1d | 图片端点 200；D2 进行中 |
| T4.2 | ⑤ 复测类转绿（InstantMix/universal/Shows/LiveTv 换真实 Id） | 音频/剧集/LiveTv fixture（已就绪） | 0.5d | ~17 点转 PASS |
| T4.3 | ④ 测试基建修复 + ③ 必填参数入 plan | — | 1d | 解包误报清零；③ 6 点转 PASS |
| T4.4 | 矩阵全量复跑 + test-report v3（含残差分类表） | T4.1–T4.3 | 0.5d | 矩阵 ≥ 90% |
| T4.5 | P1–P7 性能优化 | — | 3d | Developer 进行中；每项对比+回归 |
| T4.6 | postscan 修复批（D3）：ByName 读端点 404 化 + 日志升 Info + 幽灵行清理 | D3 诊断（已完成） | 1d | §3.2 修复批三项；删行零引用验证 |
| T4.7 | 根 GUID 扫描侧守卫 | — | 1.5d | §3.1 验收（D3 复现场景零损失） |
| T4.8 | MBZ 503 重试 | — | 0.5d | §3.3 验收 |
| T4.9 | LibraryItemsCache 失效（写后逐出+TTL） | — | 1d | §3.2 新增项验收（8097 清库 8096 免重启可见） |
| T4.10 | ByName 键解耦（默认 true + 重复行清理）——**✅ 定案 §3.4**（Comedy 复发根因入档） | — | 0.5d | §3.4 验收（跨实例 GUID 唯一；同名对清零；Comedy 图片 200） |

**分工**：Developer T4.5（+已带 ProcessLock try/finally、MBZ UA 统一）；Developer-2 T4.1→T4.2；Developer-3 T4.6（诊断完接修复）；Tester T4.3/T4.4 + fixture `-force_key_frames`；T4.7/T4.8 给先空出的开发。

**总闸门**：三套件回归不劣化 + 矩阵 ≥ 90% + P1–P7 每项有对比数字 + D3 复现场景零损失。

---

## 5. 风险登记

| 风险 | 应对 |
|------|------|
| R1 P3 jsonb 迁移动数据 | 迁移前 DB 备份；脚本幂等可回滚；独立库先演练 |
| R2 P5 DISTINCT ON 语义等价性 | Reviewer 确认等价性证明后才合；不合则降级跳过 |
| R3 扫描守卫改变删除语义 | 验收必须覆盖：正常 API 删库、幽灵库修复、手动删文件夹三流 |
| R4 矩阵复跑暴露新 bug | 登记 fail-classified 新分类，不阻断四期 |
| R5 postscan 诊断证据不足 | 已结案：D3 诊断证据链闭环（§3.2） |
| R6 ⑤ 复测撞出音频/剧集新 bug | 同 R4 口径（D2 已撞出 3 个用户作用域 500，归 D2 修） |
| R7 ByName 404 化改变读语义 | 老客户端依赖"读即建"的路径需排查（web UI 按名导航）；负路径用例先行 |
| R8 幽灵行/重复对删行误伤 | 删前零引用验证（D3 流程）；备份先行 |
| R9 缓存写后逐出影响吞吐 | 逐出只做 key 级不做全量；k6 L1 复跑对比 |

---

## 6. 五期展望（本期不做，仅登记方向）

- **规模化与内存（五期核心，已立设计）**：用户点名硬约束 100 万条/1 万用户——**docs/scale-design.md**（泄漏三件套方法论、八项嫌疑清单、1M fixture 真实祖先分布、10k 用户三场景压测、S1–S8 任务）；T4.9 缓存失效并入 S2，P7 水合升必做
- **UI 现代化立项**（D 否决后的正路）：改造 3.5.3 皮肤 or 自研前端——单独立项出设计
- **插件系统**：用户已表态"先逆向 Emby 再议"，启动时另立设计
- **100k+ 规模性能复测**（并入 scale-design S5/S6）+ 真实 IPTV 源压测（XMLTV 大文件）
- **B6 插件端点 25 个**：随插件系统决策联动
