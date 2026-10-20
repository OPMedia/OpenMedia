INSERT INTO media_items (guid, type, type_name, name, sort_name, clean_name, presentation_unique_key,
    is_folder, is_virtual_item, width, height, is_movie, is_series,
    production_year, community_rating, official_rating, inherited_parental_rating_value,
    media_type, provider_ids, overview, owner_id, index_number, parent_index_number,
    premiere_date, start_date, end_date, external_id, top_parent_id,
    series_presentation_unique_key, unrated_type, container, is_locked, original_title,
    trailer_types, extra_ids, date_created, date_last_saved)
SELECT
    ('00000000-0000-0000-0000-' || lpad(g::text, 12, '0'))::uuid,
    (g % 12),
    (ARRAY['Folder','CollectionFolder','Movie','Series','Season','Episode','MusicAlbum','Audio','Book','MusicArtist','Genre','Video'])[1 + (g % 12)],
    'item-' || g,
    'item-' || lpad(g::text, 3, '0'),
    'item ' || g,
    'puk-' || (g % 7),
    (g % 4 = 0),
    (g % 5 = 0),
    CASE WHEN g % 6 = 0 THEN NULL ELSE 400 + 1200 * (g % 4) END,
    CASE WHEN g % 7 = 0 THEN NULL ELSE 300 + 200 * (g % 3) END,
    (g % 3 = 0),
    (g % 4 = 1),
    1990 + (g % 30),
    CASE WHEN g % 5 = 0 THEN NULL ELSE (g % 10) * 0.5 END,
    CASE WHEN g % 6 = 0 THEN NULL ELSE (ARRAY['G','PG','PG-13','R','TV-MA'])[1 + (g % 5)] END,
    CASE WHEN g % 7 = 0 THEN NULL ELSE (g % 12) END,
    (ARRAY['Video','Audio','Book','Photo', NULL])[1 + (g % 5)],
    CASE WHEN g % 8 = 0 THEN NULL ELSE jsonb_build_object('Imdb', 'tt' || g, 'Tmdb', (g % 50)::text) END,
    CASE WHEN g % 9 = 0 THEN NULL ELSE 'overview ' || g END,
    CASE WHEN g % 10 = 0 THEN NULL ELSE 'owner-' || (g % 3) END,
    CASE WHEN g % 11 = 0 THEN NULL ELSE (g % 5) END,
    CASE WHEN g % 12 = 0 THEN NULL ELSE (g % 3) END,
    CASE WHEN g % 4 = 0 THEN NULL ELSE TIMESTAMPTZ '2020-01-01Z' + (g || ' days')::interval END,
    CASE WHEN g % 5 = 0 THEN NULL ELSE TIMESTAMPTZ '2021-06-01Z' + (g || ' days')::interval END,
    CASE WHEN g % 6 = 0 THEN NULL ELSE TIMESTAMPTZ '2022-01-01Z' + ((g * 7) || ' days')::interval END,
    CASE WHEN g % 7 = 0 THEN NULL ELSE 'ext-' || g END,
    CASE WHEN g % 3 = 0 THEN NULL ELSE 'tp-' || (g % 6) END,
    CASE WHEN g % 4 = 1 THEN 'spuk-' || (g % 4) ELSE NULL END,
    CASE WHEN g % 8 = 0 THEN NULL ELSE (ARRAY[NULL,'Unrated','UserUnrated'])[1 + (g % 3)] END,
    CASE WHEN g % 5 = 0 THEN NULL ELSE (ARRAY['mp4','mkv','avi'])[1 + (g % 3)] END,
    (g % 13 = 0),
    CASE WHEN g % 9 = 0 THEN NULL ELSE 'Item ' || g END,
    CASE WHEN g % 10 = 0 THEN NULL ELSE (ARRAY['Trailer','Clip'])[1 + (g % 2)] END,
    CASE WHEN g % 11 = 0 THEN NULL ELSE 'e' || g END,
    TIMESTAMPTZ '2023-01-01Z' + (g || ' hours')::interval,
    TIMESTAMPTZ '2024-01-01Z' + ((g * 3) || ' hours')::interval
FROM generate_series(1, 40) AS g
ON CONFLICT (guid, type) DO NOTHING;

INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
SELECT m.id, t.type_id, t.label, lower(t.label)
FROM media_items m
CROSS JOIN (VALUES
    (4, 'Sports'), (4, 'News'), (4, 'Kids'), (4, 'Favorite'),
    (2, 'Action'), (2, 'Comedy'),
    (3, 'Studio A'), (3, 'Studio B'),
    (0, 'Artist 1'), (0, 'Artist 2')) AS t(type_id, label)
WHERE m.name IN ('item-1','item-3','item-5','item-7','item-9','item-11','item-13','item-15')
ON CONFLICT (item_id, extradata_type_id, value) DO NOTHING;

INSERT INTO user_data_keys (user_data_key) VALUES ('diff-key-1') ON CONFLICT (user_data_key) DO NOTHING;

INSERT INTO user_datas (user_data_key_id, user_id, played, is_favorite, play_count, playback_position_ticks, last_played_date)
SELECT k.id, 42,
    (m.name IN ('item-2','item-4','item-6')),
    (m.name IN ('item-2','item-8','item-12')),
    CASE WHEN m.name IN ('item-2','item-4') THEN 3 ELSE 0 END,
    CASE WHEN m.name = 'item-4' THEN 1000 ELSE 0 END,
    CASE WHEN m.name IN ('item-2','item-6') THEN TIMESTAMPTZ '2024-05-01Z' ELSE NULL END
FROM media_items m
CROSS JOIN user_data_keys k
WHERE k.user_data_key = 'diff-key-1'
  AND m.name IN ('item-2','item-4','item-6','item-8','item-10','item-12','item-14','item-16')
ON CONFLICT (user_data_key_id, user_id) DO NOTHING;

INSERT INTO media_streams (item_id, stream_index, stream_type, codec, language, is_external)
SELECT m.id, 0, 0,
    CASE WHEN m.name IN ('item-1','item-3') THEN 'aac' ELSE 'ac3' END,
    CASE WHEN m.name IN ('item-1','item-5') THEN 'eng' ELSE 'zho' END,
    FALSE
FROM media_items m
WHERE m.name IN ('item-1','item-3','item-5','item-7','item-9','item-11','item-13','item-15')
ON CONFLICT (item_id, stream_index) DO NOTHING;

INSERT INTO media_streams (item_id, stream_index, stream_type, codec, language, is_external)
SELECT m.id, 1, 1,
    CASE WHEN m.name IN ('item-1','item-7') THEN 'subrip' ELSE 'ass' END,
    CASE WHEN m.name IN ('item-1','item-9') THEN 'eng' ELSE 'jpn' END,
    (m.name IN ('item-9','item-15'))
FROM media_items m
WHERE m.name IN ('item-1','item-3','item-5','item-7','item-9','item-11','item-13','item-15')
ON CONFLICT (item_id, stream_index) DO NOTHING;

INSERT INTO chapters (item_id, chapter_index, start_position_ticks, image_path)
SELECT m.id, 0, 0, 'chapter.jpg'
FROM media_items m
WHERE m.name IN ('item-2','item-6','item-10','item-14')
ON CONFLICT (item_id, chapter_index) DO NOTHING;

UPDATE media_items
SET images = jsonb_build_object('Primary', jsonb_build_object('Path','p.jpg'))
WHERE name IN ('item-1','item-4','item-8','item-12','item-16');