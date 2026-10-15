using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Npgsql;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PgItemRepository — 查询引擎：InternalItemsQuery → 动态 SQL。
    /// 对照旧 SqliteItemRepository 的 GetCount/GetItemList/GetItems/GetItemIdsList/
    /// GetItemIdsWithPath/GetItemIds + GetWhereClauses/GetOrderByText 逐条保留语义。
    ///
    /// PG 适配要点：
    /// - 旧 GROUP BY PresentationUniqueKey（SQLite 允许裸列）→ DISTINCT ON + 外层重排序；
    /// - SQLite 布尔乘法 ((cond) * 10) → CASE WHEN；
    /// - WHERE 不能引用 SELECT 别名（SearchScore/SimilarityScore）→ 阈值条件展开为布尔表达式；
    /// - 旧 IncludeItemTypes 的 type TEXT 全名匹配 → type INTEGER 分区码（未登记类型用 type_name 匹配）；
    /// - 旧 UserDatas join (UserDataKey=Key) → user_datas join (user_data_key_id 翻译)。
    /// </summary>
    public partial class PgItemRepository
    {
        // ===========================================================
        // 类型短名 → 完整类型名映射（旧 GetTypeMapDictionary）
        // ===========================================================

        private static readonly Type[] KnownTypes =
        {
            typeof(LiveTvProgram),
            typeof(LiveTvChannel),
            typeof(Series),
            typeof(Audio),
            typeof(MusicAlbum),
            typeof(MusicArtist),
            typeof(MusicGenre),
            typeof(MusicVideo),
            typeof(Movie),
            typeof(Playlist),
            typeof(AudioBook),
            typeof(Trailer),
            typeof(BoxSet),
            typeof(Episode),
            typeof(Season),
            typeof(Book),
            typeof(CollectionFolder),
            typeof(Folder),
            typeof(MediaBrowser.Controller.Entities.Game),
            typeof(GameGenre),
            typeof(GameSystem),
            typeof(Genre),
            typeof(Person),
            typeof(Photo),
            typeof(PhotoAlbum),
            typeof(Studio),
            typeof(UserRootFolder),
            typeof(UserView),
            typeof(Video),
            typeof(Year),
            typeof(Channel),
            typeof(AggregateFolder)
        };

        private static Dictionary<string, string[]> GetTypeMapDictionary()
        {
            var dict = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            // 短名 → FullName：AddTypeFilter/BuildTypeCodeClause 以 FullName 查
            // TypeFullNameToCode 得分区码（A.type IN），未命中才退回 type_name 匹配
            // （type_name 列存 FullName）。旧实现短名→短名恒等映射，导致已登记类型
            // 全部落入 type_name IN ('Series') 短名分支，与全名列值永不匹配。
            foreach (var t in KnownTypes)
            {
                dict[t.Name] = new[] { t.FullName };
            }

            // 旧别名
            dict["Program"] = new[] { typeof(LiveTvProgram).FullName };
            dict["TvChannel"] = new[] { typeof(LiveTvChannel).FullName };

            return dict;
        }

        private readonly Dictionary<string, string[]> _types = GetTypeMapDictionary();

        /// <summary>
        /// 类型短名 → 完整类型名（旧 MapIncludeItemTypes）。
        /// </summary>
        private IEnumerable<string> MapIncludeItemTypes(string value)
        {
            string[] result;
            if (_types.TryGetValue(value, out result))
            {
                return result;
            }

            return new[] { value }.Where(IsValidType);
        }

        private static bool IsValidType(string value)
        {
            return IsAlphaNumeric(value);
        }

        private static bool IsValidMediaType(string value)
        {
            return IsAlphaNumeric(value);
        }

        // ===========================================================
        // 查询字段判定（对照旧 HasXxx 系列）
        // ===========================================================

        private static readonly ItemFields[] AllFields = Enum.GetNames(typeof(ItemFields))
            .Select(i => (ItemFields)Enum.Parse(typeof(ItemFields), i, true))
            .ToArray();

        private static bool HasField(InternalItemsQuery query, ItemFields name)
        {
            switch (name)
            {
                case ItemFields.Tags:
                    return query.DtoOptions.ContainsField(name) || HasProgramAttributes(query);
                case ItemFields.CustomRating:
                case ItemFields.ProductionLocations:
                case ItemFields.Settings:
                case ItemFields.OriginalTitle:
                case ItemFields.Taglines:
                case ItemFields.SortName:
                case ItemFields.Studios:
                case ItemFields.ExtraIds:
                case ItemFields.DateCreated:
                case ItemFields.Overview:
                case ItemFields.Genres:
                case ItemFields.DateLastMediaAdded:
                case ItemFields.PresentationUniqueKey:
                case ItemFields.InheritedParentalRatingValue:
                case ItemFields.ExternalSeriesId:
                case ItemFields.SeriesPresentationUniqueKey:
                case ItemFields.DateLastRefreshed:
                case ItemFields.DateLastSaved:
                    return query.DtoOptions.ContainsField(name);
                default:
                    return true;
            }
        }

        private static bool HasProgramAttributes(InternalItemsQuery query)
        {
            var excludeParentTypes = new string[]
            {
                "Series", "Season", "MusicAlbum", "MusicArtist", "PhotoAlbum"
            };

            if (excludeParentTypes.Contains(query.ParentType ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (query.IncludeItemTypes.Length == 0)
            {
                return true;
            }

            var types = new[] { "Program", "TvChannel", "LiveTvProgram", "LiveTvTvChannel" };

            return types.Any(i => query.IncludeItemTypes.Contains(i, StringComparer.OrdinalIgnoreCase));
        }

        private static bool HasEpisodeAttributes(InternalItemsQuery query)
        {
            if (query.IncludeItemTypes.Length == 0)
            {
                return true;
            }

            return query.IncludeItemTypes.Contains("Episode", StringComparer.OrdinalIgnoreCase);
        }

        private static bool HasTrailerTypes(InternalItemsQuery query)
        {
            if (query.IncludeItemTypes.Length == 0)
            {
                return true;
            }

            return query.IncludeItemTypes.Contains("Trailer", StringComparer.OrdinalIgnoreCase);
        }

        private static bool HasArtistFields(InternalItemsQuery query)
        {
            var excludeParentTypes = new string[] { "Series", "Season", "PhotoAlbum" };

            if (excludeParentTypes.Contains(query.ParentType ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (query.IncludeItemTypes.Length == 0)
            {
                return true;
            }

            var types = new[] { "Audio", "MusicAlbum", "MusicVideo", "AudioBook", "AudioPodcast" };

            return types.Any(i => query.IncludeItemTypes.Contains(i, StringComparer.OrdinalIgnoreCase));
        }

        private static bool HasSeriesFields(InternalItemsQuery query)
        {
            var excludeParentTypes = new string[] { "PhotoAlbum" };

            if (excludeParentTypes.Contains(query.ParentType ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (query.IncludeItemTypes.Length == 0)
            {
                return true;
            }

            var types = new[] { "Book", "AudioBook", "Episode", "Season" };

            return types.Any(i => query.IncludeItemTypes.Contains(i, StringComparer.OrdinalIgnoreCase));
        }

        // ===========================================================
        // UserDatas join
        // ===========================================================

        private static bool EnableJoinUserData(InternalItemsQuery query)
        {
            if (query.User == null)
            {
                return false;
            }

            var sortingFields = query.OrderBy.Select(i => i.Item1).ToList();

            if (sortingFields.Contains(ItemSortBy.IsFavoriteOrLiked, StringComparer.OrdinalIgnoreCase) ||
                sortingFields.Contains(ItemSortBy.IsPlayed, StringComparer.OrdinalIgnoreCase) ||
                sortingFields.Contains(ItemSortBy.IsUnplayed, StringComparer.OrdinalIgnoreCase) ||
                sortingFields.Contains(ItemSortBy.PlayCount, StringComparer.OrdinalIgnoreCase) ||
                sortingFields.Contains(ItemSortBy.DatePlayed, StringComparer.OrdinalIgnoreCase) ||
                sortingFields.Contains(ItemSortBy.SeriesDatePlayed, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (query.IsFavoriteOrLiked.HasValue || query.IsFavorite.HasValue ||
                query.IsResumable.HasValue || query.IsPlayed.HasValue || query.IsLiked.HasValue)
            {
                return true;
            }

            return false;
        }

        private static string GetJoinUserDataText(InternalItemsQuery query)
        {
            if (!EnableJoinUserData(query))
            {
                return string.Empty;
            }

            return " LEFT JOIN user_datas ud ON ud.user_data_key_id = A.user_data_key_id AND ud.user_id = @UserId";
        }

        // ===========================================================
        // GROUP BY → DISTINCT ON
        // ===========================================================

        private static bool EnableGroupByPresentationUniqueKey(InternalItemsQuery query)
        {
            if (!query.GroupByPresentationUniqueKey)
            {
                return false;
            }

            if (query.GroupBySeriesPresentationUniqueKey)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(query.PresentationUniqueKey))
            {
                return false;
            }

            if (query.User == null)
            {
                return false;
            }

            if (query.IncludeItemTypes.Length == 0)
            {
                return true;
            }

            var types = new[]
            {
                typeof(Episode).Name, typeof(Video).Name, typeof(Movie).Name,
                typeof(MusicVideo).Name, typeof(Series).Name, typeof(Season).Name
            };

            return types.Any(i => query.IncludeItemTypes.Contains(i, StringComparer.OrdinalIgnoreCase));
        }

        private static bool EnableDistinctOn(InternalItemsQuery query)
        {
            return EnableGroupByPresentationUniqueKey(query) || query.GroupBySeriesPresentationUniqueKey;
        }

        private string GetDistinctOnExpression(InternalItemsQuery query)
        {
            if (EnableGroupByPresentationUniqueKey(query))
            {
                // P5（scale-measurement P0）：全局 PUK 无重复时 DISTINCT ON 是空转
                // （数学等价跳过）——1M 库实测 1.58s→576ms（2.7×，20 万行全库形态）。
                // 探测缓存 5min：全局无重复 ⇒ 任何结果集无重复（安全）；有重复（多
                // 版本库）退回包装现状。探测本身全表聚合 ~1s@1M，缓存摊薄。
                if (!HasPresentationUniqueKeyDuplicates())
                {
                    return null;
                }

                return "A.presentation_unique_key";
            }

            if (query.GroupBySeriesPresentationUniqueKey)
            {
                return "A.series_presentation_unique_key";
            }

            return null;
        }

        /// <summary>
        /// P5：库内是否存在同 presentation_unique_key 的多行（多版本条目）。
        /// 无重复 ⇒ DISTINCT ON 与普通查询数学等价，可跳过。结果缓存 5min。
        /// </summary>
        private static bool? _pukDuplicatesCache;
        private static DateTime _pukDuplicatesCacheTime;
        private static readonly object PukProbeLock = new object();
        private static readonly TimeSpan PukProbeCacheTtl = TimeSpan.FromMinutes(5);

        private bool HasPresentationUniqueKeyDuplicates()
        {
            lock (PukProbeLock)
            {
                if (_pukDuplicatesCache.HasValue
                    && (DateTime.UtcNow - _pukDuplicatesCacheTime) < PukProbeCacheTtl)
                {
                    return _pukDuplicatesCache.Value;
                }
            }

            var hasDuplicates = false;

            try
            {
                using (var connection = CreateConnection())
                using (var cmd = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM media_items WHERE presentation_unique_key IS NOT NULL GROUP BY presentation_unique_key HAVING count(*) > 1 LIMIT 1)",
                    connection))
                {
                    hasDuplicates = Convert.ToBoolean(cmd.ExecuteScalar());
                }
            }
            catch
            {
                // 探测失败退回保守路径（包装）
                hasDuplicates = true;
            }

            lock (PukProbeLock)
            {
                _pukDuplicatesCache = hasDuplicates;
                _pukDuplicatesCacheTime = DateTime.UtcNow;
            }

            return hasDuplicates;
        }

        // ===========================================================
        // ORDER BY
        // ===========================================================

        /// <summary>
        /// 排序表达式列表（旧 GetOrderByText + MapOrderByField）。
        /// SimilarTo 无排序时默认 SimilarityScore + Random；SearchTerm 强制 SearchScore + SortName。
        /// </summary>
        private List<Tuple<string, SortOrder>> GetOrderByExpressions(InternalItemsQuery query)
        {
            var orderBy = query.OrderBy.Select(i => new Tuple<string, SortOrder>(i.Item1, i.Item2)).ToList();

            if (query.SimilarTo != null && orderBy.Count == 0)
            {
                orderBy.Add(new Tuple<string, SortOrder>("SimilarityScore", SortOrder.Descending));
                orderBy.Add(new Tuple<string, SortOrder>(ItemSortBy.Random, SortOrder.Ascending));
            }

            if (!string.IsNullOrEmpty(query.SearchTerm))
            {
                orderBy = new List<Tuple<string, SortOrder>>
                {
                    new Tuple<string, SortOrder>("SearchScore", SortOrder.Descending),
                    new Tuple<string, SortOrder>(ItemSortBy.SortName, SortOrder.Ascending)
                };
            }

            query.OrderBy = orderBy.Select(i => (i.Item1, i.Item2)).ToArray();

            return orderBy.Select(i => new Tuple<string, SortOrder>(MapOrderByField(i.Item1, query), i.Item2)).ToList();
        }

        /// <summary>
        /// 排序字段 → PG 表达式（对照旧 MapOrderByField；passthrough 走 PascalCase→snake_case）。
        /// </summary>
        private static string MapOrderByField(string name, InternalItemsQuery query)
        {
            if (string.Equals(name, ItemSortBy.AirTime, StringComparison.OrdinalIgnoreCase))
            {
                return "A.sort_name";
            }
            if (string.Equals(name, ItemSortBy.Runtime, StringComparison.OrdinalIgnoreCase))
            {
                return "A.runtime_ticks";
            }
            if (string.Equals(name, ItemSortBy.Random, StringComparison.OrdinalIgnoreCase))
            {
                return "RANDOM()";
            }
            if (string.Equals(name, ItemSortBy.DatePlayed, StringComparison.OrdinalIgnoreCase))
            {
                if (query.GroupBySeriesPresentationUniqueKey)
                {
                    return "MAX(ud.last_played_date)";
                }

                return "ud.last_played_date";
            }
            if (string.Equals(name, ItemSortBy.PlayCount, StringComparison.OrdinalIgnoreCase))
            {
                return "ud.play_count";
            }
            if (string.Equals(name, ItemSortBy.IsFavoriteOrLiked, StringComparison.OrdinalIgnoreCase))
            {
                return "COALESCE(ud.is_favorite, FALSE)";
            }
            if (string.Equals(name, ItemSortBy.IsFolder, StringComparison.OrdinalIgnoreCase))
            {
                return "A.is_folder";
            }
            if (string.Equals(name, ItemSortBy.IsPlayed, StringComparison.OrdinalIgnoreCase))
            {
                return "ud.played";
            }
            if (string.Equals(name, ItemSortBy.IsUnplayed, StringComparison.OrdinalIgnoreCase))
            {
                return "ud.played";
            }
            if (string.Equals(name, ItemSortBy.DateLastContentAdded, StringComparison.OrdinalIgnoreCase))
            {
                return "A.date_last_media_added";
            }
            if (string.Equals(name, ItemSortBy.Artist, StringComparison.OrdinalIgnoreCase))
            {
                return "(SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 0 LIMIT 1)";
            }
            if (string.Equals(name, ItemSortBy.AlbumArtist, StringComparison.OrdinalIgnoreCase))
            {
                return "(SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 1 LIMIT 1)";
            }
            if (string.Equals(name, ItemSortBy.OfficialRating, StringComparison.OrdinalIgnoreCase))
            {
                return "A.inherited_parental_rating_value";
            }
            if (string.Equals(name, ItemSortBy.Studio, StringComparison.OrdinalIgnoreCase))
            {
                return "(SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 3 LIMIT 1)";
            }
            if (string.Equals(name, ItemSortBy.SeriesDatePlayed, StringComparison.OrdinalIgnoreCase))
            {
                return "(SELECT MAX(udb.last_played_date) FROM media_items B LEFT JOIN user_datas udb ON udb.user_data_key_id = B.user_data_key_id AND udb.user_id = @UserId WHERE udb.played = TRUE AND B.series_presentation_unique_key = A.presentation_unique_key)";
            }
            if (string.Equals(name, ItemSortBy.SeriesSortName, StringComparison.OrdinalIgnoreCase))
            {
                return "A.series_name";
            }

            // 评分列别名（BuildItemSelectText 输出）
            if (string.Equals(name, "SimilarityScore", StringComparison.OrdinalIgnoreCase))
            {
                return "similarity_score";
            }
            if (string.Equals(name, "SearchScore", StringComparison.OrdinalIgnoreCase))
            {
                return "search_score";
            }

            // passthrough：已知列名直接映射，其余 PascalCase → snake_case
            var mapped = MapColumnName(name);
            return mapped != null ? "A." + mapped : name;
        }

        /// <summary>
        /// 旧 PascalCase 列名 → 新 snake_case 列名。
        /// </summary>
        private static readonly Dictionary<string, string> OrderByColumnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "SortName", "sort_name" },
            { "Name", "name" },
            { "PremiereDate", "premiere_date" },
            { "StartDate", "start_date" },
            { "EndDate", "end_date" },
            { "ProductionYear", "production_year" },
            { "CommunityRating", "community_rating" },
            { "CriticRating", "critic_rating" },
            { "DateCreated", "date_created" },
            { "DateModified", "date_modified" },
            { "IndexNumber", "index_number" },
            { "ParentIndexNumber", "parent_index_number" },
            { "RunTimeTicks", "runtime_ticks" },
            { "Album", "album" },
            { "IsFolder", "is_folder" },
            { "IsVirtualItem", "is_virtual_item" },
            { "OfficialRating", "official_rating" },
            { "InheritedParentalRatingValue", "inherited_parental_rating_value" },
            { "PresentationUniqueKey", "presentation_unique_key" },
            { "SeriesPresentationUniqueKey", "series_presentation_unique_key" },
            { "SeriesName", "series_name" },
            { "OriginalTitle", "original_title" },
            { "Tagline", "tagline" },
            { "Overview", "overview" },
            { "Path", "path" },
            { "Width", "width" },
            { "Height", "height" },
            { "TotalBitrate", "total_bitrate" },
            { "DateLastMediaAdded", "date_last_media_added" },
            { "DateLastSaved", "date_last_saved" },
            { "DateLastRefreshed", "date_last_refreshed" }
        };

        private static string MapColumnName(string name)
        {
            string mapped;
            return OrderByColumnMap.TryGetValue(name, out mapped) ? mapped : null;
        }

        // ===========================================================
        // WHERE 构建（对照旧 GetWhereClauses 逐条翻译）
        // ===========================================================

        /// <summary>
        /// 参数注册（已存在则覆盖值）——复刻旧 SQLite TryBind 的 last-bind-wins 语义，
        /// 支撑 IsAiring 与日期范围重叠、聚合查询多子查询共享参数名等场景。
        /// </summary>
        private static void AddOrSetParam(NpgsqlCommand cmd, string name, object value)
        {
            if (cmd.Parameters.Contains(name))
            {
                cmd.Parameters[name].Value = value;
            }
            else
            {
                cmd.Parameters.AddWithValue(name, value);
            }
        }

        private List<string> GetWhereClauses(InternalItemsQuery query, NpgsqlCommand cmd)
        {
            if (query.IsResumable ?? false)
            {
                query.IsVirtualItem = false;
            }

            var whereClauses = new List<string>();

            var minWidth = query.MinWidth;
            var maxWidth = query.MaxWidth;

            if (query.IsHD.HasValue)
            {
                var threshold = 1200;
                if (query.IsHD.Value)
                {
                    minWidth = threshold;
                }
                else
                {
                    maxWidth = threshold - 1;
                }
            }

            if (query.Is4K.HasValue)
            {
                var threshold = 3800;
                if (query.Is4K.Value)
                {
                    minWidth = threshold;
                }
                else
                {
                    maxWidth = threshold - 1;
                }
            }

            if (minWidth.HasValue)
            {
                whereClauses.Add("A.width >= @MinWidth");
                if (cmd != null) AddOrSetParam(cmd, "MinWidth", minWidth.Value);
            }
            if (query.MinHeight.HasValue)
            {
                whereClauses.Add("A.height >= @MinHeight");
                if (cmd != null) AddOrSetParam(cmd, "MinHeight", query.MinHeight.Value);
            }
            if (maxWidth.HasValue)
            {
                whereClauses.Add("A.width <= @MaxWidth");
                if (cmd != null) AddOrSetParam(cmd, "MaxWidth", maxWidth.Value);
            }
            if (query.MaxHeight.HasValue)
            {
                whereClauses.Add("A.height <= @MaxHeight");
                if (cmd != null) AddOrSetParam(cmd, "MaxHeight", query.MaxHeight.Value);
            }

            if (query.IsLocked.HasValue)
            {
                whereClauses.Add("A.is_locked = @IsLocked");
                if (cmd != null) AddOrSetParam(cmd, "IsLocked", query.IsLocked.Value);
            }

            var tags = query.Tags.ToList();
            var excludeTags = query.ExcludeTags.ToList();

            if (query.IsMovie ?? false)
            {
                var alternateTypes = new List<string>();
                if (query.IncludeItemTypes.Length == 0 || query.IncludeItemTypes.Contains(typeof(Movie).Name, StringComparer.OrdinalIgnoreCase))
                {
                    alternateTypes.Add(typeof(Movie).Name);
                }
                if (query.IncludeItemTypes.Length == 0 || query.IncludeItemTypes.Contains(typeof(Trailer).Name, StringComparer.OrdinalIgnoreCase))
                {
                    alternateTypes.Add(typeof(Trailer).Name);
                }

                // 旧语义：有 Movie/Trailer 备选类型时放宽为 "IsMovie is null OR IsMovie=1"
                if (alternateTypes.Count > 0)
                {
                    whereClauses.Add("(A.is_movie IS NULL OR A.is_movie = @IsMovie)");
                }
                else
                {
                    whereClauses.Add("A.is_movie = @IsMovie");
                }

                if (cmd != null) AddOrSetParam(cmd, "IsMovie", true);
            }
            else if (query.IsMovie.HasValue)
            {
                whereClauses.Add("A.is_movie = @IsMovie");
                if (cmd != null) AddOrSetParam(cmd, "IsMovie", query.IsMovie.Value);
            }

            if (query.IsSeries.HasValue)
            {
                whereClauses.Add("A.is_series = @IsSeries");
                if (cmd != null) AddOrSetParam(cmd, "IsSeries", query.IsSeries.Value);
            }

            if (query.IsSports.HasValue)
            {
                if (query.IsSports.Value)
                {
                    tags.Add("Sports");
                }
                else
                {
                    excludeTags.Add("Sports");
                }
            }

            if (query.IsNews.HasValue)
            {
                if (query.IsNews.Value)
                {
                    tags.Add("News");
                }
                else
                {
                    excludeTags.Add("News");
                }
            }

            if (query.IsKids.HasValue)
            {
                if (query.IsKids.Value)
                {
                    tags.Add("Kids");
                }
                else
                {
                    excludeTags.Add("Kids");
                }
            }

            if (query.SimilarTo != null && query.MinSimilarityScore > 0)
            {
                whereClauses.Add(GetSimilarityScoreExpressionFull() + " > " + (query.MinSimilarityScore - 1).ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(query.SearchTerm))
            {
                // 旧 "SearchScore > 0"（WHERE 不能引用别名，展开为布尔条件）
                whereClauses.Add(GetSearchScoreBooleanExpression(query));
            }

            if (query.IsFolder.HasValue)
            {
                whereClauses.Add("A.is_folder = @IsFolder");
                if (cmd != null) AddOrSetParam(cmd, "IsFolder", query.IsFolder.Value);
            }

            var includeTypes = query.IncludeItemTypes.SelectMany(MapIncludeItemTypes).ToArray();

            if (includeTypes.Length == 1)
            {
                AddTypeFilter(whereClauses, cmd, includeTypes, negate: false);
            }
            else if (includeTypes.Length > 1)
            {
                AddTypeFilter(whereClauses, cmd, includeTypes, negate: false);
            }

            // Only specify excluded types if no included types are specified
            if (includeTypes.Length == 0)
            {
                var excludeTypes = query.ExcludeItemTypes.SelectMany(MapIncludeItemTypes).ToArray();

                if (excludeTypes.Length > 0)
                {
                    AddTypeFilter(whereClauses, cmd, excludeTypes, negate: true);
                }
            }

            if (query.ChannelIds.Length == 1)
            {
                whereClauses.Add("A.channel_id = @ChannelId");
                if (cmd != null) AddOrSetParam(cmd, "ChannelId", query.ChannelIds[0].ToString("N"));
            }
            else if (query.ChannelIds.Length > 1)
            {
                var clauses = new List<string>();
                for (var i = 0; i < query.ChannelIds.Length; i++)
                {
                    clauses.Add("@ChannelId" + i);
                    if (cmd != null) AddOrSetParam(cmd, "ChannelId" + i, query.ChannelIds[i].ToString("N"));
                }
                whereClauses.Add("A.channel_id IN (" + string.Join(",", clauses) + ")");
            }

            if (!query.ParentId.Equals(Guid.Empty))
            {
                whereClauses.Add("A.parent_id = (SELECT id FROM media_items WHERE guid = @ParentId LIMIT 1)");
                if (cmd != null) AddOrSetParam(cmd, "ParentId", query.ParentId);
            }

            if (!string.IsNullOrWhiteSpace(query.Path))
            {
                whereClauses.Add("A.path = @Path");
                if (cmd != null) AddOrSetParam(cmd, "Path", (object)GetPathToSave(query.Path) ?? DBNull.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.PresentationUniqueKey))
            {
                whereClauses.Add("A.presentation_unique_key = @PresentationUniqueKey");
                if (cmd != null) AddOrSetParam(cmd, "PresentationUniqueKey", query.PresentationUniqueKey);
            }

            if (query.MinCommunityRating.HasValue)
            {
                whereClauses.Add("A.community_rating >= @MinCommunityRating");
                if (cmd != null) AddOrSetParam(cmd, "MinCommunityRating", query.MinCommunityRating.Value);
            }

            if (query.MinIndexNumber.HasValue)
            {
                whereClauses.Add("A.index_number >= @MinIndexNumber");
                if (cmd != null) AddOrSetParam(cmd, "MinIndexNumber", query.MinIndexNumber.Value);
            }

            if (query.MinDateCreated.HasValue)
            {
                whereClauses.Add("A.date_created >= @MinDateCreated");
                if (cmd != null) AddOrSetParam(cmd, "MinDateCreated", ToUtcDateTime(query.MinDateCreated.Value));
            }

            if (query.MinDateLastSaved.HasValue)
            {
                whereClauses.Add("(A.date_last_saved IS NOT NULL AND A.date_last_saved >= @MinDateLastSaved)");
                if (cmd != null) AddOrSetParam(cmd, "MinDateLastSaved", ToUtcDateTime(query.MinDateLastSaved.Value));
            }

            if (query.MinDateLastSavedForUser.HasValue)
            {
                whereClauses.Add("(A.date_last_saved IS NOT NULL AND A.date_last_saved >= @MinDateLastSavedForUser)");
                if (cmd != null) AddOrSetParam(cmd, "MinDateLastSavedForUser", ToUtcDateTime(query.MinDateLastSavedForUser.Value));
            }

            if (query.IndexNumber.HasValue)
            {
                whereClauses.Add("A.index_number = @IndexNumber");
                if (cmd != null) AddOrSetParam(cmd, "IndexNumber", query.IndexNumber.Value);
            }
            if (query.ParentIndexNumber.HasValue)
            {
                whereClauses.Add("A.parent_index_number = @ParentIndexNumber");
                if (cmd != null) AddOrSetParam(cmd, "ParentIndexNumber", query.ParentIndexNumber.Value);
            }
            if (query.ParentIndexNumberNotEquals.HasValue)
            {
                whereClauses.Add("(A.parent_index_number <> @ParentIndexNumberNotEquals OR A.parent_index_number IS NULL)");
                if (cmd != null) AddOrSetParam(cmd, "ParentIndexNumberNotEquals", query.ParentIndexNumberNotEquals.Value);
            }

            var minEndDate = query.MinEndDate;
            var maxEndDate = query.MaxEndDate;

            if (query.HasAired.HasValue)
            {
                if (query.HasAired.Value)
                {
                    maxEndDate = DateTime.UtcNow;
                }
                else
                {
                    minEndDate = DateTime.UtcNow;
                }
            }

            if (minEndDate.HasValue)
            {
                whereClauses.Add("A.end_date >= @MinEndDate");
                if (cmd != null) AddOrSetParam(cmd, "MinEndDate", ToUtcDateTime(minEndDate.Value));
            }

            if (maxEndDate.HasValue)
            {
                whereClauses.Add("A.end_date <= @MaxEndDate");
                if (cmd != null) AddOrSetParam(cmd, "MaxEndDate", ToUtcDateTime(maxEndDate.Value));
            }

            if (query.MinStartDate.HasValue)
            {
                whereClauses.Add("A.start_date >= @MinStartDate");
                if (cmd != null) AddOrSetParam(cmd, "MinStartDate", ToUtcDateTime(query.MinStartDate.Value));
            }

            if (query.MaxStartDate.HasValue)
            {
                whereClauses.Add("A.start_date <= @MaxStartDate");
                if (cmd != null) AddOrSetParam(cmd, "MaxStartDate", ToUtcDateTime(query.MaxStartDate.Value));
            }

            if (query.MinPremiereDate.HasValue)
            {
                whereClauses.Add("A.premiere_date >= @MinPremiereDate");
                if (cmd != null) AddOrSetParam(cmd, "MinPremiereDate", ToUtcDateTime(query.MinPremiereDate.Value));
            }
            if (query.MaxPremiereDate.HasValue)
            {
                whereClauses.Add("A.premiere_date <= @MaxPremiereDate");
                if (cmd != null) AddOrSetParam(cmd, "MaxPremiereDate", ToUtcDateTime(query.MaxPremiereDate.Value));
            }

            if (query.TrailerTypes.Length > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var type in query.TrailerTypes)
                {
                    var paramName = "TrailerTypes" + index;
                    clauses.Add("A.trailer_types LIKE @" + paramName);
                    if (cmd != null) AddOrSetParam(cmd, paramName, "%" + type + "%");
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (query.IsAiring.HasValue)
            {
                if (query.IsAiring.Value)
                {
                    whereClauses.Add("A.start_date <= @MaxStartDate");
                    if (cmd != null) AddOrSetParam(cmd, "MaxStartDate", ToUtcDateTime(DateTime.UtcNow));

                    whereClauses.Add("A.end_date >= @MinEndDate");
                    if (cmd != null) AddOrSetParam(cmd, "MinEndDate", ToUtcDateTime(DateTime.UtcNow));
                }
                else
                {
                    whereClauses.Add("(A.start_date > @IsAiringDate OR A.end_date < @IsAiringDate)");
                    if (cmd != null) AddOrSetParam(cmd, "IsAiringDate", ToUtcDateTime(DateTime.UtcNow));
                }
            }

            if (query.PersonIds.Length > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var personId in query.PersonIds)
                {
                    var paramName = "PersonId" + index;
                    // A.id (bigint) IN item_people.item_id (bigint)——不能用 A.guid (uuid)
                    clauses.Add("A.id IN (SELECT ip.item_id FROM item_people ip WHERE ip.person_id = (SELECT id FROM media_items WHERE guid = @" + paramName + " LIMIT 1))");
                    if (cmd != null) AddOrSetParam(cmd, paramName, personId);
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (!string.IsNullOrWhiteSpace(query.Person))
            {
                whereClauses.Add("A.id IN (SELECT ip.item_id FROM item_people ip JOIN media_items p ON p.id = ip.person_id WHERE p.name = @PersonName)");
                if (cmd != null) AddOrSetParam(cmd, "PersonName", query.Person);
            }

            if (!string.IsNullOrWhiteSpace(query.MinSortName))
            {
                whereClauses.Add("A.sort_name >= @MinSortName");
                if (cmd != null) AddOrSetParam(cmd, "MinSortName", query.MinSortName);
            }

            if (!string.IsNullOrWhiteSpace(query.ExternalSeriesId))
            {
                whereClauses.Add("A.external_series_id = @ExternalSeriesId");
                if (cmd != null) AddOrSetParam(cmd, "ExternalSeriesId", query.ExternalSeriesId);
            }

            if (!string.IsNullOrWhiteSpace(query.ExternalId))
            {
                whereClauses.Add("A.external_id = @ExternalId");
                if (cmd != null) AddOrSetParam(cmd, "ExternalId", query.ExternalId);
            }

            if (!string.IsNullOrWhiteSpace(query.Name))
            {
                whereClauses.Add("A.clean_name = @Name");
                if (cmd != null) AddOrSetParam(cmd, "Name", GetCleanValue(query.Name));
            }

            var nameContains = query.NameContains;
            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                whereClauses.Add("(A.clean_name LIKE @NameContains OR A.original_title LIKE @NameContains)");
                if (cmd != null)
                {
                    nameContains = FixUnicodeChars(nameContains);
                    AddOrSetParam(cmd, "NameContains", "%" + GetCleanValue(nameContains) + "%");
                }
            }

            if (!string.IsNullOrWhiteSpace(query.NameStartsWith))
            {
                whereClauses.Add("A.sort_name LIKE @NameStartsWith");
                if (cmd != null) AddOrSetParam(cmd, "NameStartsWith", query.NameStartsWith + "%");
            }
            if (!string.IsNullOrWhiteSpace(query.NameStartsWithOrGreater))
            {
                whereClauses.Add("A.sort_name >= @NameStartsWithOrGreater");
                if (cmd != null) AddOrSetParam(cmd, "NameStartsWithOrGreater", query.NameStartsWithOrGreater.ToLower());
            }
            if (!string.IsNullOrWhiteSpace(query.NameLessThan))
            {
                whereClauses.Add("A.sort_name < @NameLessThan");
                if (cmd != null) AddOrSetParam(cmd, "NameLessThan", query.NameLessThan.ToLower());
            }

            if (query.ImageTypes.Length > 0)
            {
                foreach (var requiredImage in query.ImageTypes)
                {
                    var paramName = "ImageType" + (int)requiredImage;
                    whereClauses.Add("A.images::text LIKE @" + paramName);
                    if (cmd != null) AddOrSetParam(cmd, paramName, "%" + requiredImage + "%");
                }
            }

            if (query.IsLiked.HasValue)
            {
                if (query.IsLiked.Value)
                {
                    whereClauses.Add("ud.rating >= @UserRating");
                }
                else
                {
                    whereClauses.Add("(ud.rating IS NULL OR ud.rating < @UserRating)");
                }
                if (cmd != null) AddOrSetParam(cmd, "UserRating", MediaBrowser.Controller.Entities.UserItemData.MinLikeValue);
            }

            if (query.IsFavoriteOrLiked.HasValue)
            {
                if (query.IsFavoriteOrLiked.Value)
                {
                    whereClauses.Add("ud.is_favorite = @IsFavoriteOrLiked");
                }
                else
                {
                    whereClauses.Add("(ud.is_favorite IS NULL OR ud.is_favorite = @IsFavoriteOrLiked)");
                }
                if (cmd != null) AddOrSetParam(cmd, "IsFavoriteOrLiked", query.IsFavoriteOrLiked.Value);
            }

            if (query.IsFavorite.HasValue)
            {
                if (query.IsFavorite.Value)
                {
                    whereClauses.Add("ud.is_favorite = @IsFavorite");
                }
                else
                {
                    whereClauses.Add("(ud.is_favorite IS NULL OR ud.is_favorite = @IsFavorite)");
                }
                if (cmd != null) AddOrSetParam(cmd, "IsFavorite", query.IsFavorite.Value);
            }

            if (EnableJoinUserData(query))
            {
                if (query.IsPlayed.HasValue)
                {
                    // Series 特例（旧逻辑：剧集按"是否存在未播放集"判定）
                    if (query.IncludeItemTypes.Length == 1 && string.Equals(query.IncludeItemTypes[0], typeof(Series).Name, StringComparison.OrdinalIgnoreCase))
                    {
                        var subQuery = @"SELECT S.series_presentation_unique_key
                                         FROM media_items S
                                         LEFT JOIN user_datas UDS ON S.user_data_key_id = UDS.user_data_key_id AND UDS.user_id = @UserId
                                         WHERE COALESCE(UDS.played, FALSE) = FALSE
                                           AND S.is_folder = FALSE
                                           AND S.is_virtual_item = FALSE
                                           AND S.series_presentation_unique_key IS NOT NULL";

                        if (query.IsPlayed.Value)
                        {
                            whereClauses.Add("A.presentation_unique_key NOT IN (" + subQuery + ")");
                        }
                        else
                        {
                            whereClauses.Add("A.presentation_unique_key IN (" + subQuery + ")");
                        }
                    }
                    else
                    {
                        if (query.IsPlayed.Value)
                        {
                            whereClauses.Add("(ud.played = @IsPlayed)");
                        }
                        else
                        {
                            whereClauses.Add("(ud.played IS NULL OR ud.played = @IsPlayed)");
                        }
                        if (cmd != null) AddOrSetParam(cmd, "IsPlayed", query.IsPlayed.Value);
                    }
                }
            }

            if (query.IsResumable.HasValue)
            {
                if (query.IsResumable.Value)
                {
                    whereClauses.Add("ud.playback_position_ticks > 0");
                }
                else
                {
                    whereClauses.Add("(ud.playback_position_ticks IS NULL OR ud.playback_position_ticks = 0)");
                }
            }

            if (query.ArtistIds.Length > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var artistId in query.ArtistIds)
                {
                    var paramName = "ArtistIds" + index;
                    clauses.Add("A.id IN (SELECT iv.item_id FROM item_extradata iv WHERE iv.clean_value = (SELECT clean_name FROM media_items WHERE guid = @" + paramName + " LIMIT 1) AND iv.extradata_type_id <= 1)");
                    if (cmd != null) AddOrSetParam(cmd, paramName, artistId);
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (query.AlbumArtistIds.Length > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var artistId in query.AlbumArtistIds)
                {
                    var paramName = "ArtistIds" + index;
                    clauses.Add("A.id IN (SELECT iv.item_id FROM item_extradata iv WHERE iv.clean_value = (SELECT clean_name FROM media_items WHERE guid = @" + paramName + " LIMIT 1) AND iv.extradata_type_id = 1)");
                    if (cmd != null) AddOrSetParam(cmd, paramName, artistId);
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (query.ContributingArtistIds.Length > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var artistId in query.ContributingArtistIds)
                {
                    var paramName = "ArtistIds" + index;
                    clauses.Add("(SELECT clean_name FROM media_items WHERE guid = @" + paramName + " LIMIT 1) IN (SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 0)" +
                                " AND (SELECT clean_name FROM media_items WHERE guid = @" + paramName + " LIMIT 1) NOT IN (SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 1)");
                    if (cmd != null) AddOrSetParam(cmd, paramName, artistId);
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (query.AlbumIds.Length > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var albumId in query.AlbumIds)
                {
                    var paramName = "AlbumIds" + index;
                    clauses.Add("A.album IN (SELECT name FROM media_items WHERE guid = @" + paramName + " LIMIT 1)");
                    if (cmd != null) AddOrSetParam(cmd, paramName, albumId);
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (query.ExcludeArtistIds.Length > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var artistId in query.ExcludeArtistIds)
                {
                    var paramName = "ExcludeArtistId" + index;
                    clauses.Add("A.guid NOT IN (SELECT iv.item_id FROM item_extradata iv WHERE iv.clean_value = (SELECT clean_name FROM media_items WHERE guid = @" + paramName + " LIMIT 1) AND iv.extradata_type_id <= 1)");
                    if (cmd != null) AddOrSetParam(cmd, paramName, artistId);
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (query.GenreIds.Length > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var genreId in query.GenreIds)
                {
                    var paramName = "GenreId" + index;
                    clauses.Add("A.id IN (SELECT iv.item_id FROM item_extradata iv WHERE iv.clean_value = (SELECT clean_name FROM media_items WHERE guid = @" + paramName + " LIMIT 1) AND iv.extradata_type_id = 2)");
                    if (cmd != null) AddOrSetParam(cmd, paramName, genreId);
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (query.Genres.Length > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var item in query.Genres)
                {
                    clauses.Add("@Genre" + index + " IN (SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 2)");
                    if (cmd != null) AddOrSetParam(cmd, "Genre" + index, GetCleanValue(item));
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (tags.Count > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var item in tags)
                {
                    clauses.Add("@Tag" + index + " IN (SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 4)");
                    if (cmd != null) AddOrSetParam(cmd, "Tag" + index, GetCleanValue(item));
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (excludeTags.Count > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var item in excludeTags)
                {
                    clauses.Add("@ExcludeTag" + index + " NOT IN (SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 4)");
                    if (cmd != null) AddOrSetParam(cmd, "ExcludeTag" + index, GetCleanValue(item));
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (query.StudioIds.Length > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var studioId in query.StudioIds)
                {
                    var paramName = "StudioId" + index;
                    clauses.Add("A.id IN (SELECT iv.item_id FROM item_extradata iv WHERE iv.clean_value = (SELECT clean_name FROM media_items WHERE guid = @" + paramName + " LIMIT 1) AND iv.extradata_type_id = 3)");
                    if (cmd != null) AddOrSetParam(cmd, paramName, studioId);
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (query.OfficialRatings.Length > 0)
            {
                var clauses = new List<string>();
                var index = 0;
                foreach (var item in query.OfficialRatings)
                {
                    clauses.Add("A.official_rating = @OfficialRating" + index);
                    if (cmd != null) AddOrSetParam(cmd, "OfficialRating" + index, item);
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.ToArray()) + ")");
            }

            if (query.MinParentalRating.HasValue)
            {
                whereClauses.Add("A.inherited_parental_rating_value >= @MinParentalRating");
                if (cmd != null) AddOrSetParam(cmd, "MinParentalRating", query.MinParentalRating.Value);
            }

            if (query.MaxParentalRating.HasValue)
            {
                whereClauses.Add("A.inherited_parental_rating_value <= @MaxParentalRating");
                if (cmd != null) AddOrSetParam(cmd, "MaxParentalRating", query.MaxParentalRating.Value);
            }

            if (query.HasParentalRating.HasValue)
            {
                whereClauses.Add(query.HasParentalRating.Value
                    ? "A.inherited_parental_rating_value > 0"
                    : "A.inherited_parental_rating_value = 0");
            }

            if (query.HasOfficialRating.HasValue)
            {
                whereClauses.Add(query.HasOfficialRating.Value
                    ? "(A.official_rating IS NOT NULL AND A.official_rating <> '')"
                    : "(A.official_rating IS NULL OR A.official_rating = '')");
            }

            if (query.HasOverview.HasValue)
            {
                whereClauses.Add(query.HasOverview.Value
                    ? "(A.overview IS NOT NULL AND A.overview <> '')"
                    : "(A.overview IS NULL OR A.overview = '')");
            }

            if (query.HasOwnerId.HasValue)
            {
                whereClauses.Add(query.HasOwnerId.Value
                    ? "A.owner_id IS NOT NULL"
                    : "A.owner_id IS NULL");
            }

            // P6：相关标量子查询族改 EXISTS/NOT EXISTS（planner 可用 anti/semi join，
            // 实测 1.7×；语义等价——language = @x 过滤后 NULL 行本就不入子查询）
            if (!string.IsNullOrWhiteSpace(query.HasNoAudioTrackWithLanguage))
            {
                whereClauses.Add("NOT EXISTS (SELECT 1 FROM media_streams s WHERE s.item_id = A.id AND s.stream_type = 1 AND s.language = @HasNoAudioTrackWithLanguage)");
                if (cmd != null) AddOrSetParam(cmd, "HasNoAudioTrackWithLanguage", query.HasNoAudioTrackWithLanguage);
            }

            if (!string.IsNullOrWhiteSpace(query.HasNoInternalSubtitleTrackWithLanguage))
            {
                whereClauses.Add("NOT EXISTS (SELECT 1 FROM media_streams s WHERE s.item_id = A.id AND s.stream_type = 2 AND s.is_external = FALSE AND s.language = @HasNoInternalSubtitleTrackWithLanguage)");
                if (cmd != null) AddOrSetParam(cmd, "HasNoInternalSubtitleTrackWithLanguage", query.HasNoInternalSubtitleTrackWithLanguage);
            }

            if (!string.IsNullOrWhiteSpace(query.HasNoExternalSubtitleTrackWithLanguage))
            {
                whereClauses.Add("NOT EXISTS (SELECT 1 FROM media_streams s WHERE s.item_id = A.id AND s.stream_type = 2 AND s.is_external = TRUE AND s.language = @HasNoExternalSubtitleTrackWithLanguage)");
                if (cmd != null) AddOrSetParam(cmd, "HasNoExternalSubtitleTrackWithLanguage", query.HasNoExternalSubtitleTrackWithLanguage);
            }

            if (!string.IsNullOrWhiteSpace(query.HasNoSubtitleTrackWithLanguage))
            {
                whereClauses.Add("NOT EXISTS (SELECT 1 FROM media_streams s WHERE s.item_id = A.id AND s.stream_type = 2 AND s.language = @HasNoSubtitleTrackWithLanguage)");
                if (cmd != null) AddOrSetParam(cmd, "HasNoSubtitleTrackWithLanguage", query.HasNoSubtitleTrackWithLanguage);
            }

            if (query.HasSubtitles.HasValue)
            {
                whereClauses.Add(query.HasSubtitles.Value
                    ? "EXISTS (SELECT 1 FROM media_streams s WHERE s.item_id = A.id AND s.stream_type = 2)"
                    : "NOT EXISTS (SELECT 1 FROM media_streams s WHERE s.item_id = A.id AND s.stream_type = 2)");
            }

            // ===== Api B2 新接入的过滤参数（对照 Sqlite 过渡实现的语义）=====

            if (query.AudioCodecs != null && query.AudioCodecs.Length > 0)
            {
                var codecClauses = new List<string>();
                for (var i = 0; i < query.AudioCodecs.Length; i++)
                {
                    var paramName = "AudioCodec" + i;
                    codecClauses.Add("(SELECT codec FROM media_streams WHERE item_id = A.id AND stream_type = 1 AND codec = @" + paramName + " LIMIT 1) IS NOT NULL");
                    if (cmd != null) AddOrSetParam(cmd, paramName, query.AudioCodecs[i]);
                }
                whereClauses.Add("(" + string.Join(" OR ", codecClauses.ToArray()) + ")");
            }

            if (query.VideoCodecs != null && query.VideoCodecs.Length > 0)
            {
                var codecClauses = new List<string>();
                for (var i = 0; i < query.VideoCodecs.Length; i++)
                {
                    var paramName = "VideoCodec" + i;
                    codecClauses.Add("(SELECT codec FROM media_streams WHERE item_id = A.id AND stream_type = 0 AND codec = @" + paramName + " LIMIT 1) IS NOT NULL");
                    if (cmd != null) AddOrSetParam(cmd, paramName, query.VideoCodecs[i]);
                }
                whereClauses.Add("(" + string.Join(" OR ", codecClauses.ToArray()) + ")");
            }

            if (query.SubtitleCodecs != null && query.SubtitleCodecs.Length > 0)
            {
                var codecClauses = new List<string>();
                for (var i = 0; i < query.SubtitleCodecs.Length; i++)
                {
                    var paramName = "SubtitleCodec" + i;
                    codecClauses.Add("(SELECT codec FROM media_streams WHERE item_id = A.id AND stream_type = 2 AND codec = @" + paramName + " LIMIT 1) IS NOT NULL");
                    if (cmd != null) AddOrSetParam(cmd, paramName, query.SubtitleCodecs[i]);
                }
                whereClauses.Add("(" + string.Join(" OR ", codecClauses.ToArray()) + ")");
            }

            if (query.Containers != null && query.Containers.Length > 0)
            {
                // media_items 有 container 列（SaveItem 已写入），精确匹配优于旧过渡实现的 data LIKE
                var containerClauses = new List<string>();
                for (var i = 0; i < query.Containers.Length; i++)
                {
                    var paramName = "Container" + i;
                    containerClauses.Add("A.container = @" + paramName);
                    if (cmd != null) AddOrSetParam(cmd, paramName, query.Containers[i]);
                }
                whereClauses.Add("(" + string.Join(" OR ", containerClauses.ToArray()) + ")");
            }

            if (query.HasChapterImages.HasValue)
            {
                whereClauses.Add(query.HasChapterImages.Value
                    ? "((SELECT image_path FROM chapters WHERE item_id = A.id AND image_path IS NOT NULL LIMIT 1) IS NOT NULL)"
                    : "((SELECT image_path FROM chapters WHERE item_id = A.id AND image_path IS NOT NULL LIMIT 1) IS NULL)");
            }

            if (query.HasDeadParentId.HasValue && query.HasDeadParentId.Value)
            {
                whereClauses.Add("A.parent_id IS NOT NULL AND A.parent_id NOT IN (SELECT id FROM media_items)");
            }

            if (query.IsDeadArtist.HasValue && query.IsDeadArtist.Value)
            {
                whereClauses.Add("A.clean_name NOT IN (SELECT clean_value FROM item_extradata WHERE extradata_type_id IN (0, 1))");
            }

            if (query.IsDeadStudio.HasValue && query.IsDeadStudio.Value)
            {
                whereClauses.Add("A.clean_name NOT IN (SELECT clean_value FROM item_extradata WHERE extradata_type_id = 3)");
            }

            if (query.IsDeadPerson.HasValue && query.IsDeadPerson.Value)
            {
                whereClauses.Add("A.name NOT IN (SELECT p.name FROM item_people ip JOIN media_items p ON p.id = ip.person_id)");
            }

            if (query.Years.Length == 1)
            {
                whereClauses.Add("A.production_year = @Years");
                if (cmd != null) AddOrSetParam(cmd, "Years", query.Years[0]);
            }
            else if (query.Years.Length > 1)
            {
                var clauses = new List<string>();
                for (var i = 0; i < query.Years.Length; i++)
                {
                    clauses.Add("@Years" + i);
                    if (cmd != null) AddOrSetParam(cmd, "Years" + i, query.Years[i]);
                }
                whereClauses.Add("A.production_year IN (" + string.Join(",", clauses) + ")");
            }

            var isVirtualItem = query.IsVirtualItem ?? query.IsMissing;
            if (isVirtualItem.HasValue)
            {
                whereClauses.Add("A.is_virtual_item = @IsVirtualItem");
                if (cmd != null) AddOrSetParam(cmd, "IsVirtualItem", isVirtualItem.Value);
            }
            if (query.IsSpecialSeason.HasValue)
            {
                whereClauses.Add(query.IsSpecialSeason.Value
                    ? "A.index_number = 0"
                    : "A.index_number <> 0");
            }
            if (query.IsUnaired.HasValue)
            {
                whereClauses.Add(query.IsUnaired.Value
                    ? "A.premiere_date >= now()"
                    : "A.premiere_date < now()");
            }

            var queryMediaTypes = query.MediaTypes.Where(IsValidMediaType).ToArray();
            if (queryMediaTypes.Length == 1)
            {
                whereClauses.Add("A.media_type = @MediaTypes");
                if (cmd != null) AddOrSetParam(cmd, "MediaTypes", queryMediaTypes[0]);
            }
            else if (queryMediaTypes.Length > 1)
            {
                var clauses = new List<string>();
                for (var i = 0; i < queryMediaTypes.Length; i++)
                {
                    clauses.Add("@MediaTypes" + i);
                    if (cmd != null) AddOrSetParam(cmd, "MediaTypes" + i, queryMediaTypes[i]);
                }
                whereClauses.Add("A.media_type IN (" + string.Join(",", clauses) + ")");
            }

            if (query.ItemIds.Length > 0)
            {
                var includeIds = new List<string>();
                var index = 0;
                foreach (var id in query.ItemIds)
                {
                    includeIds.Add("A.guid = @IncludeId" + index);
                    if (cmd != null) AddOrSetParam(cmd, "IncludeId" + index, id);
                    index++;
                }
                whereClauses.Add("(" + string.Join(" OR ", includeIds.ToArray()) + ")");
            }

            if (query.ExcludeItemIds.Length > 0)
            {
                var excludeIds = new List<string>();
                var index = 0;
                foreach (var id in query.ExcludeItemIds)
                {
                    excludeIds.Add("A.guid <> @ExcludeId" + index);
                    if (cmd != null) AddOrSetParam(cmd, "ExcludeId" + index, id);
                    index++;
                }
                whereClauses.Add(string.Join(" AND ", excludeIds.ToArray()));
            }

            if (query.ExcludeProviderIds != null && query.ExcludeProviderIds.Count > 0)
            {
                var index = 0;
                foreach (var pair in query.ExcludeProviderIds)
                {
                    if (string.Equals(pair.Key, MetadataProviders.TmdbCollection.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    whereClauses.Add("(A.provider_ids IS NULL OR A.provider_ids->>@" + "ExcludeProviderIdKey" + index + " IS DISTINCT FROM @ExcludeProviderIdValue" + index + ")");
                    if (cmd != null)
                    {
                        AddOrSetParam(cmd, "ExcludeProviderIdKey" + index, pair.Key);
                        AddOrSetParam(cmd, "ExcludeProviderIdValue" + index, pair.Value);
                    }
                    index++;

                    break;
                }
            }

            if (query.HasAnyProviderId != null && query.HasAnyProviderId.Count > 0)
            {
                var hasProviderIds = new List<string>();
                var index = 0;
                foreach (var pair in query.HasAnyProviderId)
                {
                    if (string.Equals(pair.Key, MetadataProviders.TmdbCollection.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    hasProviderIds.Add("A.provider_ids->>@HasAnyProviderIdKey" + index + " = @HasAnyProviderIdValue" + index);
                    if (cmd != null)
                    {
                        AddOrSetParam(cmd, "HasAnyProviderIdKey" + index, pair.Key);
                        AddOrSetParam(cmd, "HasAnyProviderIdValue" + index, pair.Value);
                    }
                    index++;

                    break;
                }

                if (hasProviderIds.Count > 0)
                {
                    whereClauses.Add("(" + string.Join(" OR ", hasProviderIds.ToArray()) + ")");
                }
            }

            if (query.HasImdbId.HasValue)
            {
                whereClauses.Add("A.provider_ids->>'Imdb' IS NOT NULL");
            }

            if (query.HasTmdbId.HasValue)
            {
                whereClauses.Add("A.provider_ids->>'Tmdb' IS NOT NULL");
            }

            if (query.HasTvdbId.HasValue)
            {
                whereClauses.Add("A.provider_ids->>'Tvdb' IS NOT NULL");
            }

            var includedItemByNameTypes = GetItemByNameTypesInQuery(query).SelectMany(MapIncludeItemTypes).ToList();
            var enableItemsByName = (query.IncludeItemsByName ?? false) && includedItemByNameTypes.Count > 0;

            var queryTopParentIds = query.TopParentIds;

            if (queryTopParentIds.Length == 1)
            {
                var typeClauses = BuildItemByNameTypeClauses(includedItemByNameTypes, enableItemsByName, cmd, "IncludedItemByNameType", "IncludedItemByNameType");

                if (typeClauses != null)
                {
                    whereClauses.Add("(A.top_parent_id = @TopParentId OR " + typeClauses + ")");
                }
                else
                {
                    whereClauses.Add("(A.top_parent_id = @TopParentId)");
                }

                if (cmd != null) AddOrSetParam(cmd, "TopParentId", queryTopParentIds[0].ToString("N"));
            }
            else if (queryTopParentIds.Length > 1)
            {
                var clauses = new List<string>();
                for (var i = 0; i < queryTopParentIds.Length; i++)
                {
                    clauses.Add("@TopParentId" + i);
                    if (cmd != null) AddOrSetParam(cmd, "TopParentId" + i, queryTopParentIds[i].ToString("N"));
                }
                var inClause = string.Join(",", clauses);

                var typeClauses = BuildItemByNameTypeClauses(includedItemByNameTypes, enableItemsByName, cmd, "IncludedItemByNameType", "IncludedItemByNameType");

                if (typeClauses != null)
                {
                    whereClauses.Add("(" + typeClauses + " OR A.top_parent_id IN (" + inClause + "))");
                }
                else
                {
                    whereClauses.Add("(A.top_parent_id IN (" + inClause + "))");
                }
            }

            if (query.AncestorIds.Length == 1)
            {
                whereClauses.Add("A.id IN (SELECT item_id FROM ancestor_ids WHERE ancestor_id = (SELECT id FROM media_items WHERE guid = @AncestorId LIMIT 1))");
                if (cmd != null) AddOrSetParam(cmd, "AncestorId", query.AncestorIds[0]);
            }
            if (query.AncestorIds.Length > 1)
            {
                var clauses = new List<string>();
                for (var i = 0; i < query.AncestorIds.Length; i++)
                {
                    clauses.Add("@AncestorId" + i);
                    if (cmd != null) AddOrSetParam(cmd, "AncestorId" + i, query.AncestorIds[i]);
                }
                whereClauses.Add("A.id IN (SELECT item_id FROM ancestor_ids WHERE ancestor_id IN (SELECT id FROM media_items WHERE guid IN (" + string.Join(",", clauses) + ")))");
            }

            if (!string.IsNullOrWhiteSpace(query.AncestorWithPresentationUniqueKey))
            {
                whereClauses.Add("A.id IN (SELECT item_id FROM ancestor_ids WHERE ancestor_id IN (SELECT id FROM media_items WHERE presentation_unique_key = @AncestorWithPresentationUniqueKey))");
                if (cmd != null) AddOrSetParam(cmd, "AncestorWithPresentationUniqueKey", query.AncestorWithPresentationUniqueKey);
            }

            if (!string.IsNullOrWhiteSpace(query.SeriesPresentationUniqueKey))
            {
                whereClauses.Add("A.series_presentation_unique_key = @SeriesPresentationUniqueKey");
                if (cmd != null) AddOrSetParam(cmd, "SeriesPresentationUniqueKey", query.SeriesPresentationUniqueKey);
            }

            if (query.BlockUnratedItems.Length == 1)
            {
                whereClauses.Add("(A.inherited_parental_rating_value > 0 OR A.unrated_type <> @UnratedType)");
                if (cmd != null) AddOrSetParam(cmd, "UnratedType", query.BlockUnratedItems[0].ToString());
            }
            if (query.BlockUnratedItems.Length > 1)
            {
                var clauses = new List<string>();
                for (var i = 0; i < query.BlockUnratedItems.Length; i++)
                {
                    clauses.Add("@UnratedType" + i);
                    if (cmd != null) AddOrSetParam(cmd, "UnratedType" + i, query.BlockUnratedItems[i].ToString());
                }
                whereClauses.Add("(A.inherited_parental_rating_value > 0 OR A.unrated_type NOT IN (" + string.Join(",", clauses) + "))");
            }

            if (query.ExcludeInheritedTags.Length > 0)
            {
                var clauses = new List<string>();
                for (var i = 0; i < query.ExcludeInheritedTags.Length; i++)
                {
                    clauses.Add("@ExcludeInheritedTag" + i);
                    if (cmd != null) AddOrSetParam(cmd, "ExcludeInheritedTag" + i, GetCleanValue(query.ExcludeInheritedTags[i]));
                }
                whereClauses.Add("((SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 6 AND clean_value IN (" + string.Join(",", clauses) + ")) IS NULL)");
            }

            if (query.SeriesStatuses.Length > 0)
            {
                // P3：jsonb 运算符代替 data::text LIKE（blob 全量转文本逐行扫描 →
                // 字段级提取，实测 4×；且语义更精确——只匹配 Status 字段而非任意位置）
                var clauses = new List<string>();
                for (var i = 0; i < query.SeriesStatuses.Length; i++)
                {
                    clauses.Add("@SeriesStatus" + i);
                    if (cmd != null) AddOrSetParam(cmd, "SeriesStatus" + i, query.SeriesStatuses[i]);
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.Select(c => "A.data->>'Status' = " + c).ToArray()) + ")");
            }

            if (query.BoxSetLibraryFolders.Length > 0)
            {
                var clauses = new List<string>();
                for (var i = 0; i < query.BoxSetLibraryFolders.Length; i++)
                {
                    clauses.Add("@BoxSetLibraryFolder" + i);
                    if (cmd != null) AddOrSetParam(cmd, "BoxSetLibraryFolder" + i, "%" + query.BoxSetLibraryFolders[i].ToString("N") + "%");
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.Select(c => "A.data::text LIKE " + c).ToArray()) + ")");
            }

            if (query.VideoTypes.Length > 0)
            {
                // P3：jsonb 运算符代替 data::text LIKE（同 SeriesStatuses）
                var clauses = new List<string>();
                for (var i = 0; i < query.VideoTypes.Length; i++)
                {
                    clauses.Add("@VideoType" + i);
                    if (cmd != null) AddOrSetParam(cmd, "VideoType" + i, query.VideoTypes[i]);
                }
                whereClauses.Add("(" + string.Join(" OR ", clauses.Select(c => "A.data->>'VideoType' = " + c).ToArray()) + ")");
            }

            if (query.Is3D.HasValue)
            {
                // P3：jsonb 键存在检查代替 LIKE
                whereClauses.Add(query.Is3D.Value
                    ? "A.data ? 'Video3DFormat'"
                    : "NOT (A.data ? 'Video3DFormat')");
            }

            if (query.IsPlaceHolder.HasValue)
            {
                whereClauses.Add(query.IsPlaceHolder.Value
                    ? "A.data::text LIKE '%\"IsPlaceHolder\":true%'"
                    : "(A.data IS NULL OR A.data::text NOT LIKE '%\"IsPlaceHolder\":true%')");
            }

            if (query.HasSpecialFeature.HasValue)
            {
                whereClauses.Add(query.HasSpecialFeature.Value ? "A.extra_ids IS NOT NULL" : "A.extra_ids IS NULL");
            }

            if (query.HasTrailer.HasValue)
            {
                whereClauses.Add(query.HasTrailer.Value ? "A.extra_ids IS NOT NULL" : "A.extra_ids IS NULL");
            }

            if (query.HasThemeSong.HasValue)
            {
                whereClauses.Add(query.HasThemeSong.Value ? "A.extra_ids IS NOT NULL" : "A.extra_ids IS NULL");
            }

            if (query.HasThemeVideo.HasValue)
            {
                whereClauses.Add(query.HasThemeVideo.Value ? "A.extra_ids IS NOT NULL" : "A.extra_ids IS NULL");
            }

            return whereClauses;
        }

        /// <summary>
        /// IncludeItemTypes/ExcludeItemTypes → type 码 / type_name 过滤。
        /// 已登记类型用 type INTEGER（分区裁剪）；未登记类型退回 type_name 精确匹配。
        /// </summary>
        private static void AddTypeFilter(List<string> whereClauses, NpgsqlCommand cmd, string[] typeNames, bool negate)
        {
            var codes = new List<int>();
            var unknownNames = new List<string>();

            foreach (var name in typeNames)
            {
                var code = GetTypeCodeByName(name);
                if (code.HasValue)
                {
                    codes.Add(code.Value);
                }
                else
                {
                    unknownNames.Add(name);
                }
            }

            var clauses = new List<string>();

            if (codes.Count > 0)
            {
                var codeParams = codes.Select((c, i) =>
                {
                    var paramName = (negate ? "ExcludeTypeCode" : "TypeCode") + i;
                    if (cmd != null) AddOrSetParam(cmd, paramName, c);
                    return "@" + paramName;
                }).ToArray();

                clauses.Add("A.type " + (negate ? "NOT IN" : "IN") + " (" + string.Join(",", codeParams) + ")");
            }

            if (unknownNames.Count > 0)
            {
                var nameParams = unknownNames.Select((n, i) =>
                {
                    var paramName = (negate ? "ExcludeTypeName" : "TypeName") + i;
                    if (cmd != null) AddOrSetParam(cmd, paramName, n);
                    return "@" + paramName;
                }).ToArray();

                clauses.Add("A.type_name " + (negate ? "NOT IN" : "IN") + " (" + string.Join(",", nameParams) + ")");
            }

            if (clauses.Count == 0)
            {
                return;
            }

            var joined = string.Join(" OR ", clauses.ToArray());
            whereClauses.Add(negate && clauses.Count > 1 ? "(" + joined + ")" : joined);
        }

        /// <summary>
        /// ItemsByName 类型子句（旧 "(Type=@IncludedItemByNameType or TopParentId=...)"）。
        /// </summary>
        private static string BuildItemByNameTypeClauses(List<string> includedItemByNameTypes, bool enableItemsByName, NpgsqlCommand cmd, string singleParamName, string multiParamPrefix)
        {
            if (!enableItemsByName || includedItemByNameTypes.Count == 0)
            {
                return null;
            }

            if (includedItemByNameTypes.Count == 1)
            {
                var code = GetTypeCodeByName(includedItemByNameTypes[0]);
                if (code.HasValue)
                {
                    if (cmd != null) AddOrSetParam(cmd, singleParamName, code.Value);
                    return "A.type = @" + singleParamName;
                }

                if (cmd != null) AddOrSetParam(cmd, singleParamName, includedItemByNameTypes[0]);
                return "A.type_name = @" + singleParamName;
            }

            var parts = new List<string>();
            for (var i = 0; i < includedItemByNameTypes.Count; i++)
            {
                var paramName = multiParamPrefix + i;
                var code = GetTypeCodeByName(includedItemByNameTypes[i]);
                if (code.HasValue)
                {
                    if (cmd != null) AddOrSetParam(cmd, paramName, code.Value);
                    parts.Add("A.type = @" + paramName);
                }
                else
                {
                    if (cmd != null) AddOrSetParam(cmd, paramName, includedItemByNameTypes[i]);
                    parts.Add("A.type_name = @" + paramName);
                }
            }

            return "(" + string.Join(" OR ", parts.ToArray()) + ")";
        }

        private static List<string> GetItemByNameTypesInQuery(InternalItemsQuery query)
        {
            var list = new List<string>();

            if (IsTypeInQuery(typeof(Person).Name, query)) list.Add(typeof(Person).Name);
            if (IsTypeInQuery(typeof(Genre).Name, query)) list.Add(typeof(Genre).Name);
            if (IsTypeInQuery(typeof(MusicGenre).Name, query)) list.Add(typeof(MusicGenre).Name);
            if (IsTypeInQuery(typeof(GameGenre).Name, query)) list.Add(typeof(GameGenre).Name);
            if (IsTypeInQuery(typeof(MusicArtist).Name, query)) list.Add(typeof(MusicArtist).Name);
            if (IsTypeInQuery(typeof(Studio).Name, query)) list.Add(typeof(Studio).Name);

            return list;
        }

        private static bool IsTypeInQuery(string type, InternalItemsQuery query)
        {
            if (query.ExcludeItemTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            return query.IncludeItemTypes.Length == 0 || query.IncludeItemTypes.Contains(type, StringComparer.OrdinalIgnoreCase);
        }

        // ===========================================================
        // 相似度 / 搜索评分表达式
        // ===========================================================

        /// <summary>
        /// 相似度评分基础表达式（旧 SimilarityScore 列；SQLite 布尔乘法 → CASE WHEN）。
        /// P2 性能优化：extradata 匹配部分拆出（见 GetSimilarityMatchExpression），
        /// 由标量相关子查询（逐行回探 item_extradata，10k 条目 71ms）改为
        /// LEFT JOIN 聚合（GetSimilarityJoinText，实测 10.4ms，8.4×）。
        /// </summary>
        private static string GetSimilarityScoreExpression()
        {
            return "(CASE WHEN A.official_rating IS NULL THEN 10 ELSE 0 END)" +
                   "+ (CASE WHEN ABS(COALESCE(A.production_year, 0) - @ItemProductionYear) < 10 THEN 10 ELSE 0 END)" +
                   "+ (CASE WHEN ABS(COALESCE(A.production_year, 0) - @ItemProductionYear) < 5 THEN 5 ELSE 0 END)";
        }

        /// <summary>
        /// 相似度评分的 extradata 匹配部分：引用 sim JOIN 别名（match_count），
        /// 需配合 GetSimilarityJoinText 的 FROM 拼装使用。
        /// </summary>
        private static string GetSimilarityMatchExpression()
        {
            return "(COALESCE(sim.match_count, 0) * 10)";
        }

        /// <summary>
        /// 完整相似度评分（基础 + extradata 匹配）。调用方必须同时在 FROM 拼入
        /// GetSimilarityJoinText（query.SimilarTo != null 时）。
        /// </summary>
        private static string GetSimilarityScoreExpressionFull()
        {
            return GetSimilarityScoreExpression() + " + " + GetSimilarityMatchExpression();
        }

        /// <summary>
        /// SimilarTo 查询的 extradata 匹配 JOIN（一次聚合代替逐行相关子查询）。
        /// 内层 InitPlan（guid 解析）只执行一次；@SimilarItemId 参数由
        /// BindSimilarParams 按命令文本探测绑定（JOIN 文本含该参数名）。
        /// </summary>
        private static string GetSimilarityJoinText()
        {
            return " LEFT JOIN (SELECT e.item_id, count(*) AS match_count FROM item_extradata e" +
                   " WHERE e.clean_value IN (SELECT s.clean_value FROM item_extradata s" +
                   " WHERE s.item_id = (SELECT id FROM media_items WHERE guid = @SimilarItemId LIMIT 1))" +
                   " GROUP BY e.item_id) sim ON sim.item_id = A.id";
        }

        /// <summary>
        /// 搜索评分 > 0 的布尔等价展开（WHERE 不能引用 SELECT 别名）。
        /// </summary>
        private static string GetSearchScoreBooleanExpression(InternalItemsQuery query)
        {
            var builder = new StringBuilder("(");

            builder.Append("(A.clean_name LIKE @SearchTermStartsWith OR (A.original_title IS NOT NULL AND A.original_title LIKE @SearchTermStartsWith))");

            if (query.SearchTerm.Length > 1)
            {
                builder.Append(" OR (A.clean_name LIKE @SearchTermContains OR (A.original_title IS NOT NULL AND A.original_title LIKE @SearchTermContains))");
            }

            builder.Append(")");

            return builder.ToString();
        }

        /// <summary>
        /// 搜索评分表达式（旧 SearchScore 列；SQLite 布尔乘法 → CASE WHEN）。
        /// </summary>
        private static string GetSearchScoreExpression(InternalItemsQuery query)
        {
            var builder = new StringBuilder("(");

            builder.Append("(CASE WHEN (A.clean_name LIKE @SearchTermStartsWith OR (A.original_title IS NOT NULL AND A.original_title LIKE @SearchTermStartsWith)) THEN 10 ELSE 0 END)");

            if (query.SearchTerm.Length > 1)
            {
                builder.Append("+ (CASE WHEN (A.clean_name LIKE @SearchTermContains OR (A.original_title IS NOT NULL AND A.original_title LIKE @SearchTermContains)) THEN 10 ELSE 0 END)");
            }

            builder.Append(")");

            return builder.ToString();
        }

        private void BindSearchParams(InternalItemsQuery query, NpgsqlCommand cmd)
        {
            var searchTerm = query.SearchTerm;

            if (string.IsNullOrEmpty(searchTerm))
            {
                return;
            }

            searchTerm = FixUnicodeChars(searchTerm);
            searchTerm = GetCleanValue(searchTerm);

            var commandText = cmd.CommandText;
            if (commandText.IndexOf("@SearchTermStartsWith", StringComparison.OrdinalIgnoreCase) != -1)
            {
                AddOrSetParam(cmd, "SearchTermStartsWith", searchTerm + "%");
            }
            if (commandText.IndexOf("@SearchTermContains", StringComparison.OrdinalIgnoreCase) != -1)
            {
                AddOrSetParam(cmd, "SearchTermContains", "%" + searchTerm + "%");
            }
        }

        private void BindSimilarParams(InternalItemsQuery query, NpgsqlCommand cmd)
        {
            var item = query.SimilarTo;

            if (item == null)
            {
                return;
            }

            var commandText = cmd.CommandText;

            if (commandText.IndexOf("@ItemOfficialRating", StringComparison.OrdinalIgnoreCase) != -1)
            {
                AddOrSetParam(cmd, "ItemOfficialRating", (object)item.OfficialRating ?? DBNull.Value);
            }

            if (commandText.IndexOf("@ItemProductionYear", StringComparison.OrdinalIgnoreCase) != -1)
            {
                AddOrSetParam(cmd, "ItemProductionYear", item.ProductionYear ?? 0);
            }

            if (commandText.IndexOf("@SimilarItemId", StringComparison.OrdinalIgnoreCase) != -1)
            {
                AddOrSetParam(cmd, "SimilarItemId", item.Id);
            }
        }

        /// <summary>
        /// SimilarTo 查询的排除项预处理（旧 GetFinalColumnsToSelect 内逻辑）。
        /// </summary>
        private static void PrepareSimilarToQuery(InternalItemsQuery query)
        {
            if (query.SimilarTo == null)
            {
                return;
            }

            var item = query.SimilarTo;

            var excludeIds = query.ExcludeItemIds.ToList();
            excludeIds.Add(item.Id);
            excludeIds.AddRange(item.ExtraIds);

            query.ExcludeItemIds = excludeIds.ToArray();
            query.ExcludeProviderIds = item.ProviderIds;
        }

        // ===========================================================
        // 查询执行
        // ===========================================================

        /// <summary>
        /// 计数（旧 GetCount：count(distinct PresentationUniqueKey)）。
        /// </summary>
        public int GetCount(InternalItemsQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            var now = DateTime.UtcNow;

            PrepareSimilarToQuery(query);

            var commandText = "SELECT count(DISTINCT A.presentation_unique_key) FROM media_items A";
            commandText += GetJoinUserDataText(query);

            // P2：SimilarTo 计数路径的评分 WHERE 引用 sim.match_count，需拼入匹配 JOIN
            if (query.SimilarTo != null)
            {
                commandText += GetSimilarityJoinText();
            }

            var whereClauses = GetWhereClauses(query, null);

            var whereText = whereClauses.Count == 0
                ? string.Empty
                : " WHERE " + string.Join(" AND ", whereClauses.ToArray());

            commandText += whereText;

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(commandText, connection))
                {
                    BindQueryParameters(cmd, query);

                    var count = Convert.ToInt32(cmd.ExecuteScalar());
                    LogQueryTime("GetCount", commandText, now);
                    return count;
                }
            }
        }

        /// <summary>
        /// 统一绑定查询参数（UserId join + Similar + Search + WHERE）。
        /// </summary>
        private void BindQueryParameters(NpgsqlCommand cmd, InternalItemsQuery query)
        {
            if (EnableJoinUserData(query))
            {
                AddOrSetParam(cmd, "UserId", (int)query.User.InternalId);
            }

            BindSimilarParams(query, cmd);
            BindSearchParams(query, cmd);

            // 再跑一遍以注册 WHERE 参数（旧实现模式）
            GetWhereClauses(query, cmd);
        }

        /// <summary>
        /// 获取媒体项列表（旧 GetItemList）。
        /// </summary>
        public List<BaseItem> GetItemList(InternalItemsQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            var now = DateTime.UtcNow;

            // 旧 Hack：分组去重时多取 4 条再在内存去重
            if (query.Limit.HasValue && query.EnableGroupByMetadataKey)
            {
                query.Limit = query.Limit.Value + 4;
            }

            var commandText = BuildItemQueryText(query, out var isDistinctOnWrapped);

            using (var connection = CreateConnection())
            {
                var list = new List<BaseItem>();

                using (var cmd = new NpgsqlCommand(commandText, connection))
                {
                    BindQueryParameters(cmd, query);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var item = GetItem(reader, connection);
                            if (item != null)
                            {
                                list.Add(item);
                            }
                        }
                    }
                }

                if (query.EnableGroupByMetadataKey)
                {
                    var limit = query.Limit ?? int.MaxValue;
                    limit -= 4;
                    var newList = new List<BaseItem>();

                    foreach (var item in list)
                    {
                        AddItem(newList, item);

                        if (newList.Count >= limit)
                        {
                            break;
                        }
                    }

                    list = newList;
                }

                LogQueryTime("GetItemList", commandText, now);

                return list;
            }
        }

        /// <summary>
        /// 构建媒体项查询 SQL（DISTINCT ON 分组时包一层子查询，外层重排序）。
        /// </summary>
        private string BuildItemQueryText(InternalItemsQuery query, out bool isDistinctOnWrapped)
        {
            isDistinctOnWrapped = false;

            PrepareSimilarToQuery(query);

            var selectText = BuildItemSelectText(query);
            var fromText = " FROM media_items A";
            var joinText = GetJoinUserDataText(query);

            // P2：SimilarTo 查询的评分列/WHERE/排序展开均引用 sim.match_count，拼入匹配 JOIN
            if (query.SimilarTo != null)
            {
                joinText += GetSimilarityJoinText();
            }

            var whereClauses = GetWhereClauses(query, null);
            var whereText = whereClauses.Count == 0
                ? string.Empty
                : " WHERE " + string.Join(" AND ", whereClauses.ToArray());

            var orderByExpressions = GetOrderByExpressions(query);
            var orderByText = BuildOrderByText(orderByExpressions);

            var pagingText = BuildPagingText(query);

            var distinctOnExpr = GetDistinctOnExpression(query);

            if (distinctOnExpr == null)
            {
                return selectText + fromText + joinText + whereText + orderByText + pagingText;
            }

            // DISTINCT ON：内层按 (分组键, 排序) 取每组代表行，外层按 __sortN 别名重排序
            isDistinctOnWrapped = true;

            return BuildDistinctOnWrapper(
                selectText.Substring("SELECT ".Length),
                fromText + joinText + whereText,
                distinctOnExpr,
                orderByExpressions,
                pagingText,
                query);
        }

        /// <summary>
        /// 排序文本（非包装路径直接引用表达式）。
        /// </summary>
        private static string BuildOrderByText(List<Tuple<string, SortOrder>> orderByExpressions)
        {
            if (orderByExpressions.Count == 0)
            {
                return string.Empty;
            }

            return " ORDER BY " + string.Join(",", orderByExpressions
                .Select(i => i.Item1 + " " + (i.Item2 == SortOrder.Ascending ? "ASC" : "DESC"))
                .ToArray());
        }

        /// <summary>
        /// DISTINCT ON 包装：内层 SELECT 输出 __sortN 别名列（外层无法引用 A./ud. 前缀表达式），
        /// 内层 ORDER BY (分组键, 排序表达式) 取每组代表行，外层按 __sortN 重排序 + 分页。
        /// </summary>
        private static string BuildDistinctOnWrapper(
            string selectColumns,
            string fromJoinWhere,
            string distinctOnExpr,
            List<Tuple<string, SortOrder>> orderByExpressions,
            string pagingText,
            InternalItemsQuery query)
        {
            var sortColumnList = new List<string>();
            var innerOrderItems = new List<string>();
            var outerOrderItems = new List<string>();

            for (var i = 0; i < orderByExpressions.Count; i++)
            {
                var expr = ExpandSortAliasExpression(NormalizeSortExpressionForWrap(orderByExpressions[i].Item1), query);
                var direction = orderByExpressions[i].Item2 == SortOrder.Ascending ? "ASC" : "DESC";

                sortColumnList.Add(expr + " AS __sort" + i);
                innerOrderItems.Add(expr + " " + direction);
                outerOrderItems.Add("__sort" + i + " " + direction);
            }

            var innerOrderBy = " ORDER BY " + distinctOnExpr +
                (innerOrderItems.Count > 0 ? ", " + string.Join(",", innerOrderItems.ToArray()) : string.Empty);

            var inner = "SELECT DISTINCT ON (" + distinctOnExpr + ") " + selectColumns +
                (sortColumnList.Count > 0 ? ", " + string.Join(",", sortColumnList.ToArray()) : string.Empty) +
                fromJoinWhere + innerOrderBy;

            var outerOrderBy = outerOrderItems.Count == 0
                ? string.Empty
                : " ORDER BY " + string.Join(",", outerOrderItems.ToArray());

            return "SELECT * FROM (" + inner + ") grouped" + outerOrderBy + pagingText;
        }

        /// <summary>
        /// 包装路径的排序表达式归一化：DISTINCT ON 按组内排序取代表行，
        /// MAX() 聚合包装需剥掉（代表行即组内最值行）。
        /// </summary>
        private static string NormalizeSortExpressionForWrap(string expr)
        {
            if (string.Equals(expr, "MAX(ud.last_played_date)", StringComparison.OrdinalIgnoreCase))
            {
                return "ud.last_played_date";
            }

            return expr;
        }

        /// <summary>
        /// 包装路径的输出别名展开：similarity_score / search_score 是 SELECT 输出别名，
        /// PG 只允许 ORDER BY 引用别名，SELECT 列表（__sortN 别名列）引用会 42703。
        /// 展开为完整表达式后内层 SELECT / ORDER BY / 外层重排序三处均合法，
        /// 且对内层不输出该别名的 ID 查询路径同样成立。
        /// </summary>
        private static string ExpandSortAliasExpression(string expr, InternalItemsQuery query)
        {
            // IgnoreCase 对齐上游 MapOrderByField：异常大小写的排序名同样展开，避免包装器里重新 42703
            if (string.Equals(expr, "similarity_score", StringComparison.OrdinalIgnoreCase))
            {
                return GetSimilarityScoreExpressionFull();
            }

            if (string.Equals(expr, "search_score", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(query.SearchTerm))
                {
                    return "0";
                }

                return GetSearchScoreExpression(query);
            }

            return expr;
        }

        private static string BuildPagingText(InternalItemsQuery query)
        {
            if (!query.Limit.HasValue && !query.StartIndex.HasValue)
            {
                return string.Empty;
            }

            var offset = query.StartIndex ?? 0;
            var pagingText = string.Empty;

            if (query.Limit.HasValue || offset > 0)
            {
                pagingText += " LIMIT " + (query.Limit ?? int.MaxValue).ToString(CultureInfo.InvariantCulture);
            }

            if (offset > 0)
            {
                pagingText += " OFFSET " + offset.ToString(CultureInfo.InvariantCulture);
            }

            return pagingText;
        }

        /// <summary>
        /// 分页查询（旧 GetItems：列表 + 总数）。
        /// </summary>
        public QueryResult<BaseItem> GetItems(InternalItemsQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            if (!query.EnableTotalRecordCount || (!query.Limit.HasValue && (query.StartIndex ?? 0) == 0))
            {
                var returnList = GetItemList(query);
                return new QueryResult<BaseItem>
                {
                    Items = returnList.ToArray(),
                    TotalRecordCount = returnList.Count
                };
            }

            var now = DateTime.UtcNow;

            if (query.Limit.HasValue && query.EnableGroupByMetadataKey)
            {
                query.Limit = query.Limit.Value + 4;
            }

            bool isWrapped;
            var commandText = BuildItemQueryText(query, out isWrapped);

            var list = new List<BaseItem>();
            var result = new QueryResult<BaseItem>();

            var isReturningZeroItems = query.Limit.HasValue && query.Limit <= 0;

            var countText = BuildCountText(query);

            using (var connection = CreateConnection())
            {
                if (!isReturningZeroItems)
                {
                    using (var cmd = new NpgsqlCommand(commandText, connection))
                    {
                        BindQueryParameters(cmd, query);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var item = GetItem(reader, connection);
                                if (item != null)
                                {
                                    list.Add(item);
                                }
                            }
                        }
                    }
                }

                using (var countCmd = new NpgsqlCommand(countText, connection))
                {
                    BindQueryParameters(countCmd, query);
                    result.TotalRecordCount = Convert.ToInt32(countCmd.ExecuteScalar());
                }
            }

            if (query.EnableGroupByMetadataKey)
            {
                var limit = query.Limit ?? int.MaxValue;
                limit -= 4;
                var newList = new List<BaseItem>();

                foreach (var item in list)
                {
                    AddItem(newList, item);

                    if (newList.Count >= limit)
                    {
                        break;
                    }
                }

                list = newList;
            }

            LogQueryTime("GetItems", commandText, now);

            result.Items = list.ToArray();
            return result;
        }

        /// <summary>
        /// 计数 SQL（旧：分组时 count(distinct 分组键)，否则 count(guid)）。
        /// </summary>
        private string BuildCountText(InternalItemsQuery query)
        {
            PrepareSimilarToQuery(query);

            var countColumn = "count(*)";

            if (EnableGroupByPresentationUniqueKey(query))
            {
                countColumn = "count(DISTINCT A.presentation_unique_key)";
            }
            else if (query.GroupBySeriesPresentationUniqueKey)
            {
                countColumn = "count(DISTINCT A.series_presentation_unique_key)";
            }

            var whereClauses = GetWhereClauses(query, null);
            var whereText = whereClauses.Count == 0
                ? string.Empty
                : " WHERE " + string.Join(" AND ", whereClauses.ToArray());

            // P2：SimilarTo 计数路径的评分 WHERE 引用 sim.match_count，需拼入匹配 JOIN
            var similarityJoin = query.SimilarTo != null ? GetSimilarityJoinText() : string.Empty;

            return "SELECT " + countColumn + " FROM media_items A" + GetJoinUserDataText(query) + similarityJoin + whereText;
        }

        /// <summary>
        /// 获取 GUID 列表（旧 GetItemIdsList）。
        /// </summary>
        public List<Guid> GetItemIdsList(InternalItemsQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            var now = DateTime.UtcNow;

            var commandText = BuildIdQueryText("A.guid", query);

            using (var connection = CreateConnection())
            {
                var list = new List<Guid>();

                using (var cmd = new NpgsqlCommand(commandText, connection))
                {
                    BindQueryParameters(cmd, query);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(reader.GetGuid(0));
                        }
                    }
                }

                LogQueryTime("GetItemIdsList", commandText, now);

                return list;
            }
        }

        /// <summary>
        /// 获取 GUID + 路径列表（旧 GetItemIdsWithPath）。
        /// </summary>
        public List<Tuple<Guid, string>> GetItemIdsWithPath(InternalItemsQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            var now = DateTime.UtcNow;

            var commandText = BuildIdQueryText("A.guid, A.path", query);

            var list = new List<Tuple<Guid, string>>();

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(commandText, connection))
                {
                    BindQueryParameters(cmd, query);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetGuid(0);
                            string path = null;

                            if (!reader.IsDBNull(1))
                            {
                                path = reader.GetString(1);
                            }

                            list.Add(new Tuple<Guid, string>(id, path));
                        }
                    }
                }
            }

            LogQueryTime("GetItemIdsWithPath", commandText, now);

            return list;
        }

        /// <summary>
        /// 分页 GUID 查询（旧 GetItemIds：列表 + 总数）。
        /// </summary>
        public QueryResult<Guid> GetItemIds(InternalItemsQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            if (!query.EnableTotalRecordCount || (!query.Limit.HasValue && (query.StartIndex ?? 0) == 0))
            {
                var returnList = GetItemIdsList(query);
                return new QueryResult<Guid>
                {
                    Items = returnList.ToArray(),
                    TotalRecordCount = returnList.Count
                };
            }

            var now = DateTime.UtcNow;

            var commandText = BuildIdQueryText("A.guid", query);
            var countText = BuildCountText(query);

            var list = new List<Guid>();
            var result = new QueryResult<Guid>();

            var isReturningZeroItems = query.Limit.HasValue && query.Limit <= 0;

            using (var connection = CreateConnection())
            {
                if (!isReturningZeroItems)
                {
                    using (var cmd = new NpgsqlCommand(commandText, connection))
                    {
                        BindQueryParameters(cmd, query);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(reader.GetGuid(0));
                            }
                        }
                    }
                }

                using (var countCmd = new NpgsqlCommand(countText, connection))
                {
                    BindQueryParameters(countCmd, query);
                    result.TotalRecordCount = Convert.ToInt32(countCmd.ExecuteScalar());
                }
            }

            LogQueryTime("GetItemIds", commandText, now);

            result.Items = list.ToArray();
            return result;
        }

        /// <summary>
        /// GUID/路径查询 SQL（DISTINCT ON 分组时包一层子查询）。
        /// </summary>
        private string BuildIdQueryText(string columns, InternalItemsQuery query)
        {
            PrepareSimilarToQuery(query);

            var fromText = " FROM media_items A";
            var joinText = GetJoinUserDataText(query);

            // P2：SimilarTo 查询的评分 WHERE/排序展开引用 sim.match_count，拼入匹配 JOIN
            if (query.SimilarTo != null)
            {
                joinText += GetSimilarityJoinText();
            }

            var whereClauses = GetWhereClauses(query, null);
            var whereText = whereClauses.Count == 0
                ? string.Empty
                : " WHERE " + string.Join(" AND ", whereClauses.ToArray());

            var orderByExpressions = GetOrderByExpressions(query);
            var orderByText = BuildOrderByText(orderByExpressions);

            var pagingText = BuildPagingText(query);

            var distinctOnExpr = GetDistinctOnExpression(query);

            if (distinctOnExpr == null)
            {
                return "SELECT " + columns + fromText + joinText + whereText + orderByText + pagingText;
            }

            return BuildDistinctOnWrapper(columns, fromText + joinText + whereText, distinctOnExpr, orderByExpressions, pagingText, query);
        }

        // ===========================================================
        // 辅助
        // ===========================================================

        /// <summary>
        /// 元数据键去重（旧 AddItem：同 ProviderId 的项保留 Library 来源者）。
        /// </summary>
        private static void AddItem(List<BaseItem> items, BaseItem newItem)
        {
            var providerIds = newItem.ProviderIds.ToList();

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];

                foreach (var providerId in providerIds)
                {
                    if (providerId.Key == MetadataProviders.TmdbCollection.ToString())
                    {
                        continue;
                    }
                    if (item.GetProviderId(providerId.Key) == providerId.Value)
                    {
                        if (newItem.SourceType == SourceType.Library)
                        {
                            items[i] = newItem;
                        }
                        return;
                    }
                }
            }

            items.Add(newItem);
        }

        private void LogQueryTime(string methodName, string commandText, DateTime startDate)
        {
            var elapsed = (DateTime.UtcNow - startDate).TotalMilliseconds;

            var slowThreshold = 1000;

#if DEBUG
            slowThreshold = 10;
#endif

            // 诊断开关：环境变量 OM_TRACE_ALL_QUERIES=1 时全量输出（水合批量化排障）。
            // 注意：Start-Process 环境变量继承不可靠时可用 true 短路临时全开（测完移除）
            if (Environment.GetEnvironmentVariable("OM_TRACE_ALL_QUERIES") == "1"
                || Environment.GetEnvironmentVariable("OM_TRACE_ALL_QUERIES") == "true")
            {
                var sqlHead = commandText == null ? "" : (commandText.Length > 160 ? commandText.Substring(0, 160) : commandText);
                // 调用栈摘要（跳过 System/PgItemRepository 帧，取业务帧）
                var st = new System.Diagnostics.StackTrace(2, false);
                var frames = new List<string>();
                for (var i = 0; i < st.FrameCount && frames.Count < 5; i++)
                {
                    var m = st.GetFrame(i).GetMethod();
                    if (m == null || m.DeclaringType == null) continue;
                    var n = m.DeclaringType.Name + "." + m.Name;
                    if (n.StartsWith("PgItemRepository")) continue;
                    frames.Add(n);
                }
                Logger.Info("{2} query: {0}ms. SQL: {1} | CALL: {3}", Convert.ToInt32(elapsed), sqlHead, methodName, string.Join(" <- ", frames));
                return;
            }

            if (elapsed >= slowThreshold)
            {
                Logger.Debug("{2} query time (slow): {0}ms. Query: {1}",
                    Convert.ToInt32(elapsed),
                    commandText,
                    methodName);
            }
        }
    }
}
