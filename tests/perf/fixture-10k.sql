-- ============================================================
-- T2.1 性能审计 fixture：10k 合成条目（独立库 openmedia_perf 专用）
-- 前置：openmedia_perf 已用 deploy/schema-deploy.sql 重建
-- 严禁在共享库 openmedia 上执行（R2：功能测试断言 TotalRecordCount）
-- 模板：真实 Movie1 的 data jsonb（276B）+ Overview ~1.5KB 模拟真实元数据重量（R5 缓解）
-- ============================================================

-- 0. 基线：确认空库
SELECT 'media_items baseline' AS check, count(*) FROM media_items;

-- 1. 根 + 库文件夹（CollectionFolder type=0）
INSERT INTO media_items (guid, type, type_name, name, path, is_folder, data, presentation_unique_key, sort_name)
VALUES (gen_random_uuid(), 24, 'MediaBrowser.Controller.Entities.AggregateFolder', 'perf-root', 'D:\perf', true,
        '{}'::jsonb, 'perf-root', 'perf-root');
INSERT INTO media_items (guid, type, type_name, name, path, is_folder, data, presentation_unique_key, sort_name)
VALUES (gen_random_uuid(), 0, 'MediaBrowser.Controller.Entities.CollectionFolder', 'perf-lib', 'D:\perf\lib', true,
        '{}'::jsonb, 'perf-lib', 'perf-lib');

-- 2. user_data_keys：每部电影一把（key 文本与条目名一致，模拟真实 UserDataKey）
INSERT INTO user_data_keys (user_data_key)
SELECT 'Movie-' || lpad(n::text, 5, '0') FROM generate_series(1, 10000) n;

-- 3. 10k Movie（真实 Movie1 序列化模板，变体字段：name/path/year/rating/runtime/日期）
INSERT INTO media_items (
    guid, type, type_name, parent_id, path, name, sort_name, original_title,
    production_year, community_rating, runtime_ticks, premiere_date,
    date_created, date_modified, date_last_refreshed, date_last_saved,
    presentation_unique_key, is_movie, is_folder, is_virtual_item, container,
    user_data_key_id, data, size, height, width, total_bitrate
)
SELECT
    gen_random_uuid(),
    2, 'MediaBrowser.Controller.Entities.Movies.Movie',
    (SELECT id FROM media_items WHERE name = 'perf-lib'),
    'D:\perf\lib\Movie-' || lpad(n::text, 5, '0') || '\Movie-' || lpad(n::text, 5, '0') || '.mkv',
    'Movie-' || lpad(n::text, 5, '0'),
    'movie-' || lpad(n::text, 5, '0'),
    'Original Movie ' || n::text,
    1970 + (n % 57),
    round((4 + (n % 51) / 10.0)::numeric, 1)::double precision,
    (36000000000 + (n % 90) * 60000000000)::bigint,
    (now() - ((n % 20000) || ' days')::interval),
    (now() - ((n % 3650) || ' days')::interval),
    (now() - ((n % 3650) || ' days')::interval),
    (now() - ((n % 365) || ' days')::interval),
    (now() - ((n % 365) || ' days')::interval),
    'perf-' || lpad(n::text, 5, '0'),
    true, false, false, 'mkv,webm',
    (SELECT id FROM user_data_keys WHERE user_data_key = 'Movie-' || lpad(n::text, 5, '0')),
    jsonb_build_object(
        'Container', 'mkv,webm', 'VideoType', 'VideoFile', 'IsShortcut', false,
        'HasSubtitles', false, 'IsPlaceHolder', false, 'SubtitleFiles', '[]'::jsonb,
        'RemoteTrailers', '[]'::jsonb, 'AdditionalParts', '[]'::jsonb,
        'LocalAlternateVersions', '[]'::jsonb, 'DefaultVideoStreamIndex', 0,
        'LinkedAlternateVersions', '[]'::jsonb,
        'Name', 'Movie-' || lpad(n::text, 5, '0'),
        'Path', 'D:\perf\lib\Movie-' || lpad(n::text, 5, '0') || '\Movie-' || lpad(n::text, 5, '0') || '.mkv',
        'Overview', repeat('A synthetic overview paragraph for perf fixture item ' || n::text || '. ', 18),
        'Genres', to_jsonb(array['Genre' || (n % 30 + 1)::text, 'Genre' || ((n + 7) % 30 + 1)::text]),
        'ProductionYear', 1970 + (n % 57),
        'CommunityRating', round((4 + (n % 51) / 10.0)::numeric, 1)::double precision
    ),
    (700000000 + (n % 300) * 1000000)::bigint,
    360 + (n % 720), 640 + (n % 1280), 8000000 + (n % 20) * 500000
FROM generate_series(1, 10000) n;

-- 4. 500 Person（type=11）
INSERT INTO media_items (guid, type, type_name, name, path, is_folder, data, sort_name)
SELECT gen_random_uuid(), 11, 'MediaBrowser.Controller.Entities.Person',
       'Person-' || lpad(n::text, 3, '0'), null, false,
       jsonb_build_object('Name', 'Person-' || lpad(n::text, 3, '0')) , 'person-' || lpad(n::text, 3, '0')
FROM generate_series(1, 500) n;

-- 5. item_people：每部 3 人（1 Director + 2 Actor）→ 30k 行
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, base.pid + (m.id % 500),
       CASE WHEN m.id % 3 = 0 THEN 'Director' ELSE 'Actor' END,
       CASE WHEN m.id % 3 = 0 THEN 0 ELSE 1 END, 0,
       CASE WHEN m.id % 3 = 0 THEN 'Director' ELSE 'Actor' END, 0
FROM media_items m
CROSS JOIN (SELECT min(id) AS pid FROM media_items WHERE type = 11) base
WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, base.pid + ((m.id * 7) % 500), 'Actor', 1, 1, 'Actor', 1
FROM media_items m
CROSS JOIN (SELECT min(id) AS pid FROM media_items WHERE type = 11) base
WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, base.pid + ((m.id * 13) % 500), 'Actor', 1, 2, 'Actor', 2
FROM media_items m
CROSS JOIN (SELECT min(id) AS pid FROM media_items WHERE type = 11) base
WHERE m.type = 2 ON CONFLICT DO NOTHING;

-- 6. item_extradata_types 种子（pg_dump -s 不带数据；与实际库一致）
INSERT INTO item_extradata_types (extradata_type_id, name) VALUES
    (0, 'Artists'), (1, 'AlbumArtists'), (2, 'Genres'), (3, 'Studios'), (4, 'Tags'), (6, 'InheritedTags')
ON CONFLICT DO NOTHING;

-- 6b. item_extradata：Genres（type 2）×2/部、Studios（type 3）×1/部、Tags（type 4）×1/半部
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 2, 'Genre' || ((m.id % 30) + 1), lower('Genre' || ((m.id % 30) + 1)) FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 2, 'Genre' || (((m.id + 7) % 30) + 1), lower('Genre' || (((m.id + 7) % 30) + 1)) FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 3, 'Studio' || ((m.id % 20) + 1), lower('Studio' || ((m.id % 20) + 1)) FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 4, 'Tag' || ((m.id % 50) + 1), lower('Tag' || ((m.id % 50) + 1)) FROM media_items m WHERE m.type = 2 AND m.id % 2 = 0;

-- 7. ancestor_ids：movie → perf-lib（distance 1）
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, (SELECT id FROM media_items WHERE name = 'perf-lib'), 1
FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;

-- 8. media_streams：每部 2 流（video + audio）→ 20k 行
INSERT INTO media_streams (item_id, stream_index, stream_type, codec, language, profile, width, height, is_interlaced, bit_rate)
SELECT m.id, 0, 0, 'h264', 'eng', 'High', 640 + (m.id % 1280), 360 + (m.id % 720), false, 8000000 + (m.id % 20) * 500000 FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO media_streams (item_id, stream_index, stream_type, codec, language, channel_layout, profile, is_interlaced, bit_rate)
SELECT m.id, 1, 1, 'aac', 'eng', 'mono', 'LC', false, 192000 FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;

-- 9. 100 用户（local_users）+ user_datas（每用户 ~30% 条目）
INSERT INTO local_users (guid, data)
SELECT gen_random_uuid(), jsonb_build_object('Name', 'perf-user-' || lpad(n::text, 3, '0'), 'Password', null) FROM generate_series(1, 100) n;

INSERT INTO user_datas (user_data_key_id, user_id, rating, played, play_count, is_favorite, playback_position_ticks, last_played_date)
SELECT m.user_data_key_id, u.id,
       round((1 + (m.id % 10) / 2.0)::numeric, 1)::double precision,
       (m.id + u.id) % 3 = 0,
       CASE WHEN (m.id + u.id) % 3 = 0 THEN 1 + (m.id % 5) ELSE 0 END,
       (m.id + u.id) % 17 = 0,
       CASE WHEN (m.id + u.id) % 3 = 1 THEN (m.id % 100) * 1000000 ELSE 0 END,
       CASE WHEN (m.id + u.id) % 3 = 0 THEN now() - ((m.id % 300) || ' days')::interval ELSE NULL END
FROM media_items m
CROSS JOIN local_users u
WHERE m.type = 2 AND (m.id + u.id) % 10 < 3;

-- 10. 统计口径
SELECT 'media_items total' AS metric, count(*) FROM media_items
UNION ALL SELECT 'movies', count(*) FROM media_items WHERE type = 2
UNION ALL SELECT 'persons', count(*) FROM media_items WHERE type = 11
UNION ALL SELECT 'item_people', count(*) FROM item_people
UNION ALL SELECT 'item_extradata', count(*) FROM item_extradata
UNION ALL SELECT 'media_streams', count(*) FROM media_streams
UNION ALL SELECT 'ancestor_ids', count(*) FROM ancestor_ids
UNION ALL SELECT 'local_users', count(*) FROM local_users
UNION ALL SELECT 'user_datas', count(*) FROM user_datas
UNION ALL SELECT 'avg data blob bytes', round(avg(octet_length(data::text)))::int FROM media_items WHERE type = 2;

ANALYZE;
