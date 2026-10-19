# Emby 迁移（SPEC/emby-migration）

> 决策依据：ADR-0009（元数据是 Canonical 种子不是废料）。
> 向导入口：管理后台"Emby 迁移向导"。

## 1. 输入方式 〔定稿〕

**仅支持上传 library.db 文件组**，配合**路径前缀映射**：
- 映射表：Emby 路径前缀 → Serata 路径前缀（多行，如
  `/mnt/media/movies → /data/movies`）；Item 按映射后的路径与本地扫描结果匹配。
- 不支持远程 API 模式（定稿，明确砍掉）。
- **WAL 教训（真实案例 2026-10-13，实证入册）**：Emby 运行中拷出的
  `library.db` 可能只是 4KB 空壳，全部数据在 `library.db-wal`（实测 4KB
  主文件 + 3.7MB WAL）。向导**必须接受三件套**（`library.db` + `-wal` +
  `-shm`）或要求用户先停止 Emby 服务；入库前一律执行
  `PRAGMA wal_checkpoint(TRUNCATE)` + `PRAGMA integrity_check`（合并后
  WAL/SHM 由 SQLite 自动清理）。禁止只收裸 .db。

## 2. 迁移范围 〔定稿〕

| 内容 | 处理 |
|---|---|
| 条目元数据（电影/剧集/季/集/合集/播放列表，视频类） | 按 Canonical 种子导入；provider_ids 带入（tmdb 等映射 id 保留，供后续聚合） |
| **锁定字段**（用户手动锁定/编辑过的字段） | 原样导入 `locked_fields`；**AI 仲裁与后续刮削永不覆盖**，只能填空 |
| UserData（进度/已看/收藏/点赞/播放历史） | 按路径匹配迁入对应用户 |
| 用户账号 | 用户名导入；**密码不可迁**（闭源加密格式），导入后状态=待重置，用户走邮箱安全码重置 |
| 图片 | **不随 library.db 迁移**（图片不在库里而在服务器数据目录/媒体目录）：媒体目录内的 sidecar 图（poster.jpg 等）与 NFO 直接复用；库里引用的在线图按 provider_ids 重新抓取 |
| 服务器配置/设备/会话/通知配置 | 不迁移 |

- 未匹配的条目（路径对不上映射）**不导入**，进迁移报告（数量+清单+原因）。
- `library.db` 无官方 schema 文档：解析器以真实 4.8 库文件为验收基准，
  版本差异记录在本册附录表。

## 5. 真实库文件取证（4.x schema，实证 2026-10-13）

用户真实服务器的 `library.db`（checkpoint 合并后 25 表）证实为 **4.7+ 现代
schema**，条目主表是 `MediaItems`（非旧版 BaseItems）。关键字段实证（完整
65 列以 fixture 为准）：

- 条目：`Id, guid, type(小写), ParentId, Path, Name, OriginalTitle, SortName,
  Overview, ProductionYear, PremiereDate, CommunityRating, OfficialRating,
  RunTimeTicks, IndexNumber, ParentIndexNumber, IndexNumberEnd, SeriesName,
  SeriesId, Tagline, ProviderIds, Images, Size, Container, Width, Height,
  DisplayOrder, DateCreated, DateLastSaved`
- **`LockedFields` 列真实存在**（另有条目级 `IsLocked`）——ADR-0009 锁定
  字段语义的库级依据，迁移原样带入。
- 分表：`ItemPeople2`（人员）/ `MediaStreams2`（轨道）/ `Chapters3`（章节）/
  `UserDatas`（用户数据，经 `UserDataKeys2` 关联）/ `NamedValues` +
  `NamedValueTypes`（类型等关联值）/ `fts_search9`（全文索引）。
- 迁移映射：`ProviderIds` 原样进 `provider_data` 并入 `item.provider_ids`；
  `MediaItems` 全列 → Canonical 种子；`UserDatas` 按路径匹配迁入。
- 实测样本（35 条目 / 41 轨道 / 27 人员 / 22 章节）存 `migration-test/`，
  作为解析器 fixture 基础。

## 3. 流程 〔定稿〕

1. 上传 library.db → 解析入库暂存（`migration_job` 状态机：uploaded →
   parsed → **dry-run** → importing → done/failed）。
2. **预检（dry-run）**：跑一遍路径匹配，出报告（可匹配/不可匹配/映射冲突），
   用户确认后才真正导入。禁止未预检直接导入。
3. 导入：事务分批；Canonical 写入 + locked_fields + UserData；导入完成生成
   最终报告（匹配数/跳过数/错误明细，页面展示+可下载 JSON）。
4. 导入后这些条目跳过立即重刮（元数据已是种子），仅对**缺字段**项走
   Provider/AI 补缺（ADR-0009：只填空不改写）。

## 4. 验收

- 造一份真实 4.8 Emby 的 `library.db` fixture（含电影/剧集/合集/进度/收藏/
  锁定字段）入库：断言匹配数、locked_fields 保护（仲裁测试用同一 fixture 证明
  不覆盖）、进度迁移正确。
- 预检报告与实际导入结果一致（两次运行同报告）。
