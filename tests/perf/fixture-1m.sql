-- ============================================================
-- S5 规模化 fixture：1,000,000 媒体条目（独立库 openmedia_scale 专用）
-- 依据 docs/scale-design.md §3（BR-45 教训：祖先分布必须贴近真实库形态）
--
-- 分布模型（参数集中于本节，可复现）：
--   库结构：1 根 + 1000 CollectionFolder（250 电影库 + 250 剧集库 + 400 音乐库 + 100 其他库）
--   电影：250 库 × 800 = 200,000 Movie（深度 2：库→电影）
--   剧集：250 库 × 80 = 20,000 Series；每系列 2/3 季交替 = 50,000 Season；
--         每季 10 集 = 500,000 Episode（深度 4：库→系列→季→集）
--   音乐：400 库 × 125 = 50,000 MusicAlbum；每专辑 3 曲 = 150,000 Audio（深度 3）
--   其他：100 库 × 300 = 30,000（Video 20k / Book 5k / Photo 5k）
--   ByName：100,000 Person（item_people 引用）
--   类型配比：Episode 50% / Movie 20% / Audio 15% / Season 5% / MusicAlbum 5% / Series 2% / 其他 3%
--
-- 关联表（按比例）：
--   user_data_keys 880k（叶子条目 1:1：电影 1-200k / 剧集 200001-700k / 音频 700001-850k /
--                       Video 850001-870k / Book 870001-875k / Photo 875001-880k）
--   item_people ≈ 2.25M（电影×3 + 剧集×3 + 音频×1 ≈ 3× 叶子）
--   media_streams ≈ 1.59M（视频类×2 + 音频×1 ≈ 1.5×）
--   item_extradata ≈ 2.1M（Genres×2/电影+剧集 + Studio×1/电影 + Tag×1/半 + Artist×2/音频）
--   ancestor_ids ≈ 2.2M（按树深：集 3 / 季 2 / 曲 2 / 其余 1）
--   local_users 10,000 + user_datas 1M（10k 用户 × 100 条，确定性伪随机）
--
-- 提交粒度：每条 INSERT...SELECT 为一个原子语句（自动提交）——
--           分批语义在语句级满足（最大单批 500k 行 ≈ 750MB WAL，PG 流式处理无压力）
-- 父子链：\gset 锚点 + 算术偏移（O(1)/行，无字符串 JOIN；锚点在验收节核对）
-- 磁盘预估：data ~1.5GB + 索引 3-5GB
-- 严禁在共享库 openmedia 上执行（安全红线）
-- ============================================================

\timing on

-- 0. 基线：确认空库
SELECT 'media_items baseline (期望 0)' AS check, count(*) FROM media_items;

-- ============================================================
-- 1. 根 + 1000 库（一次插入，id 连续：电影 1-250 / 剧集 251-500 / 音乐 501-900 / 其他 901-1000）
-- ============================================================
INSERT INTO media_items (guid, type, type_name, name, path, is_folder, data, presentation_unique_key, sort_name)
VALUES (gen_random_uuid(), 24, 'MediaBrowser.Controller.Entities.AggregateFolder', 'scale-root', 'D:\scale', true,
        '{}'::jsonb, 'scale-root', 'scale-root');

INSERT INTO media_items (guid, type, type_name, name, path, is_folder, data, presentation_unique_key, sort_name)
SELECT gen_random_uuid(), 0, 'MediaBrowser.Controller.Entities.CollectionFolder',
       kind || '-lib-' || lpad(k::text, 3, '0'),
       'D:\scale\' || kind || lpad(k::text, 3, '0'),
       true, '{}'::jsonb,
       'sc-lib-' || kind || k, 'sc-lib-' || kind || lpad(k::text, 3, '0')
FROM (VALUES ('movie', 250), ('tv', 250), ('music', 400), ('other', 100)) AS plan(kind, cnt),
     generate_series(1, cnt) k;

SELECT min(id) AS lib_id_min, max(id) AS lib_id_max FROM media_items WHERE type = 0 \gset
SELECT 'libs' AS check, count(*) AS n, :lib_id_min AS id_min, :lib_id_max AS id_max FROM media_items WHERE type = 0;

-- ============================================================
-- 2. user_data_keys 880k
-- ============================================================
INSERT INTO user_data_keys (user_data_key)
SELECT 'sc-key-' || lpad(n::text, 7, '0') FROM generate_series(1, 880000) n;

SELECT min(id) AS key_id_min FROM user_data_keys \gset
SELECT 'user_data_keys' AS check, count(*) AS n, :key_id_min AS id_min FROM user_data_keys;

-- ============================================================
-- 3. 电影 200,000（库偏移 0-249：lib = lib_id_min + (n-1)/800）
-- ============================================================
INSERT INTO media_items (
    guid, type, type_name, parent_id, path, name, sort_name, original_title,
    production_year, community_rating, runtime_ticks, premiere_date,
    date_created, date_modified, date_last_refreshed, date_last_saved,
    presentation_unique_key, is_movie, is_folder, is_virtual_item, container,
    user_data_key_id, data, size, height, width, total_bitrate, media_type
)
SELECT
    gen_random_uuid(), 2, 'MediaBrowser.Controller.Entities.Movies.Movie',
    :lib_id_min + (((n - 1) / 800))::bigint,
    'D:\scale\movie' || lpad(((n - 1) / 800 + 1)::text, 3, '0') || '\Movie-' || lpad(n::text, 6, '0') || '\Movie-' || lpad(n::text, 6, '0') || '.mkv',
    'Movie-' || lpad(n::text, 6, '0'), 'movie-' || lpad(n::text, 6, '0'),
    'Original Movie ' || n::text,
    1970 + (n % 57),
    round((4 + (n % 51) / 10.0)::numeric, 1)::double precision,
    (36000000000 + (n % 90) * 60000000000)::bigint,
    now() - ((n % 20000) || ' days')::interval,
    now() - ((n % 3650) || ' days')::interval,
    now() - ((n % 3650) || ' days')::interval,
    now() - ((n % 365) || ' days')::interval,
    now() - ((n % 365) || ' days')::interval,
    'sc-mv-' || lpad(n::text, 6, '0'),
    true, false, false, 'mkv,webm',
    :key_id_min + n - 1,
    jsonb_build_object(
        'Container', 'mkv,webm', 'VideoType', 'VideoFile', 'IsShortcut', false,
        'HasSubtitles', false, 'IsPlaceHolder', false, 'SubtitleFiles', '[]'::jsonb,
        'RemoteTrailers', '[]'::jsonb, 'AdditionalParts', '[]'::jsonb,
        'LocalAlternateVersions', '[]'::jsonb, 'DefaultVideoStreamIndex', 0,
        'LinkedAlternateVersions', '[]'::jsonb,
        'Name', 'Movie-' || lpad(n::text, 6, '0'),
        'Overview', repeat('A synthetic overview paragraph for scale fixture movie ' || n::text || '. ', 18),
        'Genres', to_jsonb(array['Genre' || (n % 2000 + 1)::text, 'Genre' || ((n + 7) % 2000 + 1)::text]),
        'ProductionYear', 1970 + (n % 57),
        'CommunityRating', round((4 + (n % 51) / 10.0)::numeric, 1)::double precision
    ),
    (700000000 + (n % 300) * 1000000)::bigint,
    360 + (n % 720), 640 + (n % 1280), 8000000 + (n % 20) * 500000, 'Video'
FROM generate_series(1, 200000) n;

SELECT min(id) AS movie_id_min, count(*) AS movie_n FROM media_items WHERE type = 2 \gset
SELECT 'movies' AS check, :movie_n AS n, :movie_id_min AS id_min FROM media_items WHERE type = 2;

-- ============================================================
-- 4. 剧集树：Series 20k → Season 50k → Episode 500k
--    系列归属：tv 库偏移 250-499（lib = lib_id_min + 250 + (s-1)/80）
--    季归属：季 j (1..50k) → 系列 floor((j-1)/2.5)+1（3/2 交替，均值 2.5）
--    集归属：集 e (1..500k) → 季 ((e-1)/10)+1；集的系列 = floor((e-1)/25)+1
-- ============================================================
INSERT INTO media_items (
    guid, type, type_name, parent_id, path, name, sort_name,
    production_year, date_created, date_modified, date_last_refreshed, date_last_saved,
    presentation_unique_key, is_series, is_folder, data
)
SELECT
    gen_random_uuid(), 3, 'MediaBrowser.Controller.Entities.TV.Series',
    :lib_id_min + 250 + (((s - 1) / 80))::bigint,
    'D:\scale\tv' || lpad(((s - 1) / 80 + 1)::text, 3, '0') || '\Series-' || lpad(s::text, 5, '0'),
    'Series-' || lpad(s::text, 5, '0'), 'series-' || lpad(s::text, 5, '0'),
    1990 + (s % 36),
    now() - ((s % 3650) || ' days')::interval,
    now() - ((s % 3650) || ' days')::interval,
    now() - ((s % 365) || ' days')::interval,
    now() - ((s % 365) || ' days')::interval,
    'sc-sr-' || lpad(s::text, 5, '0'), true, true,
    jsonb_build_object(
        'Name', 'Series-' || lpad(s::text, 5, '0'),
        'Overview', repeat('A synthetic series overview for scale fixture ' || s::text || '. ', 12),
        'ProductionYear', 1990 + (s % 36)
    )
FROM generate_series(1, 20000) s;

SELECT min(id) AS series_id_min, count(*) AS series_n FROM media_items WHERE type = 3 \gset
SELECT 'series' AS check, :series_n AS n, :series_id_min AS id_min FROM media_items WHERE type = 3;

INSERT INTO media_items (
    guid, type, type_name, parent_id, path, name, sort_name, index_number,
    date_created, date_modified, date_last_refreshed, date_last_saved,
    presentation_unique_key, is_folder, data
)
SELECT
    gen_random_uuid(), 4, 'MediaBrowser.Controller.Entities.TV.Season',
    :series_id_min + ((j - 1) * 2 / 5),
    'D:\scale\tv-lib\Series-' || lpad((((j - 1) * 2 / 5) + 1)::text, 5, '0') || '\Season ' || lpad((((j - 1) % 3) + 1)::text, 2, '0'),
    'Season ' || lpad((((j - 1) % 3) + 1)::text, 2, '0'),
    'season-' || lpad((((j - 1) % 3) + 1)::text, 2, '0'),
    ((j - 1) % 3) + 1,
    now() - ((j % 3650) || ' days')::interval,
    now() - ((j % 3650) || ' days')::interval,
    now() - ((j % 365) || ' days')::interval,
    now() - ((j % 365) || ' days')::interval,
    'sc-sn-' || lpad(j::text, 5, '0'), true,
    jsonb_build_object(
        'Name', 'Season ' || lpad((((j - 1) % 3) + 1)::text, 2, '0'),
        'IndexNumber', ((j - 1) % 3) + 1,
        'SeriesName', 'Series-' || lpad((((j - 1) * 2 / 5) + 1)::text, 5, '0')
    )
FROM generate_series(1, 50000) j;

SELECT min(id) AS season_id_min, count(*) AS season_n FROM media_items WHERE type = 4 \gset
SELECT 'seasons' AS check, :season_n AS n, :season_id_min AS id_min FROM media_items WHERE type = 4;

INSERT INTO media_items (
    guid, type, type_name, parent_id, path, name, sort_name, index_number, parent_index_number,
    series_id, series_name, production_year, runtime_ticks, premiere_date,
    date_created, date_modified, date_last_refreshed, date_last_saved,
    presentation_unique_key, is_folder, is_virtual_item, container,
    user_data_key_id, data, size, height, width, total_bitrate, media_type
)
SELECT
    gen_random_uuid(), 5, 'MediaBrowser.Controller.Entities.TV.Episode',
    :season_id_min + (((e - 1) / 10))::bigint,
    'D:\scale\tv-lib\Series-' || lpad(((((e - 1) / 25))::bigint + 1)::text, 5, '0') || '\Season ' || lpad((((((e - 1) / 10))::bigint % 3) + 1)::text, 2, '0') || '\Series-' || lpad(((((e - 1) / 25))::bigint + 1)::text, 5, '0') || ' S' || lpad((((((e - 1) / 10))::bigint % 3) + 1)::text, 2, '0') || 'E' || lpad(((((e - 1) % 10) + 1)::text), 2, '0') || '.mkv',
    'Episode-' || lpad(e::text, 6, '0'), 'episode-' || lpad(e::text, 6, '0'),
    ((e - 1) % 10) + 1,
    (((((e - 1) / 10))::bigint % 3) + 1),
    :series_id_min + (((e - 1) / 25))::bigint,
    'Series-' || lpad(((((e - 1) / 25))::bigint + 1)::text, 5, '0'),
    1990 + (e % 36),
    (15000000000 + (e % 45) * 60000000000)::bigint,
    now() - ((e % 20000) || ' days')::interval,
    now() - ((e % 3650) || ' days')::interval,
    now() - ((e % 3650) || ' days')::interval,
    now() - ((e % 365) || ' days')::interval,
    now() - ((e % 365) || ' days')::interval,
    'sc-ep-' || lpad(e::text, 6, '0'), false, false, 'mkv,webm',
    :key_id_min + 200000 + e - 1,
    jsonb_build_object(
        'Container', 'mkv,webm', 'VideoType', 'VideoFile', 'IsShortcut', false,
        'Name', 'Episode-' || lpad(e::text, 6, '0'),
        'IndexNumber', ((e - 1) % 10) + 1,
        'ParentIndexNumber', (((((e - 1) / 10))::bigint % 3) + 1),
        'SeriesName', 'Series-' || lpad(((((e - 1) / 25))::bigint + 1)::text, 5, '0'),
        'Overview', repeat('A synthetic episode overview for scale fixture ' || e::text || '. ', 14),
        'ProductionYear', 1990 + (e % 36),
        'Genres', to_jsonb(array['Genre' || (e % 2000 + 1)::text])
    ),
    (400000000 + (e % 200) * 1000000)::bigint,
    360 + (e % 720), 640 + (e % 1280), 5000000 + (e % 15) * 500000, 'Video'
FROM generate_series(1, 500000) e;

SELECT min(id) AS episode_id_min, count(*) AS episode_n FROM media_items WHERE type = 5 \gset
SELECT 'episodes' AS check, :episode_n AS n, :episode_id_min AS id_min FROM media_items WHERE type = 5;

-- ============================================================
-- 5. 音乐：MusicAlbum 50k → Audio 150k（音乐库偏移 500-899）
-- ============================================================
INSERT INTO media_items (
    guid, type, type_name, parent_id, path, name, sort_name,
    production_year, date_created, date_modified, date_last_refreshed, date_last_saved,
    presentation_unique_key, is_folder, data
)
SELECT
    gen_random_uuid(), 7, 'MediaBrowser.Controller.Entities.Audio.MusicAlbum',
    :lib_id_min + 500 + (((a - 1) / 125))::bigint,
    'D:\scale\music' || lpad(((a - 1) / 125 + 1)::text, 3, '0') || '\Album-' || lpad(a::text, 5, '0'),
    'Album-' || lpad(a::text, 5, '0'), 'album-' || lpad(a::text, 5, '0'),
    1980 + (a % 46),
    now() - ((a % 3650) || ' days')::interval,
    now() - ((a % 3650) || ' days')::interval,
    now() - ((a % 365) || ' days')::interval,
    now() - ((a % 365) || ' days')::interval,
    'sc-al-' || lpad(a::text, 5, '0'), true,
    jsonb_build_object(
        'Name', 'Album-' || lpad(a::text, 5, '0'),
        'ProductionYear', 1980 + (a % 46),
        'Artists', to_jsonb(array['Artist-' || (a % 5000 + 1)::text]),
        'AlbumArtists', to_jsonb(array['Artist-' || (a % 5000 + 1)::text])
    )
FROM generate_series(1, 50000) a;

SELECT min(id) AS album_id_min, count(*) AS album_n FROM media_items WHERE type = 7 \gset
SELECT 'albums' AS check, :album_n AS n, :album_id_min AS id_min FROM media_items WHERE type = 7;

INSERT INTO media_items (
    guid, type, type_name, parent_id, path, name, sort_name, index_number,
    album_id, production_year, runtime_ticks,
    date_created, date_modified, date_last_refreshed, date_last_saved,
    presentation_unique_key, is_folder, is_virtual_item, container,
    user_data_key_id, data, size, total_bitrate, media_type
)
SELECT
    gen_random_uuid(), 6, 'MediaBrowser.Controller.Entities.Audio.Audio',
    :album_id_min + (((t - 1) / 3))::bigint,
    'D:\scale\music-lib\Album-' || lpad(((((t - 1) / 3))::bigint + 1)::text, 5, '0') || '\' || lpad((((t - 1) % 3) + 1)::text, 2, '0') || ' - Track-' || lpad(t::text, 6, '0') || '.mp3',
    'Track-' || lpad(t::text, 6, '0'), 'track-' || lpad(t::text, 6, '0'),
    ((t - 1) % 3) + 1,
    :album_id_min + (((t - 1) / 3))::bigint,
    1980 + (t % 46),
    (18000000000 + (t % 30) * 10000000000)::bigint,
    now() - ((t % 3650) || ' days')::interval,
    now() - ((t % 3650) || ' days')::interval,
    now() - ((t % 365) || ' days')::interval,
    now() - ((t % 365) || ' days')::interval,
    'sc-au-' || lpad(t::text, 6, '0'), false, false, 'mp3',
    :key_id_min + 700000 + t - 1,
    jsonb_build_object(
        'Container', 'mp3', 'Name', 'Track-' || lpad(t::text, 6, '0'),
        'IndexNumber', ((t - 1) % 3) + 1,
        'Album', 'Album-' || lpad(((((t - 1) / 3))::bigint + 1)::text, 5, '0'),
        'Artists', to_jsonb(array['Artist-' || (((t - 1) / 3) % 5000 + 1)::text]),
        'AlbumArtists', to_jsonb(array['Artist-' || (((t - 1) / 3) % 5000 + 1)::text]),
        'Genres', to_jsonb(array['Genre' || (t % 2000 + 1)::text]),
        'ProductionYear', 1980 + (t % 46)
    ),
    (3000000 + (t % 90) * 100000)::bigint,
    96000 + (t % 64) * 1000, 'Audio'
FROM generate_series(1, 150000) t;

SELECT min(id) AS audio_id_min, count(*) AS audio_n FROM media_items WHERE type = 6 \gset
SELECT 'audio' AS check, :audio_n AS n, :audio_id_min AS id_min FROM media_items WHERE type = 6;

-- ============================================================
-- 6. 其他 30k：Video 20k / Book 5k / Photo 5k（其他库偏移 900-999）
-- ============================================================
INSERT INTO media_items (
    guid, type, type_name, parent_id, path, name, sort_name,
    date_created, date_modified, presentation_unique_key, is_folder, container,
    user_data_key_id, data, media_type
)
SELECT
    gen_random_uuid(), 20, 'MediaBrowser.Controller.Entities.Video',
    :lib_id_min + 900 + (((v - 1) / 300))::bigint,
    'D:\scale\other\Video-' || lpad(v::text, 5, '0') || '.mp4',
    'Video-' || lpad(v::text, 5, '0'), 'video-' || lpad(v::text, 5, '0'),
    now() - ((v % 3650) || ' days')::interval,
    now() - ((v % 3650) || ' days')::interval,
    'sc-vd-' || lpad(v::text, 5, '0'), false, 'mp4',
    :key_id_min + 850000 + v - 1,
    jsonb_build_object('Container', 'mp4', 'Name', 'Video-' || lpad(v::text, 5, '0'),
        'Overview', repeat('A synthetic video overview ' || v::text || '. ', 8)), 'Video'
FROM generate_series(1, 20000) v;

INSERT INTO media_items (guid, type, type_name, parent_id, path, name, sort_name,
    date_created, date_modified, presentation_unique_key, is_folder, user_data_key_id, data)
SELECT
    gen_random_uuid(), 8, 'MediaBrowser.Controller.Entities.Book',
    :lib_id_min + 900 + (((b - 1) / 300))::bigint,
    'D:\scale\other\Book-' || lpad(b::text, 5, '0') || '.pdf',
    'Book-' || lpad(b::text, 5, '0'), 'book-' || lpad(b::text, 5, '0'),
    now() - ((b % 3650) || ' days')::interval,
    now() - ((b % 3650) || ' days')::interval,
    'sc-bk-' || lpad(b::text, 5, '0'), false,
    :key_id_min + 870000 + b - 1,
    jsonb_build_object('Name', 'Book-' || lpad(b::text, 5, '0'))
FROM generate_series(1, 5000) b;

INSERT INTO media_items (guid, type, type_name, parent_id, path, name, sort_name,
    date_created, date_modified, presentation_unique_key, is_folder, user_data_key_id, data)
SELECT
    gen_random_uuid(), 9, 'MediaBrowser.Controller.Entities.Photo',
    :lib_id_min + 900 + (((p - 1) / 300))::bigint,
    'D:\scale\other\Photo-' || lpad(p::text, 5, '0') || '.jpg',
    'Photo-' || lpad(p::text, 5, '0'), 'photo-' || lpad(p::text, 5, '0'),
    now() - ((p % 3650) || ' days')::interval,
    now() - ((p % 3650) || ' days')::interval,
    'sc-ph-' || lpad(p::text, 5, '0'), false,
    :key_id_min + 875000 + p - 1,
    jsonb_build_object('Name', 'Photo-' || lpad(p::text, 5, '0'))
FROM generate_series(1, 5000) p;

-- ============================================================
-- 7. Person 100k（item_people 引用）
-- ============================================================
INSERT INTO media_items (guid, type, type_name, name, path, is_folder, data, sort_name, clean_name)
SELECT gen_random_uuid(), 11, 'MediaBrowser.Controller.Entities.Person',
       'Person-' || lpad(n::text, 6, '0'), null, false,
       jsonb_build_object('Name', 'Person-' || lpad(n::text, 6, '0')),
       'person-' || lpad(n::text, 6, '0'), 'person-' || lpad(n::text, 6, '0')
FROM generate_series(1, 100000) n;

SELECT min(id) AS person_id_min, count(*) AS person_n FROM media_items WHERE type = 11 \gset
SELECT 'persons' AS check, :person_n AS n, :person_id_min AS id_min FROM media_items WHERE type = 11;

-- ============================================================
-- 8. item_extradata_types 种子（pg_dump -s 不带数据）
-- ============================================================
INSERT INTO item_extradata_types (extradata_type_id, name) VALUES
    (0, 'Artists'), (1, 'AlbumArtists'), (2, 'Genres'), (3, 'Studios'), (4, 'Tags'), (6, 'InheritedTags')
ON CONFLICT DO NOTHING;

-- ============================================================
-- 9. item_extradata ≈ 2.1M
-- ============================================================
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 2, 'Genre' || ((m.id % 2000) + 1), lower('Genre' || ((m.id % 2000) + 1)) FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 2, 'Genre' || (((m.id + 7) % 2000) + 1), lower('Genre' || (((m.id + 7) % 2000) + 1)) FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 2, 'Genre' || ((m.id % 2000) + 1), lower('Genre' || ((m.id % 2000) + 1)) FROM media_items m WHERE m.type = 5 ON CONFLICT DO NOTHING;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 2, 'Genre' || (((m.id + 7) % 2000) + 1), lower('Genre' || (((m.id + 7) % 2000) + 1)) FROM media_items m WHERE m.type = 5 ON CONFLICT DO NOTHING;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 3, 'Studio' || ((m.id % 500) + 1), lower('Studio' || ((m.id % 500) + 1)) FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 4, 'Tag' || ((m.id % 1000) + 1), lower('Tag' || ((m.id % 1000) + 1)) FROM media_items m WHERE m.type IN (2, 5) AND m.id % 2 = 0;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 0, 'Artist-' || ((m.id % 5000) + 1), lower('Artist-' || ((m.id % 5000) + 1)) FROM media_items m WHERE m.type = 6 ON CONFLICT DO NOTHING;
INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, 1, 'Artist-' || ((m.id % 5000) + 1), lower('Artist-' || ((m.id % 5000) + 1)) FROM media_items m WHERE m.type = 6 ON CONFLICT DO NOTHING;

-- ============================================================
-- 10. item_people ≈ 2.25M（电影×3 + 剧集×3 + 音频×1）
-- ============================================================
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, :person_id_min + ((m.id * 7) % 100000), 'Director', 0, 0, 'Director', 0
FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, :person_id_min + ((m.id * 13) % 100000), 'Actor', 1, 1, 'Actor', 1
FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, :person_id_min + ((m.id * 29) % 100000), 'Actor', 1, 2, 'Actor', 2
FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, :person_id_min + ((m.id * 7) % 100000), 'Director', 0, 0, 'Director', 0
FROM media_items m WHERE m.type = 5 ON CONFLICT DO NOTHING;
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, :person_id_min + ((m.id * 13) % 100000), 'Actor', 1, 1, 'Actor', 1
FROM media_items m WHERE m.type = 5 ON CONFLICT DO NOTHING;
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, :person_id_min + ((m.id * 29) % 100000), 'Writer', 2, 2, 'Writer', 2
FROM media_items m WHERE m.type = 5 ON CONFLICT DO NOTHING;
INSERT INTO item_people (item_id, person_id, role, person_type, list_order, person_type_name, sort_order)
SELECT m.id, :person_id_min + ((m.id * 11) % 100000), 'Artist', 3, 0, 'Artist', 0
FROM media_items m WHERE m.type = 6 ON CONFLICT DO NOTHING;

-- ============================================================
-- 11. ancestor_ids ≈ 2.2M（按树深：集 3 / 季 2 / 曲 2 / 其余 1）
-- ============================================================
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, m.parent_id, 1 FROM media_items m WHERE m.type = 2 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, m.parent_id, 1 FROM media_items m WHERE m.type = 3 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, m.parent_id, 1 FROM media_items m WHERE m.type = 7 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, m.parent_id, 1 FROM media_items m WHERE m.type IN (8, 9, 20) ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, m.parent_id, 1 FROM media_items m WHERE m.type = 4 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT s.id, sr.parent_id, 2 FROM media_items s JOIN media_items sr ON sr.id = s.parent_id WHERE s.type = 4 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, m.parent_id, 1 FROM media_items m WHERE m.type = 6 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT a.id, al.parent_id, 2 FROM media_items a JOIN media_items al ON al.id = a.parent_id WHERE a.type = 6 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT m.id, m.parent_id, 1 FROM media_items m WHERE m.type = 5 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT e.id, sn.parent_id, 2 FROM media_items e JOIN media_items sn ON sn.id = e.parent_id WHERE e.type = 5 ON CONFLICT DO NOTHING;
INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
SELECT e.id, sr.parent_id, 3 FROM media_items e JOIN media_items sn ON sn.id = e.parent_id JOIN media_items sr ON sr.id = sn.parent_id WHERE e.type = 5 ON CONFLICT DO NOTHING;

-- ============================================================
-- 12. media_streams ≈ 1.59M（视频类×2 + 音频×1）
-- ============================================================
INSERT INTO media_streams (item_id, stream_index, stream_type, codec, language, profile, width, height, is_interlaced, bit_rate)
SELECT m.id, 0, 0, 'h264', 'eng', 'High', 640 + (m.id % 1280), 360 + (m.id % 720), false, 8000000 + (m.id % 20) * 500000
FROM media_items m WHERE m.type IN (2, 5, 20) ON CONFLICT DO NOTHING;
INSERT INTO media_streams (item_id, stream_index, stream_type, codec, language, channel_layout, profile, is_interlaced, bit_rate)
SELECT m.id, 1, 1, 'aac', 'eng', 'mono', 'LC', false, 192000
FROM media_items m WHERE m.type IN (2, 5, 20) ON CONFLICT DO NOTHING;
INSERT INTO media_streams (item_id, stream_index, stream_type, codec, language, channel_layout, profile, is_interlaced, bit_rate)
SELECT m.id, 0, 1, 'mp3', 'eng', 'stereo', 'LC', false, 128000
FROM media_items m WHERE m.type = 6 ON CONFLICT DO NOTHING;

-- ============================================================
-- 13. local_users 10,000（S7 用户压测基础）
-- ============================================================
INSERT INTO local_users (guid, data)
SELECT gen_random_uuid(), jsonb_build_object('Name', 'scale-user-' || lpad(n::text, 5, '0'), 'Password', null)
FROM generate_series(1, 10000) n;

SELECT min(id) AS user_id_min, count(*) AS user_n FROM local_users \gset
SELECT 'local_users' AS check, :user_n AS n, :user_id_min AS id_min FROM local_users;

-- ============================================================
-- 14. user_datas 1M（10k 用户 × 100 条，确定性伪随机：key = ((u*7919 + i*104729) % 880000)）
-- ============================================================
INSERT INTO user_datas (user_data_key_id, user_id, rating, played, play_count, is_favorite, playback_position_ticks, last_played_date)
SELECT
    :key_id_min + (((u * 7919 + i * 104729) % 880000)),
    :user_id_min + u - 1,
    round((1 + ((u + i) % 10) / 2.0)::numeric, 1)::double precision,
    (u + i) % 3 = 0,
    CASE WHEN (u + i) % 3 = 0 THEN 1 + ((u + i) % 5) ELSE 0 END,
    (u + i) % 17 = 0,
    CASE WHEN (u + i) % 3 = 1 THEN ((u + i) % 100) * 1000000 ELSE 0 END,
    CASE WHEN (u + i) % 3 = 0 THEN now() - (((u + i) % 300) || ' days')::interval ELSE NULL END
FROM generate_series(1, 10000) u
CROSS JOIN generate_series(1, 100) i
ON CONFLICT DO NOTHING;

-- ============================================================
-- 15. 验收：类型计数 vs 设计值 + 结构完整性 + blob 重量
-- ============================================================
SELECT '== 类型计数（期望：Episode 500000 / Movie 200000 / Audio 150000 / Season 50000 / MusicAlbum 50000 / Series 20000 / Video 20000 / Book 5000 / Photo 5000 / Person 100000 / 库 1000 / 根 1）==' AS section;
SELECT type, count(*) FROM media_items GROUP BY type ORDER BY count(*) DESC;

SELECT '== 关联表计数（期望：keys 880000 / people ~2.25M / extradata ~2.1M / ancestors ~2.2M / streams ~1.59M / users 10000 / user_datas ~1M）==' AS section;
SELECT 'user_data_keys' AS t, count(*) FROM user_data_keys
UNION ALL SELECT 'item_people', count(*) FROM item_people
UNION ALL SELECT 'item_extradata', count(*) FROM item_extradata
UNION ALL SELECT 'ancestor_ids', count(*) FROM ancestor_ids
UNION ALL SELECT 'media_streams', count(*) FROM media_streams
UNION ALL SELECT 'local_users', count(*) FROM local_users
UNION ALL SELECT 'user_datas', count(*) FROM user_datas;

SELECT '== 结构完整性（期望全 0）==' AS section;
SELECT 'episodes 无季父' AS check, count(*) FROM media_items e WHERE e.type = 5 AND (e.parent_id IS NULL OR NOT EXISTS (SELECT 1 FROM media_items p WHERE p.id = e.parent_id AND p.type = 4))
UNION ALL SELECT 'seasons 无系列父', count(*) FROM media_items s WHERE s.type = 4 AND (s.parent_id IS NULL OR NOT EXISTS (SELECT 1 FROM media_items p WHERE p.id = s.parent_id AND p.type = 3))
UNION ALL SELECT 'series 无库父', count(*) FROM media_items s WHERE s.type = 3 AND (s.parent_id IS NULL OR NOT EXISTS (SELECT 1 FROM media_items p WHERE p.id = s.parent_id AND p.type = 0))
UNION ALL SELECT 'audio 无专辑父', count(*) FROM media_items a WHERE a.type = 6 AND (a.parent_id IS NULL OR NOT EXISTS (SELECT 1 FROM media_items p WHERE p.id = a.parent_id AND p.type = 7))
UNION ALL SELECT 'movies 无库父', count(*) FROM media_items m WHERE m.type = 2 AND (m.parent_id IS NULL OR NOT EXISTS (SELECT 1 FROM media_items p WHERE p.id = m.parent_id AND p.type = 0));

SELECT '== blob 重量（期望 avg ≥ 1000B）==' AS section;
SELECT type, round(avg(octet_length(data::text))) AS avg_blob_bytes, count(*) FROM media_items WHERE type IN (2, 5) GROUP BY type;

ANALYZE;

-- ============================================================
-- 16. top_parent_id 回填（用户作用域查询的 TopParentIds 过滤依赖；
--     生成器直插不经过服务器保存路径，需显式回填：每条目的库祖先 GUID）
-- ============================================================
UPDATE media_items m SET top_parent_id = l.guid::text
FROM ancestor_ids a, media_items l
WHERE a.item_id = m.id AND l.id = a.ancestor_id AND l.type = 0;
