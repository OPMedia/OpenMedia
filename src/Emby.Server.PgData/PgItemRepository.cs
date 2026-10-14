using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PostgreSQL 媒体项仓储（media_items LIST 分区表 + chapters/media_streams/item_people/
    /// ancestor_ids/item_extradata 等关联表）。
    /// 对照旧 SqliteItemRepository（TypedBaseItems 等 SQLite 表）逐方法保留语义。
    ///
    /// 核心翻译层：
    /// - 旧 guid GUID 主键 → 新 id BIGINT IDENTITY 代理键 + guid UUID 业务键；
    ///   内存维护 ConcurrentDictionary&lt;Guid, long&gt; 缓存，写入用 INSERT ... RETURNING id / ON CONFLICT。
    /// - 旧 type TEXT（.NET 完整类型名）→ 新 type INTEGER 分区码 + type_name TEXT 完整类型名。
    /// - 旧 ParentId/SeasonId/SeriesId GUID 列 → 新 parent_id/series_id/album_id BIGINT（写入时解析）。
    /// - 旧 UserDataKey TEXT → 新 user_data_key_id INTEGER（user_data_keys 字典表翻译）。
    /// - 旧 data BLOB（序列化 BaseItem）→ 新 data JSONB（水合主来源）。
    /// </summary>
    public partial class PgItemRepository : BasePgRepository, IItemRepository
    {
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IServerConfigurationManager _config;
        private readonly IFileSystem _fileSystem;
        private readonly IServerApplicationHost _appHost;

        /// <summary>
        /// 章节图片缓存标签计算（旧实现注入）。
        /// </summary>
        public IImageProcessor ImageProcessor { get; set; }

        /// <summary>
        /// type_name → Type 解析缓存（旧 TypeMapper 等价实现）。
        /// </summary>
        private readonly ConcurrentDictionary<string, Type> _typeMap = new ConcurrentDictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        private readonly IAssemblyInfo _assemblyInfo;

        /// <summary>
        /// GUID → media_items.id 缓存（§5.3：写入 RETURNING id 填充，读取 SELECT 填充）。
        /// </summary>
        private readonly ConcurrentDictionary<Guid, long> _guidToIdMap = new ConcurrentDictionary<Guid, long>();

        public PgItemRepository(
            IServerConfigurationManager config,
            IServerApplicationHost appHost,
            IJsonSerializer jsonSerializer,
            ILogger logger,
            IAssemblyInfo assemblyInfo,
            IFileSystem fileSystem,
            string connectionString)
            : base(logger, connectionString)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            if (jsonSerializer == null)
            {
                throw new ArgumentNullException("jsonSerializer");
            }

            _config = config;
            _appHost = appHost;
            _jsonSerializer = jsonSerializer;
            _fileSystem = fileSystem;
            _assemblyInfo = assemblyInfo;
        }

        /// <summary>
        /// 初始化：schema 由 PgSchemaInitializer 统一建好，这里只做存在性校验。
        /// </summary>
        public void Initialize()
        {
            using (var connection = CreateConnection())
            {
                var tables = new[] { "media_items", "chapters", "media_streams", "item_people", "ancestor_ids", "item_extradata" };

                foreach (var table in tables)
                {
                    if (!TableExists(connection, table))
                    {
                        throw new InvalidOperationException(table + " table does not exist. Schema initialization failed.");
                    }
                }
            }
        }

        // ===========================================================
        // type INTEGER 分区码映射（静态双向字典）
        // 0-10 按 schema 注释固定；11+ 顺序分配；未登记类型 → 99（落 media_items_other DEFAULT 分区，
        // 由 type_name 区分实际类型）。
        // ===========================================================

        private const int TypeCodeUnknown = 99;

        private static readonly Dictionary<string, int> TypeFullNameToCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { typeof(CollectionFolder).FullName, 0 },
            { typeof(Folder).FullName, 1 },
            { typeof(Movie).FullName, 2 },
            { typeof(Series).FullName, 3 },
            { typeof(Season).FullName, 4 },
            { typeof(Episode).FullName, 5 },
            { typeof(Audio).FullName, 6 },
            { typeof(MusicAlbum).FullName, 7 },
            { typeof(Book).FullName, 8 },
            { typeof(Photo).FullName, 9 },
            { typeof(MusicVideo).FullName, 10 },
            { typeof(Person).FullName, 11 },
            { typeof(Genre).FullName, 12 },
            { typeof(Studio).FullName, 13 },
            { typeof(BoxSet).FullName, 14 },
            { typeof(Playlist).FullName, 15 },
            { typeof(LiveTvProgram).FullName, 16 },
            { typeof(LiveTvChannel).FullName, 17 },
            { typeof(MusicArtist).FullName, 18 },
            { typeof(Trailer).FullName, 19 },
            { typeof(Video).FullName, 20 },
            { typeof(Channel).FullName, 21 },
            { typeof(UserView).FullName, 22 },
            { typeof(Year).FullName, 23 },
            { typeof(AggregateFolder).FullName, 24 },
            { typeof(UserRootFolder).FullName, 25 },
            { typeof(PhotoAlbum).FullName, 26 },
            { typeof(MusicGenre).FullName, 27 },
            { typeof(GameGenre).FullName, 28 },
            { typeof(MediaBrowser.Controller.Entities.Game).FullName, 29 },
            { typeof(GameSystem).FullName, 30 },
            { typeof(AudioBook).FullName, 31 }
        };

        /// <summary>
        /// .NET 类型 → 分区码（未登记类型 → 99，落 DEFAULT 分区）。
        /// </summary>
        internal static int GetTypeCode(Type type)
        {
            int code;
            return TypeFullNameToCode.TryGetValue(type.FullName, out code) ? code : TypeCodeUnknown;
        }

        /// <summary>
        /// .NET 完整类型名 → 分区码（未登记 → null，查询时改用 type_name 匹配）。
        /// </summary>
        internal static int? GetTypeCodeByName(string fullName)
        {
            int code;
            return TypeFullNameToCode.TryGetValue(fullName, out code) ? (int?)code : null;
        }

        /// <summary>
        /// type_name → Type（旧 TypeMapper：搜索已加载程序集）。
        /// </summary>
        internal Type GetTypeByName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            return _typeMap.GetOrAdd(typeName, name =>
                _assemblyInfo
                    .GetCurrentAssemblies()
                    .Select(a => a.GetType(name, false))
                    .FirstOrDefault(t => t != null));
        }

        // ===========================================================
        // GUID ↔ id 翻译层
        // ===========================================================

        /// <summary>
        /// GUID → media_items.id（先查缓存，未命中查库并回填；不存在返回 null）。
        /// </summary>
        internal long? GetInternalId(NpgsqlConnection connection, Guid guid)
        {
            return GetInternalId(connection, null, guid);
        }

        internal long? GetInternalId(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid guid)
        {
            if (guid.Equals(Guid.Empty))
            {
                return null;
            }

            long cached;
            if (_guidToIdMap.TryGetValue(guid, out cached))
            {
                return cached;
            }

            using (var cmd = new NpgsqlCommand(
                "SELECT id FROM media_items WHERE guid = @guid LIMIT 1",
                connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = guid });

                var result = cmd.ExecuteScalar();

                if (result == null || result == DBNull.Value)
                {
                    return null;
                }

                var id = Convert.ToInt64(result);
                _guidToIdMap[guid] = id;
                return id;
            }
        }

        /// <summary>
        /// id → GUID 反查（读取水合时把 parent_id/series_id 翻译回 GUID）。
        /// </summary>
        internal static Guid? GetGuidForInternalId(NpgsqlConnection connection, long? internalId)
        {
            if (internalId == null)
            {
                return null;
            }

            using (var cmd = new NpgsqlCommand(
                "SELECT guid FROM media_items WHERE id = @id LIMIT 1",
                connection))
            {
                cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = internalId });

                var result = cmd.ExecuteScalar();

                if (result == null || result == DBNull.Value)
                {
                    return null;
                }

                return (Guid)result;
            }
        }

        // ===========================================================
        // SaveItem / SaveItems / SaveImages / DeleteItem
        // ===========================================================

        /// <summary>
        /// 保存单个媒体项（旧 SaveItem → SaveItems）。
        /// </summary>
        public void SaveItem(BaseItem item, CancellationToken cancellationToken)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            SaveItems(new List<BaseItem> { item }, cancellationToken);
        }

        /// <summary>
        /// 保存图片列（旧 SaveImages：Update TypedBaseItems set Images where guid）。
        /// </summary>
        public void SaveImages(BaseItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            CheckDisposed();

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(
                    "UPDATE media_items SET images = @images WHERE guid = @guid",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = item.Id });
                    cmd.Parameters.Add(new NpgsqlParameter("images", NpgsqlDbType.Jsonb)
                    {
                        Value = (object)SerializeImages(item) ?? DBNull.Value
                    });

                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 批量保存（旧 SaveItems：单事务内逐项 replace + 祖先 + item values）。
        /// 两遍式：第一遍 upsert 全部行（父/系列/专辑引用可能暂缺 → NULL），
        /// 第二遍回填 parent_id/series_id/album_id（批量内拓扑顺序无关）+ 祖先 + extradata。
        /// </summary>
        public void SaveItems(List<BaseItem> items, CancellationToken cancellationToken)
        {
            if (items == null)
            {
                throw new ArgumentNullException("items");
            }

            cancellationToken.ThrowIfCancellationRequested();

            CheckDisposed();


            var tuples = new List<Tuple<BaseItem, List<Guid>, BaseItem, string, List<string>>>();

            foreach (var item in items)
            {
                var ancestorIds = item.SupportsAncestors
                    ? item.GetAncestorIds().Distinct().ToList()
                    : null;

                var topParent = item.GetTopParent();

                var userdataKey = item.GetUserDataKeys().FirstOrDefault();
                var inheritedTags = item.GetInheritedTags();

                tuples.Add(new Tuple<BaseItem, List<Guid>, BaseItem, string, List<string>>(
                    item, ancestorIds, topParent, userdataKey, inheritedTags));
            }

            RunInTransaction((connection, transaction) =>
            {
                foreach (var tuple in tuples)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SaveItemInternal(connection, transaction, tuple.Item1, tuple.Item3, tuple.Item4);
                }

                // 第二遍：批量内所有行已存在，回填父/系列/专辑整数引用
                foreach (var tuple in tuples)
                {
                    UpdateItemReferences(connection, transaction, tuple.Item1);
                }

                foreach (var tuple in tuples)
                {
                    var item = tuple.Item1;

                    if (item.SupportsAncestors)
                    {
                        UpdateAncestors(connection, transaction, item.Id, tuple.Item2);
                    }

                    UpdateItemValues(connection, transaction, item.Id, GetItemValuesToSave(item, tuple.Item5));
                }
            });
        }

        /// <summary>
        /// 单项 upsert（旧 SaveItem 的 replace into 语义：guid 存在则整行更新，id 保持稳定）。
        /// ON CONFLICT (guid, type) 依赖补充唯一索引 idx_media_items_guid；
        /// 同 guid 不同 type 的历史行先清理（罕见路径：类型变更，等价旧行为的整行替换）。
        /// </summary>
        private void SaveItemInternal(NpgsqlConnection connection, NpgsqlTransaction transaction, BaseItem item, BaseItem topParent, string userDataKey)
        {
            var type = item.GetType();
            var typeCode = GetTypeCode(type);

            using (var cmd = new NpgsqlCommand(
                "DELETE FROM media_items WHERE guid = @guid AND type <> @type",
                connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = item.Id });
                cmd.Parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Integer) { Value = typeCode });
                cmd.ExecuteNonQuery();
            }

            int? userDataKeyId = null;

            if (!string.IsNullOrWhiteSpace(userDataKey))
            {
                userDataKeyId = GetOrInsertUserDataKey(connection, transaction, userDataKey);
            }

            string data = null;

            if (TypeRequiresDeserialization(type))
            {
                data = _jsonSerializer.SerializeToString(item);
            }

            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO media_items
                    (guid, type, type_name, data, parent_id, path, start_date, end_date,
                     is_movie, is_series, is_repeat, community_rating, custom_rating,
                     index_number, is_locked, name, official_rating, overview,
                     parent_index_number, premiere_date, production_year, sort_name,
                     runtime_ticks, date_created, date_modified, preferred_metadata_language,
                     preferred_metadata_country_code, date_last_refreshed, date_last_saved,
                     is_in_mixed_folder, locked_fields, inherited_parental_rating_value,
                     critic_rating, presentation_unique_key, original_title, series_name,
                     series_id, album_id, tagline, provider_ids, images, production_locations,
                     total_bitrate, extra_type, series_presentation_unique_key, external_id,
                     width, height, size, container, user_data_key_id,
                     is_folder, is_virtual_item, media_type, channel_id, top_parent_id,
                     clean_name, album, extra_ids, unrated_type, trailer_types,
                     external_series_id, owner_id, date_last_media_added)
                  VALUES
                    (@guid, @type, @type_name, CAST(@data AS jsonb), @parent_id, @path, @start_date, @end_date,
                     @is_movie, @is_series, @is_repeat, @community_rating, @custom_rating,
                     @index_number, @is_locked, @name, @official_rating, @overview,
                     @parent_index_number, @premiere_date, @production_year, @sort_name,
                     @runtime_ticks, @date_created, @date_modified, @preferred_metadata_language,
                     @preferred_metadata_country_code, @date_last_refreshed, @date_last_saved,
                     @is_in_mixed_folder, @locked_fields, @inherited_parental_rating_value,
                     @critic_rating, @presentation_unique_key, @original_title, @series_name,
                     @series_id, @album_id, @tagline, CAST(@provider_ids AS jsonb), CAST(@images AS jsonb), CAST(@production_locations AS jsonb),
                     @total_bitrate, @extra_type, @series_presentation_unique_key, @external_id,
                     @width, @height, @size, @container, @user_data_key_id,
                     @is_folder, @is_virtual_item, @media_type, @channel_id, @top_parent_id,
                     @clean_name, @album, @extra_ids, @unrated_type, @trailer_types,
                     @external_series_id, @owner_id, @date_last_media_added)
                  ON CONFLICT (guid, type) DO UPDATE SET
                    type_name = EXCLUDED.type_name,
                    data = EXCLUDED.data,
                    parent_id = EXCLUDED.parent_id,
                    path = EXCLUDED.path,
                    start_date = EXCLUDED.start_date,
                    end_date = EXCLUDED.end_date,
                    is_movie = EXCLUDED.is_movie,
                    is_series = EXCLUDED.is_series,
                    is_repeat = EXCLUDED.is_repeat,
                    community_rating = EXCLUDED.community_rating,
                    custom_rating = EXCLUDED.custom_rating,
                    index_number = EXCLUDED.index_number,
                    is_locked = EXCLUDED.is_locked,
                    name = EXCLUDED.name,
                    official_rating = EXCLUDED.official_rating,
                    overview = EXCLUDED.overview,
                    parent_index_number = EXCLUDED.parent_index_number,
                    premiere_date = EXCLUDED.premiere_date,
                    production_year = EXCLUDED.production_year,
                    sort_name = EXCLUDED.sort_name,
                    runtime_ticks = EXCLUDED.runtime_ticks,
                    date_created = EXCLUDED.date_created,
                    date_modified = EXCLUDED.date_modified,
                    preferred_metadata_language = EXCLUDED.preferred_metadata_language,
                    preferred_metadata_country_code = EXCLUDED.preferred_metadata_country_code,
                    date_last_refreshed = EXCLUDED.date_last_refreshed,
                    date_last_saved = EXCLUDED.date_last_saved,
                    is_in_mixed_folder = EXCLUDED.is_in_mixed_folder,
                    locked_fields = EXCLUDED.locked_fields,
                    inherited_parental_rating_value = EXCLUDED.inherited_parental_rating_value,
                    critic_rating = EXCLUDED.critic_rating,
                    presentation_unique_key = EXCLUDED.presentation_unique_key,
                    original_title = EXCLUDED.original_title,
                    series_name = EXCLUDED.series_name,
                    series_id = EXCLUDED.series_id,
                    album_id = EXCLUDED.album_id,
                    tagline = EXCLUDED.tagline,
                    provider_ids = EXCLUDED.provider_ids,
                    images = EXCLUDED.images,
                    production_locations = EXCLUDED.production_locations,
                    total_bitrate = EXCLUDED.total_bitrate,
                    extra_type = EXCLUDED.extra_type,
                    series_presentation_unique_key = EXCLUDED.series_presentation_unique_key,
                    external_id = EXCLUDED.external_id,
                    width = EXCLUDED.width,
                    height = EXCLUDED.height,
                    size = EXCLUDED.size,
                    container = EXCLUDED.container,
                    user_data_key_id = EXCLUDED.user_data_key_id,
                    is_folder = EXCLUDED.is_folder,
                    is_virtual_item = EXCLUDED.is_virtual_item,
                    media_type = EXCLUDED.media_type,
                    channel_id = EXCLUDED.channel_id,
                    top_parent_id = EXCLUDED.top_parent_id,
                    clean_name = EXCLUDED.clean_name,
                    album = EXCLUDED.album,
                    extra_ids = EXCLUDED.extra_ids,
                    unrated_type = EXCLUDED.unrated_type,
                    trailer_types = EXCLUDED.trailer_types,
                    external_series_id = EXCLUDED.external_series_id,
                    owner_id = EXCLUDED.owner_id,
                    date_last_media_added = EXCLUDED.date_last_media_added
                  RETURNING id",
                connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = item.Id });
                cmd.Parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Integer) { Value = typeCode });
                cmd.Parameters.Add(new NpgsqlParameter("type_name", NpgsqlDbType.Text) { Value = (object)type.FullName ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Text) { Value = (object)data ?? DBNull.Value });

                cmd.Parameters.Add(new NpgsqlParameter("parent_id", NpgsqlDbType.Bigint)
                {
                    Value = (object)GetInternalId(connection, transaction, item.ParentId) ?? DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("path", NpgsqlDbType.Text) { Value = (object)GetPathToSave(item.Path) ?? DBNull.Value });

                var hasStartDate = item as IHasStartDate;
                cmd.Parameters.Add(new NpgsqlParameter("start_date", NpgsqlDbType.TimestampTz)
                {
                    Value = hasStartDate != null ? (object)ToUtcDateTime(hasStartDate.StartDate) : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("end_date", NpgsqlDbType.TimestampTz)
                {
                    Value = item.EndDate.HasValue ? (object)ToUtcDateTime(item.EndDate.Value) : DBNull.Value
                });

                var hasProgramAttributes = item as IHasProgramAttributes;
                cmd.Parameters.Add(new NpgsqlParameter("is_movie", NpgsqlDbType.Boolean)
                {
                    Value = hasProgramAttributes != null ? (object)hasProgramAttributes.IsMovie : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("is_series", NpgsqlDbType.Boolean)
                {
                    Value = hasProgramAttributes != null ? (object)hasProgramAttributes.IsSeries : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("is_repeat", NpgsqlDbType.Boolean)
                {
                    Value = hasProgramAttributes != null ? (object)hasProgramAttributes.IsRepeat : DBNull.Value
                });

                cmd.Parameters.Add(new NpgsqlParameter("community_rating", NpgsqlDbType.Double)
                {
                    Value = item.CommunityRating.HasValue ? (object)(double)item.CommunityRating.Value : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("custom_rating", NpgsqlDbType.Text) { Value = (object)item.CustomRating ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter("index_number", NpgsqlDbType.Integer)
                {
                    Value = item.IndexNumber.HasValue ? (object)item.IndexNumber.Value : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("is_locked", NpgsqlDbType.Boolean) { Value = item.IsLocked });
                cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = (object)item.Name ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter("official_rating", NpgsqlDbType.Text) { Value = (object)item.OfficialRating ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter("overview", NpgsqlDbType.Text) { Value = (object)item.Overview ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter("parent_index_number", NpgsqlDbType.Integer)
                {
                    Value = item.ParentIndexNumber.HasValue ? (object)item.ParentIndexNumber.Value : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("premiere_date", NpgsqlDbType.TimestampTz)
                {
                    Value = item.PremiereDate.HasValue ? (object)ToUtcDateTime(item.PremiereDate.Value) : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("production_year", NpgsqlDbType.Integer)
                {
                    Value = item.ProductionYear.HasValue ? (object)item.ProductionYear.Value : DBNull.Value
                });

                cmd.Parameters.Add(new NpgsqlParameter("sort_name", NpgsqlDbType.Text) { Value = (object)item.SortName ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter("runtime_ticks", NpgsqlDbType.Bigint)
                {
                    Value = item.RunTimeTicks.HasValue ? (object)item.RunTimeTicks.Value : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("date_created", NpgsqlDbType.TimestampTz) { Value = ToUtcDateTime(item.DateCreated) });
                cmd.Parameters.Add(new NpgsqlParameter("date_modified", NpgsqlDbType.TimestampTz) { Value = ToUtcDateTime(item.DateModified) });
                cmd.Parameters.Add(new NpgsqlParameter("preferred_metadata_language", NpgsqlDbType.Text)
                {
                    Value = (object)item.PreferredMetadataLanguage ?? DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("preferred_metadata_country_code", NpgsqlDbType.Text)
                {
                    Value = (object)item.PreferredMetadataCountryCode ?? DBNull.Value
                });

                cmd.Parameters.Add(new NpgsqlParameter("date_last_refreshed", NpgsqlDbType.TimestampTz)
                {
                    Value = item.DateLastRefreshed != default(DateTime) ? (object)ToUtcDateTime(item.DateLastRefreshed) : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("date_last_saved", NpgsqlDbType.TimestampTz)
                {
                    Value = item.DateLastSaved != default(DateTime) ? (object)ToUtcDateTime(item.DateLastSaved) : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("is_in_mixed_folder", NpgsqlDbType.Boolean) { Value = item.IsInMixedFolder });

                cmd.Parameters.Add(new NpgsqlParameter("locked_fields", NpgsqlDbType.Text)
                {
                    Value = item.LockedFields != null && item.LockedFields.Length > 0
                        ? (object)string.Join("|", item.LockedFields.Select(i => i.ToString()).ToArray())
                        : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("inherited_parental_rating_value", NpgsqlDbType.Integer)
                {
                    Value = item.InheritedParentalRatingValue > 0 ? (object)item.InheritedParentalRatingValue : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("critic_rating", NpgsqlDbType.Double)
                {
                    Value = item.CriticRating.HasValue ? (object)(double)item.CriticRating.Value : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("presentation_unique_key", NpgsqlDbType.Text)
                {
                    Value = (object)item.PresentationUniqueKey ?? DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("original_title", NpgsqlDbType.Text) { Value = (object)item.OriginalTitle ?? DBNull.Value });

                var hasSeriesName = item as IHasSeriesName;
                cmd.Parameters.Add(new NpgsqlParameter("series_name", NpgsqlDbType.Text)
                {
                    Value = hasSeriesName != null && !string.IsNullOrEmpty(hasSeriesName.SeriesName) ? (object)hasSeriesName.SeriesName : DBNull.Value
                });

                var hasSeries = item as IHasSeries;
                cmd.Parameters.Add(new NpgsqlParameter("series_id", NpgsqlDbType.Bigint)
                {
                    Value = hasSeries != null ? (object)GetInternalId(connection, transaction, hasSeries.SeriesId) ?? DBNull.Value : DBNull.Value
                });
                // album_id：旧语义靠 Album 名称列（album TEXT 补充列）过滤；整数外键列留空
                cmd.Parameters.Add(new NpgsqlParameter("album_id", NpgsqlDbType.Bigint) { Value = DBNull.Value });

                cmd.Parameters.Add(new NpgsqlParameter("tagline", NpgsqlDbType.Text) { Value = (object)item.Tagline ?? DBNull.Value });

                var providerIds = SerializeProviderIds(item);
                cmd.Parameters.Add(new NpgsqlParameter("provider_ids", NpgsqlDbType.Text) { Value = (object)providerIds ?? DBNull.Value });
                var images = SerializeImages(item);
                cmd.Parameters.Add(new NpgsqlParameter("images", NpgsqlDbType.Text) { Value = (object)images ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter("production_locations", NpgsqlDbType.Text)
                {
                    Value = item.ProductionLocations != null && item.ProductionLocations.Length > 0
                        ? (object)_jsonSerializer.SerializeToString(item.ProductionLocations)
                        : DBNull.Value
                });

                cmd.Parameters.Add(new NpgsqlParameter("total_bitrate", NpgsqlDbType.Integer)
                {
                    Value = item.TotalBitrate.HasValue ? (object)item.TotalBitrate.Value : DBNull.Value
                });

                cmd.Parameters.Add(new NpgsqlParameter("extra_type", NpgsqlDbType.Integer)
                {
                    Value = item.ExtraType.HasValue ? (object)(int)item.ExtraType.Value : DBNull.Value
                });

                cmd.Parameters.Add(new NpgsqlParameter("series_presentation_unique_key", NpgsqlDbType.Text)
                {
                    Value = hasSeries != null && !string.IsNullOrEmpty(hasSeries.SeriesPresentationUniqueKey)
                        ? (object)hasSeries.SeriesPresentationUniqueKey
                        : DBNull.Value
                });

                cmd.Parameters.Add(new NpgsqlParameter("external_id", NpgsqlDbType.Text) { Value = (object)item.ExternalId ?? DBNull.Value });

                cmd.Parameters.Add(new NpgsqlParameter("width", NpgsqlDbType.Integer)
                {
                    Value = item.Width > 0 ? (object)item.Width : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("height", NpgsqlDbType.Integer)
                {
                    Value = item.Height > 0 ? (object)item.Height : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("size", NpgsqlDbType.Bigint)
                {
                    Value = item.Size.HasValue ? (object)item.Size.Value : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("container", NpgsqlDbType.Text) { Value = (object)item.Container ?? DBNull.Value });

                cmd.Parameters.Add(new NpgsqlParameter("user_data_key_id", NpgsqlDbType.Integer)
                {
                    Value = (object)userDataKeyId ?? DBNull.Value
                });

                // ===== 查询引擎补充列（旧 TypedBaseItems 的过滤列） =====

                cmd.Parameters.Add(new NpgsqlParameter("is_folder", NpgsqlDbType.Boolean) { Value = item.IsFolder });
                cmd.Parameters.Add(new NpgsqlParameter("is_virtual_item", NpgsqlDbType.Boolean) { Value = item.IsVirtualItem });
                cmd.Parameters.Add(new NpgsqlParameter("media_type", NpgsqlDbType.Text) { Value = (object)item.MediaType ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter("channel_id", NpgsqlDbType.Text)
                {
                    Value = item.ChannelId.Equals(Guid.Empty) ? (object)DBNull.Value : item.ChannelId.ToString("N")
                });

                cmd.Parameters.Add(new NpgsqlParameter("top_parent_id", NpgsqlDbType.Text)
                {
                    Value = topParent != null ? (object)topParent.Id.ToString("N") : DBNull.Value
                });

                cmd.Parameters.Add(new NpgsqlParameter("clean_name", NpgsqlDbType.Text)
                {
                    Value = string.IsNullOrWhiteSpace(item.Name) ? (object)DBNull.Value : GetCleanValue(item.Name)
                });

                cmd.Parameters.Add(new NpgsqlParameter("album", NpgsqlDbType.Text) { Value = (object)item.Album ?? DBNull.Value });

                cmd.Parameters.Add(new NpgsqlParameter("extra_ids", NpgsqlDbType.Text)
                {
                    Value = item.ExtraIds != null && item.ExtraIds.Length > 0
                        ? (object)string.Join("|", item.ExtraIds.Select(i => i.ToString()).ToArray())
                        : DBNull.Value
                });

                cmd.Parameters.Add(new NpgsqlParameter("unrated_type", NpgsqlDbType.Text)
                {
                    Value = (object)item.GetBlockUnratedType().ToString()
                });

                var trailer = item as Trailer;
                cmd.Parameters.Add(new NpgsqlParameter("trailer_types", NpgsqlDbType.Text)
                {
                    Value = trailer != null && trailer.TrailerTypes != null && trailer.TrailerTypes.Length > 0
                        ? (object)string.Join("|", trailer.TrailerTypes.Select(i => i.ToString()).ToArray())
                        : DBNull.Value
                });

                cmd.Parameters.Add(new NpgsqlParameter("external_series_id", NpgsqlDbType.Text)
                {
                    Value = (object)item.ExternalSeriesId ?? DBNull.Value
                });

                cmd.Parameters.Add(new NpgsqlParameter("owner_id", NpgsqlDbType.Text)
                {
                    Value = item.OwnerId.Equals(Guid.Empty) ? (object)DBNull.Value : item.OwnerId.ToString("N")
                });

                var folder = item as Folder;
                cmd.Parameters.Add(new NpgsqlParameter("date_last_media_added", NpgsqlDbType.TimestampTz)
                {
                    Value = folder != null && folder.DateLastMediaAdded.HasValue
                        ? (object)ToUtcDateTime(folder.DateLastMediaAdded.Value)
                        : DBNull.Value
                });

                var id = Convert.ToInt64(cmd.ExecuteScalar());
                _guidToIdMap[item.Id] = id;
            }
        }

        /// <summary>
        /// 第二遍回填 parent_id/series_id（子查询解析，批量内拓扑顺序无关）。
        /// </summary>
        private void UpdateItemReferences(NpgsqlConnection connection, NpgsqlTransaction transaction, BaseItem item)
        {
            var hasSeries = item as IHasSeries;

            using (var cmd = new NpgsqlCommand(
                @"UPDATE media_items SET
                    parent_id = (SELECT id FROM media_items WHERE guid = @parent_guid LIMIT 1),
                    series_id = @series_id
                  WHERE guid = @guid",
                connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = item.Id });
                cmd.Parameters.Add(new NpgsqlParameter("parent_guid", NpgsqlDbType.Uuid)
                {
                    Value = item.ParentId.Equals(Guid.Empty) ? (object)Guid.Empty : item.ParentId
                });

                cmd.Parameters.Add(new NpgsqlParameter("series_id", NpgsqlDbType.Bigint)
                {
                    Value = hasSeries != null
                        ? (object)GetInternalId(connection, transaction, hasSeries.SeriesId) ?? DBNull.Value
                        : DBNull.Value
                });

                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// userDataKey → user_data_keys.id（upsert）。
        /// </summary>
        internal static int GetOrInsertUserDataKey(NpgsqlConnection connection, NpgsqlTransaction transaction, string key)
        {
            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO user_data_keys (user_data_key)
                  VALUES (@user_data_key)
                  ON CONFLICT (user_data_key) DO UPDATE SET user_data_key = EXCLUDED.user_data_key
                  RETURNING id",
                connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("user_data_key", NpgsqlDbType.Text) { Value = key });
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// 删除媒体项（旧 DeleteItem：显式删除 People/Chapters/MediaStreams/AncestorIds/ItemValues/TypedBaseItems）。
        /// 新 schema 无外键级联（非法单列外键已在 SQL 文件内移除，完整性由应用层维护），同样显式删除子行。
        /// </summary>
        public void DeleteItem(Guid id, CancellationToken cancellationToken)
        {
            if (id.Equals(Guid.Empty))
            {
                throw new ArgumentNullException("id");
            }

            CheckDisposed();

            RunInTransaction((connection, transaction) =>
            {
                long? internalId = null;

                using (var cmd = new NpgsqlCommand("SELECT id FROM media_items WHERE guid = @guid LIMIT 1", connection, transaction))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = id });
                    var result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        internalId = Convert.ToInt64(result);
                    }
                }

                if (internalId == null)
                {
                    return;
                }

                var itemId = internalId.Value;

                ExecuteWithSingleParam(connection, transaction, "DELETE FROM item_people WHERE item_id = @id", itemId);
                ExecuteWithSingleParam(connection, transaction, "DELETE FROM chapters WHERE item_id = @id", itemId);
                ExecuteWithSingleParam(connection, transaction, "DELETE FROM media_streams WHERE item_id = @id", itemId);
                ExecuteWithSingleParam(connection, transaction, "DELETE FROM ancestor_ids WHERE item_id = @id", itemId);
                ExecuteWithSingleParam(connection, transaction, "DELETE FROM item_extradata WHERE item_id = @id", itemId);
                ExecuteWithSingleParam(connection, transaction, "DELETE FROM media_items WHERE id = @id", itemId);

                long removed;
                _guidToIdMap.TryRemove(id, out removed);
            });
        }

        private static void ExecuteWithSingleParam(NpgsqlConnection connection, NpgsqlTransaction transaction, string query, long value)
        {
            using (var cmd = new NpgsqlCommand(query, connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = value });
                cmd.ExecuteNonQuery();
            }
        }

        // ===========================================================
        // RetrieveItem / GetItem 水合
        // ===========================================================

        /// <summary>
        /// 按 GUID 检索单个媒体项（旧 RetrieveItem）。
        /// </summary>
        public BaseItem RetrieveItem(Guid id)
        {
            if (id.Equals(Guid.Empty))
            {
                throw new ArgumentNullException("id");
            }

            CheckDisposed();

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(
                    BuildItemSelectText(null) + " FROM media_items A WHERE A.guid = @guid LIMIT 1",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = id });

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return GetItem(reader, connection);
                        }
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// 构造媒体项水合 SELECT 列（固定列集 + 按需的 item_extradata 聚合子查询）。
        /// </summary>
        internal static string BuildItemSelectText(InternalItemsQuery query)
        {
            var columns = new List<string>
            {
                "A.id",
                "A.guid",
                "A.type",
                "A.type_name",
                "A.data::text",
                "A.path",
                "A.parent_id",
                "A.series_id",
                "A.album_id",
                "A.start_date",
                "A.end_date",
                "A.is_movie",
                "A.is_series",
                "A.is_repeat",
                "A.community_rating",
                "A.custom_rating",
                "A.index_number",
                "A.is_locked",
                "A.name",
                "A.official_rating",
                "A.overview",
                "A.parent_index_number",
                "A.premiere_date",
                "A.production_year",
                "A.sort_name",
                "A.runtime_ticks",
                "A.date_created",
                "A.date_modified",
                "A.preferred_metadata_language",
                "A.preferred_metadata_country_code",
                "A.date_last_refreshed",
                "A.date_last_saved",
                "A.is_in_mixed_folder",
                "A.locked_fields",
                "A.inherited_parental_rating_value",
                "A.critic_rating",
                "A.presentation_unique_key",
                "A.original_title",
                "A.series_name",
                "A.tagline",
                "A.provider_ids::text",
                "A.images::text",
                "A.production_locations",
                "A.total_bitrate",
                "A.extra_type",
                "A.series_presentation_unique_key",
                "A.external_id",
                "A.width",
                "A.height",
                "A.size",
                "A.container",
                "A.user_data_key_id",
                "A.is_folder",
                "A.is_virtual_item",
                "A.media_type",
                "A.channel_id",
                "A.top_parent_id",
                "A.clean_name",
                "A.album",
                "A.extra_ids",
                "A.unrated_type",
                "A.trailer_types",
                "A.external_series_id",
                "A.owner_id",
                "A.date_last_media_added",
                "(SELECT p.guid FROM media_items p WHERE p.id = A.parent_id) AS parent_guid",
                "(SELECT s.guid FROM media_items s WHERE s.id = A.series_id) AS series_guid"
            };

            if (query == null || HasField(query, ItemFields.Genres))
            {
                columns.Add("(SELECT string_agg(value, '|') FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 2) AS genres");
            }

            if (query == null || HasField(query, ItemFields.Studios))
            {
                columns.Add("(SELECT string_agg(value, '|') FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 3) AS studios");
            }

            if (query == null || HasField(query, ItemFields.Tags) || HasProgramAttributes(query))
            {
                columns.Add("(SELECT string_agg(value, '|') FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 4) AS tags");
            }

            if (query == null || HasArtistFields(query))
            {
                columns.Add("(SELECT string_agg(value, '|') FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 0) AS artists");
                columns.Add("(SELECT string_agg(value, '|') FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 1) AS album_artists");
            }

            // 相似度/搜索评分列（旧 SimilarityScore/SearchScore；PG ORDER BY 可引用输出别名）
            if (query != null && query.SimilarTo != null)
            {
                columns.Add(GetSimilarityScoreExpression() + " AS similarity_score");
            }

            if (query != null && !string.IsNullOrEmpty(query.SearchTerm))
            {
                columns.Add(GetSearchScoreExpression(query) + " AS search_score");
            }

            // 只返回 SELECT 列清单，不带 FROM——由调用方提供（Query/Aggregates 各自拼 FROM，
            // RetrieveItem 单独拼 " FROM media_items A WHERE ..."）
            return "SELECT " + string.Join(",", columns.ToArray());
        }

        /// <summary>
        /// 行 → BaseItem 水合（旧 GetItem 语义）：
        /// 1) data JSONB 反序列化（旧 data BLOB 路径）；
        /// 2) 失败或类型不需要序列化 → Activator.CreateInstance + 扁平列赋值；
        /// 3) 与旧实现一致，扁平列在反序列化结果上再覆盖一次（查询可见字段以列为准）。
        /// </summary>
        internal BaseItem GetItem(NpgsqlDataReader reader, NpgsqlConnection connection)
        {
            var typeName = ReadStringOrNull(reader, "type_name");

            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            var type = GetTypeByName(typeName);

            if (type == null)
            {
                return null;
            }

            if (reader.IsDBNull(reader.GetOrdinal("guid")))
            {
                return null;
            }

            BaseItem item = null;

            var data = ReadStringOrNull(reader, "data");

            if (!string.IsNullOrEmpty(data) && TypeRequiresDeserialization(type))
            {
                try
                {
                    item = _jsonSerializer.DeserializeFromString(data, type) as BaseItem;
                }
                catch (Exception ex)
                {
                    Logger.ErrorException("Error deserializing item", ex);
                }
            }

            if (item == null)
            {
                try
                {
                    item = Activator.CreateInstance(type) as BaseItem;
                }
                catch
                {
                }
            }

            if (item == null)
            {
                return null;
            }

            item.Id = reader.GetGuid(reader.GetOrdinal("guid"));

            long internalId = reader.GetInt64(reader.GetOrdinal("id"));
            _guidToIdMap[item.Id] = internalId;

            // ===== 扁平列覆盖（对照旧 GetItem 逐字段） =====

            if (!reader.IsDBNull(reader.GetOrdinal("path")))
            {
                item.Path = RestorePath(reader.GetString(reader.GetOrdinal("path")));
            }

            var hasStartDate = item as IHasStartDate;
            if (hasStartDate != null && !reader.IsDBNull(reader.GetOrdinal("start_date")))
            {
                hasStartDate.StartDate = reader.GetDateTime(reader.GetOrdinal("start_date"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("end_date")))
            {
                item.EndDate = reader.GetDateTime(reader.GetOrdinal("end_date"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("channel_id")))
            {
                item.ChannelId = new Guid(reader.GetString(reader.GetOrdinal("channel_id")));
            }

            var hasProgramAttributes = item as IHasProgramAttributes;
            if (hasProgramAttributes != null)
            {
                if (!reader.IsDBNull(reader.GetOrdinal("is_movie")))
                {
                    hasProgramAttributes.IsMovie = reader.GetBoolean(reader.GetOrdinal("is_movie"));
                }
                if (!reader.IsDBNull(reader.GetOrdinal("is_series")))
                {
                    hasProgramAttributes.IsSeries = reader.GetBoolean(reader.GetOrdinal("is_series"));
                }
                if (!reader.IsDBNull(reader.GetOrdinal("is_repeat")))
                {
                    hasProgramAttributes.IsRepeat = reader.GetBoolean(reader.GetOrdinal("is_repeat"));
                }
            }

            if (!reader.IsDBNull(reader.GetOrdinal("community_rating")))
            {
                item.CommunityRating = (float)reader.GetDouble(reader.GetOrdinal("community_rating"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("custom_rating")))
            {
                item.CustomRating = reader.GetString(reader.GetOrdinal("custom_rating"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("index_number")))
            {
                item.IndexNumber = reader.GetInt32(reader.GetOrdinal("index_number"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("is_locked")))
            {
                item.IsLocked = reader.GetBoolean(reader.GetOrdinal("is_locked"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("name")))
            {
                item.Name = reader.GetString(reader.GetOrdinal("name"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("official_rating")))
            {
                item.OfficialRating = reader.GetString(reader.GetOrdinal("official_rating"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("overview")))
            {
                item.Overview = reader.GetString(reader.GetOrdinal("overview"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("parent_index_number")))
            {
                item.ParentIndexNumber = reader.GetInt32(reader.GetOrdinal("parent_index_number"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("premiere_date")))
            {
                item.PremiereDate = reader.GetDateTime(reader.GetOrdinal("premiere_date"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("production_year")))
            {
                item.ProductionYear = reader.GetInt32(reader.GetOrdinal("production_year"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("sort_name")))
            {
                item.ForcedSortName = reader.GetString(reader.GetOrdinal("sort_name"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("runtime_ticks")))
            {
                item.RunTimeTicks = reader.GetInt64(reader.GetOrdinal("runtime_ticks"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("date_created")))
            {
                item.DateCreated = reader.GetDateTime(reader.GetOrdinal("date_created"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("date_modified")))
            {
                item.DateModified = reader.GetDateTime(reader.GetOrdinal("date_modified"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("preferred_metadata_language")))
            {
                item.PreferredMetadataLanguage = reader.GetString(reader.GetOrdinal("preferred_metadata_language"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("preferred_metadata_country_code")))
            {
                item.PreferredMetadataCountryCode = reader.GetString(reader.GetOrdinal("preferred_metadata_country_code"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("date_last_refreshed")))
            {
                item.DateLastRefreshed = reader.GetDateTime(reader.GetOrdinal("date_last_refreshed"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("date_last_saved")))
            {
                item.DateLastSaved = reader.GetDateTime(reader.GetOrdinal("date_last_saved"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("is_in_mixed_folder")))
            {
                item.IsInMixedFolder = reader.GetBoolean(reader.GetOrdinal("is_in_mixed_folder"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("locked_fields")))
            {
                item.LockedFields = reader.GetString(reader.GetOrdinal("locked_fields"))
                    .Split('|')
                    .Where(i => !string.IsNullOrWhiteSpace(i))
                    .Select(i =>
                    {
                        MetadataFields parsedValue;
                        return Enum.TryParse(i, true, out parsedValue) ? (MetadataFields?)parsedValue : null;
                    })
                    .Where(i => i.HasValue)
                    .Select(i => i.Value)
                    .ToArray();
            }

            if (!reader.IsDBNull(reader.GetOrdinal("inherited_parental_rating_value")))
            {
                item.InheritedParentalRatingValue = reader.GetInt32(reader.GetOrdinal("inherited_parental_rating_value"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("critic_rating")))
            {
                item.CriticRating = (float)reader.GetDouble(reader.GetOrdinal("critic_rating"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("presentation_unique_key")))
            {
                item.PresentationUniqueKey = reader.GetString(reader.GetOrdinal("presentation_unique_key"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("original_title")))
            {
                item.OriginalTitle = reader.GetString(reader.GetOrdinal("original_title"));
            }

            var hasSeriesName = item as IHasSeriesName;
            if (hasSeriesName != null && !reader.IsDBNull(reader.GetOrdinal("series_name")))
            {
                hasSeriesName.SeriesName = reader.GetString(reader.GetOrdinal("series_name"));
            }

            var hasSeries = item as IHasSeries;
            if (hasSeries != null)
            {
                if (!reader.IsDBNull(reader.GetOrdinal("series_guid")))
                {
                    hasSeries.SeriesId = reader.GetGuid(reader.GetOrdinal("series_guid"));
                }

                if (!reader.IsDBNull(reader.GetOrdinal("series_presentation_unique_key")))
                {
                    hasSeries.SeriesPresentationUniqueKey = reader.GetString(reader.GetOrdinal("series_presentation_unique_key"));
                }
            }

            var episode = item as Episode;
            if (episode != null)
            {
                // 旧 SeasonId 列 → 新：Episode 的 parent 即 Season
                if (!reader.IsDBNull(reader.GetOrdinal("parent_guid")))
                {
                    episode.SeasonId = reader.GetGuid(reader.GetOrdinal("parent_guid"));
                }
            }

            if (!reader.IsDBNull(reader.GetOrdinal("parent_guid")))
            {
                item.ParentId = reader.GetGuid(reader.GetOrdinal("parent_guid"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("tagline")))
            {
                item.Tagline = reader.GetString(reader.GetOrdinal("tagline"));
            }

            var providerIds = ReadStringOrNull(reader, "provider_ids");
            if (providerIds != null)
            {
                DeserializeProviderIds(providerIds, item);
            }

            var images = ReadStringOrNull(reader, "images");
            if (images != null)
            {
                DeserializeImages(images, item);
            }

            if (!reader.IsDBNull(reader.GetOrdinal("production_locations")))
            {
                try
                {
                    var locations = _jsonSerializer.DeserializeFromString<string[]>(reader.GetString(reader.GetOrdinal("production_locations")));
                    if (locations != null)
                    {
                        item.ProductionLocations = locations.Where(i => !string.IsNullOrWhiteSpace(i)).ToArray();
                    }
                }
                catch
                {
                }
            }

            if (!reader.IsDBNull(reader.GetOrdinal("total_bitrate")))
            {
                item.TotalBitrate = reader.GetInt32(reader.GetOrdinal("total_bitrate"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("extra_type")))
            {
                item.ExtraType = (ExtraType)reader.GetInt32(reader.GetOrdinal("extra_type"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("external_id")))
            {
                item.ExternalId = reader.GetString(reader.GetOrdinal("external_id"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("width")))
            {
                item.Width = reader.GetInt32(reader.GetOrdinal("width"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("height")))
            {
                item.Height = reader.GetInt32(reader.GetOrdinal("height"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("is_virtual_item")))
            {
                item.IsVirtualItem = reader.GetBoolean(reader.GetOrdinal("is_virtual_item"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("external_series_id")))
            {
                item.ExternalSeriesId = reader.GetString(reader.GetOrdinal("external_series_id"));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("owner_id")))
            {
                item.OwnerId = new Guid(reader.GetString(reader.GetOrdinal("owner_id")));
            }

            if (!reader.IsDBNull(reader.GetOrdinal("album")))
            {
                item.Album = reader.GetString(reader.GetOrdinal("album"));
            }

            // ===== item_extradata 聚合列（对照旧 Genres/Studios/Tags/Artists 扁平列） =====
            // 这些列是条件列（HasField(query, ItemFields.X) 才进 SELECT），读取需容错

            var genres = ReadOptionalStringOrNull(reader, "genres");
            if (genres != null)
            {
                item.Genres = genres.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            }

            var studios = ReadOptionalStringOrNull(reader, "studios");
            if (studios != null)
            {
                item.Studios = studios.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            }

            var tags = ReadOptionalStringOrNull(reader, "tags");
            if (tags != null)
            {
                item.Tags = tags.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            }

            var hasArtist = item as IHasArtist;
            if (hasArtist != null)
            {
                var artists = ReadOptionalStringOrNull(reader, "artists");
                if (artists != null)
                {
                    hasArtist.Artists = artists.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                }
            }

            var hasAlbumArtist = item as IHasAlbumArtist;
            if (hasAlbumArtist != null)
            {
                var albumArtists = ReadOptionalStringOrNull(reader, "album_artists");
                if (albumArtists != null)
                {
                    hasAlbumArtist.AlbumArtists = albumArtists.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                }
            }

            return item;
        }

        /// <summary>
        /// 条件列容错读取：列不在 SELECT 清单中时返回 null（extradata 聚合列
        /// 仅在 HasField(query, ItemFields.X) 时才包含）。
        /// </summary>
        private static string ReadOptionalStringOrNull(NpgsqlDataReader reader, string column)
        {
            var ordinal = -1;
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), column, StringComparison.Ordinal))
                {
                    ordinal = i;
                    break;
                }
            }

            if (ordinal < 0)
            {
                return null;
            }

            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static string ReadStringOrNull(NpgsqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        // ===========================================================
        // 序列化助手（对照旧实现）
        // ===========================================================

        /// <summary>
        /// 旧 TypeRequiresDeserialization：这些类型不序列化 data，靠扁平列水合。
        /// </summary>
        internal bool TypeRequiresDeserialization(Type type)
        {
            if (_config.Configuration.SkipDeserializationForBasicTypes)
            {
                if (type == typeof(Channel))
                {
                    return false;
                }
                if (type == typeof(UserRootFolder))
                {
                    return false;
                }
            }

            if (type == typeof(Season)) return false;
            if (type == typeof(MusicArtist)) return false;
            if (type == typeof(Person)) return false;
            if (type == typeof(MusicGenre)) return false;
            if (type == typeof(GameGenre)) return false;
            if (type == typeof(Genre)) return false;
            if (type == typeof(Studio)) return false;
            // PlaylistsFolder 位于 Emby.Server.Implementations（本程序集不引用），按类型全名判定
            if (string.Equals(type.FullName, "Emby.Server.Implementations.Playlists.PlaylistsFolder", StringComparison.Ordinal)) return false;
            if (type == typeof(PhotoAlbum)) return false;
            if (type == typeof(Year)) return false;
            if (type == typeof(Book)) return false;
            if (type == typeof(LiveTvProgram)) return false;
            if (type == typeof(AudioBook)) return false;
            if (type == typeof(Audio)) return false;
            if (type == typeof(MusicAlbum)) return false;

            return true;
        }

        /// <summary>
        /// ProviderIds 序列化为 JSONB（旧为 "K=V|K=V" 管道格式；新 schema 列为 JSONB，
        /// 存 JSON 对象，查询用 provider_ids->>'K' 与 ::text LIKE 两种方式）。
        /// </summary>
        internal string SerializeProviderIds(BaseItem item)
        {
            var ids = item.ProviderIds
                .Where(i => !string.IsNullOrWhiteSpace(i.Value))
                .ToList();

            if (ids.Count == 0)
            {
                return null;
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in ids)
            {
                dict[pair.Key] = pair.Value;
            }

            return _jsonSerializer.SerializeToString(dict);
        }

        internal void DeserializeProviderIds(string value, BaseItem item)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (item.ProviderIds.Count > 0)
            {
                return;
            }

            try
            {
                var dict = _jsonSerializer.DeserializeFromString<Dictionary<string, string>>(value);
                if (dict != null)
                {
                    foreach (var pair in dict)
                    {
                        item.SetProviderId(pair.Key, pair.Value);
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Images 序列化为 JSONB 数组（旧为 "path*ticks*type*w*h|..." 管道格式）。
        /// </summary>
        internal string SerializeImages(BaseItem item)
        {
            var images = item.ImageInfos;

            if (images == null || images.Length == 0)
            {
                return null;
            }

            var imageStrings = images
                .Where(i => !string.IsNullOrWhiteSpace(i.Path))
                .Select(ToValueString)
                .ToArray();

            if (imageStrings.Length == 0)
            {
                return null;
            }

            return _jsonSerializer.SerializeToString(imageStrings);
        }

        internal void DeserializeImages(string value, BaseItem item)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (item.ImageInfos != null && item.ImageInfos.Length > 0)
            {
                return;
            }

            try
            {
                var parts = _jsonSerializer.DeserializeFromString<string[]>(value);
                if (parts != null)
                {
                    var list = new List<ItemImageInfo>();

                    foreach (var part in parts)
                    {
                        var image = ItemImageInfoFromValueString(part);
                        if (image != null)
                        {
                            list.Add(image);
                        }
                    }

                    item.ImageInfos = list.ToArray();
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 单张图片序列化（旧 ToValueString：path*ticks*type*width*height）。
        /// </summary>
        public string ToValueString(ItemImageInfo image)
        {
            var delimeter = "*";

            var path = image.Path ?? string.Empty;

            return GetPathToSave(path) +
                   delimeter +
                   image.DateModified.Ticks.ToString(CultureInfo.InvariantCulture) +
                   delimeter +
                   image.Type +
                   delimeter +
                   image.Width.ToString(CultureInfo.InvariantCulture) +
                   delimeter +
                   image.Height.ToString(CultureInfo.InvariantCulture);
        }

        public ItemImageInfo ItemImageInfoFromValueString(string value)
        {
            var parts = value.Split(new[] { '*' }, StringSplitOptions.None);

            if (parts.Length < 3)
            {
                return null;
            }

            var image = new ItemImageInfo();

            image.Path = RestorePath(parts[0]);

            long ticks;
            if (long.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out ticks))
            {
                image.DateModified = new DateTime(ticks, DateTimeKind.Utc);
            }

            ImageType type;
            if (Enum.TryParse(parts[2], true, out type))
            {
                image.Type = type;
            }

            if (parts.Length >= 5)
            {
                int width;
                int height;
                if (int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out width))
                {
                    if (int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out height))
                    {
                        image.Width = width;
                        image.Height = height;
                    }
                }
            }

            return image;
        }

        private string GetPathToSave(string path)
        {
            if (path == null)
            {
                return null;
            }

            return _appHost != null ? _appHost.ReverseVirtualPath(path) : path;
        }

        private string RestorePath(string path)
        {
            return _appHost != null ? _appHost.ExpandVirtualPath(path) : path;
        }

        /// <summary>
        /// 旧 GetCleanValue：RemoveDiacritics + ToLower。
        /// </summary>
        internal static string GetCleanValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return value.RemoveDiacritics().ToLower();
        }

        /// <summary>
        /// 旧 FixUnicodeChars：搜索词标点归一化。
        /// </summary>
        internal static string FixUnicodeChars(string buffer)
        {
            if (buffer.IndexOf('\u2013') > -1) buffer = buffer.Replace('\u2013', '-'); // en dash
            if (buffer.IndexOf('\u2014') > -1) buffer = buffer.Replace('\u2014', '-'); // em dash
            if (buffer.IndexOf('\u2015') > -1) buffer = buffer.Replace('\u2015', '-'); // horizontal bar
            if (buffer.IndexOf('\u2017') > -1) buffer = buffer.Replace('\u2017', '_'); // double low line
            if (buffer.IndexOf('\u2018') > -1) buffer = buffer.Replace('\u2018', '\''); // left single quotation mark
            if (buffer.IndexOf('\u2019') > -1) buffer = buffer.Replace('\u2019', '\''); // right single quotation mark
            if (buffer.IndexOf('\u201a') > -1) buffer = buffer.Replace('\u201a', ','); // single low-9 quotation mark
            if (buffer.IndexOf('\u201b') > -1) buffer = buffer.Replace('\u201b', '\''); // single high-reversed-9 quotation mark
            if (buffer.IndexOf('\u201c') > -1) buffer = buffer.Replace('\u201c', '\"'); // left double quotation mark
            if (buffer.IndexOf('\u201d') > -1) buffer = buffer.Replace('\u201d', '\"'); // right double quotation mark
            if (buffer.IndexOf('\u201e') > -1) buffer = buffer.Replace('\u201e', '\"'); // double low-9 quotation mark
            if (buffer.IndexOf('\u2026') > -1) buffer = buffer.Replace("\u2026", "..."); // horizontal ellipsis
            if (buffer.IndexOf('\u2032') > -1) buffer = buffer.Replace('\u2032', '\''); // prime
            if (buffer.IndexOf('\u2033') > -1) buffer = buffer.Replace('\u2033', '\"'); // double prime
            if (buffer.IndexOf('\u0060') > -1) buffer = buffer.Replace('\u0060', '\''); // grave accent
            if (buffer.IndexOf('\u00B4') > -1) buffer = buffer.Replace('\u00B4', '\''); // acute accent

            return buffer;
        }

        /// <summary>
        /// 旧 BaseExtensions.GetMD5（CryptographyProvider.GetMD5）：
        /// MD5(UTF-16LE 字节) 前 16 字节构造 Guid。
        /// </summary>
        internal static Guid GetMd5(string str)
        {
            using (var provider = MD5.Create())
            {
                var hash = provider.ComputeHash(Encoding.Unicode.GetBytes(str));
                return new Guid(hash);
            }
        }

        /// <summary>
        /// 复刻 LibraryManager.GetItemByNameId&lt;T&gt;(getPathFn, name)：
        /// guid = MD5(typeof(T).FullName + path.ToLower())（受 EnableNormalizedItemByNameIds /
        /// EnableCaseSensitiveItemIds 配置影响），保证仓储 upsert 的 Person 项与
        /// LibraryManager 创建的 Person 项 GUID 一致，避免重复项。
        /// </summary>
        internal Guid GetItemByNameId(Type type, string path)
        {
            var forceCaseInsensitive = _config.Configuration.EnableNormalizedItemByNameIds;

            var key = path;

            if (forceCaseInsensitive || !_config.Configuration.EnableCaseSensitiveItemIds)
            {
                key = key.ToLower();
            }

            key = type.FullName + key;

            return GetMd5(key);
        }

        /// <summary>
        /// 复刻 Person.GetPath(name)（PeoplePath 下按首字母分目录）。
        /// </summary>
        internal string GetPersonPath(string name)
        {
            var text = _fileSystem.GetValidFilename(name).Trim().TrimEnd('.');
            string text2 = null;

            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsLetterOrDigit(text[i]))
                {
                    text2 = text[i].ToString();
                    break;
                }
            }

            var peoplePath = _config.ApplicationPaths.PeoplePath;

            if (!string.IsNullOrEmpty(text2))
            {
                return Path.Combine(peoplePath, text2, text);
            }

            return Path.Combine(peoplePath, text);
        }
    }
}
