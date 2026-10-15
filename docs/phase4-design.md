# OpenMedia 四期设计：矩阵收尾 + 性能落地 + 架构隐患处置

> 依据：docs/test-report.md §6（83 FAIL 分类）、docs/perf-baseline.md（P1–P7 优化候选）、docs/phase3-design.md §6（三期终态）、D3 事故报告（根 GUID 同构）、Developer-3 postscan 疑点
> 前置：三期 A/B/C 已落地、D 已否决；8096 跑全量最新构建（PID 26456）
> 代码证据核验日期：2026-10-15（Architect 实读 LibraryManager GUID 链 / postscan 注册链 / MusicBrainz 节流）

## 0. 目标与总验收

| 目标 | 内容 | 验收 |
|------|------|------|
| G1 矩阵收尾 | ⑤ fixture 32 点 + ④ 测试基建 12 点 + ③ 必填参数 6 点清零 | 矩阵 ≥ 90%（230 口径）；残差全部归类记账（B6 22 用户跳过 + 已知接受项）；test-report v3 |
| G2 性能落地 | P1–P7 优化实施（perf-baseline.md §5） | 每项前后对比数字 + k6 复跑 + 三套件回归不劣化 |
| G3 架构隐患 | 根 GUID 同构处置、postscan 修复、MBZ 503 重试 | D3 复现场景共享库零损失；清库后普通重扫自动重建艺人；MBZ 间歇 503 不再断刷新链 |
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

### 3.2 postscan ArtistsValidator 疑点

**机制链**（实读）：ApplicationHost `GetExports<ILibraryPostScanTask>`（:1410）→ LibraryManager `RunPostScanTasks`（:1107，全量扫描尾部）→ ArtistsPostScanTask → ArtistsValidator.Run：`GetAllArtistNames()` → 逐名 `GetArtist(name)`（**缺失即创建**）→ 刷元数据 → 清死艺人。

已知：`e4215dcb4c` 修过 postscan 聚合 SQL（PG 42883）——postscan 至少跑过且有 SQL 翻译 bug 史。疑点（D3 只读诊断进行中）：05:29 创建过艺人、05:32 起四次扫描零动作且无报错。

**修复方向**（按 D3 诊断证据选一）：①DI 导出未注册 → 注册修复；②GetAllArtistNames 查询返空（PG 翻译 bug）→ 查询修复；③异常被吞 → 日志可见性；④任务跑了但 GetArtist 创建路径静默失败。

**验收**：清库后**普通重扫**（非 ReplaceAllMetadata）自动重建 ByName 艺人；扫描日志可见 postscan 任务执行痕迹。

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
| T4.6 | postscan ArtistsValidator 修复 | D3 诊断证据 | 1d | §3.2 验收 |
| T4.7 | 根 GUID 扫描侧守卫 | — | 1.5d | §3.1 验收（D3 复现场景零损失） |
| T4.8 | MBZ 503 重试 | — | 0.5d | §3.3 验收 |

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
| R5 postscan 诊断证据不足 | 先诊断后修（D3 只读任务）；证据不足则加 Debug 日志再复现 |
| R6 ⑤ 复测撞出音频/剧集新 bug | 同 R4 口径 |

---

## 6. 五期展望（本期不做，仅登记方向）

- **UI 现代化立项**（D 否决后的正路）：改造 3.5.3 皮肤 or 自研前端——单独立项出设计
- **插件系统**：用户已表态"先逆向 Emby 再议"，启动时另立设计
- **100k+ 规模性能复测**（P7 水合嫌疑）+ 真实 IPTV 源压测（XMLTV 大文件）
- **B6 插件端点 25 个**：随插件系统决策联动
