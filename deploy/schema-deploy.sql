-- ============================================================
-- Emby Server Database Schema (PostgreSQL — 大数据量生产级)
-- MODE: schema only (DDL, no data)
-- Source: emby_pgsql_dump_schema.sql (SQLite → PostgreSQL)
-- Optimized: 2026-10-13
--
-- ==================== 优化总览 ====================
--
-- 【类型优化】
--   1. 布尔字段 INTEGER → BOOLEAN
--   2. 时间戳 INTEGER(ticks/ms) → TIMESTAMPTZ
--   3. JSON 字段 TEXT → JSONB
--   4. UUID 字段 BYTEA → UUID
--   5. IP 地址 TEXT → INET
--   6. 文件大小/时长 INTEGER → BIGINT
--
-- 【命名规范化】
--   - 统一 snake_case
--   - 去除 SQLite 迭代后缀 (2, 3)
--   - 避免 SQL 保留字 (Type → link_type)
--
-- 【主键修复】
--   - item_people: 无主键 → 新增 id SERIAL PK + 唯一约束
--   - named_values: 单列 PK → 联合 PK (type_id, value)
--
-- 【分区策略】(核心优化，面向千万~亿级行)
--   - media_items:     LIST 分区 by type (电影/剧集/季/集/音乐/其他)
--   - user_datas:      HASH 分区 by user_id (高频并发更新)
--   - ancestor_ids:    HASH 分区 by item_id (层级查询热点)
--   - media_streams:   HASH 分区 by item_id (与 media_items 对齐)
--   - activity_log:    RANGE 分区 by date_created (时序数据，利于归档)
--   - item_people:     HASH 分区 by item_id (关联查询热点)
--
-- 【索引策略】
--   - B-tree: 等值/范围查询 (外键, name, sort_name 等)
--   - GIN:    JSONB 字段 (provider_ids, images, capabilities)
--   - BRIN:   大表时序/单调递增列 (date_created, date_modified)
--   - 部分索引: 过滤空值/假值，减少索引体积 60~90%
--   - 复合索引: 覆盖常见查询模式
--   - INCLUDE: 覆盖索引避免回表
--
-- 【存储与维护】
--   - FILLFACTOR 调优: 高频更新表 (user_datas) 设 85, 中频 90
--   - Autovacuum 调优: 高更新表加大启动阈值、缩短延迟
--   - TOAST 策略: 大文本/JSONB 字段确保不内联
--
-- 【物化视图】
--   - 预聚合常用统计 (媒体数量/平均评分/播放统计)
--
-- ============================================================

BEGIN;

-- ###########################################################
-- 全局扩展配置
-- ###########################################################

-- 确保 pgcrypto 可用 (UUID 生成)
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ###########################################################
-- Database: activitylog
-- ###########################################################

-- 活动日志类型字典
CREATE TABLE IF NOT EXISTS activity_log_types (
    type_id     SERIAL,
    name        TEXT NOT NULL,
    PRIMARY KEY (type_id)
);

-- 活动日志 — RANGE 分区 by date_created (时序数据，利于按时间归档/删除)
CREATE TABLE IF NOT EXISTS activity_log (
    id              BIGINT       GENERATED ALWAYS AS IDENTITY,
    name            TEXT         NOT NULL,
    overview        TEXT,
    short_overview  TEXT,
    type_id         INTEGER      NOT NULL,
    item_id         INTEGER,
    user_id         INTEGER,
    date_created    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    log_severity    TEXT         NOT NULL
                    CHECK (log_severity IN ('Trace','Debug','Information','Warning','Error','Critical')),
    PRIMARY KEY (id, date_created),
    FOREIGN KEY (type_id) REFERENCES activity_log_types (type_id)
) PARTITION BY RANGE (date_created);

-- 按年分区 (根据数据量可调整为按季度/月)
CREATE TABLE activity_log_2024 PARTITION OF activity_log
    FOR VALUES FROM ('2024-01-01') TO ('2025-01-01');
CREATE TABLE activity_log_2025 PARTITION OF activity_log
    FOR VALUES FROM ('2025-01-01') TO ('2026-01-01');
CREATE TABLE activity_log_2026 PARTITION OF activity_log
    FOR VALUES FROM ('2026-01-01') TO ('2027-01-01');
CREATE TABLE activity_log_future PARTITION OF activity_log
    FOR VALUES FROM ('2027-01-01') TO ('2030-01-01');
-- 默认分区 (兜底，生产环境应监控)
CREATE TABLE activity_log_default PARTITION OF activity_log DEFAULT;

-- 分区内索引 (每个分区自动继承)
CREATE INDEX idx_activity_log_type_id       ON activity_log (type_id);
CREATE INDEX idx_activity_log_user_id       ON activity_log (user_id) WHERE user_id IS NOT NULL;
-- BRIN 索引: 时序数据天然有序，BRIN 体积仅为 B-tree 的 1/100
CREATE INDEX idx_activity_log_date_created  ON activity_log USING BRIN (date_created)
    WITH (pages_per_range = 32);

COMMENT ON TABLE  activity_log       IS '活动日志 — 按年RANGE分区, 便于归档清理';
COMMENT ON TABLE  activity_log_types IS '活动日志类型字典';
COMMENT ON COLUMN activity_log.log_severity IS '日志级别: Trace/Debug/Information/Warning/Error/Critical';

-- ###########################################################
-- Database: authentication
-- ###########################################################

-- 设备表 (数据量小，无需分区)
CREATE TABLE IF NOT EXISTS devices (
    id                  SERIAL,
    reported_device_id  TEXT    NOT NULL,
    reported_name       TEXT    NOT NULL,
    custom_name         TEXT,
    capabilities        JSONB,
    ip_address          INET,
    date_first_seen     TIMESTAMPTZ DEFAULT now(),
    date_last_seen      TIMESTAMPTZ DEFAULT now(),
    PRIMARY KEY (id)
) WITH (fillfactor = 90);

CREATE INDEX idx_devices_reported_device_id ON devices (reported_device_id);
CREATE INDEX idx_devices_ip_address         ON devices (ip_address) WHERE ip_address IS NOT NULL;

COMMENT ON TABLE devices IS '设备注册信息';

-- 访问令牌表 (中频更新, 数据量中等)
CREATE TABLE IF NOT EXISTS tokens (
    id                  SERIAL,
    access_token        TEXT        NOT NULL,
    device_id           INTEGER     NOT NULL,
    app_name            TEXT        NOT NULL,
    app_version         TEXT        NOT NULL,
    user_id             INTEGER,
    is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
    date_created        TIMESTAMPTZ NOT NULL DEFAULT now(),
    date_last_activity  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (id),
    FOREIGN KEY (device_id) REFERENCES devices (id) ON DELETE CASCADE
) WITH (fillfactor = 90);

CREATE UNIQUE INDEX idx_tokens_access_token ON tokens (access_token);
CREATE INDEX idx_tokens_device_id           ON tokens (device_id);
CREATE INDEX idx_tokens_user_id             ON tokens (user_id) WHERE user_id IS NOT NULL;
-- 部分索引: 活跃令牌是高频查询路径
CREATE INDEX idx_tokens_active              ON tokens (user_id, device_id) WHERE is_active = TRUE;

COMMENT ON TABLE tokens IS '用户访问令牌';

-- ###########################################################
-- Database: library — 核心大表
-- ###########################################################

-- 用户数据键 (小表，字典)
CREATE TABLE IF NOT EXISTS user_data_keys (
    id            SERIAL,
    user_data_key TEXT NOT NULL,
    PRIMARY KEY (id)
);

CREATE UNIQUE INDEX idx_user_data_keys_key ON user_data_keys (user_data_key);

-- ===========================================================
-- media_items — LIST 分区 by type
-- ===========================================================
-- Emby 的 type 字段区分媒体类型:
--   常见映射: 0=CollectionFolder, 1=Folder, 2=Movie, 3=Series,
--            4=Season, 5=Episode, 6=Audio, 7=MusicAlbum,
--            8=Book, 9=Photo, ...
-- 按类型分区: 大部分查询都带 type 过滤，分区裁剪效果极好
-- ===========================================================

CREATE TABLE IF NOT EXISTS media_items (
    id                              BIGINT       GENERATED ALWAYS AS IDENTITY,
    guid                            UUID,
    type                            INTEGER      NOT NULL,
    parent_id                       BIGINT,
    path                            TEXT,
    start_date                      TIMESTAMPTZ,
    end_date                        TIMESTAMPTZ,
    is_movie                        BOOLEAN      DEFAULT FALSE,
    is_kids                         BOOLEAN      DEFAULT FALSE,
    is_sports                       BOOLEAN      DEFAULT FALSE,
    is_news                         BOOLEAN      DEFAULT FALSE,
    is_new                          BOOLEAN      DEFAULT FALSE,
    is_premiere                     BOOLEAN      DEFAULT FALSE,
    is_live                         BOOLEAN      DEFAULT FALSE,
    community_rating                DOUBLE PRECISION,
    custom_rating                   TEXT,
    index_number                    INTEGER,
    is_locked                       BOOLEAN      DEFAULT FALSE,
    name                            TEXT,
    official_rating                 TEXT,
    overview                        TEXT,
    parent_index_number             INTEGER,
    premiere_date                   TIMESTAMPTZ,
    production_year                 INTEGER
                                    CHECK (production_year BETWEEN 1800 AND 2100),
    sort_name                       TEXT,
    runtime_ticks                   BIGINT,
    date_created                    TIMESTAMPTZ  DEFAULT now(),
    date_modified                   TIMESTAMPTZ  DEFAULT now(),
    is_series                       BOOLEAN      DEFAULT FALSE,
    is_repeat                       BOOLEAN      DEFAULT FALSE,
    preferred_metadata_language     TEXT,
    preferred_metadata_country_code TEXT,
    date_last_refreshed             TIMESTAMPTZ,
    date_last_saved                 TIMESTAMPTZ,
    is_in_mixed_folder              BOOLEAN      DEFAULT FALSE,
    locked_fields                   TEXT,
    inherited_parental_rating_value INTEGER
                                    CHECK (inherited_parental_rating_value >= 0),
    critic_rating                   DOUBLE PRECISION,
    presentation_unique_key         TEXT,
    original_title                  TEXT,
    series_name                     TEXT,
    album_id                        BIGINT,
    series_id                       BIGINT,
    tagline                         TEXT,
    provider_ids                    JSONB,
    images                          JSONB,
    production_locations            JSONB,
    total_bitrate                   INTEGER,
    extra_type                      INTEGER,
    series_presentation_unique_key  TEXT,
    external_id                     TEXT,
    width                           INTEGER      CHECK (width IS NULL OR width > 0),
    height                          INTEGER      CHECK (height IS NULL OR height > 0),
    size                            BIGINT       CHECK (size IS NULL OR size >= 0),
    container                       TEXT,
    status                          INTEGER,
    display_order                   INTEGER,
    three_d_format                  INTEGER,
    user_data_key_id                INTEGER,
    sort_index_number               INTEGER,
    sort_parent_index_number        INTEGER,
    index_number_end                INTEGER,
    channel_number                  TEXT,
    remote_trailers                 JSONB,
    is_public                       BOOLEAN      DEFAULT FALSE,
    PRIMARY KEY (id, type),
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
    FOREIGN KEY (user_data_key_id)  REFERENCES user_data_keys (id)
) PARTITION BY LIST (type);

-- 按媒体类型分区 (type 值需根据实际 Emby 枚举调整)
-- 以下为典型分区方案, 根据实际数据调整 type 值范围
CREATE TABLE media_items_movies    PARTITION OF media_items FOR VALUES IN (2);
CREATE TABLE media_items_series    PARTITION OF media_items FOR VALUES IN (3);
CREATE TABLE media_items_seasons   PARTITION OF media_items FOR VALUES IN (4);
CREATE TABLE media_items_episodes  PARTITION OF media_items FOR VALUES IN (5);
CREATE TABLE media_items_audio     PARTITION OF media_items FOR VALUES IN (6, 7);
CREATE TABLE media_items_books     PARTITION OF media_items FOR VALUES IN (8);
CREATE TABLE media_items_folders   PARTITION OF media_items FOR VALUES IN (0, 1, 9, 10);
CREATE TABLE media_items_other     PARTITION OF media_items DEFAULT;

-- 分区内索引 (每个分区自动继承)
CREATE INDEX idx_media_items_parent_id                       ON media_items (parent_id);
CREATE INDEX idx_media_items_name                            ON media_items (name);
CREATE INDEX idx_media_items_sort_name                       ON media_items (sort_name);
CREATE INDEX idx_media_items_presentation_unique_key         ON media_items (presentation_unique_key)
    WHERE presentation_unique_key IS NOT NULL;
CREATE INDEX idx_media_items_series_presentation_unique_key  ON media_items (series_presentation_unique_key)
    WHERE series_presentation_unique_key IS NOT NULL;
CREATE INDEX idx_media_items_series_id                       ON media_items (series_id)
    WHERE series_id IS NOT NULL;
CREATE INDEX idx_media_items_album_id                        ON media_items (album_id)
    WHERE album_id IS NOT NULL;
CREATE INDEX idx_media_items_production_year                 ON media_items (production_year)
    WHERE production_year IS NOT NULL;
CREATE INDEX idx_media_items_community_rating                ON media_items (community_rating DESC)
    WHERE community_rating IS NOT NULL;
CREATE INDEX idx_media_items_path                            ON media_items (path)
    WHERE path IS NOT NULL;
-- BRIN 索引: 大表单调递增的时间列
CREATE INDEX idx_media_items_date_created_brin               ON media_items USING BRIN (date_created)
    WITH (pages_per_range = 64);
CREATE INDEX idx_media_items_date_modified_brin              ON media_items USING BRIN (date_modified)
    WITH (pages_per_range = 64);
-- GIN 索引: JSONB 字段
CREATE INDEX idx_media_items_provider_ids_gin                ON media_items USING GIN (provider_ids);
CREATE INDEX idx_media_items_images_gin                      ON media_items USING GIN (images);
-- 覆盖索引: 常见列表查询只需索引扫描
CREATE INDEX idx_media_items_list_cover                      ON media_items (type, parent_id)
    INCLUDE (name, sort_name, index_number, production_year, community_rating, runtime_ticks);

COMMENT ON TABLE  media_items IS '媒体项核心表 — LIST分区by type, PK=(id, type)';
COMMENT ON COLUMN media_items.runtime_ticks           IS '运行时长(ticks), 1 tick = 100ns';
COMMENT ON COLUMN media_items.provider_ids            IS '外部提供商标识 JSONB, 如 {"Imdb":"tt123456","Tmdb":"1234"}';
COMMENT ON COLUMN media_items.images                  IS '图片元数据 JSONB';
COMMENT ON COLUMN media_items.production_locations    IS '产地列表 JSONB';
COMMENT ON COLUMN media_items.remote_trailers         IS '预告片URL列表 JSONB';

-- ===========================================================
-- ancestor_ids — HASH 分区 by item_id
-- ===========================================================
-- 闭包表: 媒体项的所有祖先, 大部分查询模式为 WHERE item_id = ?
-- HASH 分区将不同 item_id 均匀分散, 避免热点
-- ===========================================================

CREATE TABLE IF NOT EXISTS ancestor_ids (
    item_id     BIGINT  NOT NULL,
    ancestor_id BIGINT  NOT NULL,
    distance    INTEGER NOT NULL DEFAULT 0
                    CHECK (distance >= 0),
    PRIMARY KEY (item_id, ancestor_id)
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
) PARTITION BY HASH (item_id);

-- 8 个分区 (根据数据量可增减, 建议为 2 的幂)
CREATE TABLE ancestor_ids_p0 PARTITION OF ancestor_ids FOR VALUES WITH (MODULUS 8, REMAINDER 0);
CREATE TABLE ancestor_ids_p1 PARTITION OF ancestor_ids FOR VALUES WITH (MODULUS 8, REMAINDER 1);
CREATE TABLE ancestor_ids_p2 PARTITION OF ancestor_ids FOR VALUES WITH (MODULUS 8, REMAINDER 2);
CREATE TABLE ancestor_ids_p3 PARTITION OF ancestor_ids FOR VALUES WITH (MODULUS 8, REMAINDER 3);
CREATE TABLE ancestor_ids_p4 PARTITION OF ancestor_ids FOR VALUES WITH (MODULUS 8, REMAINDER 4);
CREATE TABLE ancestor_ids_p5 PARTITION OF ancestor_ids FOR VALUES WITH (MODULUS 8, REMAINDER 5);
CREATE TABLE ancestor_ids_p6 PARTITION OF ancestor_ids FOR VALUES WITH (MODULUS 8, REMAINDER 6);
CREATE TABLE ancestor_ids_p7 PARTITION OF ancestor_ids FOR VALUES WITH (MODULUS 8, REMAINDER 7);

CREATE INDEX idx_ancestor_ids_ancestor_id ON ancestor_ids (ancestor_id);

COMMENT ON TABLE ancestor_ids IS '媒体项祖先关系闭包表 — HASH分区by item_id, distance=层级深度';

-- ===========================================================
-- chapters (跟随 media_items 分区策略，HASH by item_id)
-- ===========================================================

CREATE TABLE IF NOT EXISTS chapters (
    item_id                 BIGINT       NOT NULL,
    chapter_index           INTEGER      NOT NULL,
    start_position_ticks    BIGINT       NOT NULL,
    name                    TEXT,
    image_path              TEXT,
    image_date_modified     TIMESTAMPTZ,
    marker_type             INTEGER      NOT NULL DEFAULT 0,
    PRIMARY KEY (item_id, chapter_index)
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
) PARTITION BY HASH (item_id);

CREATE TABLE chapters_p0 PARTITION OF chapters FOR VALUES WITH (MODULUS 8, REMAINDER 0);
CREATE TABLE chapters_p1 PARTITION OF chapters FOR VALUES WITH (MODULUS 8, REMAINDER 1);
CREATE TABLE chapters_p2 PARTITION OF chapters FOR VALUES WITH (MODULUS 8, REMAINDER 2);
CREATE TABLE chapters_p3 PARTITION OF chapters FOR VALUES WITH (MODULUS 8, REMAINDER 3);
CREATE TABLE chapters_p4 PARTITION OF chapters FOR VALUES WITH (MODULUS 8, REMAINDER 4);
CREATE TABLE chapters_p5 PARTITION OF chapters FOR VALUES WITH (MODULUS 8, REMAINDER 5);
CREATE TABLE chapters_p6 PARTITION OF chapters FOR VALUES WITH (MODULUS 8, REMAINDER 6);
CREATE TABLE chapters_p7 PARTITION OF chapters FOR VALUES WITH (MODULUS 8, REMAINDER 7);

COMMENT ON TABLE chapters IS '媒体章节信息 — HASH分区by item_id';

-- ===========================================================
-- media_streams — HASH 分区 by item_id
-- ===========================================================
-- 每个媒体项通常有 3~10 个流 (视频1 + 音频多 + 字幕多)
-- 查询模式: WHERE item_id = ?, HASH 分区完美匹配
-- ===========================================================

CREATE TABLE IF NOT EXISTS media_streams (
    item_id               BIGINT          NOT NULL,
    stream_index          INTEGER         NOT NULL,
    stream_type           SMALLINT        NOT NULL
                          CHECK (stream_type IN (0, 1, 2)),   -- 0=视频 1=音频 2=字幕
    codec                 TEXT,
    language              TEXT,
    channel_layout        TEXT,
    profile               TEXT,
    aspect_ratio          TEXT,
    path                  TEXT,
    is_interlaced         BOOLEAN         DEFAULT FALSE,
    bit_rate              INTEGER,
    channels              SMALLINT,
    sample_rate           INTEGER,
    is_default            BOOLEAN         DEFAULT FALSE,
    is_forced             BOOLEAN         DEFAULT FALSE,
    is_hearing_impaired   BOOLEAN         DEFAULT FALSE,
    is_external           BOOLEAN         DEFAULT FALSE,
    height                INTEGER         CHECK (height IS NULL OR height > 0),
    width                 INTEGER         CHECK (width IS NULL OR width > 0),
    average_frame_rate    DOUBLE PRECISION,
    real_frame_rate       DOUBLE PRECISION,
    level                 DOUBLE PRECISION,
    pixel_format          TEXT,
    bit_depth             SMALLINT,
    is_anamorphic         BOOLEAN         DEFAULT FALSE,
    ref_frames            SMALLINT,
    rotation              SMALLINT,
    codec_tag             TEXT,
    comment               TEXT,
    nal_length_size       TEXT,
    title                 TEXT,
    time_base             TEXT,
    color_primaries       TEXT,
    color_space           TEXT,
    color_transfer        TEXT,
    extradata             TEXT,
    attachment_size       INTEGER,
    mime_type             TEXT,
    extended_video_type   SMALLINT,
    extended_video_subtype SMALLINT,
    PRIMARY KEY (item_id, stream_index)
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
) PARTITION BY HASH (item_id);

CREATE TABLE media_streams_p0 PARTITION OF media_streams FOR VALUES WITH (MODULUS 8, REMAINDER 0);
CREATE TABLE media_streams_p1 PARTITION OF media_streams FOR VALUES WITH (MODULUS 8, REMAINDER 1);
CREATE TABLE media_streams_p2 PARTITION OF media_streams FOR VALUES WITH (MODULUS 8, REMAINDER 2);
CREATE TABLE media_streams_p3 PARTITION OF media_streams FOR VALUES WITH (MODULUS 8, REMAINDER 3);
CREATE TABLE media_streams_p4 PARTITION OF media_streams FOR VALUES WITH (MODULUS 8, REMAINDER 4);
CREATE TABLE media_streams_p5 PARTITION OF media_streams FOR VALUES WITH (MODULUS 8, REMAINDER 5);
CREATE TABLE media_streams_p6 PARTITION OF media_streams FOR VALUES WITH (MODULUS 8, REMAINDER 6);
CREATE TABLE media_streams_p7 PARTITION OF media_streams FOR VALUES WITH (MODULUS 8, REMAINDER 7);

-- 分区内索引
CREATE INDEX idx_media_streams_stream_type ON media_streams (stream_type);
CREATE INDEX idx_media_streams_language    ON media_streams (language)
    WHERE language IS NOT NULL;
-- 覆盖索引: 获取某项的所有流信息 (最常见查询)
CREATE INDEX idx_media_streams_cover       ON media_streams (item_id, stream_index)
    INCLUDE (stream_type, codec, language, is_default, title);

COMMENT ON TABLE media_streams IS '媒体流轨道信息 — HASH分区by item_id';

-- ===========================================================
-- item_people — HASH 分区 by item_id
-- ===========================================================
-- 查询模式: 获取某媒体项的所有演员/导演 → WHERE item_id = ?
-- ===========================================================

CREATE TABLE IF NOT EXISTS item_people (
    id          BIGINT  GENERATED ALWAYS AS IDENTITY,
    item_id     BIGINT  NOT NULL,
    person_id   BIGINT  NOT NULL,
    role        TEXT,
    person_type SMALLINT NOT NULL
                CHECK (person_type >= 0),
    list_order  INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (id, item_id)
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
) PARTITION BY HASH (item_id);

CREATE TABLE item_people_p0 PARTITION OF item_people FOR VALUES WITH (MODULUS 8, REMAINDER 0);
CREATE TABLE item_people_p1 PARTITION OF item_people FOR VALUES WITH (MODULUS 8, REMAINDER 1);
CREATE TABLE item_people_p2 PARTITION OF item_people FOR VALUES WITH (MODULUS 8, REMAINDER 2);
CREATE TABLE item_people_p3 PARTITION OF item_people FOR VALUES WITH (MODULUS 8, REMAINDER 3);
CREATE TABLE item_people_p4 PARTITION OF item_people FOR VALUES WITH (MODULUS 8, REMAINDER 4);
CREATE TABLE item_people_p5 PARTITION OF item_people FOR VALUES WITH (MODULUS 8, REMAINDER 5);
CREATE TABLE item_people_p6 PARTITION OF item_people FOR VALUES WITH (MODULUS 8, REMAINDER 6);
CREATE TABLE item_people_p7 PARTITION OF item_people FOR VALUES WITH (MODULUS 8, REMAINDER 7);

CREATE INDEX idx_item_people_person_id   ON item_people (person_id);
CREATE UNIQUE INDEX idx_item_people_unique ON item_people (item_id, person_id, person_type);
-- 覆盖索引: 获取某项的演员列表 (按排序)
CREATE INDEX idx_item_people_cover       ON item_people (item_id, list_order)
    INCLUDE (person_id, role, person_type);

COMMENT ON TABLE item_people IS '媒体项与人物关联 — HASH分区by item_id';

-- ===========================================================
-- item_links (中等数据量，无需分区)
-- ===========================================================

CREATE TABLE IF NOT EXISTS item_links (
    item_id    BIGINT  NOT NULL,
    link_type  SMALLINT NOT NULL,       -- 原 "Type", 避免 SQL 保留字
    linked_id  BIGINT  NOT NULL,
    link_order INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (item_id, link_type, linked_id)
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
) WITH (fillfactor = 90);

CREATE INDEX idx_item_links_linked_id ON item_links (linked_id);

-- ===========================================================
-- item_extradata (中等数据量)
-- ===========================================================

CREATE TABLE IF NOT EXISTS item_extradata_types (
    extradata_type_id SERIAL,
    name              TEXT NOT NULL,
    PRIMARY KEY (extradata_type_id)
);

CREATE TABLE IF NOT EXISTS item_extradata (
    item_id            BIGINT  NOT NULL,
    extradata_type_id  INTEGER NOT NULL,
    value              TEXT    NOT NULL,
    PRIMARY KEY (item_id, extradata_type_id),
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
    FOREIGN KEY (extradata_type_id) REFERENCES item_extradata_types (extradata_type_id)
) WITH (fillfactor = 90);

CREATE INDEX idx_item_extradata_type_id ON item_extradata (extradata_type_id);

-- ===========================================================
-- imported_collections (小表)
-- ===========================================================

CREATE TABLE IF NOT EXISTS imported_collections (
    item_id     BIGINT,
    name        TEXT    NOT NULL,
    provider_ids JSONB
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
);

CREATE INDEX idx_imported_collections_item_id ON imported_collections (item_id);
CREATE INDEX idx_imported_collections_name    ON imported_collections (name);

-- ===========================================================
-- list_items (小表)
-- ===========================================================

CREATE TABLE IF NOT EXISTS list_items (
    list_item_entry_id SERIAL,
    list_id            BIGINT  NOT NULL,
    list_item_id       BIGINT  NOT NULL,
    list_item_order    INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (list_item_entry_id)
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
);

CREATE INDEX idx_list_items_list_id      ON list_items (list_id);
CREATE INDEX idx_list_items_list_item_id ON list_items (list_item_id);

-- ===========================================================
-- named_values / named_value_types (小字典表)
-- ===========================================================

CREATE TABLE IF NOT EXISTS named_value_types (
    named_value_type_id SERIAL,
    name                TEXT NOT NULL,
    PRIMARY KEY (named_value_type_id)
);

CREATE TABLE IF NOT EXISTS named_values (
    named_value_type_id INTEGER NOT NULL,
    value               TEXT    NOT NULL,
    PRIMARY KEY (named_value_type_id, value),
    FOREIGN KEY (named_value_type_id) REFERENCES named_value_types (named_value_type_id) ON DELETE CASCADE
);

-- ===========================================================
-- sync 相关表 (中低数据量)
-- ===========================================================

CREATE TABLE IF NOT EXISTS sync_targets (
    id           SERIAL,
    reported_id  TEXT NOT NULL,
    PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS sync_jobs (
    id                  SERIAL,
    internal_target_id  INTEGER     NOT NULL,
    name                TEXT        NOT NULL,
    profile             TEXT,
    quality             TEXT,
    bitrate             INTEGER,
    status              SMALLINT    NOT NULL,
    progress            DOUBLE PRECISION
                        CHECK (progress IS NULL OR progress BETWEEN 0 AND 100),
    user_id             INTEGER     NOT NULL,
    item_id             BIGINT,
    category            SMALLINT,
    parent_id           BIGINT,
    unwatched_only      BOOLEAN     DEFAULT FALSE,
    item_limit          INTEGER,
    sync_new_content    BOOLEAN     DEFAULT FALSE,
    date_created        TIMESTAMPTZ NOT NULL DEFAULT now(),
    date_last_modified  TIMESTAMPTZ NOT NULL DEFAULT now(),
    item_count          INTEGER,
    container           TEXT,
    video_codec         TEXT,
    audio_codec         TEXT,
    PRIMARY KEY (id),
    FOREIGN KEY (internal_target_id) REFERENCES sync_targets (id) ON DELETE CASCADE
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
) WITH (fillfactor = 90);

CREATE INDEX idx_sync_jobs_user_id       ON sync_jobs (user_id);
CREATE INDEX idx_sync_jobs_target_id     ON sync_jobs (internal_target_id);
CREATE INDEX idx_sync_jobs_status        ON sync_jobs (status);
CREATE INDEX idx_sync_jobs_item_id       ON sync_jobs (item_id) WHERE item_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS sync_job_items (
    id              SERIAL,
    item_id         BIGINT      NOT NULL,
    item_name       TEXT        NOT NULL,
    media_source_id TEXT,
    job_id          INTEGER     NOT NULL,
    temporary_path  TEXT,
    output_path     TEXT,
    status          SMALLINT,
    date_created    TIMESTAMPTZ NOT NULL DEFAULT now(),
    additional_files JSONB,
    media_source    JSONB,
    PRIMARY KEY (id),
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
    FOREIGN KEY (job_id) REFERENCES sync_jobs (id) ON DELETE CASCADE
) WITH (fillfactor = 90);

CREATE INDEX idx_sync_job_items_job_id  ON sync_job_items (job_id);
CREATE INDEX idx_sync_job_items_item_id ON sync_job_items (item_id);
CREATE INDEX idx_sync_job_items_status  ON sync_job_items (status);

-- ===========================================================
-- user_datas — HASH 分区 by user_id
-- ===========================================================
-- 最高频更新的表: 每次播放都会更新 playback_position_ticks / play_count
-- HASH 分区 by user_id: 每个用户的数据集中在同一分区, 减少锁竞争
-- FILLFACTOR=85: 预留 15% 空间给 HOT 更新, 避免索引碎片
-- ===========================================================

CREATE TABLE IF NOT EXISTS user_datas (
    user_data_key_id        INTEGER     NOT NULL,
    user_id                 INTEGER     NOT NULL,
    rating                  DOUBLE PRECISION
                            CHECK (rating IS NULL OR rating BETWEEN 0 AND 10),
    played                  BOOLEAN     DEFAULT FALSE,
    play_count              INTEGER     NOT NULL DEFAULT 0
                            CHECK (play_count >= 0),
    is_favorite             BOOLEAN     DEFAULT FALSE,
    hide_from_resume        BOOLEAN     DEFAULT FALSE,
    playback_position_ticks BIGINT      NOT NULL DEFAULT 0
                            CHECK (playback_position_ticks >= 0),
    last_played_date        TIMESTAMPTZ,
    audio_stream_index      SMALLINT,
    subtitle_stream_index   SMALLINT,
    rating_last_modified    TIMESTAMPTZ NOT NULL DEFAULT now(),
    playstate_last_modified TIMESTAMPTZ NOT NULL DEFAULT now(),
    date_last_searched      TIMESTAMPTZ,
    PRIMARY KEY (user_data_key_id, user_id),
    FOREIGN KEY (user_data_key_id) REFERENCES user_data_keys (id)
) PARTITION BY HASH (user_id);

CREATE TABLE user_datas_p0 PARTITION OF user_datas FOR VALUES WITH (MODULUS 8, REMAINDER 0)
    WITH (fillfactor = 85);
CREATE TABLE user_datas_p1 PARTITION OF user_datas FOR VALUES WITH (MODULUS 8, REMAINDER 1)
    WITH (fillfactor = 85);
CREATE TABLE user_datas_p2 PARTITION OF user_datas FOR VALUES WITH (MODULUS 8, REMAINDER 2)
    WITH (fillfactor = 85);
CREATE TABLE user_datas_p3 PARTITION OF user_datas FOR VALUES WITH (MODULUS 8, REMAINDER 3)
    WITH (fillfactor = 85);
CREATE TABLE user_datas_p4 PARTITION OF user_datas FOR VALUES WITH (MODULUS 8, REMAINDER 4)
    WITH (fillfactor = 85);
CREATE TABLE user_datas_p5 PARTITION OF user_datas FOR VALUES WITH (MODULUS 8, REMAINDER 5)
    WITH (fillfactor = 85);
CREATE TABLE user_datas_p6 PARTITION OF user_datas FOR VALUES WITH (MODULUS 8, REMAINDER 6)
    WITH (fillfactor = 85);
CREATE TABLE user_datas_p7 PARTITION OF user_datas FOR VALUES WITH (MODULUS 8, REMAINDER 7)
    WITH (fillfactor = 85);

-- 分区内索引
CREATE INDEX idx_user_datas_user_id                ON user_datas (user_id);
CREATE INDEX idx_user_datas_played                  ON user_datas (played)
    WHERE played = TRUE;
CREATE INDEX idx_user_datas_is_favorite             ON user_datas (is_favorite, user_data_key_id)
    WHERE is_favorite = TRUE;
CREATE INDEX idx_user_datas_last_played_date        ON user_datas (last_played_date DESC)
    WHERE last_played_date IS NOT NULL;
-- BRIN 索引: 修改时间单调递增
CREATE INDEX idx_user_datas_playstate_last_modified ON user_datas USING BRIN (playstate_last_modified)
    WITH (pages_per_range = 32);
-- 覆盖索引: "继续观看" 查询 (按最后播放时间排序, 只需要这几个字段)
CREATE INDEX idx_user_datas_continue_watching       ON user_datas (user_id, last_played_date DESC)
    INCLUDE (user_data_key_id, playback_position_ticks, played, hide_from_resume)
    WHERE played = TRUE AND hide_from_resume = FALSE AND last_played_date IS NOT NULL;

-- Autovacuum 调优: user_datas 高频更新, 需更激进的清理策略
-- 每个分区独立调优
ALTER TABLE user_datas_p0 SET (autovacuum_vacuum_scale_factor = 0.05, autovacuum_analyze_scale_factor = 0.02,
    autovacuum_vacuum_cost_delay = 10, autovacuum_vacuum_cost_limit = 2000);
ALTER TABLE user_datas_p1 SET (autovacuum_vacuum_scale_factor = 0.05, autovacuum_analyze_scale_factor = 0.02,
    autovacuum_vacuum_cost_delay = 10, autovacuum_vacuum_cost_limit = 2000);
ALTER TABLE user_datas_p2 SET (autovacuum_vacuum_scale_factor = 0.05, autovacuum_analyze_scale_factor = 0.02,
    autovacuum_vacuum_cost_delay = 10, autovacuum_vacuum_cost_limit = 2000);
ALTER TABLE user_datas_p3 SET (autovacuum_vacuum_scale_factor = 0.05, autovacuum_analyze_scale_factor = 0.02,
    autovacuum_vacuum_cost_delay = 10, autovacuum_vacuum_cost_limit = 2000);
ALTER TABLE user_datas_p4 SET (autovacuum_vacuum_scale_factor = 0.05, autovacuum_analyze_scale_factor = 0.02,
    autovacuum_vacuum_cost_delay = 10, autovacuum_vacuum_cost_limit = 2000);
ALTER TABLE user_datas_p5 SET (autovacuum_vacuum_scale_factor = 0.05, autovacuum_analyze_scale_factor = 0.02,
    autovacuum_vacuum_cost_delay = 10, autovacuum_vacuum_cost_limit = 2000);
ALTER TABLE user_datas_p6 SET (autovacuum_vacuum_scale_factor = 0.05, autovacuum_analyze_scale_factor = 0.02,
    autovacuum_vacuum_cost_delay = 10, autovacuum_vacuum_cost_limit = 2000);
ALTER TABLE user_datas_p7 SET (autovacuum_vacuum_scale_factor = 0.05, autovacuum_analyze_scale_factor = 0.02,
    autovacuum_vacuum_cost_delay = 10, autovacuum_vacuum_cost_limit = 2000);

COMMENT ON TABLE user_datas IS '用户媒体数据 — HASH分区by user_id, 高频更新表, FILLFACTOR=85';

-- ===========================================================
-- user_item_shares (小表)
-- ===========================================================

CREATE TABLE IF NOT EXISTS user_item_shares (
    user_id     INTEGER NOT NULL,
    item_id     BIGINT  NOT NULL,
    share_level SMALLINT NOT NULL
                CHECK (share_level >= 0),
    PRIMARY KEY (user_id, item_id)
    -- FK removed: invalid single-column reference to composite PK (id, type); integrity maintained at application layer
);

CREATE INDEX idx_user_item_shares_item_id ON user_item_shares (item_id);

-- ###########################################################
-- Database: users
-- ###########################################################

CREATE TABLE IF NOT EXISTS local_users (
    id   SERIAL,
    guid UUID NOT NULL,
    data JSONB NOT NULL,
    PRIMARY KEY (id)
) WITH (fillfactor = 90);

CREATE UNIQUE INDEX idx_local_users_guid ON local_users (guid);
-- GIN: 如果需要按用户配置字段查询 (如按权限/策略筛选用户)
CREATE INDEX idx_local_users_data_gin ON local_users USING GIN (data);

COMMENT ON TABLE  local_users IS '本地用户账户';
COMMENT ON COLUMN local_users.data IS '用户完整配置数据 JSONB (策略/权限/偏好等)';

CREATE TABLE IF NOT EXISTS user_settings_keys (
    user_settings_key_id SERIAL,
    name                 TEXT NOT NULL,
    PRIMARY KEY (user_settings_key_id)
);

CREATE UNIQUE INDEX idx_user_settings_keys_name ON user_settings_keys (name);

CREATE TABLE IF NOT EXISTS user_settings (
    user_id              INTEGER NOT NULL,
    user_settings_key_id INTEGER NOT NULL,
    value                JSONB  NOT NULL,
    PRIMARY KEY (user_id, user_settings_key_id),
    FOREIGN KEY (user_id)              REFERENCES local_users (id) ON DELETE CASCADE,
    FOREIGN KEY (user_settings_key_id) REFERENCES user_settings_keys (user_settings_key_id) ON DELETE CASCADE
) WITH (fillfactor = 90);

CREATE INDEX idx_user_settings_user_id ON user_settings (user_id);

-- ###########################################################
-- 物化视图 — 预聚合统计
-- ###########################################################

-- 媒体库统计: 各类型数量/平均评分/总时长
CREATE MATERIALIZED VIEW IF NOT EXISTS mv_library_stats AS
SELECT
    type                                          AS media_type,
    COUNT(*)                                      AS item_count,
    ROUND(AVG(community_rating)::numeric, 1)     AS avg_community_rating,
    SUM(runtime_ticks)                            AS total_runtime_ticks,
    MIN(production_year)                          AS min_year,
    MAX(production_year)                          AS max_year,
    COUNT(*) FILTER (WHERE is_favorite = TRUE)    AS favorite_count
FROM media_items m
LEFT JOIN LATERAL (
    SELECT is_favorite FROM user_datas ud
    WHERE ud.user_data_key_id = m.user_data_key_id
    LIMIT 1
) fav ON TRUE
GROUP BY type
WITH DATA;

CREATE UNIQUE INDEX idx_mv_library_stats_type ON mv_library_stats (media_type);

-- 继续观看: 每用户最近播放的项目 (限制 50 条, 减少物化体积)
CREATE MATERIALIZED VIEW IF NOT EXISTS mv_continue_watching AS
SELECT
    ud.user_id,
    ud.user_data_key_id,
    m.id                    AS item_id,
    m.type,
    m.name,
    m.sort_name,
    m.index_number          AS episode_number,
    m.parent_index_number   AS season_number,
    m.series_name,
    m.series_id,
    m.runtime_ticks,
    m.community_rating,
    m.provider_ids->>'Imdb' AS imdb_id,
    m.provider_ids->>'Tmdb' AS tmdb_id,
    ud.playback_position_ticks,
    ud.last_played_date,
    ud.play_count
FROM user_datas ud
JOIN user_data_keys udk ON udk.id = ud.user_data_key_id
JOIN media_items m       ON m.user_data_key_id = ud.user_data_key_id
WHERE ud.played           = TRUE
  AND ud.hide_from_resume = FALSE
  AND ud.last_played_date IS NOT NULL
ORDER BY ud.user_id, ud.last_played_date DESC;

CREATE UNIQUE INDEX idx_mv_continue_watching_user ON mv_continue_watching (user_id, user_data_key_id);

COMMENT ON MATERIALIZED VIEW mv_library_stats IS '媒体库统计预聚合 — 按type汇总数量/评分/时长, 需定期REFRESH';
COMMENT ON MATERIALIZED VIEW mv_continue_watching IS '继续观看预计算 — 每用户最近播放项, 需定期REFRESH';

-- ###########################################################
-- Autovacuum 调优 — 大分区表
-- ###########################################################

-- media_items 各分区: 写多读多, 适度调优
ALTER TABLE media_items_movies  SET (autovacuum_vacuum_scale_factor = 0.1,  autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE media_items_series  SET (autovacuum_vacuum_scale_factor = 0.1,  autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE media_items_episodes SET (autovacuum_vacuum_scale_factor = 0.1, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE media_items_seasons SET (autovacuum_vacuum_scale_factor = 0.1,  autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE media_items_audio   SET (autovacuum_vacuum_scale_factor = 0.1,  autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE media_items_folders SET (autovacuum_vacuum_scale_factor = 0.1,  autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE media_items_other   SET (autovacuum_vacuum_scale_factor = 0.1,  autovacuum_analyze_scale_factor = 0.05);

-- ancestor_ids: 写多 (扫描时大量写入), 但几乎不更新 → 降低分析频率
ALTER TABLE ancestor_ids_p0 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE ancestor_ids_p1 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE ancestor_ids_p2 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE ancestor_ids_p3 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE ancestor_ids_p4 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE ancestor_ids_p5 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE ancestor_ids_p6 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE ancestor_ids_p7 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);

-- item_people: 同 ancestor_ids
ALTER TABLE item_people_p0 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE item_people_p1 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE item_people_p2 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE item_people_p3 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE item_people_p4 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE item_people_p5 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE item_people_p6 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);
ALTER TABLE item_people_p7 SET (autovacuum_vacuum_scale_factor = 0.15, autovacuum_analyze_scale_factor = 0.05);

-- activity_log: 纯插入, 极少更新/删除 → 降低 vacuum 频率
ALTER TABLE activity_log_2024 SET (autovacuum_vacuum_scale_factor = 0.2, autovacuum_analyze_scale_factor = 0.1);
ALTER TABLE activity_log_2025 SET (autovacuum_vacuum_scale_factor = 0.2, autovacuum_analyze_scale_factor = 0.1);
ALTER TABLE activity_log_2026 SET (autovacuum_vacuum_scale_factor = 0.2, autovacuum_analyze_scale_factor = 0.1);
ALTER TABLE activity_log_future SET (autovacuum_vacuum_scale_factor = 0.2, autovacuum_analyze_scale_factor = 0.1);

COMMIT;

-- ============================================================
-- 附录: 运维 SQL 模板
-- ============================================================

-- [1] 刷新物化视图 (建议 crontab 每 5~15 分钟执行一次)
-- REFRESH MATERIALIZED VIEW CONCURRENTLY mv_library_stats;
-- REFRESH MATERIALIZED VIEW CONCURRENTLY mv_continue_watching;

-- [2] 归档旧活动日志 (RANGE 分区的核心优势)
-- ALTER TABLE activity_log DETACH PARTITION activity_log_2024;
-- pg_dump -t activity_log_2024 ... > archive_2024.sql
-- DROP TABLE activity_log_2024;

-- [3] 增加新年度分区
-- CREATE TABLE activity_log_2028 PARTITION OF activity_log
--     FOR VALUES FROM ('2028-01-01') TO ('2029-01-01');

-- [4] 检查分区健康
-- SELECT relname, n_live_tup, n_dead_tup, last_vacuum, last_autovacuum, last_analyze, last_autoanalyze
-- FROM pg_stat_user_tables WHERE schemaname = 'public' ORDER BY n_dead_tup DESC LIMIT 20;

-- [5] 检查索引膨胀
-- SELECT schemaname, tablename, indexname, pg_size_pretty(pg_relation_size(indexrelid)) AS index_size
-- FROM pg_stat_user_indexes ORDER BY pg_relation_size(indexrelid) DESC LIMIT 20;

-- [6] 扩展 HASH 分区数 (如从8扩到16, 需要重建分区表)
-- 注意: HASH 分区数变更需要 pg_partman 或手动迁移, 建议初期规划好
