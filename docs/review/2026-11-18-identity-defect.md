# Forensic review — 2026-11-18 — media_items identity / GUID-type defect claim

Scope: read-only. Emby 3.5.3 fork on .NET 10 + PostgreSQL. No source modified.

## Verdict

**PARTIALLY CONFIRMED — storage-level hazard real; end-to-end claim refuted for the normal save path.**

Confirmed: `media_items` is LIST-partitioned by `type` with composite PK `(id, type)`; the app-created unique index and the UPSERT conflict target are `(guid, type)`, not `guid`; `RetrieveItemAsync` has no `ORDER BY`; no FK protects identity. Two rows with one GUID are therefore legal at the DB level, and `LIMIT 1` may return either.

Refuted: "a type change inserts a second row instead of updating" is **not** what the repository's write path does. `SaveItemsAsync` first deletes same-GUID/different-type rows (`PgItemRepository.cs:472-485`) and `SaveItem` delegates to it (`:276`). A type change for a stable GUID is also **rare / effectively unreachable via the scanner**, because scanner-generated GUIDs hash `type.FullName` (`LibraryManager.cs:531-533`, `ResolverHelper.cs:40,73`). The defect is latent: it needs an out-of-band writer, externally supplied GUID, or a race, before readers can pick an arbitrary row.

Severity: **Low (latent)** in the supported write path; **High impact if triggered** (wrong item hydration, orphaned child rows keyed by stale internal id).

## 1. Exact DDL / conflict target (verbatim)

| # | File:line | Quote |
|---|---|---|
| 1a | `emby_pgsql_dump_schema_optimized.sql:247-252` | `PRIMARY KEY (id, type),`<br>`-- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer` (×3, lines 248-250)<br>`FOREIGN KEY (user_data_key_id)  REFERENCES user_data_keys (id)`<br>`) PARTITION BY LIST (type);` |
| 1b | `emby_pgsql_dump_schema_optimized.sql:253-261` | `CREATE TABLE media_items_movies    PARTITION OF media_items FOR VALUES IN (2);`<br>`CREATE TABLE media_items_series    PARTITION OF media_items FOR VALUES IN (3);`<br>`CREATE TABLE media_items_other     PARTITION OF media_items DEFAULT;` |
| 1c | `src/Emby.Server.PgData/PgSchemaInitializer.cs:159-160` | `// §5.2-3: guid 唯一索引（分区表唯一索引必须含分区键 type；guid 为最左前缀可服务 GUID 查找）`<br>`Execute(connection, "CREATE UNIQUE INDEX IF NOT EXISTS idx_media_items_guid ON media_items (guid, type)");` |
| 1d | `src/Emby.Server.PgData/PgItemRepository.cs:924` | `ON CONFLICT (guid, type) DO UPDATE SET` |

Note: `guid UUID` is **nullable** (`emby_pgsql_dump_schema_optimized.sql:181`); PostgreSQL unique indexes allow multiple NULLs, a second independent reason GUID is not a database-level identity.

## 2. Does DO UPDATE set `type`? — No; different type does not conflict

`PgItemRepository.cs:924-962` SET list begins `type_name = EXCLUDED.type_name, data = EXCLUDED.data, ...` and ends `date_last_media_added = EXCLUDED.date_last_media_added`. There is **no `type = EXCLUDED.type`** anywhere in the list (grep for `type = EXCLUDED.type` in the file: no match). `type` is also not assignable to a conflicting row in a partitioned table: it is the partition key and part of the conflict target.

Consequence in isolation: `ON CONFLICT (guid, type)` matches only a row with the **same guid and same type**. A same-guid/different-type arrival does not conflict and would be an INSERT — creating the second row the claim describes. **But the repository pre-empts this**:

`PgItemRepository.cs:472-485` (verbatim):
```
                // ---- 1. 清理同 guid 异 type 的历史行（一次集合删除）----
                var guids = rows.Select(r => r.Guid).ToArray();
                var types = rows.Select(r => r.TypeCode).ToArray();
                ...
                    @"DELETE FROM media_items t
                      WHERE t.guid = ANY(@guids)
                        AND EXISTS (
                            SELECT 1 FROM UNNEST(@guids, @types) AS x(g, ty)
                            WHERE x.g = t.guid AND x.ty <> t.type)",
```
and `PgItemRepository.cs:330`: `///  1. 删除同 guid 异 type 的历史行（罕见类型变更路径）；`

So in the supported path the old-type row is deleted **before** the upsert (`:491`), then the cache is refreshed from `RETURNING guid, id` (`:494`). Answer: **normally no second row**; the insert-without-conflict behavior only materializes if `UpsertMediaItemsBatchAsync` (`:732`) is called outside `UpsertItemsChunkAsync`, or by an external SQL writer. Repo-wide grep finds its only caller at `:491`.

## 3. Is a CLR-type change for a stable GUID reachable? — **rare (scanner-impossible, out-of-band possible)**

| File:line | Evidence |
|---|---|
| `PgItemRepository.cs:595,607-608` | `var type = item.GetType();` → `TypeCode = GetTypeCode(type), TypeName = type.FullName` |
| `PgItemRepository.cs:148-195` | `GetTypeCode(Type type)` maps `type.FullName` → 0..31, unknown → `TypeCodeUnknown = 99` (DEFAULT partition). CLR type is the sole source of the partition key. |
| `LibraryManager.cs:531-533` | `key = type.FullName + key;` / `return key.GetMD5();` — the type name is **hashed into the GUID**; same path + different CLR type ⇒ different GUID. |
| `ResolverHelper.cs:40,73` | `item.Id = libraryManager.GetNewItemId(item.Path, item.GetType());` — applied unconditionally by resolver initialization. |
| `LibraryManager.cs:470-493,203-204` | Resolvers are ordered by `Priority`; `ResolveItem` picks the first non-null `ResolvePath(args)`. Class changes (`Movie` vs `Folder`, `Series`, etc.) therefore change both GUID and type together. |
| `LibraryManager.cs`, resolvers dir | `ChangeType` and any `.Id = existing.Id` / ID-preservation assignment: **no matches** (grepped both). |

Verdict: **rare**, not routine. Reachable only via imported/plugin-supplied stable GUIDs, direct SQL, or a GUID reused after a type change; the ordinary library scanner cannot produce "same GUID, new CLR type".

## 4. `RetrieveItemAsync` with two rows — arbitrary row, no ordering

`PgItemRepository.cs:1239` (verbatim):
```
                BuildItemSelectText(null) + " FROM media_items A WHERE A.guid = @guid LIMIT 1",
```
`BuildItemSelectText` (`:1252+`) is a column list only — no `ORDER BY` is present in this statement.

PostgreSQL behavior: the unique index `(guid, type)` is created on the partitioned parent and propagated to each partition, so `guid` is the **leading** column of a per-partition index; each partition can return at most one matching row. With a guid-only predicate, no partition pruning occurs: the planner does an `Append`/`Merge Append` over all partitions, each doing an index scan, and `LIMIT 1` stops at the **first row delivered by the first partition that produces one**. Rows are "adjacent" inside each partition's own index but **scattered across independent per-partition indexes**; which one wins is not specified by SQL and can change with planner/partition order/parallelism. No stable or "newest wins" semantics.

Same pattern in sibling read paths: `PgItemRepository.ChaptersStreams.cs:77,105,245` and `PgItemRepository.People.cs:175,184` use `(SELECT id FROM media_items WHERE guid = @guid LIMIT 1)` — also unordered.

## 5. `_guidToIdMap` and stale ids

| File:line | What happens |
|---|---|
| `PgItemRepository.cs:76,86-93` | `ConcurrentDictionary<Guid,long> _guidToIdMap`; `CacheGuidId` writes `_guidToIdMap[guid] = id`, clearing wholesale above 200 000 entries; `Initialize` clears it at `:128`. |
| `PgItemRepository.cs:229-259` | `GetInternalIdAsync` returns cached id **without any DB validation** at `:240-243`; only on a miss does it run `SELECT id FROM media_items WHERE guid = @guid LIMIT 1` (`:248`, again unordered) and cache at `:258`. |
| `PgItemRepository.cs:494` | `SaveItemsAsync` overwrites the cache with `insertedIds[i]` returned by the upsert — a stale mapping is self-healed on the normal type-change path. |
| `PgItemRepository.cs:478-485` | Old-type row is deleted first; `DeleteItem` also evicts by GUID at `:1216`. |
| `PgItemRepository.cs:1091-1099` | `BackfillReferencesAsync` does **not** read the cache. It writes `parent_id = ... (SELECT p.id FROM media_items p WHERE p.guid = u.pg LIMIT 1)` and `series_id = ... (SELECT s.id FROM media_items s WHERE s.guid = u.sg LIMIT 1)`, with `WHERE m.guid = u.g`. With duplicate GUID rows, the arbitrary parent id is written into **all** rows sharing that guid (no type predicate). |
| `PgItemRepository.ChaptersStreams.cs:173,179,184-212` | Cached id consumed: `var internalId = await GetInternalIdAsync(...)`, then `DELETE FROM chapters WHERE item_id = @item_id` and `INSERT INTO chapters (item_id, ...) SELECT @item_id, ...`. |
| `PgItemRepository.ChaptersStreams.cs:322,328,333-432` | Same for `media_streams`. |
| `PgItemRepository.People.cs:289,295,300-359` | Same for `item_people`. |

Net: a surviving stale mapping writes chapter/stream/people rows against the **old internal id** (now possibly deleted) — orphaned child rows. `BackfillReferencesAsync` does not consume the cache, but is independently guid-only and therefore ambiguous with duplicates.

## 6. Foreign keys — none protect identity

`emby_pgsql_dump_schema_optimized.sql:248-250` (quoted in §1) is the in-table record. Same comment repeats at lines 315, 316, 346, 410, 447, 448, 478, 479, 499, 513, 529, 530, 588, 609, 713; mirrored in `deploy/schema-deploy.sql:258-260` et seq. `src/Emby.Server.PgData/PgSchemaInitializer.cs:15-18` states:
```
    ///    文件中曾存在的非法单列外键 REFERENCES media_items (id, type)（引用列数与复合主键不匹配，
    ///    PostgreSQL 报 "number of referencing and referenced columns for foreign key disagree"）
    ///    已直接在 SQL 文件内移除；引用完整性由仓储层维护（与旧 SQLite 行为一致，
    ///    DeleteItem 显式删除子行）。
```
Grep for `REFERENCES media_items` in the dump returns **no matches**: no FK constrains any `item_id` to `media_items`. Referential integrity is application-only; cascades cannot clean up duplicate/orphan rows.

## Minimal repro test sketch (not executed)

```sql
-- 1) Create two legal rows for one guid (no constraint violation):
INSERT INTO media_items (guid, type, name) VALUES ('11111111-1111-1111-1111-111111111111', 2, 'movie') RETURNING id;
INSERT INTO media_items (guid, type, name) VALUES ('11111111-1111-1111-1111-111111111111', 3, 'series') RETURNING id;
SELECT count(*) FROM media_items WHERE guid = '11111111-1111-1111-1111-111111111111'; -- expect 2

-- 2) Prove ON CONFLICT target is type-scoped:
INSERT INTO media_items (guid, type, name) VALUES ('11111111-1111-1111-1111-111111111111', 2, 'movie2')
  ON CONFLICT (guid, type) DO UPDATE SET name = EXCLUDED.name RETURNING id; -- updates type 2 only
-- a guid with type 4 would INSERT a third row; the SET list has no `type = ...`

-- 3) Prove arbitrary read:
EXPLAIN (ANALYZE, BUFFERS) SELECT id, type FROM media_items WHERE guid = '11111111-1111-1111-1111-111111111111' LIMIT 1;
-- expect Append over partitions with per-partition Index Scan using idx_media_items_guid; no Sort
SELECT type FROM media_items WHERE guid = '11111111-1111-1111-1111-111111111111' LIMIT 1;

-- 4) App-level negation: SaveItemsAsync(item guid=..., TypeCode=2) executes the DELETE at
--    PgItemRepository.cs:478-485 before the upsert -> count returns to 1. Assert this in an integration test.
-- 5) Cache test: seed _guidToIdMap with old id, call SaveChaptersAsync/SaveMediaStreamsAsync/SavePeopleAsync,
--    assert child rows reference the new id (or assert the map was refreshed at :494 first).
```

## Checked and cleared

- **PK must include partition key** — yes; `(id, type)` is mandatory under `PARTITION BY LIST (type)`, not an oversight in the PK itself.
- **`idx_media_items_guid` exists and `guid` is its leftmost prefix** — yes (`PgSchemaInitializer.cs:160`); GUID-only lookups are indexed per partition.
- **`SaveItem` path** — delegates to `SaveItemsAsync` (`PgItemRepository.cs:276`), so the cleanup DELETE covers single-item saves too.
- **Only caller of `UpsertMediaItemsBatchAsync`** — `PgItemRepository.cs:491`, inside the cleanup transaction; no unguarded direct writer found in the repo.
- **FKs to `media_items`** — none in dump or initializer; FK comments are accurate, not stale.
- **Scanner type-change reachability** — GUID generation includes `type.FullName`; no `ChangeType` or ID-preservation path found in `LibraryManager.cs` or `Library/Resolvers/`.
- **Concurrent transaction lock order** — `SaveItemsAsync` sorts by GUID (`PgItemRepository.cs:347`) to avoid deadlocks; not part of this defect.

---

## 附：实测复核（2026-11-18 17:10，不再是静态推断）

环境：PostgreSQL **16.2**（rootless 二进制，`127.0.0.1:5433`），
`emby_pgsql_dump_schema_optimized.sql` 原样导入成功（0 错误），
`MediaBrowser.Tests` 的 PgData 用例 **22/22 通过**。

| # | 实测断言 | 结果 |
|---|---|---|
| 1 | 同一 guid 两个 type 能否共存 | **能**。插入 `(guid, 2)` 与 `(guid, 3)` 均成功，`count(*) = 2`，`array_agg(type) = {2,3}` |
| 2 | `ON CONFLICT (guid, type)` 是否跨 type 匹配 | **不匹配，直接 INSERT**。type=2 的 upsert 更新原行；type=3 的 upsert 新增第二行 |
| 3 | 应用层前置 DELETE（`PgItemRepository.cs:472-485`）能否自愈 | **能**。执行后同一 guid 回到 1 行 → 证实正文结论"潜伏而非正在损坏" |
| 4 | `UPDATE media_items SET type=5` 是否被拒 | **没有被拒**，PG16 走行迁移，`id` 保持 `5` 不变。**正文若暗示"分区键不可改"，那条是错的** |
| 5 | `guid` 允许 NULL 且放行多个 | **是**。连插两条 `guid IS NULL` 均成功，`count = 2` |
| 6 | 是否存在指向 `media_items` 的外键 | **零个**（`pg_constraint` 全表列举：只有 `user_data_keys`/`activity_log_types`/`devices`/`sync_jobs` 等，无一条引用 item） |

### 新发现（静态审查看不出来，只有跑起来才会撞上）

`ON CONFLICT (guid, type)` 依赖的唯一索引 **不在 dump 里**，只由 `PgSchemaInitializer.cs:160`
在启动时创建。只灌 dump 的库上，任何写路径都会抛：

```
there is no unique or exclusion constraint matching the ON CONFLICT specification
```

也就是说：**schema 的两处真相源不只是"不优雅"，而是写路径的启动期硬依赖**——
initializer 没跑成功 = 全站不可写。这条直接支持把 schema 收敛成单一迁移序列。

### 仍然成立的结论（未因实测改变）

正文 §4/§5 的无序 `LIMIT 1` 与 stale cache 分析不变；实测 #1/#2 证明重复行**合法可造**，
只是受支持的写入路径会自己清理（#3）。
