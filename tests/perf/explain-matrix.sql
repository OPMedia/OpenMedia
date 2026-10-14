-- ============================================================
-- T2.2 查询分支 EXPLAIN ANALYZE 矩阵（openmedia_perf，10k 条目）
-- 覆盖 PgItemRepository.Query.cs GetWhereClauses 全部分支族 + 主查询形态
-- 用法：psql -d openmedia_perf -f explain-matrix.sql > explain-matrix.out
-- 每分支前 \echo === BRANCH-nn: 名称；输出含 Execution Time / 扫描方式
-- 参数以字面量内联（占位符形态由 Npgsql prepared statement 另行评估）
-- ============================================================
\set ON_ERROR_STOP off
\timing off

-- ---------- 主查询形态 ----------
\echo === BRANCH-01: 基线-类型过滤+排序+分页（/Items 列表页）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id, A.guid, A.type, A.name, A.production_year FROM media_items A WHERE A.type = 2 ORDER BY A.sort_name ASC LIMIT 100 OFFSET 0;

\echo === BRANCH-02: 基线-水合路径（列表页取 data jsonb 全量）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id, A.data FROM media_items A WHERE A.type = 2 ORDER BY A.sort_name ASC LIMIT 100 OFFSET 0;

\echo === BRANCH-03: DISTINCT ON 包装（GroupByPresentationUniqueKey）
EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM (SELECT DISTINCT ON (A.presentation_unique_key) A.id, A.name, A.sort_name AS __sort0 FROM media_items A WHERE A.type = 2 ORDER BY A.presentation_unique_key, A.sort_name ASC) grouped ORDER BY __sort0 ASC LIMIT 100;

\echo === BRANCH-04: COUNT-DISTINCT（TotalRecordCount）
EXPLAIN (ANALYZE, BUFFERS) SELECT count(DISTINCT A.presentation_unique_key) FROM media_items A WHERE A.type = 2;

\echo === BRANCH-05: user_datas join（EnableJoinUserData 基础形态）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id, A.name FROM media_items A LEFT JOIN user_datas ud ON ud.user_data_key_id = A.user_data_key_id AND ud.user_id = 5 WHERE A.type = 2 ORDER BY A.sort_name LIMIT 100;

-- ---------- 数值/范围族 ----------
\echo === BRANCH-06: MinCommunityRating
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.community_rating >= 8 ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-07: Years IN
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.production_year IN (2000, 2005, 2010, 2015, 2020) ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-08: MinWidth/MaxHeight
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.width >= 1000 AND A.height <= 720 ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-09: MinIndexNumber / ParentIndexNumber
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.parent_index_number = 1 AND A.index_number >= 2 ORDER BY A.sort_name LIMIT 100;

-- ---------- 布尔族 ----------
\echo === BRANCH-10: IsMovie/IsFolder/IsVirtualItem
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.is_movie = TRUE AND A.is_virtual_item = FALSE AND A.is_folder = FALSE ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-11: IsLocked
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.is_locked = TRUE;

-- ---------- 名称/模糊族 ----------
\echo === BRANCH-12: NameContains（clean_name LIKE %x% OR original_title LIKE %x%）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND (A.clean_name LIKE '%movie-123%' OR A.original_title LIKE '%movie-123%') ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-13: NameStartsWith（sort_name LIKE 'x%'）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.sort_name LIKE 'movie-12%' ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-14: SortName 范围（>= / <）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.sort_name >= 'movie-05000' AND A.sort_name < 'movie-06000' ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-15: Name 精确（clean_name =）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.clean_name = 'Movie-01234';

-- ---------- 日期族 ----------
\echo === BRANCH-16: MinDateCreated
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.date_created >= now() - interval '365 days' ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-17: PremiereDate 范围
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.premiere_date >= '2000-01-01' AND A.premiere_date <= '2010-12-31' ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-18: MinDateLastSaved（IS NOT NULL AND >=）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE (A.date_last_saved IS NOT NULL AND A.date_last_saved >= now() - interval '30 days');

-- ---------- user_datas 条件族 ----------
\echo === BRANCH-19: IsPlayed（ud.played = TRUE）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id, A.name FROM media_items A LEFT JOIN user_datas ud ON ud.user_data_key_id = A.user_data_key_id AND ud.user_id = 5 WHERE A.type = 2 AND (ud.played = TRUE) ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-20: IsPlayed OR-NULL 形态（ud.played IS NULL OR ud.played = FALSE）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id, A.name FROM media_items A LEFT JOIN user_datas ud ON ud.user_data_key_id = A.user_data_key_id AND ud.user_id = 5 WHERE A.type = 2 AND (ud.played IS NULL OR ud.played = FALSE) ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-21: IsFavorite
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id, A.name FROM media_items A LEFT JOIN user_datas ud ON ud.user_data_key_id = A.user_data_key_id AND ud.user_id = 5 WHERE A.type = 2 AND ud.is_favorite = TRUE ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-22: UserRating >=
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id, A.name FROM media_items A LEFT JOIN user_datas ud ON ud.user_data_key_id = A.user_data_key_id AND ud.user_id = 5 WHERE A.type = 2 AND ud.rating >= 7 ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-23: 恢复播放（playback_position_ticks > 0）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id, A.name FROM media_items A LEFT JOIN user_datas ud ON ud.user_data_key_id = A.user_data_key_id AND ud.user_id = 5 WHERE A.type = 2 AND ud.playback_position_ticks > 0 ORDER BY ud.last_played_date DESC LIMIT 100;

-- ---------- item_extradata 族 ----------
\echo === BRANCH-24: GenreIds（IN 子查询，clean_value 精确）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.id IN (SELECT iv.item_id FROM item_extradata iv WHERE iv.clean_value = 'genre5' AND iv.extradata_type_id = 2) ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-25: Genres 名称（相关子查询：'x' IN (SELECT clean_value ... WHERE item_id = A.id)）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND 'genre5' IN (SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 2) ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-26: Tags
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND 'tag7' IN (SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 4) ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-27: Studios
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.id IN (SELECT iv.item_id FROM item_extradata iv WHERE iv.clean_value = 'studio3' AND iv.extradata_type_id = 3) ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-28: ExcludeTag NOT IN 相关子查询
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND 'tag7' NOT IN (SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 4) ORDER BY A.sort_name LIMIT 100;

-- ---------- item_people 族 ----------
\echo === BRANCH-29: PersonIds（IN 子查询）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.id IN (SELECT ip.item_id FROM item_people ip WHERE ip.person_id = 123) ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-30: PersonName（join + name 过滤）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.id IN (SELECT ip.item_id FROM item_people ip JOIN media_items p ON p.id = ip.person_id WHERE p.name = 'Person-001') ORDER BY A.sort_name LIMIT 100;

-- ---------- 层级族 ----------
\echo === BRANCH-31: AncestorId（ancestor_ids 闭包表）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.id IN (SELECT item_id FROM ancestor_ids WHERE ancestor_id = (SELECT id FROM media_items WHERE name = 'perf-lib' LIMIT 1)) ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-32: ParentId
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.parent_id = (SELECT id FROM media_items WHERE name = 'perf-lib' LIMIT 1) ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-33: ItemIds（guid = OR guid =）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE (A.guid = '00000000-0000-0000-0000-000000000001' OR A.guid = '00000000-0000-0000-0000-000000000002' OR A.guid = '00000000-0000-0000-0000-000000000003');

-- ---------- 媒体流族 ----------
\echo === BRANCH-34: HasNoAudioTrackWithLanguage（相关标量子查询）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND ((SELECT language FROM media_streams WHERE item_id = A.id AND stream_type = 1 AND language = 'eng' LIMIT 1) IS NULL) ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-35: HasSubtitles（media_streams stream_type=2 EXISTS 形态）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND EXISTS (SELECT 1 FROM media_streams WHERE item_id = A.id AND stream_type = 2) ORDER BY A.sort_name LIMIT 100;

-- ---------- jsonb / LIKE-on-blob 族 ----------
\echo === BRANCH-36: VideoType（A.data::text LIKE）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND (A.data::text LIKE '%"VideoType":"Bluray"%' OR A.data::text LIKE '%"VideoType":"Dvd"%') ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-37: SeriesStatus（A.data::text LIKE）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.data::text LIKE '%"Status":"Continuing"%' ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-38: ProviderIds jsonb（->>'Imdb' IS NOT NULL）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.provider_ids->>'Imdb' IS NOT NULL ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-39: Images LIKE（A.images::text LIKE '%Primary%'）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND A.images::text LIKE '%Primary%' ORDER BY A.sort_name LIMIT 100;

-- ---------- 评分/相似度族 ----------
\echo === BRANCH-40: similarity_score 表达式（Similar 端点 ORDER BY）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id, A.name,
  (CASE WHEN A.official_rating IS NULL THEN 10 ELSE 0 END)
  + (CASE WHEN ABS(COALESCE(A.production_year, 0) - 2000) < 10 THEN 10 ELSE 0 END)
  + (CASE WHEN ABS(COALESCE(A.production_year, 0) - 2000) < 5 THEN 5 ELSE 0 END)
  + ((SELECT count(*) FROM item_extradata WHERE item_id = A.id AND clean_value IN (SELECT clean_value FROM item_extradata WHERE item_id = (SELECT id FROM media_items WHERE name = 'Movie-01234' LIMIT 1))) * 10) AS similarity_score
FROM media_items A WHERE A.type = 2 ORDER BY similarity_score DESC, A.sort_name LIMIT 20;

\echo === BRANCH-41: search_score 布尔过滤（SearchTerm）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND ((CASE WHEN (A.clean_name LIKE 'movie-12%' OR (A.original_title IS NOT NULL AND A.original_title LIKE 'movie-12%')) THEN 10 ELSE 0 END) + (CASE WHEN (A.clean_name LIKE '%movie-12%' OR (A.original_title IS NOT NULL AND A.original_title LIKE '%movie-12%')) THEN 10 ELSE 0 END)) > 0 ORDER BY A.sort_name LIMIT 100;

-- ---------- 其他 ----------
\echo === BRANCH-42: MediaTypes
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.media_type = 'Video' ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-43: OfficialRating
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE (A.official_rating = 'PG' OR A.official_rating = 'PG-13') ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-44: HasOverview（overview IS NOT NULL）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.overview IS NOT NULL AND A.type = 2 ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-45: AncestorWithPresentationUniqueKey
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.id IN (SELECT item_id FROM ancestor_ids WHERE ancestor_id IN (SELECT id FROM media_items WHERE presentation_unique_key = 'perf-lib')) ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-46: SeriesPresentationUniqueKey
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.series_presentation_unique_key = 'perf-series-1' ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-47: TopParentId
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE (A.top_parent_id = '3') ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-48: MinEndDate/MaxStartDate（LiveTv 形态）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.start_date >= now() - interval '1 day' AND A.end_date <= now() + interval '1 day' ORDER BY A.start_date LIMIT 100;

\echo === BRANCH-49: ExcludeProviderIds jsonb（->> IS DISTINCT FROM）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id FROM media_items A WHERE A.type = 2 AND (A.provider_ids IS NULL OR A.provider_ids->>'Tmdb' IS DISTINCT FROM '12345') ORDER BY A.sort_name LIMIT 100;

\echo === BRANCH-50: 未看过的 Series 聚合（MAX(udb.last_played_date) 相关子查询）
EXPLAIN (ANALYZE, BUFFERS) SELECT A.id, A.name FROM media_items A WHERE A.type = 3 AND NOT EXISTS (SELECT 1 FROM media_items B JOIN user_datas udb ON udb.user_data_key_id = B.user_data_key_id WHERE udb.user_id = 5 AND udb.played = TRUE AND B.series_presentation_unique_key = A.presentation_unique_key) LIMIT 100;
