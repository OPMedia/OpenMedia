# Surgery #1 — One Identity: GUID becomes the primary key of `media_items`

> Status: design proposal, 2026-11-18.
> Scope: `media_items` identity unification — de-partition `media_items`, make `guid UUID`
> the single primary key, restore referential integrity, delete the guid→int translation
> layer and the two-phase write. No API contract change.
> Evidence base: current code at `9e3d0b28e6` (HEAD), recovered `docs/adr/0007-schema-style.md`
> and `docs/spec/data-model.md` from `9e3d0b28e6^` (the commit that deleted them).

---

## 0. Recovered intent (ADR-0007 / data-model.md), and where today's schema left it

`docs/adr/0007-schema-style.md` (recovered from `git show 9e3d0b28e6^:docs/adr/0007-schema-style.md`)
states the schema philosophy:

- **Normalized relational model is the single source of truth**; a JSONB "payload" is a
  rebuildable cache, never a second truth. Rejected options explicitly include anything
  that produces *dual truth sources*.
- `user_data` is a separate write-hot table keyed by `(user_id, item_id)` unique index.
- Performance budget: "百万级 Item 全库查询 P95 < 100ms".

`docs/spec/data-model.md` (recovered) says `item` has `id` (bigint), `parent_id`,
`library_id` … i.e. **the original design assumed a single integer identity with real
foreign keys inside one unpartitioned table**. What was actually built diverged from both:

- The Emby-4.8 anchoring (ADR-0004/0015) forced GUID-keyed API contract, so a second
  identity (`guid UUID`) was bolted on top of the bigint identity — exactly the "dual
  truth source" ADR-0007 forbids, now enforced in code (`_guidToIdMap`).
- LIST partitioning by `type` was introduced as a perf bet that **structurally forbids**
  the ADR-0007 data model: it forces composite PK `(id, type)`, forces the unique index
  to be `(guid, type)`, and makes every single-column FK illegal — hence 14
  `-- FK removed: … integrity maintained at application layer` comments in the schema and
  the two-phase write + map cache in the repository.

**Verdict: the chosen direction (GUID PK, unpartitioned `media_items`, restored FKs,
delete translation layer) is a *return* to ADR-0007's philosophy, not a departure.**
The only ADR-0007 element we consciously do not touch is `user_datas` (kept on the
`user_data_keys` indirection — see §9).

---

## 1. Current state, verified (with citations)

### 1.1 Schema (`emby_pgsql_dump_schema_optimized.sql`, embedded resource of PgData)

| What | Where | Detail |
|---|---|---|
| `media_items` table | L179–L252 | `id BIGINT GENERATED ALWAYS AS IDENTITY`, `guid UUID` **nullable**, PK `(id, type)` (L247) |
| FK removal comments | L248–L250, L315–316, L341, L379, L436–437, L457–458, L481–482, L507–508 | parent/series/album/children FKs all dropped |
| LIST partitions | L252–L263 | 8 partitions keyed on `type` (2/3/4/5/6+7/8/0,1,9,10/DEFAULT) |
| `idx_media_items_guid` | `PgSchemaInitializer.cs:160` | `CREATE UNIQUE INDEX … (guid, type)` — the only GUID constraint; **GUID is not globally unique** |
| `ancestor_ids` | L309–331 | HASH-partitioned by `item_id`, PK `(item_id, ancestor_id)`, no FK |
| `chapters` | L337–352 | HASH by `item_id`, PK `(item_id, chapter_index)`, no FK |
| `media_streams` | L357–425 | HASH by `item_id`, PK `(item_id, stream_index)`, no FK |
| `item_people` | L430–462 | HASH by `item_id`, PK `(id, item_id)` + unique `(item_id, person_id, person_type)`, no FK |
| `item_extradata` | L503–520 (+ rebuilt by `PgSchemaInitializer.FixItemExtradata` L285–365) | PK `(item_id, extradata_type_id, value)`, FK only to the type dictionary |
| `user_datas` / `user_data_keys` | L620–733 | PK `(user_data_key_id, user_id)` — **keyed by a dictionary, not by item identity** |
| Materialized views | L787–L813 | `mv_continue_watching` reads `m.id AS item_id` from `media_items` |
| `display_preferences` | `PgSchemaInitializer.cs:168` | `item_id UUID` — already GUID-keyed; precedent for the target shape |
| Dead child tables | L487–L612 | `item_links`, `item_extradata` siblings, `imported_collections`, `list_items`, `sync_*`, `user_item_shares` — **zero code references** (verified by grep over src/ + Server + Api); created "照建留空" |

### 1.2 Repository (`src/Emby.Server.PgData/`)

- **Dual identity core**: `_guidToIdMap` (`PgItemRepository.cs:76`), `GuidMapSoftLimit=200_000`
  with whole-map `Clear()` (`:86–93`), init clear (`:128`), `GetInternalIdAsync`
  (`:229–262`, cache miss → `SELECT id FROM media_items WHERE guid = @guid LIMIT 1` —
  note: no ORDER BY, so with duplicate guids the row returned is *arbitrary*).
- **Two-phase write**: `SaveItemsAsync` (`:337–455`) = phase 1 chunked upsert with
  `parent_id/series_id/album_id` **hard-coded NULL** (`UpsertMediaItemsBatchAsync`,
  `:726–1075`, params at `:971–973` and `:1007–1013`) → phase 2 `BackfillReferencesAllAsync`
  (`:506–524` → `BackfillReferencesAsync` `:1077–1105`, resolves by correlated subquery)
  → phase 3 `RebuildAncestorsAsync` (`:408–431`, recursive CTE over `parent_id`).
  A crash between phase 1 and 2 leaves `parent_id = NULL` on disk.
- **Type-change cleanup**: `UpsertItemsChunkAsync` step 1 (`:479–492`) deletes
  same-guid-different-type rows on every save — proof the app *treats* guid as unique
  even though the schema does not.
- **Type → int code map**: `TypeFullNameToCode` (`:164–215`), `TypeCodeUnknown=99` falls
  into `media_items_other` (DEFAULT partition). Used by write (`BuildBatchRow` `:604–681`)
  and by every type filter in `Query.cs:1786/1826/1841`.
- **Child writers**: `SaveChaptersAsync` / `SaveMediaStreamsAsync`
  (`PgItemRepository.ChaptersStreams.cs:155/296`) call `GetInternalIdAsync`
  (`:173`, `:322`); `UpdatePeopleAsync` (`PgItemRepository.People.cs:266`) does the same
  (`:289`); `WriteExtraDataBatchAsync` (`PgItemRepository.cs:1109–1168`) joins by guid.
- **Hydration**: `BuildItemSelectText` (`PgItemRepository.cs:1255–1385`) selects `A.id` and
  re-derives `parent_guid`/`series_guid` via scalar subqueries (`~:1383–1384`);
  `GetItem` (`:1391…`) caches `long internalId` (`:1446–1447`), reads back
  `ParentId/SeasonId/SeriesId` from those subquery columns (`~:1582–1594`).
- **Delete**: `DeleteItemAsync` (`:1173–1223`) resolves guid→id then deletes five child
  tables by `item_id = @id` and the row by `id = @id`, then `_guidToIdMap.TryRemove`
  (`:1216`).
- **Query engine**: `PgItemRepository.Query.cs` — 31 references to `A.id`, guid→id
  subqueries at `:822, :972–982, :1094–1095, :1144, :1158, :1172–1173, :1187, :1201,
  :1215, :1268, :1626, :1637, :1642, :1921`; `GetWhereClauses` (`:625`, 124 `whereClauses.Add`
  branches); `OrderByColumnMap` (`:564`); user_data join `:279`, `:539`; aggregates
  (`PgItemRepository.Aggregates.cs:251, :286, :369, :460–499`).
  **Latent bug found during enumeration**: `Query.cs:1201` emits
  `A.guid NOT IN (SELECT iv.item_id FROM item_extradata …)` — a `uuid = bigint`
  comparison that cannot execute on PG; the identity unification makes it
  `A.id NOT IN (SELECT item_id …)` with both sides UUID. This is filed as a fixed-by-design
  defect (see §6).
- **Activity log**: `PgActivityRepository.ResolveMediaItemIdAsync` (`:391–405`) translates
  guid→bigint for `activity_log.item_id` (schema L79 says `INTEGER` — type drift vs the
  code's BIGINT, flagged and fixed in §4).
- **Tests**: `MediaBrowser.Tests/PgData/QueryEngine/` (differential harness,
  `QueryDiffTestBase.cs` + `WhereClauseDifferentialTests.cs` + `ProductionSchemaTestBase.cs`
  which derives the test schema from the same embedded SQL) and
  `BatchWriteIntegrationTests.cs` (asserts RETURNING guid→id pairing and the two-phase
  parent/ancestor resolution).

### 1.3 GUID provenance (matters for the UUIDv7 question)

`media_items.guid` is **always application-supplied**. The dominant source is
`LibraryManager.GetNewItemId(key, type)` = `MD5(UTF-16LE(type.FullName + path.ToLower()))`
(`PgItemRepository.cs:2075–2090`, `MediaBrowser.Controller/Library/ILibraryManager.cs:147`,
`MediaBrowser.Controller/Entities/Folder.cs:182`). These are deterministic by contract:
the same file must resolve to the same Id on every rescan, and Emby clients persist those
Ids. **The database never generates an item GUID.**

---

## 2. Pushback on "UUIDv7 where available"

The direction statement says: convert `media_items.id` to UUIDv7 "where available". The
evidence says **don't chase UUIDv7 — keep the existing deterministic GUIDs as the PK**:

1. **UUIDv7 is a PG 18 function** (`uuidv7()`). `docker-compose.yml:3` pins
   `postgres:16-alpine`. The host runs PG 18, but CI and deploy run PG 16 — designing
   against `uuidv7()` means either a version floor of 18 (breaks the compose deploy) or a
   generator split we must maintain forever.
2. **It is unimplementable for the dominant identity source.** Path-derived items are
   MD5-deterministic (§1.3). Making them UUIDv7 changes every item's API-visible Id on
   every rescan — an API contract break (openapi.json anchors GUID Ids) *and* a data
   break (playback progress, client-side favorites keyed by Id).
3. **The local-index-locality argument for v7 is weak here.** The PK index is written by
   scan batches of ≤250 rows (BatchChunkSize, `PgItemRepository.cs:325`); random-uuid
   write amplification at ≤1M rows is invisible next to the JSONB `data` column cost.
4. What we *do* need from the DB is only `gen_random_uuid()` (v4) to backfill any
   `guid IS NULL` legacy rows during migration. That is core since PG 13 (and pgcrypto is
   already created at schema L59), so **the version floor for this migration is PG 13** —
   both `postgres:16-alpine` and host PG 18 satisfy it. No extension, no version split.

Net: the identity conversion is a *type change only*. UUIDv7 would only become relevant
if we later change `GetNewItemId` (rejected here; listed as non-goal in §9).

---

## 3. Target schema (v2 DDL)

`media_items` collapses to one unpartitioned table; child tables keep their HASH
partitioning (keyed on `item_id`, now UUID — HASH partitioning on `uuid` is native; they
are out of scope for this surgery except for the type change). `type` and `type_name`
columns survive unchanged.

```sql
-- ============================================================
-- media_items v2 — single unpartitioned table, GUID PK
-- ============================================================
CREATE TABLE media_items_v2 (
    guid                            UUID         NOT NULL PRIMARY KEY,
    type                            INTEGER      NOT NULL,
    type_name                       TEXT,
    data                            JSONB,
    parent_id                       UUID,
    path                            TEXT,
    -- … every remaining column copied verbatim from the current definition
    --   (L183–L246 of the current schema + PgSchemaInitializer alignment columns:
    --    start_date, end_date, is_movie, is_kids, is_sports, is_news, is_new,
    --    is_premiere, is_live, community_rating, custom_rating, index_number,
    --    is_locked, name, official_rating, overview, parent_index_number,
    --    premiere_date, production_year, sort_name, runtime_ticks, date_created,
    --    date_modified, is_series, is_repeat, preferred_metadata_language,
    --    preferred_metadata_country_code, date_last_refreshed, date_last_saved,
    --    is_in_mixed_folder, locked_fields, inherited_parental_rating_value,
    --    critic_rating, presentation_unique_key, original_title, series_name,
    --    series_id, album_id, tagline, provider_ids JSONB, images JSONB,
    --    production_locations JSONB, total_bitrate, extra_type,
    --    series_presentation_unique_key, external_id, width, height, size,
    --    container, status, display_order, three_d_format, user_data_key_id,
    --    sort_index_number, sort_parent_index_number, index_number_end,
    --    channel_number, remote_trailers JSONB, is_public,
    --    is_folder, is_virtual_item, media_type, channel_id, top_parent_id,
    --    clean_name, album, extra_ids, unrated_type, trailer_types,
    --    external_series_id, owner_id, date_last_media_added … ),
    CONSTRAINT fk_media_items_parent  FOREIGN KEY (parent_id) REFERENCES media_items_v2 (guid),
    CONSTRAINT fk_media_items_series  FOREIGN KEY (series_id) REFERENCES media_items_v2 (guid),
    CONSTRAINT fk_media_items_album   FOREIGN KEY (album_id)  REFERENCES media_items_v2 (guid),
    CONSTRAINT fk_media_items_udkey   FOREIGN KEY (user_data_key_id) REFERENCES user_data_keys (id)
);

-- Indexes: same set as today, minus the partition-only workarounds.
-- PK (guid) REPLACES: PRIMARY KEY (id, type) + idx_media_items_guid (guid, type).
CREATE INDEX idx_media_items_parent_id   ON media_items_v2 (parent_id)   WHERE parent_id IS NOT NULL;
CREATE INDEX idx_media_items_series_id   ON media_items_v2 (series_id)   WHERE series_id IS NOT NULL;
CREATE INDEX idx_media_items_album_id    ON media_items_v2 (album_id)    WHERE album_id IS NOT NULL;
-- list_cover keeps (type, parent_id): type is still the hottest filter column.
CREATE INDEX idx_media_items_list_cover  ON media_items_v2 (type, parent_id)
    INCLUDE (name, sort_name, index_number, production_year, community_rating, runtime_ticks);
-- … plus name/sort_name/puk/year/rating/path/BRIN/GIN indexes copied verbatim
--   (L267–L290 of current schema; BRIN on date_created/date_modified stays valid
--    — scan inserts are roughly time-ordered).

-- Child tables: item_id (and person_id) BIGINT → UUID, FKs restored.
ALTER-TARGET for ancestor_ids / chapters / media_streams / item_people / item_extradata:
    item_id UUID NOT NULL REFERENCES media_items_v2 (guid) ON DELETE CASCADE
    (item_people.person_id likewise; ancestor_ids.ancestor_id likewise).
```

FK policy notes:

- `parent_id / series_id / album_id`: **NO ACTION**, nullable. Single-phase writes resolve
  references via `LEFT JOIN`; an unsaved parent resolves to NULL exactly as
  `BackfillReferencesAsync` behaves today — the FK only ever guards against *corruption*,
  not against scan ordering.
- `channel_id / top_parent_id / owner_id` (`TEXT` "N"-format GUIDs): converted to `UUID`
  columns, **no FK** — they are denormalized query accelerators and may reference
  LiveTV/session entities that are never persisted as `media_items` rows. Enforcing RI
  here would break LiveTV saves for zero integrity gain.
- Dead child tables (`item_links`, `list_items`, `user_item_shares`,
  `imported_collections`, `sync_jobs.item_id`, `sync_job_items.item_id`): converted to
  UUID + FK **while empty** — free integrity, zero data risk.
- `activity_log.item_id`: `INTEGER` (L79) / BIGINT-in-code → `UUID`, nullable. Small
  RANGE-partitioned table; a per-partition rewrite is trivial.

### What replaces partition pruning

Once LIST partitioning by `type` is gone, nothing "replaces" pruning because nothing was
being bought: pruning only helps when the table is far larger than shared buffers *and*
queries carry `type` predicates — and the hottest patterns (`type IN (…)` +
`parent_id = …`, ADR-0007's list-page shape) are served identically by the plain B-tree
`idx_media_items_list_cover (type, parent_id)` on the unpartitioned table; the PG planner
uses the same leading-column predicate selectivity. Meanwhile we shed three liabilities:

1. every new `BaseItem` subtype that misses `TypeFullNameToCode` lands in code 99 and
   silently lands in the DEFAULT partition (`media_items_other`) — an unbounded dumping
   ground;
2. PK uniqueness of `guid` becomes *possible*;
3. `TypeFullNameToCode`'s role shrinks from "partition routing + filter" to "plain filter
   column encoding" — `A.type IN/@=` clauses (`Query.cs:1786/1826/1841`) and the
   `ItemCounts` type switch (`Aggregates.cs:460–499`) continue to work unchanged, and the
   `type_name` fallback for unregistered types already exists (`Query.cs:1794–1798`).

---

## 4. Exhaustive code touchpoints

Legend: **[D]** delete, **[C]** convert (bigint id → uuid guid), **[S]** simplify,
**[K]** keep unchanged.

### src/Emby.Server.PgData/PgItemRepository.cs

| Lines | Change |
|---|---|
| 76–93 | **[D]** `_guidToIdMap`, `GuidMapSoftLimit`, `CacheGuidId` |
| 118–135 | **[D]** map `Clear()` in `InitializeAsync` |
| 164–215 | **[K]** `TypeFullNameToCode` / `GetTypeCode` / `GetTypeCodeByName` (filter encoding only now; update the comment that says 分区码) |
| 229–262 | **[D]** `GetInternalIdAsync` |
| 282–294 | **[K]** `SaveImagesAsync` (`WHERE guid = @guid` already identity-native) |
| 316–323 | **[C]** `MediaItemsBatchColumns`: `parent_id, series_id, album_id` become UUID; drop nothing else |
| 337–455 | **[S]** `SaveItemsAsync` collapses 3 phases → 1: chunked upsert now resolves `parent_id`/`series_id` inline (`LEFT JOIN media_items parent ON parent.guid = u.parent_guid`) and runs the ancestor recursive CTE in the same transaction; **[D]** `BackfillReferencesAllAsync` (506–524), `BackfillReferencesAsync` (1077–1105) |
| 408–431 | **[C]** `RebuildAncestorsAsync`: `m.guid = ANY(@guids)` → `m.guid = ANY(@guids)` still works (PK is guid), but the CTE's `item_id/ancestor_id` outputs become UUID |
| 465–505 | **[S]** `UpsertItemsChunkAsync`: step 1 (same-guid-different-type DELETE, 479–492) becomes unnecessary — `ON CONFLICT (guid) DO UPDATE` now updates `type` in place (guid is the PK); keep a deliberate `type` update in the conflict list |
| 726–1075 | **[C]** `UpsertMediaItemsBatchAsync`: `@parent_id/@series_id/@album_id` params switch from `NULL`-filled BIGINT arrays to UUID arrays resolved inline; `RETURNING guid, id` → `RETURNING guid` (id **is** guid) — the `map.Count != count` guard at 1047–1073 collapses to a row-count check; **[D]** the whole guid→id re-pairing dance |
| 1109–1168 | **[C]** `WriteExtraDataBatchAsync`: `item_id` UUID, join `m ON m.guid = u.cg` → `m ON m.id = u.cg` |
| 1173–1223 | **[S]** `DeleteItemAsync`: delete children by `item_id = @guid` directly, delete row by `guid = @guid`; **[D]** `SELECT id …`, `_guidToIdMap.TryRemove` (1216) |
| 1255–1385 | **[S]** `BuildItemSelectText`: drop `A.id` column; `parent_guid`/`series_guid` scalar subqueries → `A.parent_id AS parent_guid, A.series_id AS series_guid`; extradata subqueries (`item_id = A.id`) unchanged semantically (both UUID) |
| 1391–1620 | **[C]** `GetItem`: **[D]** `long internalId = reader.GetInt64("id"); CacheGuidId(...)` (1446–1447); `parent_guid`/`series_guid` reads keep working (columns renamed semantically) |
| 2075–2090 | **[K]** `GetMD5` guid generation (identity provenance unchanged) |

### src/Emby.Server.PgData/PgItemRepository.Query.cs

| Lines | Change |
|---|---|
| 279, 539, 1094–1095 | **[K]** user_data joins (keyed on `user_data_key_id`, untouched) |
| 523–527, 535 | **[C]** extradata subqueries: `item_id = A.id` — A.id is now UUID, same text |
| 822 | **[S]** `A.parent_id = (SELECT id FROM media_items WHERE guid = @ParentId)` → `A.parent_id = @ParentId` (@ParentId already a UUID parameter — the subquery disappears entirely) |
| 972–982 | **[S]** person clauses: `ip.person_id = (SELECT id …)` → direct UUID; `A.id IN (SELECT ip.item_id …)` stays (both UUID) |
| 1144, 1158, 1172–1173, 1215, 1215, 1268, 1626, 1637, 1642, 1676, 1921 | **[C]** guid→id subqueries → direct UUID comparisons / join by guid |
| 1187, 1201 | **[S]** `A.album IN (SELECT name …)` / the **broken** `A.guid NOT IN (SELECT iv.item_id …)` (uuid=bigint — could never execute) → `A.id NOT IN (SELECT iv.item_id …)`; add a differential test case |
| 1332–1421 | **[C]** media_streams/chapters EXISTS clauses: `s.item_id = A.id` (UUID=UUID); 1421 `A.parent_id NOT IN (SELECT id FROM media_items)` → `NOT EXISTS (SELECT 1 FROM media_items p WHERE p.guid = A.parent_id)` |
| 1436 | **[C]** people-name join |
| 1786–1841 | **[K]** type filter clauses (plain predicates now) |
| 2053, 2185, 2370, 2536, 2687 | **[K]** FROM/count scaffolding |

### Other repositories

| File:Lines | Change |
|---|---|
| `PgItemRepository.ChaptersStreams.cs:66–120, 155–225, 228–294, 296–460` | **[C]** all `WHERE item_id = (SELECT id FROM media_items WHERE guid = @guid)` → `WHERE item_id = @guid`; **[D]** `GetInternalIdAsync` calls (173, 322); insert/delete params BIGINT → UUID |
| `PgItemRepository.People.cs:53–120, 175–184, 266–370` | **[C]** same conversion; person_id resolved by `JOIN media_items ON m.id = u.person_guid` |
| `PgItemRepository.Aggregates.cs:251, 286, 369, 460–499` | **[K]** (user_data joins, puk count, type-code counting) |
| `PgItemRepository.Ancestors.cs:20–46` | **[C]** `ancestor_ids` join types (mechanical) |
| `PgActivityRepository.cs:21, 62, 77, 162, 391–405` | **[C]** `ResolveMediaItemIdAsync` returns UUID; `activity_log.item_id` param → UUID; `LEFT JOIN media_items m ON m.id = a.item_id` now type-correct |
| `PgUserDataRepository.cs:45–310` | **[K]** entire file — user_data_keys indirection retained (§9) |
| `PgDisplayPreferencesRepository.cs` | **[K]** already GUID-keyed |
| `PgSchemaInitializer.cs:150–176, 214–241` | **[C]** unique `(guid, type)` → PK `guid` in the embedded schema; alignment DDL for the v2 shape; new `schema_migrations` gate (see §5) |
| `MediaBrowser.Tests/PgData/BatchWriteIntegrationTests.cs:23–280` | **[C]** seed SQL/ON CONFLICT (guid,type) → (guid); P0_2 rewritten for single-phase writes; new FK tests (§6) |
| `MediaBrowser.Tests/PgData/QueryEngine/*` | **[C]** fixture + fragments (§6) |

Also grep-verified no other C# file touches `media_items.id` (Server/Api layers speak
GUID-only — that is what ADR-0004 anchoring bought us).

---

## 5. Data migration: strategy, dedupe, restartability

### 5.1 Strategy decision — (b) new-table + copy + atomic swap

| Option | Verdict | Why |
|---|---|---|
| (a) in-place `ALTER` | rejected | Changing `id BIGINT IDENTITY` → `uuid` + dropping PK/indexes + de-partitioning rewrites the whole table *and* every child table in **one transaction** (an ALTER is transactional but not checkpointable). For the 195k fixture that's fine; for an unknown real library (multi-million episodes + streams + chapters) a failure at hour 2 rolls back everything. No partial rollback surface, no way to validate before committing. |
| (b) new-table + copy + swap | **chosen** | Copy is batched + checkpointed (restartable), fully validatable pre-swap, swap is a single millisecond-scale rename transaction, old tables retained as rollback until acceptance passes. The rewrite cost is identical to (a); the risk profile is strictly better. |
| (c) dump/restore tool | rejected | Throws away the live DB as serving truth, needs a second database-sized volume, and makes "restart mid-migration" a restore-from-scratch. Only worth it for cross-engine moves. |

Server is offline-stoppable by constraint, so no CONCURRENTLY builds, no online renames.

### 5.2 Duplicate-GUID handling

Today's `(guid, type)` unique index permits one guid across N type rows. On the write
path, `UpsertItemsChunkAsync` step 1 (`PgItemRepository.cs:479–492`) deletes same-guid-
different-type rows for items *currently being saved* — so duplicates persist only for
items no longer re-saved. `GetInternalIdAsync`'s `LIMIT 1` with no ORDER BY
(`PgItemRepository.cs:248`) resolves them **arbitrarily** today.

**Dedupe rule** (winner selection, evaluated per guid):

1. `date_last_saved DESC NULLS LAST` — the metadata pipeline stamps this on every
   `SaveItemsAsync`; the most recently saved row is the shape the current library
   scan believes in.
2. tie → `date_modified DESC NULLS LAST` (same rationale, coarser clock).
3. tie → `data IS NOT NULL DESC` (a hydrated row beats a stub).
4. tie → `id DESC` (highest internal identity = newest row; makes the rule total and
   reproducible across runs — required for idempotency).

Justification: this preserves "what the scanner last wrote wins", which is exactly the
semantics the live write path implements (`ON CONFLICT (guid, type) DO UPDATE` = the
latest save overwrites). The loser is, by construction, a stale ghost. The alternatives
are worse: prefer-the-largest-subtree invents semantics the writer never had;
prefer-lower-id arbitrarily resurrects oldest-shape ghosts after a type migration.

**Loser disposition** — this falls out of the new PK almost for free:

- During copy we build a mapping table `migration_id_map(old_id BIGINT PK, new_id UUID)`
  for **every** old row; losers map to the *winner's* guid (new_id IS guid).
- Child rows (`ancestor_ids`, `chapters`, `media_streams`, `item_people`,
  `item_extradata`) are copied through the map with
  `INSERT … SELECT … ON CONFLICT DO NOTHING` against the child PK. If the winner already
  has the same chapter index / stream index / person, the winner's row wins (correct:
  the surviving item's metadata governs); loser-only rows survive under the winner.
- `parent_id`/`series_id` back-references pointing at a loser's internal id are resolved
  through the same map during copy.
- **user_data / playback progress**: `user_datas` hangs off `user_data_keys` (the
  dictionary), not item ids — so progress is never orphaned by the row swap. One
  guard: if the winner's `user_data_key_id IS NULL` and the loser's is not, the winner
  adopts the loser's key (single UPDATE during copy). We deliberately do **not** merge
  two distinct keys' `user_datas` rows — that would fabricate play counts.
- The loser `media_items` row is simply never inserted into v2. Nothing else references
  its internal id because the map resolves every reference to the winner's guid.

### 5.3 Transaction boundaries & idempotency — exact phases

Runner: new `PgIdentityMigration` (invoked from `PgSchemaInitializer` before alignment
DDL, gated by a new `schema_migrations` table — the current `to_regclass('media_items')`
gate is too coarse to record what already ran).

```sql
-- P0: bookkeeping (idempotent, one tx)
CREATE TABLE IF NOT EXISTS schema_migrations (
    name TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT now());
INSERT INTO schema_migrations(name) VALUES ('identity-v2') ON CONFLICT DO NOTHING;

-- P1: NULL-guid backfill on the LIVE table (idempotent, batched tx per 50k)
UPDATE media_items SET guid = gen_random_uuid()
WHERE id IN (SELECT id FROM media_items WHERE guid IS NULL LIMIT 50000);
-- repeat until 0 rows; guids are app-supplied in practice, expect 0.

-- P2: build v2 shapes (idempotent: IF NOT EXISTS / to_regclass guards)
CREATE TABLE IF NOT EXISTS media_items_v2 ( … §3 DDL, no item FKs yet … );
CREATE TABLE IF NOT EXISTS migration_id_map (
    old_id BIGINT PRIMARY KEY, new_id UUID NOT NULL, is_winner BOOLEAN NOT NULL);

-- P3: copy — RESTARTABLE, one tx per batch
-- checkpoint row in migration_state(step, last_old_id); each batch:
--   1) winners+losers for id-range: SELECT DISTINCT ON (guid) … ORDER BY guid,
--      date_last_saved DESC NULLS LAST, date_modified DESC NULLS LAST,
--      data DESC, id DESC  → INSERT INTO media_items_v2 … ON CONFLICT (guid) DO NOTHING
--   2) INSERT INTO migration_id_map … ON CONFLICT (old_id) DO NOTHING
--   3) child tables: INSERT … SELECT via map … ON CONFLICT DO NOTHING
--   4) UPDATE migration_state SET last_old_id = :range_end
-- Crash-safe: re-running a batch is a no-op (all inserts conflict-safe);
-- losers re-derived deterministically (rule of §5.2).

-- P4: post-copy integrity pass (one tx, idempotent)
--   adopt loser user_data keys; verify counts; ANALYZE media_items_v2;
--   ALTER TABLE media_items_v2 ADD CONSTRAINT fk_media_items_parent … NOT VALID;
--   … series, album (NOT VALID first, VALIDATE after — cheap, offline so we can
--   VALIDATE immediately; NOT VALID keeps the tx short if data surprises us).

-- P5: THE SWAP (single transaction, the one irreversible action)
BEGIN;
DROP MATERIALIZED VIEW IF EXISTS mv_library_stats;          -- views bind to table OIDs;
DROP MATERIALIZED VIEW IF EXISTS mv_continue_watching;       -- they would follow the rename
ALTER TABLE media_items        RENAME TO media_items_legacy;
ALTER TABLE ancestor_ids       RENAME TO ancestor_ids_legacy;
ALTER TABLE chapters           RENAME TO chapters_legacy;
ALTER TABLE media_streams      RENAME TO media_streams_legacy;
ALTER TABLE item_people        RENAME TO item_people_legacy;
ALTER TABLE item_extradata     RENAME TO item_extradata_legacy;
ALTER TABLE media_items_v2     RENAME TO media_items;
-- … child _v2 tables likewise, with their partition children renamed via parent rename
--   (PARTITION OF follows the parent), then FKs (now valid immediately — data verified
--   in P4), index renames to final names.
COMMIT;

-- P6: recreate materialized views on the new table (one tx, idempotent),
--     re-run PgSchemaInitializer.ApplyAlignmentDdl-equivalent for v2 indexes.
```

Non-transactional, restartable parts: P1 (batched), P3 (batched + checkpoint).
Everything else is one short transaction. Idempotency: every phase guards or is
conflict-safe; re-running the whole runner after a crash resumes at the last checkpoint
of P3.

### 5.4 What we do to the *fixture* and *fresh installs*

- Fresh installs: the rewritten embedded schema file creates the v2 shape directly —
  no migration path executes (`schema_migrations` gate).
- Perf fixture (195k, regenerated by CI tooling): regenerated post-swap; the ab-compare
  harness (`perf-smoke`, 471-item tree fixture) is rerun against old and new builds.

---

## 6. Staged plan (each stage ships, server works, ≤1 irreversible action)

### Stage 1 — Migration scaffolding + data hygiene (no identity change)
- **Changes**: `schema_migrations` table + runner skeleton; NULL-guid backfill (P1);
  duplicate-GUID census query logged at startup (count only, no mutation).
- **Verify**: restart twice, `schema_migrations` row persists; census logs expected 0
  duplicates on fixture.
- **Rollback**: revert commit (no schema semantics changed).
- **Risk**: negligible; backfill only touches rows `guid IS NULL` (expected zero).

### Stage 2 — Single-phase write on the OLD schema (kills the two-phase write)
- **Changes**: `SaveItemsAsync` 3 phases → 1 (inline `LEFT JOIN` resolution of
  `parent_id`/`series_id` inside the UNNEST upsert; ancestor CTE in the same tx). Delete
  `BackfillReferences*` (`PgItemRepository.cs:506–524, :1077–1105`). Identity mapping and
  bigint columns unchanged.
- **Verify**: `BatchWriteIntegrationTests.P0_2_CrossChunkParentAndAncestors_AreFullyResolved`
  rewritten to assert parent_id after a single call; new test
  `SaveItems_CrashBetweenChunks_LeavesNoNullParentId` (kill the process mid-save on the
  fixture, restart, assert `count(*) WHERE parent_id IS NULL AND parent in memory ≠ null` = 0);
  full PgData test filter green; ab-compare perf within ±10%.
- **Rollback**: revert commit; schema untouched.
- **Risk**: upsert statement grows by two joins — watch batch latency on 195k.

### Stage 3 — Delete the guid→id translation cache on the OLD schema
- **Changes**: all `GetInternalIdAsync` callers (ChaptersStreams `:173/:322`, People
  `:289`, DeleteItem `:1187`) switch to inline `(SELECT id FROM media_items WHERE guid=@g)`
  subqueries or guid-join forms; `_guidToIdMap`/`CacheGuidId`/`GetInternalIdAsync`
  deleted; `Query.cs` guid→id subqueries that reference the map-free path stay (already
  subquery-based).
- **Verify**: new test `ChildWrites_NeverDoPerItemRoundTrips` (pg_stat_statements count
  of `SELECT id FROM media_items WHERE guid` inside child-save paths = 0 per save);
  existing PgData suite; ab-compare.
- **Rollback**: revert commit.
- **Risk**: slightly heavier child-save SQL; measured, not guessed.

*(Stages 2–3 de-risk Stage 4 to a mostly mechanical type flip: by the time identity
changes, the only remaining coupling is column *types*.)*

### Stage 4 — The surgery: v2 schema + copy + swap + code cutover (ONE deploy)
- **Changes**: §3 DDL (embedded schema rewritten; fresh installs get v2),
  `PgIdentityMigration` (§5), all §4 `[C]` repository conversions, test conversions.
  **Irreversible action: the P5 swap.** Mitigations: legacy tables retained (renamed,
  not dropped), copy restartable, pre-swap validation gate (P4 fails → no swap), dry-run
  on the 195k fixture + a synthetic duplicate-GUID fixture (§6 acceptance queries run in
  CI against the dry-run output).
- **Verify**: full acceptance battery §7; openapi.json `git diff` empty; scenario suite
  (tests/README.md) 21/21; contract tests 20/1 unchanged; 195k load run.
- **Rollback**: post-swap pre-DROP: rename back (`media_items` → `media_items_v2_broken`,
  `media_items_legacy` → `media_items`) + revert code deploy — copies/child tables are
  still legacy-shaped, server rejoins the old world. After code is reverted, any rows
  written to v2 in the interim are re-migrated on next attempt (idempotent copy).
- **Observable risk**: uuid PK changes index locality and plan shapes; JSONB `data` and
  guid-keyed lookups dominate anyway; the honest risk is *behavioral* — differential
  query tests are the guard.

### Stage 5 — Integrity hardening + legacy teardown
- **Changes**: FK `VALIDATE` on everything added NOT VALID; FKs on dead child tables
  (empty); `activity_log.item_id` → UUID; recreate materialized views; after a soak
  window (≥1 full library rescan + playback week): **irreversible action — DROP
  `media_items_legacy` + 5 legacy child tables + old partitions**.
- **Verify**: §7 queries re-run post-soak; pg_stat_statements shows zero
  `ON CONFLICT (guid, type)` remnants; autovacuum health query from schema appendix [4].
- **Rollback**: everything except the final DROP (which is gated by the soak gate).
- **Risk**: dropping legacy is the point of no return — the soak gate is the mitigation.

---

## 7. Acceptance criteria (grader-checkable)

### 7.1 Invariant queries (run in CI against the migrated fixture; all must return the stated value)

```sql
-- A1: GUID globally unique — structurally guaranteed AND verifiable
SELECT count(*) FROM (SELECT guid FROM media_items GROUP BY guid HAVING count(*) > 1) t;   -- = 0
SELECT indisprimary FROM pg_index i
 WHERE i.indrelid = 'media_items'::regclass AND i.indkey::text = '{1}'::int2vector
   AND (SELECT attname FROM pg_attribute WHERE attrelid='media_items'::regclass AND attnum=1) = 'guid'; -- = t

-- A2: every parent_id resolves
SELECT count(*) FROM media_items c
 WHERE c.parent_id IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM media_items p WHERE p.guid = c.parent_id);                 -- = 0

-- A3: every series_id / album_id resolves (same shape)                                      -- = 0 / = 0

-- A4: closure table fully resolves in both directions
SELECT count(*) FROM ancestor_ids a
 LEFT JOIN media_items c ON c.guid = a.item_id
 LEFT JOIN media_items p ON p.guid = a.ancestor_id
 WHERE c.guid IS NULL OR p.guid IS NULL;                                                    -- = 0

-- A5: no orphaned child rows (repeat per table)
SELECT 'chapters' t, count(*) FROM chapters c
 LEFT JOIN media_items m ON m.guid = c.item_id WHERE m.guid IS NULL
UNION ALL SELECT 'media_streams', count(*) FROM media_streams s
 LEFT JOIN media_items m ON m.guid = s.item_id WHERE m.guid IS NULL
UNION ALL SELECT 'item_people',  count(*) FROM item_people ip
 LEFT JOIN media_items m ON m.guid = ip.item_id WHERE m.guid IS NULL
UNION ALL SELECT 'item_extradata', count(*) FROM item_extradata e
 LEFT JOIN media_items m ON m.guid = e.item_id WHERE m.guid IS NULL;                        -- all = 0

-- A6: ancestor closure distance is truthful (sample spot-check)
WITH RECURSIVE truth AS (
  SELECT guid AS item, parent_id AS anc, 1 d FROM media_items WHERE parent_id IS NOT NULL
  UNION ALL SELECT t.item, m.parent_id, t.d+1 FROM truth t
  JOIN media_items m ON m.guid = t.anc WHERE t.d < 64)
SELECT count(*) FROM (
  SELECT item, anc, min(d) d FROM truth GROUP BY item, anc
  EXCEPT SELECT item_id, ancestor_id, distance FROM ancestor_ids) x;                        -- = 0

-- A7: FK constraints exist
SELECT count(*) FROM pg_constraint
 WHERE conrelid = 'media_items'::regclass AND contype='f'
   AND conname LIKE 'fk_media_items%';                                                      -- >= 4

-- A8: no two-phase leftovers
SELECT count(*) FROM migration_state WHERE step='copy' AND last_old_id <
  (SELECT max(old_id) FROM migration_id_map);                                               -- = 0
```

### 7.2 Tests (names to exist and pass)

- `BatchWriteIntegrationTests.P0_1_BatchUpsert_ReturningGuidId_IsCorrect_ForMixedInsertAndUpdate`
  (updated: RETURNING guid = PK)
- `BatchWriteIntegrationTests.P0_2_CrossChunkParentAndAncestors_AreFullyResolved`
  (updated: single-phase)
- **new** `BatchWriteIntegrationTests.P0_3_FkEnforcement_RejectsDanglingParentId`
  (expects PG 23503)
- **new** `BatchWriteIntegrationTests.P0_4_TypeChange_UpdatesSameGuidRow_InPlace`
  (the old delete-same-guid-different-type path becomes a PK update)
- **new** `PgIdentityMigrationTests.Migrate_DuplicateGuidAcrossTypes_WinnerPerRule_ChildrenRepointed`
  (fixture with 2 rows sharing guid + child rows on both)
- **new** `PgIdentityMigrationTests.Migrate_IsRestartable_AfterBatchCrash`
- **new** `PgIdentityMigrationTests.Migrate_NullGuids_BackedFilled`
- `WhereClauseDifferentialTests` — every DiffCase updated to v2 fragments; **case count
  must not decrease** (124 branch emitters); the previously-tabled uuid=bigint divergence
  at `Query.cs:1201` flips `WithKnownDivergence` → `ExpectMatch=true` and is asserted to
  no longer reproduce.
- Harness changes (exact): `ProductionSchemaTestBase.SplitSchemaStatements` partition-
  stripping regexes become inert (keep them — they still strip `user_datas` HASH partitions);
  `query-diff-fixture.sql` seeds `guid UUID PRIMARY KEY` and UUID child keys;
  `QueryDiffTestBase.ExecuteAsync` name-normalization unchanged (rows still compared by
  `name`); `PgFragment` strings updated where they cite `(SELECT id FROM media_items …)`.
  The harness protects us from: WHERE-branch semantic drift during the 31 `A.id` site
  conversions and the parent/series predicate simplifications (§4 Query.cs table).

### 7.3 Performance gates

- 195k fixture full-library browse/list P95 < 100ms (ADR-0007 budget) — local run, not CI.
- ab-compare (471-item tree, CI perf-smoke): list-page + detail + search P95 within
  **+10%** of pre-surgery baseline; no regression allowed on `GetItemsCoreAsync` path.
- pgss forensics: UPSERT invocation count per scan stays at the ADR-0018 post-fix
  baseline (0 extraneous per-item UPSERT calls; `e39df4f4a0` is the reference number).
- Save throughput: 195k-item rescan wall time within +10% of baseline (uuid PK vs
  bigint identity; measured on the fixture, not asserted).

### 7.4 Contract

- `git diff openapi.json` is **empty**; all 336 anchored routes unchanged.
- Scenario regression (tests/README.md): 21/21; contract: 20/1 (unchanged known-fail).

---

## 8. Explicit non-goals / debt deliberately left behind

1. **`user_data_keys` indirection stays.** `user_datas` remains keyed
   `(user_data_key_id, user_id)`; the dictionary is a *merge key* (Emby shares playback
   progress across alternate versions / grouped items), not an identity surrogate.
   Converting to `(user_id, item_guid)` would change Emby-compatible semantics. Debt:
   the extra join on every user-data read (`PgUserDataRepository.cs:204–208`) remains.
2. **HASH partitioning of `ancestor_ids/chapters/media_streams/item_people/user_datas`
   stays** (keyed types change to UUID where applicable). Re-evaluating those is
   Surgery #2 material; there is no measured evidence they pay for themselves at
   ≤1M rows, but removing them is a separate decision with separate risk.
3. **BaseItem / LibraryManager god objects, the 1133-line `GetWhereClauses`
   (`Query.cs:625–1758`), and the string-built SQL are untouched** — only the identity
   substrings inside them change. Rewriting the query builder is a different surgery.
4. **No cross-aggregate transactions.** `SaveItemsAsync` still commits per 250-row chunk;
   a crash mid-save leaves earlier chunks committed (single-item atomicity only).
   The two-phase-write elimination removes the *reference* hole, not the chunk hole.
5. **JSONB `data` dual-shape** (flat columns + serialized blob) stays as-is, per
   ADR-0007's "rebuildable payload" split — not this surgery.
6. **UUIDv7 / `GetNewItemId` rework** rejected (§2); deterministic MD5 guids remain.
7. **Dead tables (`item_links`, `list_items`, `sync_*`, `imported_collections`,
   `named_values`, `user_item_shares`) are not deleted** — they get UUID+FK while empty
   and stay otherwise untouched.
8. **`mv_library_stats` / `mv_continue_watching` are recreated, not redesigned** (they
   still key on `user_data_key_id`, inheriting debt #1).
9. No ORM, no schema drift tooling beyond `schema_migrations`, no change to
   `AGENTS.md` conventions (async, parameterized SQL, timestamptz UTC, no blocking calls
   — the migration runner obeys the same rules).

---

## 9. 如果你只有一天

Minimal subset capturing most of the value, all on the live schema, no identity swap:

1. **Stage 2 only** (single-phase write): inline-resolve `parent_id`/`series_id` in the
   upsert, delete `BackfillReferencesAllAsync`/`BackfillReferencesAsync`
   (`PgItemRepository.cs:506–524, 1077–1105`). Kills the NULL-parent crash window and
   the biggest correctness wart. ~1 day including tests.
2. **Plus the 10-line hygiene patch**: NULL-guid backfill + duplicate-GUID census log
   (Stage 1's data part), so the day ends knowing whether real duplicates exist and how
   big the swap-day will be.
3. **Plus the one-line bug fix with a test**: `Query.cs:1201` uuid=bigint
   `NOT IN` → guarded EXISTS form (it can 42883 today under the ExcludeArtist filter).

What one day does *not* buy: the guid→id cache deletion (Stage 3), FK restore, and the
duplicate-GUID *resolution* still require the swap. But after day one, every remaining
change is column-typing + copy mechanics — the design risk is retired, only the
execution remains.

---

## Appendix A — evidence receipts

- Recovered docs: `git show 9e3d0b28e6^:docs/adr/0007-schema-style.md`,
  `git show 9e3d0b28e6^:docs/spec/data-model.md` (deleting commit found via
  `git log --all --diff-filter=D --name-only`).
- docker-compose PG pin: `docker-compose.yml:3` (`postgres:16-alpine`).
- CI perf-smoke: `.github/workflows/ci.yml:165–333` (471-item CI fixture; 195k local,
  ~5.6 min fixture generation).
- GUID determinism: `PgItemRepository.cs:2075–2090`, `ILibraryManager.cs:147`,
  `Folder.cs:182`.
- Dead tables verified by grep: zero references to `item_links|list_items|sync_jobs|
  user_item_shares|imported_collections|named_values` in src/, MediaBrowser.Server/,
  MediaBrowser.Api/.

---

## 10. 取证修订（2026-11-18 16:30，基于 `2026-11-18-identity-defect.md`）

本方案 §1/§2 的动机陈述里有一条被取证推翻，必须记录，否则后续阶段的价值判断会错。

**被推翻的：** "同一个 GUID 换 type 会插入第二行"——在受支持的写入路径上**不成立**。
`SaveItemsAsync` 在 upsert **之前**先做了一次集合删除（`PgItemRepository.cs:472-485`），
把同 guid 异 type 的历史行清掉；`UpsertMediaItemsBatchAsync` 的唯一调用点就在这个事务内
（`:491`）。而且 GUID 是 `type.FullName + path` 的 MD5（`LibraryManager.cs:531-533`），
CLR 类型一变 GUID 跟着变，扫描器根本产不出"同 GUID 新 type"。

**因此缺陷定性为：潜伏（latent），不是线上正在发生的损坏。**
- 触发条件：外部 SQL 直写 / 插件或迁移工具供给稳定 GUID / 并发竞态。
- 触发后后果仍然严重：`RetrieveItemAsync` 与三处子表写入都是无 ORDER BY 的
  `WHERE guid = @guid LIMIT 1`（`PgItemRepository.cs:1239`、`ChaptersStreams.cs:77,105,245`、
  `People.cs:175,184`），分区索引各自独立，拿到哪一行由规划器决定；
  `BackfillReferencesAsync`（`:1091-1099`）不带 type 谓词，会把任意一行的 id 写进所有同 guid 行。

**仍然成立的（这些才是本方案的真实理由）：**

1. **全库零外键**。`grep "REFERENCES media_items"` 在 schema 里零命中，
   `PgSchemaInitializer.cs:15-18` 明写"引用完整性由仓储层维护"。
   这是靠代码纪律维持的一致性，任何一次漏删就是孤儿行，且没有级联可救。
2. **`guid` 可为 NULL**（`emby_pgsql_dump_schema_optimized.sql:181`），
   PG 的唯一索引允许多个 NULL —— 数据库层面 GUID 连"非空身份"都不是。
3. **两阶段写**（`parent_id` 先写 NULL 再 `BackfillReferencesAsync`）和
   **guid→id 翻译缓存**（20 万条整体清空）与重复行无关，它们是纯粹的复杂度税，
   每一次写入都要付。Stage 2 / Stage 3 的价值不受本次修订影响。

**优先级调整：** 不变的是顺序（先 Stage 2 单阶段写、再 Stage 3 删缓存、最后 Stage 4 换身份），
但**理由从"修正在发生的 bug"改为"拆掉结构性税 + 把一致性从代码纪律搬进数据库约束"**。
若只能做一件事，做 Stage 2：它独立可上线、可回滚，且直接消掉崩溃窗口。
