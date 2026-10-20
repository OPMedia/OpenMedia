-- ============================================================
-- 树形合成 fixture：150 库 x (电影 400 + 系列 20x4季x10集)
-- 生成器：tests\perf\fixture-100k-tree.ps1（勿手改，改生成器参数重跑）
-- 形态：root(1) -> CollectionFolder(150) -> {Movie|Series->Season->Episode}
-- 规模：movie 60000 / series 3000 / season 12000 / episode 120000 / person 100
-- 总量：~195251（另 1 root + 150 folder + 100 person）
-- 严禁在共享库 openmedia 上执行（安全红线 R2）
-- ============================================================

\timing on

-- 0. 基线
SELECT 'baseline (期望 0)' AS check, count(*) FROM media_items;

-- 1. 根 + 库（一次插入，id 连续：root + LibCount 个 folder）
INSERT INTO media_items (guid, type, type_name, name, path, is_folder, data, presentation_unique_key, sort_name)
VALUES (gen_random_uuid(), 24, 'MediaBrowser.Controller.Entities.AggregateFolder', 'perf-tree-root', 'D:\perf-tree', true,
        '{}'::jsonb, 'perf-tree-root', 'perf-tree-root');

INSERT INTO media_items (guid, type, type_name, name, path, is_folder, data, presentation_unique_key, sort_name, top_parent_id)
SELECT gen_random_uuid(), 0, 'MediaBrowser.Controller.Entities.CollectionFolder',
       'perf-lib-' || lpad(k::text, 3, '0'),
       'D:\perf-tree\perf-lib-' || lpad(k::text, 3, '0'),
       true, '{}'::jsonb,
       'pf-lib-' || lpad(k::text, 3, '0'), 'pf-lib-' || lpad(k::text, 3, '0'),
       'perf-lib-' || lpad(k::text, 3, '0')
FROM generate_series(1, 150) k;

SELECT min(id) AS lib_id_min, max(id) AS lib_id_max, count(*) AS n FROM media_items WHERE type = 0 \gset

-- 2. user_data_keys（叶子条目 1:1：电影 + 剧集）
INSERT INTO user_data_keys (user_data_key)
SELECT 'pf-key-' || lpad(n::text, 7, '0') FROM generate_series(1, 300000) n;

SELECT min(id) AS key_id_min FROM user_data_keys \gset

-- 3. 电影 60000（每库 400；parent = 库；深度 2）
INSERT INTO media_items (
    guid, type, type_name, parent_id, path, name, sort_name, original_title,
    production_year, community_rating, runtime_ticks, premiere_date,
    date_created, date_modified, date_last_refreshed, date_last_saved,
    presentation_unique_key, is_movie, is_folder, is_virtual_item, container,
    user_data_key_id, data, size, height, width, total_bitrate, media_type, top_parent_id
)
SELECT
    gen_random_uuid(), 2, 'MediaBrowser.Controller.Entities.Movies.Movie',
    :lib_id_min + ((n - 1) / 400)::bigint,
    'D:\perf-tree\' || 'perf-lib-' || lpad(((n - 1) / 400 + 1)::text, 3, '0') || '\Movie-' || lpad(n::text, 6, '0') || '.mkv',
    'Movie-' || lpad(n::text, 6, '0'), 'movie-' || lpad(n::text, 6, '0'),
    'Original Movie ' || n::text,
    1970 + (n % 57),
    round((4 + (n % 51) / 10.0)::numeric, 1)::double precision,
    (36000000000 + (n % 90) * 60000000000)::bigint,
    now() - ((n % 20000) || ' days')::interval,
    now() - ((n % 3650) || ' days')::interval,
    now() - ((n % 365) || ' days')::interval,
    now() - ((n % 365) || ' days')::interval,
    'pf-mv-' || lpad(n::text, 6, '0'),
    true, false, false, 'mkv,webm',
    :key_id_min + n - 1,
    jsonb_build_object(
        'Container', 'mkv,webm', 'VideoType', 'VideoFile', 'IsShortcut', false,
        'HasSubtitles', false, 'IsPlaceHolder', false,
        'Name', 'Movie-' || lpad(n::text, 6, '0'),
        'Overview', repeat('A synthetic movie overview for tree fixture ' || n::text || '. ', 18),
        'Genres', to_jsonb(array['Genre' || (n % 2000 + 1)::text, 'Genre' || ((n + 7) % 2000 + 1)::text]),
        'ProductionYear', 1970 + (n % 57),
        'CommunityRating', round((4 + (n % 51) / 10.0)::numeric, 1)::double precision
    ),
    (700000000 + (n % 300) * 1000000)::bigint,
    360 + (n % 720), 640 + (n % 1280), 8000000 + (n % 20) * 500000, 'Video',
    'perf-lib-' || lpad(((n - 1) / 400 + 1)::text, 3, '0')
FROM generate_series(1, 60000) n;

SELECT min(id) AS movie_id_min, count(*) AS n FROM media_items WHERE type = 2 \gset

-- 4. 系列 3000（每库 20；parent = 库；深度 2）
INSERT INTO media_items (
    guid, type, type_name, parent_id, path, name, sort_name,
    production_year, date_created, date_modified, date_last_refreshed, date_last_saved,
    presentation_unique_key, is_series, is_folder, data, top_parent_id
)
SELECT
    gen_random_uuid(), 3, 'MediaBrowser.Controller.Entities.TV.Series',
    :lib_id_min + ((s - 1) / 20)::bigint,
    'D:\perf-tree\' || 'perf-lib-' || lpad(((s - 1) / 20 + 1)::text, 3, '0') || '\Series-' || lpad(s::text, 5, '0'),
    'Series-' || lpad(s::text, 5, '0'), 'series-' || lpad(s::text, 5, '0'),
    1990 + (s % 36),
    now() - ((s % 3650) || ' days')::interval,
    now() - ((s % 3650) || ' days')::interval,
    now() - ((s % 365) || ' days')::interval,
    now() - ((s % 365) || ' days')::interval,
    'pf-sr-' || lpad(s::text, 5, '0'), true, true,
    jsonb_build_object(
        'Name', 'Series-' || lpad(s::text, 5, '0'),
        'Overview', repeat('A synthetic series overview for tree fixture ' || s::text || '. ', 12),
        'ProductionYear', 1990 + (s % 36)
    ),
    'perf-lib-' || lpad(((s - 1) / 20 + 1)::text, 3, '0')
FROM generate_series(1, 3000) s;

SELECT min(id) AS series_id_min FROM media_items WHERE type = 3 \gset

-- 5. 季 12000（每系列 4；深度 3）
INSERT INTO media_items (
    guid, type, type_name, parent_id, path, name, sort_name, index_number,
    date_created, date_modified, date_last_refreshed, date_last_saved,
    presentation_unique_key, is_folder, data, top_parent_id
)
SELECT
    gen_random_uuid(), 4, 'MediaBrowser.Controller.Entities.TV.Season',
    :series_id_min + ((j - 1) / 4)::bigint,
    'D:\perf-tree\' || 'perf-lib-' || lpad((((j - 1) / 4) / 20 + 1)::text, 3, '0')
        || '\Series-' || lpad((((j - 1) / 4) + 1)::text, 5, '0')
        || '\Season ' || lpad((((j - 1) % 4) + 1)::text, 2, '0'),
    'Season ' || lpad((((j - 1) % 4) + 1)::text, 2, '0'),
    'season-' || lpad((((j - 1) % 4) + 1)::text, 2, '0'),
    (((j - 1) % 4) + 1),
    now() - ((j % 3650) || ' days')::interval,
    now() - ((j % 3650) || ' days')::interval,
    now() - ((j % 365) || ' days')::interval,
    now() - ((j % 365) || ' days')::interval,
    'pf-sn-' || lpad(j::text, 6, '0'), true,
    jsonb_build_object(
        'Name', 'Season ' || lpad((((j - 1) % 4) + 1)::text, 2, '0'),
        'IndexNumber', (((j - 1) % 4) + 1),
        'SeriesName', 'Series-' || lpad((((j - 1) / 4) + 1)::text, 5, '0')
    ),
    'perf-lib-' || lpad((((j - 1) / 4) / 20 + 1)::text, 3, '0')
FROM generate_series(1, 12000) j;
 -- top_parent_id 单独回填（继承自系列，路径推导比表达式可靠）
UPDATE media_items sn SET top_parent_id = s.top_parent_id
FROM media_items s WHERE sn.parent_id = s.id AND sn.type = 4 AND s.top_parent_id IS NOT NULL;

SELECT min(id) AS season_id_min FROM media_items WHERE type = 4 \gset

-- 6. 集 120000（每季 10；深度 4）
INSERT INTO media_items (
    guid, type, type_name, parent_id, path, name, sort_name, index_number, parent_index_number,
    series_id, series_name, production_year, runtime_ticks, premiere_date,
    date_created, date_modified, date_last_refreshed, date_last_saved,
    presentation_unique_key, series_presentation_unique_key,
    is_folder, is_virtual_item, container, user_data_key_id, data, size, height, width, total_bitrate, media_type
)
SELECT
    gen_random_uuid(), 5, 'MediaBrowser.Controller.Entities.TV.Episode',
    :season_id_min + ((e - 1) / 10)::bigint,
    'D:\perf-tree\' || 'perf-lib-' || lpad(((((e - 1) / 10) / 4) / 20 + 1)::text, 3, '0')
        || '\Series-' || lpad((((e - 1) / 10) / 4 + 1)::text, 5, '0')
        || '\Season ' || lpad((((e - 1) / 10) % 4 + 1)::text, 2, '0')
        || '\Episode-' || lpad(e::text, 6, '0') || '.mkv',
    'Episode-' || lpad(e::text, 6, '0'), 'episode-' || lpad(e::text, 6, '0'),
    ((e - 1) % 10) + 1,
    ((((e - 1) / 10) % 4) + 1),
    :series_id_min + (((e - 1) / 10) / 4)::bigint,
    'Series-' || lpad((((e - 1) / 10) / 4 + 1)::text, 5, '0'),
    1990 + (e % 36),
    (15000000000 + (e % 45) * 60000000000)::bigint,
    now() - ((e % 20000) || ' days')::interval,
    now() - ((e % 3650) || ' days')::interval,
    now() - ((e % 3650) || ' days')::interval,
    now() - ((e % 365) || ' days')::interval,
    now() - ((e % 365) || ' days')::interval,
    'pf-ep-' || lpad(e::text, 6, '0'),
    'pf-sr-' || lpad((((e - 1) / 10) / 4 + 1)::text, 5, '0'),
    false, false, 'mkv,webm',
    :key_id_min + 60000 + e - 1,
    jsonb_build_object(
        'Container', 'mkv,webm', 'VideoType', 'VideoFile', 'IsShortcut', false,
        'Name', 'Episode-' || lpad(e::text, 6, '0'),
        'IndexNumber', ((e - 1) % 10) + 1,
        'ParentIndexNumber', ((((e - 1) / 10) % 4) + 1),
        'SeriesName', 'Series-' || lpad((((e - 1) / 10) / 4 + 1)::text, 5, '0'),
        'Overview', repeat('A synthetic episode overview for tree fixture ' || e::text || '. ', 14),
        'ProductionYear', 1990 + (e % 36),
        'Genres', to_jsonb(array['Genre' || (e % 2000 + 1)::text])
    ),
    (400000000 + (e % 200) * 1000000)::bigint,
    360 + (e % 720), 640 + (e % 1280), 5000000 + (e % 15) * 500000, 'Video'
FROM generate_series(1, 120000) e;

SELECT min(id) AS episode_id_min, count(*) AS n FROM media_items WHERE type = 5 \gset
 -- 集的 top_parent_id 经 series_id 回填
UPDATE media_items e SET top_parent_id = s.top_parent_id
FROM media_items s WHERE e.series_id = s.id AND e.type = 5 AND s.top_parent_id IS NOT NULL;

-- 7. Person 100
INSERT INTO media_items (guid, type, type_name, name, path, is_folder, data, sort_name, clean_name)
SELECT gen_random_uuid(), 11, 'MediaBrowser.Controller.Entities.Person',
       'Person-' || lpad(n::text, 3, '0'), null, false,
       jsonb_build_object('Name', 'Person-' || lpad(n::text, 3, '0')),
       'person-' || lpad(n::text, 3, '0'), 'person-' || lpad(n::text, 3, '0')
FROM generate_series(1, 100) n;

SELECT min(id) AS person_id_min FROM media_items WHERE type = 11 \gset

-- 8. item_extradata_types 种子
INSERT INTO item_extradata_types (extradata_type_id, name) VALUES
    (0, 'Artists'), (1, 'AlbumArtists'), (2, 'Genres'), (3, 'Studios'), (4, 'Tags'), (6, 'InheritedTags')
ON CONFLICT DO NOTHING;

-- 9. item_extradata（Genres x2/条 + Studios x1/电影 + Tags x1/半）
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 2, 'Genre' || ((m.id % 2000) + 1), lower('Genre' || ((m.id % 2000) + 1)) FROM media_items m WHERE m.type IN (2, 5) ON CONFLICT DO NOTHING;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 2, 'Genre' || (((m.id + 7) % 2000) + 1), lower('Genre' || (((m.id + 7) % 2000) + 1)) FROM media_items m WHERE m.type IN (2, 5) ON CONFLICT DO NOTHING;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 3, 'Studio' || ((m.id % 500) + 1), lower('Studio' || ((m.id % 500) + 1)) FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 4, 'Tag' || ((m.id % 1000) + 1), lower('Tag' || ((m.id % 1000) + 1)) FROM media_items m WHERE m.type IN (2, 5) AND m.id % 2 = 0 ON CONFLICT DO NOTHING;

-- 10. item_people（电影/剧集各 3 条，映射到 100 个 Person）
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, :person_id_min + ((m.id * 7) % 100), 'Director', 0, 0, 'Director', 0 FROM media_items m WHERE m.type IN (2, 5) ON CONFLICT DO NOTHING;
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, :person_id_min + ((m.id * 13) % 100), 'Actor', 1, 1, 'Actor', 1 FROM media_items m WHERE m.type IN (2, 5) ON CONFLICT DO NOTHING;
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, :person_id_min + ((m.id * 29) % 100), 'Actor', 1, 2, 'Actor', 2 FROM media_items m WHERE m.type IN (2, 5) ON CONFLICT DO NOTHING;

-- 11. ancestor_ids 闭包（按树深：电影 2 / 系列 2 / 季 3 / 集 4）
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, m.parent_id, 1 FROM media_items m WHERE m.type = 3 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, m.parent_id, 1 FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, s.parent_id, 2 FROM media_items m JOIN media_items s ON s.id = m.parent_id WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, s.parent_id, 2 FROM media_items m JOIN media_items s ON s.id = m.parent_id WHERE m.type = 3 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, m.parent_id, 1 FROM media_items m WHERE m.type = 4 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, s.parent_id, 2 FROM media_items m JOIN media_items s ON s.id = m.parent_id WHERE m.type = 4 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, s.parent_id, 3 FROM media_items m JOIN media_items s ON s.id = m.parent_id WHERE m.type = 4 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, m.parent_id, 1 FROM media_items m WHERE m.type = 5 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, sn.parent_id, 2 FROM media_items m JOIN media_items sn ON sn.id = m.parent_id WHERE m.type = 5 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT e.id, s.parent_id, 2 FROM media_items e JOIN media_items s ON s.id = e.series_id WHERE e.type = 5 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT e.id, sr.parent_id, 3 FROM media_items e JOIN media_items s ON s.id = e.series_id JOIN media_items sr ON sr.id = s.parent_id WHERE e.type = 5 ON CONFLICT DO NOTHING;

-- 12. media_streams（电影/集 x2 + 无音频流）
INSERT INTO media_streams (item_id, stream_index, stream_type, codec, language, profile, width, height, is_interlaced, bit_rate)
SELECT m.id, 0, 0, 'h264', 'eng', 'High', 640 + (m.id % 1280), 360 + (m.id % 720), false, 8000000 + (m.id % 20) * 500000
FROM media_items m WHERE m.type IN (2, 5) ON CONFLICT DO NOTHING;
INSERT INTO media_streams (item_id, stream_index, stream_type, codec, language, channel_layout, profile, is_interlaced, bit_rate)
SELECT m.id, 1, 1, 'aac', 'eng', 'mono', 'LC', false, 192000
FROM media_items m WHERE m.type IN (2, 5) ON CONFLICT DO NOTHING;

-- 13. 用户 100（含 EnableAllFolders，用户视图可用）
INSERT INTO local_users (guid, data)
SELECT gen_random_uuid(), jsonb_build_object(
       'Name', 'perf-user-' || lpad(n::text, 3, '0'),
       'Policy', jsonb_build_object('IsAdministrator', false, 'EnableAllFolders', true, 'Enabled', true)
   )
FROM generate_series(1, 100) n;

SELECT min(id) AS user_id_min FROM local_users \gset

-- 14. user_datas（约 3 成条目 x 100 用户；确定性伪随机）
INSERT INTO user_datas (user_data_key_id, user_id, rating, played, play_count, is_favorite, playback_position_ticks, last_played_date)
SELECT m.user_data_key_id, :user_id_min + u - 1,
       round((1 + ((m.id + u) % 10) / 2.0)::numeric, 1)::double precision,
       (m.id + u) % 3 = 0,
       CASE WHEN (m.id + u) % 3 = 0 THEN 1 + (m.id % 5) ELSE 0 END,
       (m.id + u) % 17 = 0,
       CASE WHEN (m.id + u) % 3 = 1 THEN (m.id % 100) * 1000000 ELSE 0 END,
       CASE WHEN (m.id + u) % 3 = 0 THEN now() - ((m.id % 300) || ' days')::interval ELSE NULL END
FROM media_items m
CROSS JOIN generate_series(1, 100) u
WHERE m.type IN (2, 5) AND (m.id + u) % 10 < 3
ON CONFLICT DO NOTHING;

-- 15. 验收（祖先分布是核心断言）
SELECT 'media_items total' AS metric, count(*) FROM media_items
UNION ALL SELECT 'collection_folders', count(*) FROM media_items WHERE type = 0
UNION ALL SELECT 'movies', count(*) FROM media_items WHERE type = 2
UNION ALL SELECT 'series', count(*) FROM media_items WHERE type = 3
UNION ALL SELECT 'seasons', count(*) FROM media_items WHERE type = 4
UNION ALL SELECT 'episodes', count(*) FROM media_items WHERE type = 5
UNION ALL SELECT 'persons', count(*) FROM media_items WHERE type = 11
UNION ALL SELECT 'local_users', count(*) FROM local_users
UNION ALL SELECT 'user_datas', count(*) FROM user_datas
UNION ALL SELECT 'item_people', count(*) FROM item_people
UNION ALL SELECT 'item_extradata', count(*) FROM item_extradata
UNION ALL SELECT 'media_streams', count(*) FROM media_streams
UNION ALL SELECT 'ancestor_ids', count(*) FROM ancestor_ids
UNION ALL SELECT 'avg data blob bytes', round(avg(octet_length(data::text)))::int FROM media_items WHERE type IN (2, 5);

-- 祖先分布断言：每库子孙数（BR-45 假象的判别式——历史 fixture 这里会是单行 10k）
SELECT l.name AS lib, count(a.item_id) AS descendants
FROM media_items l
JOIN ancestor_ids a ON a.ancestor_id = l.id
WHERE l.type = 0
GROUP BY l.name
ORDER BY descendants DESC
LIMIT 5;
SELECT 'libs_with_descendants (期望 = 150)' AS check, count(DISTINCT l.id)
FROM media_items l JOIN ancestor_ids a ON a.ancestor_id = l.id WHERE l.type = 0;
SELECT 'orphan_rows (期望 0)' AS check, count(*)
FROM media_items m WHERE m.parent_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM media_items p WHERE p.id = m.parent_id);

ANALYZE;

-- ============================================================
-- end of fixture
-- ============================================================
