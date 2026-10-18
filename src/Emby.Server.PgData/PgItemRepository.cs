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
using System.Threading.Tasks;
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
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            var tables = new[] { "media_items", "chapters", "media_streams", "item_people", "ancestor_ids", "item_extradata" };

            foreach (var table in tables)
            {
                if (!await TableExistsAsync(connection, table, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(table + " table does not exist. Schema initialization failed.");
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
        internal async Task<long?> GetInternalIdAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid guid,
            CancellationToken cancellationToken)
        {
            if (guid.Equals(Guid.Empty))
            {
                return null;
            }

            if (_guidToIdMap.TryGetValue(guid, out var cached))
            {
                return cached;
            }

            var result = await ExecuteScalarAsync(
                connection,
                transaction,
                "SELECT id FROM media_items WHERE guid = @guid LIMIT 1",
                cancellationToken,
                new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = guid }).ConfigureAwait(false);

            if (result == null || result == DBNull.Value)
            {
                return null;
            }

            var id = Convert.ToInt64(result);
            _guidToIdMap[guid] = id;
            return id;
        }

        // ===========================================================
        // SaveItem / SaveItems / SaveImages / DeleteItem
        // ===========================================================

        /// <summary>
        /// 保存单个媒体项（旧 SaveItem → SaveItems）。
        /// </summary>
        public Task SaveItemAsync(BaseItem item, CancellationToken cancellationToken)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return SaveItemsAsync(new List<BaseItem> { item }, cancellationToken);
        }

        /// <summary>
        /// 保存图片列（旧 SaveImages：Update TypedBaseItems set Images where guid）。
        /// </summary>
        public async Task SaveImagesAsync(BaseItem item, CancellationToken cancellationToken = default)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            CheckDisposed();

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            await ExecuteNonQueryAsync(
                connection,
                null,
                "UPDATE media_items SET images = @images WHERE guid = @guid",
                cancellationToken,
                new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = item.Id },
                new NpgsqlParameter("images", NpgsqlDbType.Jsonb) { Value = ToDbValue(SerializeImages(item)) }).ConfigureAwait(false);
        }

        /// <summary>
        /// 批量提交块大小（工业级推荐 200~500：短事务限制锁持有时间与 WAL 体积）。
        /// </summary>
        private const int BatchChunkSize = 250;

        /// <summary>
        /// 媒体项批量写入的列清单（顺序与 SaveItemsBatchAsync 的参数数组严格一致）。
        /// </summary>
        private const string MediaItemsBatchColumns =
            "guid, type, type_name, data, parent_id, path, start_date, end_date, " +
            "is_movie, is_series, is_repeat, community_rating, custom_rating, " +
            "index_number, is_locked, name, official_rating, overview, " +
            "parent_index_number, premiere_date, production_year, sort_name, " +
            "runtime_ticks, date_created, date_modified, preferred_metadata_language, " +
            "preferred_metadata_country_code, date_last_refreshed, date_last_saved, " +
            "is_in_mixed_folder, locked_fields, inherited_parental_rating_value, " +
            "critic_rating, presentation_unique_key, original_title, series_name, " +
            "series_id, album_id, tagline, provider_ids, images, production_locations, " +
            "total_bitrate, extra_type, series_presentation_unique_key, external_id, " +
            "width, height, size, container, user_data_key_id, " +
            "is_folder, is_virtual_item, media_type, channel_id, top_parent_id, " +
            "clean_name, album, extra_ids, unrated_type, trailer_types, " +
            "external_series_id, owner_id, date_last_media_added";

        /// <summary>
        /// 批量保存（真批量：单条 UNNEST 语句落库整块，网络往返 O(1)）。
        ///
        /// 流程（每块 250 条，独立短事务）：
        ///  1. 删除同 guid 异 type 的历史行（罕见类型变更路径）；
        ///  2. 批量解析 user_data_key → id（单次 UNNEST）；
        ///  3. 单条 INSERT ... SELECT FROM UNNEST(...) ON CONFLICT (guid, type) DO UPDATE 落库全部行；
        ///  4. 单条 UPDATE ... FROM (VALUES ...) 回填 parent_id/series_id；
        ///  5. 祖先闭包表与 extradata 亦为批量 UNNEST。
        /// 块内按 Guid 升序，保证并发事务加锁顺序一致（杜绝 40P01）。
        /// </summary>
        public async Task SaveItemsAsync(List<BaseItem> items, CancellationToken cancellationToken)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            cancellationToken.ThrowIfCancellationRequested();
            CheckDisposed();

            var sortedItems = items.Where(i => i != null).OrderBy(i => i.Id).ToList();
            if (sortedItems.Count == 0)
            {
                return;
            }

            for (int chunkStart = 0; chunkStart < sortedItems.Count; chunkStart += BatchChunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = sortedItems.Skip(chunkStart).Take(BatchChunkSize).ToList();

                await SaveItemsChunkAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 单块批量落库（一个短事务内完成，全部为集合式 SQL）。
        /// </summary>
        private Task SaveItemsChunkAsync(List<BaseItem> chunk, CancellationToken cancellationToken)
        {
            // ---- 事务外完成全部内存侧字段提取（不占用连接）----
            var rows = chunk.Select(item => BuildBatchRow(item)).ToList();
            var ancestorRows = chunk
                .Where(i => i.SupportsAncestors)
                .Select(i => (Item: i.Id, Ancestors: i.GetAncestorIds().Distinct().OrderBy(g => g).ToList()))
                .ToList();
            var extraRows = chunk
                .Select(i => (Item: i.Id, Values: GetItemValuesToSave(i, i.GetInheritedTags())))
                .ToList();

            return RunInTransactionAsync(async (connection, transaction, ct) =>
            {
                // ---- 1. 清理同 guid 异 type 的历史行（一次集合删除）----
                var guids = rows.Select(r => r.Guid).ToArray();
                var types = rows.Select(r => r.TypeCode).ToArray();
                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    @"DELETE FROM media_items t
                      WHERE t.guid = ANY(@guids)
                        AND EXISTS (
                            SELECT 1 FROM UNNEST(@guids, @types) AS x(g, ty)
                            WHERE x.g = t.guid AND x.ty <> t.type)",
                    ct,
                    new NpgsqlParameter("guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = guids },
                    new NpgsqlParameter("types", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = types }).ConfigureAwait(false);

                // ---- 2. 批量解析 user_data_key → id ----
                var userDataKeyIdMap = await ResolveUserDataKeyIdsAsync(connection, transaction, rows, ct).ConfigureAwait(false);

                // ---- 3. 单条 UNNEST 批量 UPSERT 全部媒体项（引用列先留 NULL）----
                var insertedIds = await UpsertMediaItemsBatchAsync(connection, transaction, rows, userDataKeyIdMap, ct).ConfigureAwait(false);
                for (int i = 0; i < rows.Count; i++)
                {
                    _guidToIdMap[rows[i].Guid] = insertedIds[i];
                }

                // ---- 4. 单条集合 UPDATE 回填 parent_id / series_id（GUID → id 子查询）----
                await BackfillReferencesAsync(connection, transaction, rows, ct).ConfigureAwait(false);

                // ---- 5. 祖先闭包 + extradata 批量写入 ----
                await WriteAncestorsBatchAsync(connection, transaction, ancestorRows, ct).ConfigureAwait(false);
                await WriteExtraDataBatchAsync(connection, transaction, extraRows, ct).ConfigureAwait(false);
            }, cancellationToken);
        }

        /// <summary>
        /// 单条媒体项 → 批量行快照（全部标量字段，事务外提取，避免持连接做序列化）。
        /// </summary>
        private sealed class MediaItemBatchRow
        {
            public Guid Guid;
            public int TypeCode;
            public string TypeName;
            public string Data;
            public Guid ParentGuid;
            public string Path;
            public DateTime? StartDate;
            public DateTime? EndDate;
            public bool? IsMovie;
            public bool? IsSeries;
            public bool? IsRepeat;
            public double? CommunityRating;
            public string CustomRating;
            public int? IndexNumber;
            public bool IsLocked;
            public string Name;
            public string OfficialRating;
            public string Overview;
            public int? ParentIndexNumber;
            public DateTime? PremiereDate;
            public int? ProductionYear;
            public string SortName;
            public long? RuntimeTicks;
            public DateTime DateCreated;
            public DateTime DateModified;
            public string PreferredMetadataLanguage;
            public string PreferredMetadataCountryCode;
            public DateTime? DateLastRefreshed;
            public DateTime? DateLastSaved;
            public bool IsInMixedFolder;
            public string LockedFields;
            public int? InheritedParentalRatingValue;
            public double? CriticRating;
            public string PresentationUniqueKey;
            public string OriginalTitle;
            public string SeriesName;
            public Guid SeriesGuid;
            public string Tagline;
            public string ProviderIds;
            public string Images;
            public string ProductionLocations;
            public int? TotalBitrate;
            public int? ExtraType;
            public string SeriesPresentationUniqueKey;
            public string ExternalId;
            public int? Width;
            public int? Height;
            public long? Size;
            public string Container;
            public string UserDataKey;
            public bool IsFolder;
            public bool IsVirtualItem;
            public string MediaType;
            public string ChannelId;
            public string TopParentId;
            public string CleanName;
            public string Album;
            public string ExtraIds;
            public string UnratedType;
            public string TrailerTypes;
            public string ExternalSeriesId;
            public string OwnerId;
            public DateTime? DateLastMediaAdded;
        }

        /// <summary>
        /// 从 BaseItem 提取批量写入所需的全部标量字段（纯内存操作，无 DB 访问）。
        /// </summary>
        private MediaItemBatchRow BuildBatchRow(BaseItem item)
        {
            var type = item.GetType();
            var topParent = item.GetTopParent();
            var hasProgramAttributes = item as IHasProgramAttributes;
            var hasSeries = item as IHasSeries;
            var hasSeriesName = item as IHasSeriesName;
            var hasStartDate = item as IHasStartDate;
            var trailer = item as Trailer;
            var folder = item as Folder;

            return new MediaItemBatchRow
            {
                Guid = item.Id,
                TypeCode = GetTypeCode(type),
                TypeName = type.FullName,
                Data = TypeRequiresDeserialization(type) ? _jsonSerializer.SerializeToString(item) : null,
                ParentGuid = item.ParentId,
                Path = GetPathToSave(item.Path),
                StartDate = hasStartDate != null ? hasStartDate.StartDate : (DateTime?)null,
                EndDate = item.EndDate,
                IsMovie = hasProgramAttributes != null ? hasProgramAttributes.IsMovie : (bool?)null,
                IsSeries = hasProgramAttributes != null ? hasProgramAttributes.IsSeries : (bool?)null,
                IsRepeat = hasProgramAttributes != null ? hasProgramAttributes.IsRepeat : (bool?)null,
                CommunityRating = item.CommunityRating.HasValue ? (double?)item.CommunityRating.Value : null,
                CustomRating = item.CustomRating,
                IndexNumber = item.IndexNumber,
                IsLocked = item.IsLocked,
                Name = item.Name,
                OfficialRating = item.OfficialRating,
                Overview = item.Overview,
                ParentIndexNumber = item.ParentIndexNumber,
                PremiereDate = item.PremiereDate,
                ProductionYear = item.ProductionYear,
                SortName = item.SortName,
                RuntimeTicks = item.RunTimeTicks,
                DateCreated = item.DateCreated,
                DateModified = item.DateModified,
                PreferredMetadataLanguage = item.PreferredMetadataLanguage,
                PreferredMetadataCountryCode = item.PreferredMetadataCountryCode,
                DateLastRefreshed = item.DateLastRefreshed != default(DateTime) ? item.DateLastRefreshed : (DateTime?)null,
                DateLastSaved = item.DateLastSaved != default(DateTime) ? item.DateLastSaved : (DateTime?)null,
                IsInMixedFolder = item.IsInMixedFolder,
                LockedFields = item.LockedFields != null && item.LockedFields.Length > 0
                    ? string.Join("|", item.LockedFields.Select(i => i.ToString()).ToArray())
                    : null,
                InheritedParentalRatingValue = item.InheritedParentalRatingValue > 0 ? item.InheritedParentalRatingValue : (int?)null,
                CriticRating = item.CriticRating.HasValue ? (double?)item.CriticRating.Value : null,
                PresentationUniqueKey = item.PresentationUniqueKey,
                OriginalTitle = item.OriginalTitle,
                SeriesName = hasSeriesName != null && !string.IsNullOrEmpty(hasSeriesName.SeriesName) ? hasSeriesName.SeriesName : null,
                SeriesGuid = hasSeries != null ? hasSeries.SeriesId : Guid.Empty,
                Tagline = item.Tagline,
                ProviderIds = SerializeProviderIds(item),
                Images = SerializeImages(item),
                ProductionLocations = item.ProductionLocations != null && item.ProductionLocations.Length > 0
                    ? _jsonSerializer.SerializeToString(item.ProductionLocations)
                    : null,
                TotalBitrate = item.TotalBitrate,
                ExtraType = item.ExtraType.HasValue ? (int?)item.ExtraType.Value : null,
                SeriesPresentationUniqueKey = hasSeries != null && !string.IsNullOrEmpty(hasSeries.SeriesPresentationUniqueKey)
                    ? hasSeries.SeriesPresentationUniqueKey
                    : null,
                ExternalId = item.ExternalId,
                Width = item.Width > 0 ? item.Width : (int?)null,
                Height = item.Height > 0 ? item.Height : (int?)null,
                Size = item.Size,
                Container = item.Container,
                UserDataKey = item.GetUserDataKeys().FirstOrDefault(),
                IsFolder = item.IsFolder,
                IsVirtualItem = item.IsVirtualItem,
                MediaType = item.MediaType,
                ChannelId = item.ChannelId.Equals(Guid.Empty) ? null : item.ChannelId.ToString("N"),
                TopParentId = topParent != null ? topParent.Id.ToString("N") : null,
                CleanName = string.IsNullOrWhiteSpace(item.Name) ? null : GetCleanValue(item.Name),
                Album = item.Album,
                ExtraIds = item.ExtraIds != null && item.ExtraIds.Length > 0
                    ? string.Join("|", item.ExtraIds.Select(i => i.ToString()).ToArray())
                    : null,
                UnratedType = item.GetBlockUnratedType().ToString(),
                TrailerTypes = trailer != null && trailer.TrailerTypes != null && trailer.TrailerTypes.Length > 0
                    ? string.Join("|", trailer.TrailerTypes.Select(i => i.ToString()).ToArray())
                    : null,
                ExternalSeriesId = item.ExternalSeriesId,
                OwnerId = item.OwnerId.Equals(Guid.Empty) ? null : item.OwnerId.ToString("N"),
                DateLastMediaAdded = folder != null ? folder.DateLastMediaAdded : (DateTime?)null
            };
        }

        /// <summary>
        /// 批量解析 user_data_key → user_data_keys.id（单次 UNNEST upsert + 单次查询）。
        /// </summary>
        private static async Task<Dictionary<string, int>> ResolveUserDataKeyIdsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            List<MediaItemBatchRow> rows,
            CancellationToken cancellationToken)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);

            var keys = rows
                .Select(r => r.UserDataKey)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

            if (keys.Length == 0)
            {
                return map;
            }

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                @"INSERT INTO user_data_keys (user_data_key)
                  SELECT unnest_key FROM UNNEST(@keys) AS unnest_key
                  ON CONFLICT (user_data_key) DO UPDATE SET user_data_key = EXCLUDED.user_data_key",
                cancellationToken,
                new NpgsqlParameter("keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = keys }).ConfigureAwait(false);

            await using (var cmd = new NpgsqlCommand(
                "SELECT id, user_data_key FROM user_data_keys WHERE user_data_key = ANY(@keys)",
                connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = keys });
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    map[reader.GetString(1)] = reader.GetInt32(0);
                }
            }

            return map;
        }

        /// <summary>
        /// 单条 UNNEST 语句批量 UPSERT 全部媒体项，返回按输入顺序排列的 id 数组。
        /// </summary>
        private static async Task<long[]> UpsertMediaItemsBatchAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            List<MediaItemBatchRow> rows,
            Dictionary<string, int> userDataKeyIdMap,
            CancellationToken cancellationToken)
        {
            var count = rows.Count;

            var guid = new Guid[count];
            var type = new int[count];
            var typeName = new string[count];
            var data = new string[count];
            var parentGuid = new Guid[count];
            var path = new string[count];
            var startDate = new object[count];
            var endDate = new object[count];
            var isMovie = new object[count];
            var isSeries = new object[count];
            var isRepeat = new object[count];
            var communityRating = new object[count];
            var customRating = new string[count];
            var indexNumber = new object[count];
            var isLocked = new bool[count];
            var name = new string[count];
            var officialRating = new string[count];
            var overview = new string[count];
            var parentIndexNumber = new object[count];
            var premiereDate = new object[count];
            var productionYear = new object[count];
            var sortName = new string[count];
            var runtimeTicks = new object[count];
            var dateCreated = new DateTime[count];
            var dateModified = new DateTime[count];
            var prefLang = new string[count];
            var prefCountry = new string[count];
            var dateLastRefreshed = new object[count];
            var dateLastSaved = new object[count];
            var isInMixedFolder = new bool[count];
            var lockedFields = new string[count];
            var inheritedParentalRating = new object[count];
            var criticRating = new object[count];
            var presentationUniqueKey = new string[count];
            var originalTitle = new string[count];
            var seriesName = new string[count];
            var seriesGuid = new Guid[count];
            var tagline = new string[count];
            var providerIds = new string[count];
            var images = new string[count];
            var productionLocations = new string[count];
            var totalBitrate = new object[count];
            var extraType = new object[count];
            var seriesPresentationUniqueKey = new string[count];
            var externalId = new string[count];
            var width = new object[count];
            var height = new object[count];
            var size = new object[count];
            var container = new string[count];
            var userDataKeyId = new object[count];
            var isFolder = new bool[count];
            var isVirtualItem = new bool[count];
            var mediaType = new string[count];
            var channelId = new string[count];
            var topParentId = new string[count];
            var cleanName = new string[count];
            var album = new string[count];
            var extraIds = new string[count];
            var unratedType = new string[count];
            var trailerTypes = new string[count];
            var externalSeriesId = new string[count];
            var ownerId = new string[count];
            var dateLastMediaAdded = new object[count];

            for (int i = 0; i < count; i++)
            {
                var r = rows[i];
                guid[i] = r.Guid;
                type[i] = r.TypeCode;
                typeName[i] = r.TypeName;
                data[i] = r.Data;
                parentGuid[i] = r.ParentGuid;
                path[i] = r.Path;
                startDate[i] = ToDbValue(r.StartDate);
                endDate[i] = ToDbValue(r.EndDate);
                isMovie[i] = r.IsMovie.HasValue ? (object)r.IsMovie.Value : DBNull.Value;
                isSeries[i] = r.IsSeries.HasValue ? (object)r.IsSeries.Value : DBNull.Value;
                isRepeat[i] = r.IsRepeat.HasValue ? (object)r.IsRepeat.Value : DBNull.Value;
                communityRating[i] = r.CommunityRating.HasValue ? (object)r.CommunityRating.Value : DBNull.Value;
                customRating[i] = r.CustomRating;
                indexNumber[i] = r.IndexNumber.HasValue ? (object)r.IndexNumber.Value : DBNull.Value;
                isLocked[i] = r.IsLocked;
                name[i] = r.Name;
                officialRating[i] = r.OfficialRating;
                overview[i] = r.Overview;
                parentIndexNumber[i] = r.ParentIndexNumber.HasValue ? (object)r.ParentIndexNumber.Value : DBNull.Value;
                premiereDate[i] = ToDbValue(r.PremiereDate);
                productionYear[i] = r.ProductionYear.HasValue ? (object)r.ProductionYear.Value : DBNull.Value;
                sortName[i] = r.SortName;
                runtimeTicks[i] = r.RuntimeTicks.HasValue ? (object)r.RuntimeTicks.Value : DBNull.Value;
                dateCreated[i] = ToUtcDateTime(r.DateCreated);
                dateModified[i] = ToUtcDateTime(r.DateModified);
                prefLang[i] = r.PreferredMetadataLanguage;
                prefCountry[i] = r.PreferredMetadataCountryCode;
                dateLastRefreshed[i] = ToDbValue(r.DateLastRefreshed);
                dateLastSaved[i] = ToDbValue(r.DateLastSaved);
                isInMixedFolder[i] = r.IsInMixedFolder;
                lockedFields[i] = r.LockedFields;
                inheritedParentalRating[i] = r.InheritedParentalRatingValue.HasValue ? (object)r.InheritedParentalRatingValue.Value : DBNull.Value;
                criticRating[i] = r.CriticRating.HasValue ? (object)r.CriticRating.Value : DBNull.Value;
                presentationUniqueKey[i] = r.PresentationUniqueKey;
                originalTitle[i] = r.OriginalTitle;
                seriesName[i] = r.SeriesName;
                seriesGuid[i] = r.SeriesGuid;
                tagline[i] = r.Tagline;
                providerIds[i] = r.ProviderIds;
                images[i] = r.Images;
                productionLocations[i] = r.ProductionLocations;
                totalBitrate[i] = r.TotalBitrate.HasValue ? (object)r.TotalBitrate.Value : DBNull.Value;
                extraType[i] = r.ExtraType.HasValue ? (object)r.ExtraType.Value : DBNull.Value;
                seriesPresentationUniqueKey[i] = r.SeriesPresentationUniqueKey;
                externalId[i] = r.ExternalId;
                width[i] = r.Width.HasValue ? (object)r.Width.Value : DBNull.Value;
                height[i] = r.Height.HasValue ? (object)r.Height.Value : DBNull.Value;
                size[i] = r.Size.HasValue ? (object)r.Size.Value : DBNull.Value;
                container[i] = r.Container;
                userDataKeyId[i] = !string.IsNullOrWhiteSpace(r.UserDataKey) && userDataKeyIdMap.TryGetValue(r.UserDataKey, out var kid)
                    ? (object)kid
                    : DBNull.Value;
                isFolder[i] = r.IsFolder;
                isVirtualItem[i] = r.IsVirtualItem;
                mediaType[i] = r.MediaType;
                channelId[i] = r.ChannelId;
                topParentId[i] = r.TopParentId;
                cleanName[i] = r.CleanName;
                album[i] = r.Album;
                extraIds[i] = r.ExtraIds;
                unratedType[i] = r.UnratedType;
                trailerTypes[i] = r.TrailerTypes;
                externalSeriesId[i] = r.ExternalSeriesId;
                ownerId[i] = r.OwnerId;
                dateLastMediaAdded[i] = ToDbValue(r.DateLastMediaAdded);
            }

            // 单条语句：UNNEST 展开全部列 → INSERT ... SELECT → ON CONFLICT DO UPDATE，RETURNING id
            var sql = @"
                INSERT INTO media_items (" + MediaItemsBatchColumns + @")
                SELECT
                    u.guid, u.type, u.type_name, u.data, u.parent_id, u.path, u.start_date, u.end_date,
                    u.is_movie, u.is_series, u.is_repeat, u.community_rating, u.custom_rating,
                    u.index_number, u.is_locked, u.name, u.official_rating, u.overview,
                    u.parent_index_number, u.premiere_date, u.production_year, u.sort_name,
                    u.runtime_ticks, u.date_created, u.date_modified, u.preferred_metadata_language,
                    u.preferred_metadata_country_code, u.date_last_refreshed, u.date_last_saved,
                    u.is_in_mixed_folder, u.locked_fields, u.inherited_parental_rating_value,
                    u.critic_rating, u.presentation_unique_key, u.original_title, u.series_name,
                    u.series_id, u.album_id, u.tagline, u.provider_ids, u.images, u.production_locations,
                    u.total_bitrate, u.extra_type, u.series_presentation_unique_key, u.external_id,
                    u.width, u.height, u.size, u.container, u.user_data_key_id,
                    u.is_folder, u.is_virtual_item, u.media_type, u.channel_id, u.top_parent_id,
                    u.clean_name, u.album, u.extra_ids, u.unrated_type, u.trailer_types,
                    u.external_series_id, u.owner_id, u.date_last_media_added
                FROM UNNEST(
                    @guid, @type, @type_name, @data, @parent_id, @path, @start_date, @end_date,
                    @is_movie, @is_series, @is_repeat, @community_rating, @custom_rating,
                    @index_number, @is_locked, @name, @official_rating, @overview,
                    @parent_index_number, @premiere_date, @production_year, @sort_name,
                    @runtime_ticks, @date_created, @date_modified, @preferred_metadata_language,
                    @preferred_metadata_country_code, @date_last_refreshed, @date_last_saved,
                    @is_in_mixed_folder, @locked_fields, @inherited_parental_rating_value,
                    @critic_rating, @presentation_unique_key, @original_title, @series_name,
                    @series_id, @album_id, @tagline, @provider_ids, @images, @production_locations,
                    @total_bitrate, @extra_type, @series_presentation_unique_key, @external_id,
                    @width, @height, @size, @container, @user_data_key_id,
                    @is_folder, @is_virtual_item, @media_type, @channel_id, @top_parent_id,
                    @clean_name, @album, @extra_ids, @unrated_type, @trailer_types,
                    @external_series_id, @owner_id, @date_last_media_added
                ) AS u(
                    guid, type, type_name, data, parent_id, path, start_date, end_date,
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
                    external_series_id, owner_id, date_last_media_added
                )
                ON CONFLICT (guid, type) DO UPDATE SET
                    type_name = EXCLUDED.type_name, data = EXCLUDED.data,
                    parent_id = EXCLUDED.parent_id, path = EXCLUDED.path,
                    start_date = EXCLUDED.start_date, end_date = EXCLUDED.end_date,
                    is_movie = EXCLUDED.is_movie, is_series = EXCLUDED.is_series,
                    is_repeat = EXCLUDED.is_repeat, community_rating = EXCLUDED.community_rating,
                    custom_rating = EXCLUDED.custom_rating, index_number = EXCLUDED.index_number,
                    is_locked = EXCLUDED.is_locked, name = EXCLUDED.name,
                    official_rating = EXCLUDED.official_rating, overview = EXCLUDED.overview,
                    parent_index_number = EXCLUDED.parent_index_number,
                    premiere_date = EXCLUDED.premiere_date, production_year = EXCLUDED.production_year,
                    sort_name = EXCLUDED.sort_name, runtime_ticks = EXCLUDED.runtime_ticks,
                    date_created = EXCLUDED.date_created, date_modified = EXCLUDED.date_modified,
                    preferred_metadata_language = EXCLUDED.preferred_metadata_language,
                    preferred_metadata_country_code = EXCLUDED.preferred_metadata_country_code,
                    date_last_refreshed = EXCLUDED.date_last_refreshed,
                    date_last_saved = EXCLUDED.date_last_saved,
                    is_in_mixed_folder = EXCLUDED.is_in_mixed_folder,
                    locked_fields = EXCLUDED.locked_fields,
                    inherited_parental_rating_value = EXCLUDED.inherited_parental_rating_value,
                    critic_rating = EXCLUDED.critic_rating,
                    presentation_unique_key = EXCLUDED.presentation_unique_key,
                    original_title = EXCLUDED.original_title, series_name = EXCLUDED.series_name,
                    series_id = EXCLUDED.series_id, album_id = EXCLUDED.album_id,
                    tagline = EXCLUDED.tagline, provider_ids = EXCLUDED.provider_ids,
                    images = EXCLUDED.images, production_locations = EXCLUDED.production_locations,
                    total_bitrate = EXCLUDED.total_bitrate, extra_type = EXCLUDED.extra_type,
                    series_presentation_unique_key = EXCLUDED.series_presentation_unique_key,
                    external_id = EXCLUDED.external_id, width = EXCLUDED.width,
                    height = EXCLUDED.height, size = EXCLUDED.size, container = EXCLUDED.container,
                    user_data_key_id = EXCLUDED.user_data_key_id, is_folder = EXCLUDED.is_folder,
                    is_virtual_item = EXCLUDED.is_virtual_item, media_type = EXCLUDED.media_type,
                    channel_id = EXCLUDED.channel_id, top_parent_id = EXCLUDED.top_parent_id,
                    clean_name = EXCLUDED.clean_name, album = EXCLUDED.album,
                    extra_ids = EXCLUDED.extra_ids, unrated_type = EXCLUDED.unrated_type,
                    trailer_types = EXCLUDED.trailer_types,
                    external_series_id = EXCLUDED.external_series_id,
                    owner_id = EXCLUDED.owner_id,
                    date_last_media_added = EXCLUDED.date_last_media_added
                RETURNING id";

            await using var cmd = new NpgsqlCommand(sql, connection, transaction);
            cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = guid });
            cmd.Parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = type });
            cmd.Parameters.Add(new NpgsqlParameter("type_name", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = typeName });
            cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Array | NpgsqlDbType.Jsonb) { Value = data });
            cmd.Parameters.Add(new NpgsqlParameter("parent_id", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = parentGuid });
            cmd.Parameters.Add(new NpgsqlParameter("path", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = path });
            cmd.Parameters.Add(new NpgsqlParameter("start_date", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = startDate });
            cmd.Parameters.Add(new NpgsqlParameter("end_date", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = endDate });
            cmd.Parameters.Add(new NpgsqlParameter("is_movie", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isMovie });
            cmd.Parameters.Add(new NpgsqlParameter("is_series", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isSeries });
            cmd.Parameters.Add(new NpgsqlParameter("is_repeat", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isRepeat });
            cmd.Parameters.Add(new NpgsqlParameter("community_rating", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = communityRating });
            cmd.Parameters.Add(new NpgsqlParameter("custom_rating", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = customRating });
            cmd.Parameters.Add(new NpgsqlParameter("index_number", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = indexNumber });
            cmd.Parameters.Add(new NpgsqlParameter("is_locked", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isLocked });
            cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = name });
            cmd.Parameters.Add(new NpgsqlParameter("official_rating", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = officialRating });
            cmd.Parameters.Add(new NpgsqlParameter("overview", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = overview });
            cmd.Parameters.Add(new NpgsqlParameter("parent_index_number", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = parentIndexNumber });
            cmd.Parameters.Add(new NpgsqlParameter("premiere_date", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = premiereDate });
            cmd.Parameters.Add(new NpgsqlParameter("production_year", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = productionYear });
            cmd.Parameters.Add(new NpgsqlParameter("sort_name", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = sortName });
            cmd.Parameters.Add(new NpgsqlParameter("runtime_ticks", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = runtimeTicks });
            cmd.Parameters.Add(new NpgsqlParameter("date_created", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = dateCreated });
            cmd.Parameters.Add(new NpgsqlParameter("date_modified", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = dateModified });
            cmd.Parameters.Add(new NpgsqlParameter("preferred_metadata_language", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = prefLang });
            cmd.Parameters.Add(new NpgsqlParameter("preferred_metadata_country_code", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = prefCountry });
            cmd.Parameters.Add(new NpgsqlParameter("date_last_refreshed", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = dateLastRefreshed });
            cmd.Parameters.Add(new NpgsqlParameter("date_last_saved", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = dateLastSaved });
            cmd.Parameters.Add(new NpgsqlParameter("is_in_mixed_folder", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isInMixedFolder });
            cmd.Parameters.Add(new NpgsqlParameter("locked_fields", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = lockedFields });
            cmd.Parameters.Add(new NpgsqlParameter("inherited_parental_rating_value", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = inheritedParentalRating });
            cmd.Parameters.Add(new NpgsqlParameter("critic_rating", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = criticRating });
            cmd.Parameters.Add(new NpgsqlParameter("presentation_unique_key", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = presentationUniqueKey });
            cmd.Parameters.Add(new NpgsqlParameter("original_title", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = originalTitle });
            cmd.Parameters.Add(new NpgsqlParameter("series_name", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = seriesName });
            // series_id / album_id 为 BIGINT 外键：本批次先写 NULL，由 BackfillReferencesAsync 统一解析回填
            cmd.Parameters.Add(new NpgsqlParameter("series_id", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
            {
                Value = new long?[count]
            });
            cmd.Parameters.Add(new NpgsqlParameter("album_id", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
            {
                Value = new long?[count]
            });
            cmd.Parameters.Add(new NpgsqlParameter("tagline", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = tagline });
            cmd.Parameters.Add(new NpgsqlParameter("provider_ids", NpgsqlDbType.Array | NpgsqlDbType.Jsonb) { Value = providerIds });
            cmd.Parameters.Add(new NpgsqlParameter("images", NpgsqlDbType.Array | NpgsqlDbType.Jsonb) { Value = images });
            cmd.Parameters.Add(new NpgsqlParameter("production_locations", NpgsqlDbType.Array | NpgsqlDbType.Jsonb) { Value = productionLocations });
            cmd.Parameters.Add(new NpgsqlParameter("total_bitrate", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = totalBitrate });
            cmd.Parameters.Add(new NpgsqlParameter("extra_type", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = extraType });
            cmd.Parameters.Add(new NpgsqlParameter("series_presentation_unique_key", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = seriesPresentationUniqueKey });
            cmd.Parameters.Add(new NpgsqlParameter("external_id", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = externalId });
            cmd.Parameters.Add(new NpgsqlParameter("width", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = width });
            cmd.Parameters.Add(new NpgsqlParameter("height", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = height });
            cmd.Parameters.Add(new NpgsqlParameter("size", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = size });
            cmd.Parameters.Add(new NpgsqlParameter("container", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = container });
            cmd.Parameters.Add(new NpgsqlParameter("user_data_key_id", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = userDataKeyId });
            cmd.Parameters.Add(new NpgsqlParameter("is_folder", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isFolder });
            cmd.Parameters.Add(new NpgsqlParameter("is_virtual_item", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isVirtualItem });
            cmd.Parameters.Add(new NpgsqlParameter("media_type", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = mediaType });
            cmd.Parameters.Add(new NpgsqlParameter("channel_id", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = channelId });
            cmd.Parameters.Add(new NpgsqlParameter("top_parent_id", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = topParentId });
            cmd.Parameters.Add(new NpgsqlParameter("clean_name", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = cleanName });
            cmd.Parameters.Add(new NpgsqlParameter("album", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = album });
            cmd.Parameters.Add(new NpgsqlParameter("extra_ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = extraIds });
            cmd.Parameters.Add(new NpgsqlParameter("unrated_type", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = unratedType });
            cmd.Parameters.Add(new NpgsqlParameter("trailer_types", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = trailerTypes });
            cmd.Parameters.Add(new NpgsqlParameter("external_series_id", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = externalSeriesId });
            cmd.Parameters.Add(new NpgsqlParameter("owner_id", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = ownerId });
            cmd.Parameters.Add(new NpgsqlParameter("date_last_media_added", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = dateLastMediaAdded });

            var ids = new List<long>(count);
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    ids.Add(reader.GetInt64(0));
                }
            }

            // RETURNING 顺序不保证与输入一致 → 再按 guid 对齐
            var map = new Dictionary<Guid, long>(ids.Count);
            if (ids.Count == count)
            {
                for (int i = 0; i < count; i++)
                {
                    map[guid[i]] = ids[i];
                }
            }
            else
            {
                await using var lookup = new NpgsqlCommand(
                    "SELECT guid, id FROM media_items WHERE guid = ANY(@guids)",
                    connection, transaction);
                lookup.Parameters.Add(new NpgsqlParameter("guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = guid });
                await using var lr = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await lr.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    map[lr.GetGuid(0)] = lr.GetInt64(1);
                }
            }

            var result = new long[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = map.TryGetValue(guid[i], out var v) ? v : 0L;
            }

            return result;
        }

        /// <summary>
        /// 单条集合 UPDATE 回填 parent_id / series_id（子查询解析 GUID，块内拓扑顺序无关）。
        /// </summary>
        private static Task BackfillReferencesAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            List<MediaItemBatchRow> rows,
            CancellationToken cancellationToken)
        {
            var count = rows.Count;
            var guids = rows.Select(r => r.Guid).ToArray();
            var parentGuids = rows.Select(r => r.ParentGuid).ToArray();
            var seriesGuids = rows.Select(r => r.SeriesGuid).ToArray();

            return ExecuteNonQueryAsync(
                connection,
                transaction,
                @"UPDATE media_items m SET
                      parent_id = CASE WHEN u.pg = '00000000-0000-0000-0000-000000000000'::uuid
                                       THEN NULL
                                       ELSE (SELECT p.id FROM media_items p WHERE p.guid = u.pg LIMIT 1) END,
                      series_id = CASE WHEN u.sg = '00000000-0000-0000-0000-000000000000'::uuid
                                       THEN NULL
                                       ELSE (SELECT s.id FROM media_items s WHERE s.guid = u.sg LIMIT 1) END
                  FROM UNNEST(@guids, @parent_guids, @series_guids) AS u(g, pg, sg)
                  WHERE m.guid = u.g",
                cancellationToken,
                new NpgsqlParameter("guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = guids },
                new NpgsqlParameter("parent_guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = parentGuids },
                new NpgsqlParameter("series_guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = seriesGuids });
        }

        /// <summary>
        /// 祖先闭包表批量写入（单条 UNNEST：先按 item 集合删除，再集合插入）。
        /// </summary>
        private static async Task WriteAncestorsBatchAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            List<(Guid Item, List<Guid> Ancestors)> ancestorRows,
            CancellationToken cancellationToken)
        {
            if (ancestorRows.Count == 0)
            {
                return;
            }

            var itemGuids = ancestorRows.Select(r => r.Item).ToArray();

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                @"DELETE FROM ancestor_ids a
                  USING media_items m
                  WHERE a.item_id = m.id AND m.guid = ANY(@item_guids)",
                cancellationToken,
                new NpgsqlParameter("item_guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = itemGuids }).ConfigureAwait(false);

            var childGuids = new List<Guid>();
            var ancestorGuids = new List<Guid>();
            foreach (var row in ancestorRows)
            {
                foreach (var anc in row.Ancestors)
                {
                    if (anc.Equals(Guid.Empty))
                    {
                        continue;
                    }

                    childGuids.Add(row.Item);
                    ancestorGuids.Add(anc);
                }
            }

            if (childGuids.Count == 0)
            {
                return;
            }

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                @"INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
                  SELECT c.id, p.id, 0
                  FROM UNNEST(@child_guids, @ancestor_guids) AS u(cg, ag)
                  JOIN media_items c ON c.guid = u.cg
                  JOIN media_items p ON p.guid = u.ag
                  ON CONFLICT (item_id, ancestor_id) DO NOTHING",
                cancellationToken,
                new NpgsqlParameter("child_guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = childGuids.ToArray() },
                new NpgsqlParameter("ancestor_guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ancestorGuids.ToArray() }).ConfigureAwait(false);
        }

        /// <summary>
        /// extradata 批量写入（单条 UNNEST：先按 item 集合删除，再集合插入）。
        /// </summary>
        private static async Task WriteExtraDataBatchAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            List<(Guid Item, List<Tuple<int, string>> Values)> extraRows,
            CancellationToken cancellationToken)
        {
            if (extraRows.Count == 0)
            {
                return;
            }

            var itemGuids = extraRows.Select(r => r.Item).ToArray();

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                @"DELETE FROM item_extradata e
                  USING media_items m
                  WHERE e.item_id = m.id AND m.guid = ANY(@item_guids)",
                cancellationToken,
                new NpgsqlParameter("item_guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = itemGuids }).ConfigureAwait(false);

            var childGuids = new List<Guid>();
            var typeIds = new List<int>();
            var values = new List<string>();
            var cleanValues = new List<string>();

            foreach (var row in extraRows)
            {
                foreach (var v in row.Values.Where(v => !string.IsNullOrWhiteSpace(v.Item2))
                                            .OrderBy(v => v.Item1)
                                            .ThenBy(v => v.Item2, StringComparer.Ordinal))
                {
                    childGuids.Add(row.Item);
                    typeIds.Add(v.Item1);
                    values.Add(v.Item2);
                    cleanValues.Add(GetCleanValue(v.Item2));
                }
            }

            if (childGuids.Count == 0)
            {
                return;
            }

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                @"INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
                  SELECT m.id, u.type_id, u.val, u.clean_val
                  FROM UNNEST(@child_guids, @type_ids, @vals, @clean_vals) AS u(cg, type_id, val, clean_val)
                  JOIN media_items m ON m.guid = u.cg
                  ON CONFLICT (item_id, extradata_type_id, value) DO NOTHING",
                cancellationToken,
                new NpgsqlParameter("child_guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = childGuids.ToArray() },
                new NpgsqlParameter("type_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = typeIds.ToArray() },
                new NpgsqlParameter("vals", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values.ToArray() },
                new NpgsqlParameter("clean_vals", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = cleanValues.ToArray() }).ConfigureAwait(false);
        }

        /// <summary>
        /// 删除媒体项（旧 DeleteItem：显式删除 People/Chapters/MediaStreams/AncestorIds/ItemValues/TypedBaseItems）。
        /// 新 schema 无外键级联（非法单列外键已在 SQL 文件内移除，完整性由应用层维护），同样显式删除子行。
        /// </summary>
        public Task DeleteItemAsync(Guid id, CancellationToken cancellationToken)
        {
            if (id.Equals(Guid.Empty))
            {
                throw new ArgumentNullException("id");
            }

            CheckDisposed();

            return RunInTransactionAsync(async (connection, transaction, ct) =>
            {
                var result = await ExecuteScalarAsync(
                    connection,
                    transaction,
                    "SELECT id FROM media_items WHERE guid = @guid LIMIT 1",
                    ct,
                    new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = id }).ConfigureAwait(false);

                if (result == null || result == DBNull.Value)
                {
                    return;
                }

                var itemId = Convert.ToInt64(result);

                // 子表清理（固定顺序，避免与其它事务交叉加锁）
                foreach (var table in new[] { "item_people", "chapters", "media_streams", "ancestor_ids", "item_extradata" })
                {
                    await ExecuteNonQueryAsync(
                        connection,
                        transaction,
                        "DELETE FROM " + table + " WHERE item_id = @id",
                        ct,
                        new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = itemId }).ConfigureAwait(false);
                }

                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    "DELETE FROM media_items WHERE id = @id",
                    ct,
                    new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = itemId }).ConfigureAwait(false);

                _guidToIdMap.TryRemove(id, out _);
            }, cancellationToken);
        }

        // ===========================================================
        // RetrieveItem / GetItem 水合
        // ===========================================================

        /// <summary>
        /// 按 GUID 检索单个媒体项（旧 RetrieveItem）。
        /// </summary>
        public async Task<BaseItem> RetrieveItemAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (id.Equals(Guid.Empty))
            {
                throw new ArgumentNullException(nameof(id));
            }

            CheckDisposed();

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = new NpgsqlCommand(
                BuildItemSelectText(null) + " FROM media_items A WHERE A.guid = @guid LIMIT 1",
                connection);
            cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = id });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return GetItem(reader, connection);
            }

            return null;
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
                // P8：partial hydration 路径摘掉 data 列（~1.5KB/行的传输+PG 成本白付，
                // Reviewer 建议②）——query 非 null 且无需 data 独有字段时不选；
                // query == null（RetrieveItem 单条全量）或需要 data 字段时保留。
                // GetItem 的 skipDataHydration 短路保证不读该列，两侧判定一致。
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
            // P2：similarity_score 含 sim.match_count 引用，调用方 FROM 必须拼 GetSimilarityJoinText
            if (query != null && query.SimilarTo != null)
            {
                columns.Add(GetSimilarityScoreExpressionFull() + " AS similarity_score");
            }

            if (query != null && !string.IsNullOrEmpty(query.SearchTerm))
            {
                columns.Add(GetSearchScoreExpression(query) + " AS search_score");
            }

            // P8：partial hydration 路径摘掉 data 列（~1.5KB/行的 PG 读取+网络传输
            // 白付，Reviewer 建议②）——判定与 GetItem 的 skipDataHydration 同源
            // （RequiresFullHydration），两侧一致不会出现"列在但没读/列不在却要读"。
            // query == null（RetrieveItem 单条全量）不摘。
            if (query != null && !RequiresFullHydration(query))
            {
                columns.Remove("A.data::text");
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
        /// P7 partial hydration：skipDataHydration=true 时跳过 data JSONB 反序列化
        /// （~1.5KB/行的全量反序列化是列表页水合主成本，S6 实测 0.37-1.56ms/条）——
        /// 列表 DTO 的核心字段全在扁平/聚合列（Name/Images/ProviderIds/Genres/
        /// Studios/Tags/Artists/排序字段），data 独有的 Overview/People/Taglines
        /// 仅详情页 DtoOptions 需要；partial 实例不进 LibraryItemsCache（GetItemList
        /// 路径无 RegisterItem），不会污染缓存。
        /// </summary>
        internal BaseItem GetItem(NpgsqlDataReader reader, NpgsqlConnection connection, bool skipDataHydration = false)
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

            var data = skipDataHydration ? null : ReadStringOrNull(reader, "data");

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
