using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PgItemRepository — 聚合查询：Genres/Studios/Artists/PeopleNames（item_extradata 分组统计）。
    /// 对照旧 SqliteItemRepository 的 GetItemValues/GetItemValueNames/GetItemCounts 保留语义。
    ///
    /// extradata_type_id 对照旧 ItemValues.Type：
    /// 0=Artists, 1=AlbumArtists, 2=Genres, 3=Studios, 4=Tags, 6=InheritedTags
    /// </summary>
    public partial class PgItemRepository
    {
        public Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetAllArtistsAsync(InternalItemsQuery query, CancellationToken cancellationToken = default)
        {
            return GetItemValuesAsync(query, new[] { 0, 1 }, typeof(MusicArtist).FullName, cancellationToken);
        }

        public Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetArtistsAsync(InternalItemsQuery query, CancellationToken cancellationToken = default)
        {
            return GetItemValuesAsync(query, new[] { 0 }, typeof(MusicArtist).FullName, cancellationToken);
        }

        public Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetAlbumArtistsAsync(InternalItemsQuery query, CancellationToken cancellationToken = default)
        {
            return GetItemValuesAsync(query, new[] { 1 }, typeof(MusicArtist).FullName, cancellationToken);
        }

        public Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetStudiosAsync(InternalItemsQuery query, CancellationToken cancellationToken = default)
        {
            return GetItemValuesAsync(query, new[] { 3 }, typeof(Studio).FullName, cancellationToken);
        }

        public Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetGenresAsync(InternalItemsQuery query, CancellationToken cancellationToken = default)
        {
            return GetItemValuesAsync(query, new[] { 2 }, typeof(Genre).FullName, cancellationToken);
        }

        public Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetGameGenresAsync(InternalItemsQuery query, CancellationToken cancellationToken = default)
        {
            return GetItemValuesAsync(query, new[] { 2 }, typeof(GameGenre).FullName, cancellationToken);
        }

        public Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetMusicGenresAsync(InternalItemsQuery query, CancellationToken cancellationToken = default)
        {
            return GetItemValuesAsync(query, new[] { 2 }, typeof(MusicGenre).FullName, cancellationToken);
        }

        public Task<List<string>> GetStudioNamesAsync(CancellationToken cancellationToken = default)
        {
            return GetItemValueNamesAsync(new[] { 3 }, new List<string>(), new List<string>(), cancellationToken);
        }

        public Task<List<string>> GetAllArtistNamesAsync(CancellationToken cancellationToken = default)
        {
            return GetItemValueNamesAsync(new[] { 0, 1 }, new List<string>(), new List<string>(), cancellationToken);
        }

        public Task<List<string>> GetMusicGenreNamesAsync(CancellationToken cancellationToken = default)
        {
            return GetItemValueNamesAsync(new[] { 2 }, new List<string> { "Audio", "MusicVideo", "MusicAlbum", "MusicArtist" }, new List<string>(), cancellationToken);
        }

        public Task<List<string>> GetGameGenreNamesAsync(CancellationToken cancellationToken = default)
        {
            return GetItemValueNamesAsync(new[] { 2 }, new List<string> { "Game" }, new List<string>(), cancellationToken);
        }

        public Task<List<string>> GetGenreNamesAsync(CancellationToken cancellationToken = default)
        {
            return GetItemValueNamesAsync(new[] { 2 }, new List<string>(), new List<string> { "Audio", "MusicVideo", "MusicAlbum", "MusicArtist", "Game", "GameSystem" }, cancellationToken);
        }

        /// <summary>
        /// 值名称列表（旧 GetItemValueNames：按 CleanValue 分组取 Value，
        /// 可按 with/exclude item 类型过滤）。
        /// </summary>
        private async Task<List<string>> GetItemValueNamesAsync(int[] itemValueTypes, List<string> withItemTypes, List<string> excludeItemTypes, CancellationToken cancellationToken)
        {
            CheckDisposed();

            withItemTypes = withItemTypes.SelectMany(MapIncludeItemTypes).ToList();
            excludeItemTypes = excludeItemTypes.SelectMany(MapIncludeItemTypes).ToList();

            var now = DateTime.UtcNow;

            var typeClause = BuildExtradataTypeClause(itemValueTypes);

            // 旧 SQLite "SELECT Value ... GROUP BY CleanValue" 在 PG 下非法（非聚合列）；
            // MIN(value) 保留旧语义：每个 clean 分组取一个代表显示值
            var commandText = "SELECT MIN(value) FROM item_extradata WHERE " + typeClause;

            if (withItemTypes.Count > 0)
            {
                commandText += " AND item_id IN (SELECT id FROM media_items WHERE " + BuildTypeCodeClause(withItemTypes) + ")";
            }
            if (excludeItemTypes.Count > 0)
            {
                commandText += " AND item_id NOT IN (SELECT id FROM media_items WHERE " + BuildTypeCodeClause(excludeItemTypes) + ")";
            }

            commandText += " GROUP BY clean_value";

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            var list = new List<string>();

            await using (var cmd = new NpgsqlCommand(commandText, connection))
            {
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false))
                    {
                        list.Add(reader.GetString(0));
                    }
                }
            }

            LogQueryTime("GetItemValueNames", commandText, now);

            return list;
        }

        /// <summary>
        /// extradata_type_id 子句（旧 Type 子句）。
        /// </summary>
        private static string BuildExtradataTypeClause(int[] itemValueTypes)
        {
            return itemValueTypes.Length == 1
                ? "extradata_type_id = " + itemValueTypes[0].ToString(CultureInfo.InvariantCulture)
                : "extradata_type_id IN (" + string.Join(",", itemValueTypes.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray()) + ")";
        }

        /// <summary>
        /// 类型名列表 → "type IN (码...)" 子句（未登记类型退回 type_name）。
        /// </summary>
        private static string BuildTypeCodeClause(List<string> typeNames)
        {
            var codes = new List<string>();
            var names = new List<string>();

            foreach (var name in typeNames)
            {
                var code = GetTypeCodeByName(name);
                if (code.HasValue)
                {
                    codes.Add(code.Value.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    names.Add("'" + name.Replace("'", "''") + "'");
                }
            }

            var parts = new List<string>();

            if (codes.Count > 0)
            {
                parts.Add("type IN (" + string.Join(",", codes) + ")");
            }

            if (names.Count > 0)
            {
                parts.Add("type_name IN (" + string.Join(",", names) + ")");
            }

            if (parts.Count == 0)
            {
                return "FALSE";
            }

            return string.Join(" OR ", parts.ToArray());
        }

        /// <summary>
        /// 聚合查询（旧 GetItemValues）：
        /// 选出 returnType 类型的 item-by-name 项（其 clean_name 出现在符合 innerQuery 条件
        /// 的媒体的 extradata 中），附带 itemTypes 统计列与 outerQuery 过滤。
        /// 旧 "group by PresentationUniqueKey" → DISTINCT ON + 外层重排序。
        /// P8：纳入查询门（Reviewer 备注②——1M 规模的重查询入口，不留第二个 U2 形态）。
        /// </summary>
        public Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetItemValuesAsync(InternalItemsQuery query, int[] itemValueTypes, string returnType, CancellationToken cancellationToken)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            return WithQueryGateAsync(
                ct => GetItemValuesCoreAsync(query, itemValueTypes, returnType, ct),
                cancellationToken);
        }

        private async Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetItemValuesCoreAsync(InternalItemsQuery query, int[] itemValueTypes, string returnType, CancellationToken cancellationToken)
        {
            if (!query.Limit.HasValue)
            {
                query.EnableTotalRecordCount = false;
            }

            CheckDisposed();

            var now = DateTime.UtcNow;

            var typeClause = BuildExtradataTypeClause(itemValueTypes);

            var typesToCount = query.IncludeItemTypes;

            // ===== itemTypes 统计列（旧 group_concat(type, '|') 子查询）=====

            string itemTypesColumn = null;
            InternalItemsQuery typeSubQuery = null;

            if (typesToCount.Length > 0)
            {
                typeSubQuery = new InternalItemsQuery(query.User)
                {
                    ExcludeItemTypes = query.ExcludeItemTypes,
                    IncludeItemTypes = query.IncludeItemTypes,
                    MediaTypes = query.MediaTypes,
                    AncestorIds = query.AncestorIds,
                    ExcludeItemIds = query.ExcludeItemIds,
                    ItemIds = query.ItemIds,
                    TopParentIds = query.TopParentIds,
                    ParentId = query.ParentId,
                    IsPlayed = query.IsPlayed
                };

                var typeSubClauses = GetWhereClauses(typeSubQuery, null)
                    .Select(c => c.Replace("A.", "B.").Replace("ud.", "UDB."))
                    .ToList();

                typeSubClauses.Add("B.id IN (SELECT item_id FROM item_extradata WHERE clean_value = A.clean_name AND " + typeClause + ")");

                var typeSubJoin = EnableJoinUserData(typeSubQuery)
                    ? " LEFT JOIN user_datas UDB ON UDB.user_data_key_id = B.user_data_key_id AND UDB.user_id = @UserId"
                    : string.Empty;

                var typeSubWhereText = typeSubClauses.Count == 0
                    ? string.Empty
                    : " WHERE " + string.Join(" AND ", typeSubClauses.ToArray());

                itemTypesColumn = "(SELECT string_agg(B.type::text, '|') FROM media_items B" + typeSubJoin + typeSubWhereText + ") AS item_types";
            }

            // ===== innerQuery（哪些媒体的 extradata 值参与统计）=====

            var innerQuery = new InternalItemsQuery(query.User)
            {
                ExcludeItemTypes = query.ExcludeItemTypes,
                IncludeItemTypes = query.IncludeItemTypes,
                MediaTypes = query.MediaTypes,
                AncestorIds = query.AncestorIds,
                ItemIds = query.ItemIds,
                TopParentIds = query.TopParentIds,
                ParentId = query.ParentId,
                IsPlayed = query.IsPlayed,
                IsAiring = query.IsAiring,
                IsMovie = query.IsMovie,
                IsSports = query.IsSports,
                IsKids = query.IsKids,
                IsNews = query.IsNews,
                IsSeries = query.IsSeries
            };

            var innerClauses = GetWhereClauses(innerQuery, null)
                .Select(c => c.Replace("A.", "B.").Replace("ud.", "UDB."))
                .ToList();

            var innerJoin = EnableJoinUserData(innerQuery)
                ? " LEFT JOIN user_datas UDB ON UDB.user_data_key_id = B.user_data_key_id AND UDB.user_id = @UserId"
                : string.Empty;

            var innerWhereText = innerClauses.Count == 0
                ? string.Empty
                : " WHERE " + string.Join(" AND ", innerClauses.ToArray());

            // SimilarTo 排除项预处理（旧在 GetFinalColumnsToSelect 中做，位于 innerQuery 之后、outerQuery 之前）
            PrepareSimilarToQuery(query);

            // ===== 主查询 =====

            var returnTypeCode = GetTypeCodeByName(returnType);

            var selectColumns = BuildItemSelectText(query).Substring("SELECT ".Length);

            if (itemTypesColumn != null)
            {
                selectColumns += ", " + itemTypesColumn;
            }

            var whereText = " WHERE " + (returnTypeCode.HasValue
                ? "A.type = @SelectType"
                : "A.type_name = @SelectTypeName");

            whereText += " AND A.clean_name IN (SELECT clean_value FROM item_extradata WHERE " + typeClause +
                         " AND item_id IN (SELECT B.id FROM media_items B" + innerJoin + innerWhereText + "))";

            // ===== outerQuery（对 item-by-name 项本身的过滤）=====

            var outerQuery = new InternalItemsQuery(query.User)
            {
                IsFavorite = query.IsFavorite,
                IsFavoriteOrLiked = query.IsFavoriteOrLiked,
                IsLiked = query.IsLiked,
                IsLocked = query.IsLocked,
                NameLessThan = query.NameLessThan,
                NameStartsWith = query.NameStartsWith,
                NameStartsWithOrGreater = query.NameStartsWithOrGreater,
                Tags = query.Tags,
                OfficialRatings = query.OfficialRatings,
                GenreIds = query.GenreIds,
                Genres = query.Genres,
                Years = query.Years,
                NameContains = query.NameContains,
                SearchTerm = query.SearchTerm,
                SimilarTo = query.SimilarTo,
                ExcludeItemIds = query.ExcludeItemIds
            };

            var outerClauses = GetWhereClauses(outerQuery, null);

            whereText += outerClauses.Count == 0
                ? string.Empty
                : " AND " + string.Join(" AND ", outerClauses.ToArray());

            var joinText = GetJoinUserDataText(query);

            // ===== 排序（旧：similar/search 用 GetOrderByText，否则 order by SortName）=====

            var orderByExpressions = GetOrderByExpressions(query);

            if (query.SimilarTo == null && string.IsNullOrEmpty(query.SearchTerm))
            {
                orderByExpressions = new List<Tuple<string, SortOrder>>
                {
                    new Tuple<string, SortOrder>("A.sort_name", SortOrder.Ascending)
                };
            }

            var pagingText = BuildPagingText(query);

            // DISTINCT ON (presentation_unique_key) 包裹（旧 group by PresentationUniqueKey）
            var commandText = BuildDistinctOnWrapper(
                selectColumns,
                " FROM media_items A" + joinText + whereText,
                "A.presentation_unique_key",
                orderByExpressions,
                pagingText,
                query);

            var isReturningZeroItems = query.Limit.HasValue && query.Limit <= 0;

            var countText = "SELECT count(DISTINCT A.presentation_unique_key) FROM media_items A" + joinText + whereText;

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            var list = new List<Tuple<BaseItem, ItemCounts>>();
            var result = new QueryResult<Tuple<BaseItem, ItemCounts>>();

            if (!isReturningZeroItems)
            {
                await using (var cmd = new NpgsqlCommand(commandText, connection))
                {
                    BindItemValuesParameters(cmd, query, typeSubQuery, innerQuery, outerQuery, returnType, returnTypeCode);

                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                    // P7/P8：与 GetItemList 同款 partial 水合 + data 列摘除判定
                    var skipDataHydration = !RequiresFullHydration(query);

                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var item = GetItem(reader, connection, skipDataHydration);
                        if (item != null)
                        {
                            var itemTypesOrdinal = typesToCount.Length > 0 ? reader.GetOrdinal("item_types") : -1;
                            list.Add(new Tuple<BaseItem, ItemCounts>(item, GetItemCounts(reader, itemTypesOrdinal, typesToCount)));
                        }
                    }
                }

                LogQueryTime("GetItemValues", commandText, now);
            }

            if (query.EnableTotalRecordCount)
            {
                await using var countCmd = new NpgsqlCommand(countText, connection);
                BindItemValuesParameters(countCmd, query, typeSubQuery, innerQuery, outerQuery, returnType, returnTypeCode);

                result.TotalRecordCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            }

            if (result.TotalRecordCount == 0)
            {
                result.TotalRecordCount = list.Count;
            }

            result.Items = list.ToArray();

            return result;
        }

        /// <summary>
        /// GetItemValues 的参数绑定（@UserId/@SelectType + 各子查询参数）。
        /// typeSubQuery 参数仅在命令文本含 itemTypes 统计列时注册（计数查询不含该列）。
        /// </summary>
        private void BindItemValuesParameters(NpgsqlCommand cmd, InternalItemsQuery query, InternalItemsQuery typeSubQuery, InternalItemsQuery innerQuery, InternalItemsQuery outerQuery, string returnType, int? returnTypeCode)
        {
            if (EnableJoinUserData(query) || (typeSubQuery != null && EnableJoinUserData(typeSubQuery)) || EnableJoinUserData(innerQuery))
            {
                cmd.Parameters.AddWithValue("UserId", (int)query.User.InternalId);
            }

            if (returnTypeCode.HasValue)
            {
                cmd.Parameters.AddWithValue("SelectType", returnTypeCode.Value);
            }
            else
            {
                cmd.Parameters.AddWithValue("SelectTypeName", returnType);
            }

            BindSimilarParams(query, cmd);
            BindSearchParams(query, cmd);

            if (typeSubQuery != null && cmd.CommandText.IndexOf("string_agg", StringComparison.OrdinalIgnoreCase) != -1)
            {
                GetWhereClauses(typeSubQuery, cmd);
            }

            GetWhereClauses(innerQuery, cmd);
            GetWhereClauses(outerQuery, cmd);
        }

        /// <summary>
        /// itemTypes 统计列 → ItemCounts（旧按完整类型名计数；新按 type 码计数）。
        /// </summary>
        private static ItemCounts GetItemCounts(NpgsqlDataReader reader, int itemTypesOrdinal, string[] typesToCount)
        {
            var counts = new ItemCounts();

            if (typesToCount.Length == 0)
            {
                return counts;
            }

            if (reader.IsDBNull(itemTypesOrdinal))
            {
                return counts;
            }

            var typeString = reader.GetString(itemTypesOrdinal);

            if (string.IsNullOrWhiteSpace(typeString))
            {
                return counts;
            }

            var allTypes = typeString.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .ToLookup(i => i)
                .ToList();

            foreach (var type in allTypes)
            {
                var value = type.ToList().Count;

                int typeCode;
                if (!int.TryParse(type.Key, out typeCode))
                {
                    counts.ItemCount += value;
                    continue;
                }

                if (typeCode == GetTypeCode(typeof(Movie))) { counts.MovieCount = value; }
                else if (typeCode == GetTypeCode(typeof(Series))) { counts.SeriesCount = value; }
                else if (typeCode == GetTypeCode(typeof(Episode))) { counts.EpisodeCount = value; }
                else if (typeCode == GetTypeCode(typeof(MusicAlbum))) { counts.AlbumCount = value; }
                else if (typeCode == GetTypeCode(typeof(MusicArtist))) { counts.ArtistCount = value; }
                else if (typeCode == GetTypeCode(typeof(Audio))) { counts.SongCount = value; }
                else if (typeCode == GetTypeCode(typeof(MediaBrowser.Controller.Entities.Game))) { counts.GameCount = value; }
                else if (typeCode == GetTypeCode(typeof(Trailer))) { counts.TrailerCount = value; }

                counts.ItemCount += value;
            }

            return counts;
        }

        // ===========================================================
        // item_extradata 写入（旧 GetItemValuesToSave/UpdateItemValues/InsertItemValues）
        // ===========================================================

        /// <summary>
        /// 待保存的 extradata 值（旧 GetItemValuesToSave：0=Artists 1=AlbumArtists 2=Genres 3=Studios 4=Tags 6=InheritedTags）。
        /// </summary>
        internal static List<Tuple<int, string>> GetItemValuesToSave(BaseItem item, List<string> inheritedTags)
        {
            var list = new List<Tuple<int, string>>();

            var hasArtist = item as IHasArtist;
            if (hasArtist != null)
            {
                list.AddRange(hasArtist.Artists.Select(i => new Tuple<int, string>(0, i)));
            }

            var hasAlbumArtist = item as IHasAlbumArtist;
            if (hasAlbumArtist != null)
            {
                list.AddRange(hasAlbumArtist.AlbumArtists.Select(i => new Tuple<int, string>(1, i)));
            }

            list.AddRange(item.Genres.Select(i => new Tuple<int, string>(2, i)));
            list.AddRange(item.Studios.Select(i => new Tuple<int, string>(3, i)));
            list.AddRange(item.Tags.Select(i => new Tuple<int, string>(4, i)));

            // keywords was 5

            list.AddRange(inheritedTags.Select(i => new Tuple<int, string>(6, i)));

            return list;
        }

    }
}
