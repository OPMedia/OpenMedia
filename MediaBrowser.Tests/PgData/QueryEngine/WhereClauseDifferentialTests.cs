using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// WHERE-branch differential tests (Task B / ADR-0016 divergence table).
    /// One test method per branch family; each case asserts
    /// (a) the real generated predicate fragment, and
    /// (b) the real generated row set versus the old SQLite rendering on the identical fixture.
    /// Branch ids match the W-* rows of the ADR divergence table.
    /// </summary>
    [TestClass]
    public class WhereClauseDifferentialTests : QueryDiffTestBase
    {
        // ===========================================================
        // 分辨率 / HD / 4K 分支（旧 3556-3614；新 636-681）
        // ===========================================================
        [TestMethod]
        public async Task Diff_ResolutionBranches()
        {
            await SeedDiffFixtureAsync().ConfigureAwait(false);

            // 旧: IsHD=true -> Width>=1200
            await RunDiffAsync(new DiffCase(
                "W-IsHD-true", "IsHD=true emits width >= 1200",
                q => q.IsHD = true,
                "A.width >= @MinWidth",
                "SELECT name FROM media_items WHERE width >= 1200 ORDER BY name")).ConfigureAwait(false);

            // 旧: IsHD=false -> Width<1200
            await RunDiffAsync(new DiffCase(
                "W-IsHD-false", "IsHD=false emits width <= 1199",
                q => q.IsHD = false,
                "A.width <= @MaxWidth",
                "SELECT name FROM media_items WHERE width <= 1199 ORDER BY name")).ConfigureAwait(false);

            // 旧: Is4K=true -> Width>=3800
            await RunDiffAsync(new DiffCase(
                "W-Is4K-true", "Is4K=true emits width >= 3800",
                q => q.Is4K = true,
                "A.width >= @MinWidth",
                "SELECT name FROM media_items WHERE width >= 3800 ORDER BY name")).ConfigureAwait(false);

            // 旧: MinWidth 直通
            await RunDiffAsync(new DiffCase(
                "W-MinWidth", "MinWidth direct threshold",
                q => q.MinWidth = 1300,
                "A.width >= @MinWidth",
                "SELECT name FROM media_items WHERE width >= 1300 ORDER BY name")).ConfigureAwait(false);

            // 旧: MaxWidth 直通
            await RunDiffAsync(new DiffCase(
                "W-MaxWidth", "MaxWidth direct threshold",
                q => q.MaxWidth = 1300,
                "A.width <= @MaxWidth",
                "SELECT name FROM media_items WHERE width <= 1300 ORDER BY name")).ConfigureAwait(false);

            // 旧: MinHeight / MaxHeight 直通（新实现同样保留）
            await RunDiffAsync(new DiffCase(
                "W-MinHeight", "MinHeight direct threshold",
                q => q.MinHeight = 400,
                "A.height >= @MinHeight",
                "SELECT name FROM media_items WHERE height >= 400 ORDER BY name")).ConfigureAwait(false);
        }

        // ===========================================================
        // 布尔旗标分支（旧 3615-3760；新 683-780）
        // ===========================================================
        [TestMethod]
        public async Task Diff_BooleanFlagBranches()
        {
            await SeedDiffFixtureAsync().ConfigureAwait(false);

            // 旧: IsLocked=@IsLocked ; 新: A.is_locked = @IsLocked
            await RunDiffAsync(new DiffCase(
                "W-IsLocked", "IsLocked=true",
                q => q.IsLocked = true,
                "A.is_locked = @IsLocked",
                "SELECT name FROM media_items WHERE is_locked = TRUE ORDER BY name")).ConfigureAwait(false);

            // 旧: IsFolder=@IsFolder ; 新: A.is_folder = @IsFolder
            await RunDiffAsync(new DiffCase(
                "W-IsFolder", "IsFolder=true",
                q => q.IsFolder = true,
                "A.is_folder = @IsFolder",
                "SELECT name FROM media_items WHERE is_folder = TRUE ORDER BY name")).ConfigureAwait(false);

            // 旧: IsSeries=@IsSeries ; 新: A.is_series = @IsSeries
            await RunDiffAsync(new DiffCase(
                "W-IsSeries", "IsSeries=true",
                q => q.IsSeries = true,
                "A.is_series = @IsSeries",
                "SELECT name FROM media_items WHERE is_series = TRUE ORDER BY name")).ConfigureAwait(false);

            // 旧: IsMovie=true -> IsMovie=@IsMovie（有 Movie/Trailer 备选时放宽为 null OR =1）
            // 新: (A.is_movie IS NULL OR A.is_movie = @IsMovie) —— 语义一致
            await RunDiffAsync(new DiffCase(
                "W-IsMovie", "IsMovie=true relaxed null-tolerant form",
                q => q.IsMovie = true,
                "A.is_movie = @IsMovie",
                "SELECT name FROM media_items WHERE (is_movie IS NULL OR is_movie = TRUE) ORDER BY name")).ConfigureAwait(false);

            // 旧: IsVirtualItem=@IsVirtualItem（IsVirtualItem ?? IsMissing）
            // 新: A.is_virtual_item = @IsVirtualItem —— 同一回退链
            await RunDiffAsync(new DiffCase(
                "W-IsVirtualItem", "IsVirtualItem falls back to IsMissing",
                q => q.IsMissing = true,
                "A.is_virtual_item = @IsVirtualItem",
                "SELECT name FROM media_items WHERE is_virtual_item = TRUE ORDER BY name")).ConfigureAwait(false);
        }

        // ===========================================================
        // 名称 / 排序分支（旧 4069-4145；新 979-1035）
        // ===========================================================
        [TestMethod]
        public async Task Diff_NameAndSortNameBranches()
        {
            await SeedDiffFixtureAsync().ConfigureAwait(false);

            // 旧: Name=@Name（GetCleanValue 归一后比较 CleanName）
            await RunDiffAsync(new DiffCase(
                "W-Name", "Name exact match on clean_name",
                q => q.Name = "item 5",
                "A.clean_name = @Name",
                "SELECT name FROM media_items WHERE clean_name = 'item 5' ORDER BY name")).ConfigureAwait(false);

            // 旧 4111: "(CleanName like @NameContains or OriginalTitle like @NameContains)"
            // 新: "(A.clean_name LIKE @NameContains OR A.original_title LIKE @NameContains)" —— 忠实翻译，非差异。
            // 两侧都必须在同一 fixture 上跑：original_title 保留原始大小写，clean_name 经 GetCleanValue 小写化，
            // 若只在 PG 侧或只按 clean_name 断言，会把方言大小写敏感性误判成实现差异。
            await RunDiffAsync(new DiffCase(
                "W-NameContains", "NameContains matches clean_name OR original_title",
                q => q.NameContains = "item",
                "A.clean_name LIKE @NameContains",
                "SELECT name FROM media_items WHERE (clean_name LIKE '%item%' OR original_title LIKE '%item%') ORDER BY name")).ConfigureAwait(false);

            // 旧: SortName LIKE @NameStartsWith+"%"
            await RunDiffAsync(new DiffCase(
                "W-NameStartsWith", "NameStartsWith on sort_name",
                q => q.NameStartsWith = "item-1",
                "A.sort_name LIKE @NameStartsWith",
                "SELECT name FROM media_items WHERE sort_name LIKE 'item-1%' ORDER BY name")).ConfigureAwait(false);
            // 旧: SortName>=@NameStartsWithOrGreater
            await RunDiffAsync(new DiffCase(
                "W-NameStartsWithOrGreater", "NameStartsWithOrGreater on sort_name",
                q => q.NameStartsWithOrGreater = "item-020",
                "A.sort_name >= @NameStartsWithOrGreater",
                "SELECT name FROM media_items WHERE sort_name >= 'item-020' ORDER BY name")).ConfigureAwait(false);

            // 旧: SortName<@NameLessThan
            await RunDiffAsync(new DiffCase(
                "W-NameLessThan", "NameLessThan on sort_name",
                q => q.NameLessThan = "item-010",
                "A.sort_name < @NameLessThan",
                "SELECT name FROM media_items WHERE sort_name < 'item-010' ORDER BY name")).ConfigureAwait(false);

            // 旧: MinSortName=@MinSortName
            await RunDiffAsync(new DiffCase(
                "W-MinSortName", "MinSortName on sort_name",
                q => q.MinSortName = "item-030",
                "A.sort_name >= @MinSortName",
                "SELECT name FROM media_items WHERE sort_name >= 'item-030' ORDER BY name")).ConfigureAwait(false);
        }

        // ===========================================================
        // 评分 / 索引号 / 日期分支（旧 3843-4000；新 837-945）
        // ===========================================================
        [TestMethod]
        public async Task Diff_RatingIndexAndDateBranches()
        {
            await SeedDiffFixtureAsync().ConfigureAwait(false);

            // 旧: CommunityRating>=@MinCommunityRating
            await RunDiffAsync(new DiffCase(
                "W-MinCommunityRating", "MinCommunityRating threshold",
                q => q.MinCommunityRating = 2.0,
                "A.community_rating >= @MinCommunityRating",
                "SELECT name FROM media_items WHERE community_rating >= 2.0 ORDER BY name")).ConfigureAwait(false);

            // 旧: OfficialRating in (...) OR 列表
            await RunDiffAsync(new DiffCase(
                "W-OfficialRatings", "OfficialRatings OR list",
                q => q.OfficialRatings = new[] { "G", "R" },
                "A.official_rating = @OfficialRating0",
                "SELECT name FROM media_items WHERE official_rating IN ('G','R') ORDER BY name")).ConfigureAwait(false);

            // 旧: HasOfficialRating -> OfficialRating is not null
            await RunDiffAsync(new DiffCase(
                "W-HasOfficialRating", "HasOfficialRating=true",
                q => q.HasOfficialRating = true,
                "A.official_rating IS NOT NULL",
                "SELECT name FROM media_items WHERE official_rating IS NOT NULL ORDER BY name")).ConfigureAwait(false);

            // 旧: IndexNumber>=@MinIndexNumber
            await RunDiffAsync(new DiffCase(
                "W-MinIndexNumber", "MinIndexNumber threshold",
                q => q.MinIndexNumber = 2,
                "A.index_number >= @MinIndexNumber",
                "SELECT name FROM media_items WHERE index_number >= 2 ORDER BY name")).ConfigureAwait(false);

            // 旧: IndexNumber=@IndexNumber
            await RunDiffAsync(new DiffCase(
                "W-IndexNumber", "IndexNumber equality",
                q => q.IndexNumber = 3,
                "A.index_number = @IndexNumber",
                "SELECT name FROM media_items WHERE index_number = 3 ORDER BY name")).ConfigureAwait(false);

            // 旧: ParentIndexNumber=@ParentIndexNumber
            await RunDiffAsync(new DiffCase(
                "W-ParentIndexNumber", "ParentIndexNumber equality",
                q => q.ParentIndexNumber = 2,
                "A.parent_index_number = @ParentIndexNumber",
                "SELECT name FROM media_items WHERE parent_index_number = 2 ORDER BY name")).ConfigureAwait(false);

            // 旧: IsSpecialSeason -> IndexNumber = 0 / <> 0
            await RunDiffAsync(new DiffCase(
                "W-IsSpecialSeason", "IsSpecialSeason=true: index_number = 0",
                q => q.IsSpecialSeason = true,
                "A.index_number = 0",
                "SELECT name FROM media_items WHERE index_number = 0 ORDER BY name")).ConfigureAwait(false);

            // 旧: PremiereDate>=@MinPremiereDate
            await RunDiffAsync(new DiffCase(
                "W-MinPremiereDate", "MinPremiereDate threshold",
                q => q.MinPremiereDate = new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                "A.premiere_date >= @MinPremiereDate",
                "SELECT name FROM media_items WHERE premiere_date >= '2020-06-01' ORDER BY name")).ConfigureAwait(false);

            // 旧: PremiereDate<=@MaxPremiereDate
            await RunDiffAsync(new DiffCase(
                "W-MaxPremiereDate", "MaxPremiereDate threshold",
                q => q.MaxPremiereDate = new DateTime(2020, 6, 15, 0, 0, 0, DateTimeKind.Utc),
                "A.premiere_date <= @MaxPremiereDate",
                "SELECT name FROM media_items WHERE premiere_date <= '2020-06-15' ORDER BY name")).ConfigureAwait(false);

            // 旧: DateCreated>=@MinDateCreated
            await RunDiffAsync(new DiffCase(
                "W-MinDateCreated", "MinDateCreated threshold",
                q => q.MinDateCreated = new DateTime(2023, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                "A.date_created >= @MinDateCreated",
                "SELECT name FROM media_items WHERE date_created >= '2023-01-10' ORDER BY name")).ConfigureAwait(false);
        }

        // ===========================================================
        // extradata（Tags / Genres / Studios）分支（旧 4383-4436；新 1234-1273）
        // ===========================================================
        [TestMethod]
        public async Task Diff_ExtraDataBranches()
        {
            await SeedDiffFixtureAsync().ConfigureAwait(false);

            // 旧: @Tag0 in (select CleanValue from itemvalues where ItemId=Guid and Type=4)
            // 新: @Tag0 IN (SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 4)
            await RunDiffAsync(new DiffCase(
                "W-Tags", "Tags OR list via extradata type 4",
                q => q.Tags = new[] { "Sports" },
                "IN (SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 4)",
                "SELECT m.name FROM media_items m WHERE 'sports' IN (SELECT clean_value FROM item_extradata WHERE item_id = m.id AND extradata_type_id = 4) ORDER BY name")).ConfigureAwait(false);

            // 旧: @ExcludeTag0 not in (... Type=4)
            await RunDiffAsync(new DiffCase(
                "W-ExcludeTags", "ExcludeTags via extradata type 4",
                q => q.ExcludeTags = new[] { "Sports" },
                "NOT IN (SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 4)",
                "SELECT m.name FROM media_items m WHERE 'sports' NOT IN (SELECT clean_value FROM item_extradata WHERE item_id = m.id AND extradata_type_id = 4) ORDER BY name")).ConfigureAwait(false);

            // 旧: @Genre0 in (select CleanValue from itemvalues where ItemId=Guid and Type=2)
            await RunDiffAsync(new DiffCase(
                "W-Genres", "Genres OR list via extradata type 2",
                q => q.Genres = new[] { "Action" },
                "IN (SELECT clean_value FROM item_extradata WHERE item_id = A.id AND extradata_type_id = 2)",
                "SELECT m.name FROM media_items m WHERE 'action' IN (SELECT clean_value FROM item_extradata WHERE item_id = m.id AND extradata_type_id = 2) ORDER BY name")).ConfigureAwait(false);

            // 旧: guid in (select itemid from itemvalues where CleanValue=(select CleanName ...) and Type=3)
            // 新: A.id IN (SELECT iv.item_id FROM item_extradata iv WHERE iv.clean_value = (SELECT clean_name ...) AND iv.extradata_type_id = 3)
            // fixture 没有 Studio 类型的 media_items 行（type_name 集合无 Studio），两侧都空
            await RunDiffAsync(new DiffCase(
                "W-StudioIds", "StudioIds via extradata type 3",
                q => q.StudioIds = new[] { Guid.Parse("00000000-0000-0000-0000-000000000001") },
                "AND iv.extradata_type_id = 3",
                "SELECT m.name FROM media_items m WHERE m.id IN (SELECT iv.item_id FROM item_extradata iv WHERE iv.clean_value = (SELECT clean_name FROM media_items WHERE guid = '00000000-0000-0000-0000-000000000001') AND iv.extradata_type_id = 3) ORDER BY name")).ConfigureAwait(false);
        }

        // ===========================================================
        // 标识 / provider / 键值分支（旧 3815-3842、4696-4835；新 819-837、1490-1585）
        // ===========================================================
        [TestMethod]
        public async Task Diff_IdentifierAndProviderBranches()
        {
            await SeedDiffFixtureAsync().ConfigureAwait(false);

            // 旧: PresentationUniqueKey=@PresentationUniqueKey
            await RunDiffAsync(new DiffCase(
                "W-PresentationUniqueKey", "PresentationUniqueKey equality",
                q => q.PresentationUniqueKey = "puk-3",
                "A.presentation_unique_key = @PresentationUniqueKey",
                "SELECT name FROM media_items WHERE presentation_unique_key = 'puk-3' ORDER BY name")).ConfigureAwait(false);

            // 旧: SeriesPresentationUniqueKey=@SeriesPresentationUniqueKey
            await RunDiffAsync(new DiffCase(
                "W-SeriesPresentationUniqueKey", "SeriesPresentationUniqueKey equality",
                q => q.SeriesPresentationUniqueKey = "spuk-2",
                "A.series_presentation_unique_key = @SeriesPresentationUniqueKey",
                "SELECT name FROM media_items WHERE series_presentation_unique_key = 'spuk-2' ORDER BY name")).ConfigureAwait(false);

            // 旧: ChannelId=@ChannelId（N 格式 GUID 文本）
            // 新: A.channel_id = @ChannelId —— 同格式（fixture 无 channel_id，两侧都空）
            await RunDiffAsync(new DiffCase(
                "W-ChannelIds", "ChannelIds single via channel_id",
                q => q.ChannelIds = new[] { Guid.Parse("00000000-0000-0000-0000-000000000001") },
                "A.channel_id = @ChannelId",
                "SELECT name FROM media_items WHERE channel_id = '00000000000000000000000000000001' ORDER BY name")).ConfigureAwait(false);

            // 旧: ItemId=@ItemId（GUID 等值）
            await RunDiffAsync(new DiffCase(
                "W-ItemIds", "ItemIds OR list on guid",
                q => q.ItemIds = new[] { Guid.Parse("00000000-0000-0000-0000-000000000005") },
                "A.guid = @IncludeId0",
                "SELECT name FROM media_items WHERE guid = '00000000-0000-0000-0000-000000000005' ORDER BY name")).ConfigureAwait(false);

            // 旧: HasImdbId -> data LIKE "%Imdb%"（语义不精确）
            // 新: A.provider_ids->>Imdb IS NOT NULL —— 精确字段级判定
            await RunDiffAsync(new DiffCase(
                "W-HasImdbId", "HasImdbId via jsonb field extraction",
                q => q.HasImdbId = true,
                "A.provider_ids->>'Imdb' IS NOT NULL",
                "SELECT name FROM media_items WHERE provider_ids->>'Imdb' IS NOT NULL ORDER BY name")).ConfigureAwait(false);

            // 旧: HasTmdbId 同理
            await RunDiffAsync(new DiffCase(
                "W-HasTmdbId", "HasTmdbId via jsonb field extraction",
                q => q.HasTmdbId = true,
                "A.provider_ids->>'Tmdb' IS NOT NULL",
                "SELECT name FROM media_items WHERE provider_ids->>'Tmdb' IS NOT NULL ORDER BY name")).ConfigureAwait(false);
        }

        // ===========================================================
        // 其他列存在性 / 年份 / media_type 分支（旧 4496-4680；新 1313-1490）
        // ===========================================================
        [TestMethod]
        public async Task Diff_PresenceAndValueListBranches()
        {
            await SeedDiffFixtureAsync().ConfigureAwait(false);

            // 旧: HasOverview -> Overview is not null and Overview<>''
            // 新: A.overview IS NOT NULL AND A.overview <> ''
            await RunDiffAsync(new DiffCase(
                "W-HasOverview", "HasOverview=true",
                q => q.HasOverview = true,
                "A.overview IS NOT NULL",
                "SELECT name FROM media_items WHERE overview IS NOT NULL AND overview <> '' ORDER BY name")).ConfigureAwait(false);

            // 旧: HasOwnerId -> OwnerId is not null
            await RunDiffAsync(new DiffCase(
                "W-HasOwnerId", "HasOwnerId=true",
                q => q.HasOwnerId = true,
                "A.owner_id IS NOT NULL",
                "SELECT name FROM media_items WHERE owner_id IS NOT NULL ORDER BY name")).ConfigureAwait(false);

            // 旧: ProductionYear=@Years（单值）
            await RunDiffAsync(new DiffCase(
                "W-Years-single", "Years single equality",
                q => q.Years = new[] { 2000 },
                "A.production_year = @Years",
                "SELECT name FROM media_items WHERE production_year = 2000 ORDER BY name")).ConfigureAwait(false);

            // 旧: ProductionYear in (...)（多值）
            await RunDiffAsync(new DiffCase(
                "W-Years-multi", "Years multi IN list",
                q => q.Years = new[] { 2000, 2005 },
                "A.production_year IN (@Years0,@Years1)",
                "SELECT name FROM media_items WHERE production_year IN (2000, 2005) ORDER BY name")).ConfigureAwait(false);

            // 旧: MediaType=@MediaTypes（单值）
            await RunDiffAsync(new DiffCase(
                "W-MediaTypes-single", "MediaTypes single equality",
                q => q.MediaTypes = new[] { "Video" },
                "A.media_type = @MediaTypes",
                "SELECT name FROM media_items WHERE media_type = 'Video' ORDER BY name")).ConfigureAwait(false);

            // 旧: MediaType in (...)（多值）
            await RunDiffAsync(new DiffCase(
                "W-MediaTypes-multi", "MediaTypes multi IN list",
                q => q.MediaTypes = new[] { "Video", "Audio" },
                "A.media_type IN (@MediaTypes0,@MediaTypes1)",
                "SELECT name FROM media_items WHERE media_type IN ('Video','Audio') ORDER BY name")).ConfigureAwait(false);

            // 旧: ExcludeItemTypes -> type<>@type（无 IncludeItemTypes 时生效）
            await RunDiffAsync(new DiffCase(
                "W-ExcludeItemTypes", "ExcludeItemTypes maps to type code exclusion",
                q => q.ExcludeItemTypes = new[] { "Movie" },
                "A.type NOT IN (@ExcludeTypeCode0)",
                "SELECT name FROM media_items WHERE type <> 2 ORDER BY name")).ConfigureAwait(false);
        }
    }
}
