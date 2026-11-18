using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.PgData;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// 批量写入正确性集成测试（对应评审 P0-1 / P0-2）。
    ///
    /// 说明：P0-1 直接驱动生产 UPSERT 的 SQL 形态（INSERT ... SELECT FROM UNNEST ...
    /// ON CONFLICT ... RETURNING guid, id）；P0-2 及单阶段写入用例调用真实
    /// PgItemRepository.SaveItemsAsync（生产 schema），验证父项/系列内联解析、
    /// 跨块父子引用与 ancestor_ids 层级 distance。
    /// </summary>
    [TestClass]
    public class BatchWriteIntegrationTests : ProductionSchemaTestBase
    {
        private const string UpsertSql = @"
            INSERT INTO media_items (guid, type, type_name, name, sort_name, path,
                                     production_year, community_rating, clean_name,
                                     presentation_unique_key, is_folder, is_virtual_item)
            SELECT u.guid, u.type, u.type_name, u.name, u.name, u.path,
                   u.year, u.rating, u.name, u.puk, u.is_folder, FALSE
            FROM UNNEST(@guid, @type, @type_name, @name, @path, @year, @rating, @puk, @is_folder)
                 AS u(guid, type, type_name, name, path, year, rating, puk, is_folder)
            ON CONFLICT (guid, type) DO UPDATE SET
                type_name = EXCLUDED.type_name,
                name = EXCLUDED.name,
                sort_name = EXCLUDED.sort_name,
                path = EXCLUDED.path,
                production_year = EXCLUDED.production_year,
                community_rating = EXCLUDED.community_rating,
                clean_name = EXCLUDED.clean_name,
                presentation_unique_key = EXCLUDED.presentation_unique_key,
                is_folder = EXCLUDED.is_folder
            RETURNING guid, id";

        /// <summary>
        /// P0-1：250 条混合（100 条已存在走 UPDATE + 150 条新插入），
        /// RETURNING guid,id 必须让每条 guid 都映射到正确的 id。
        /// 用 upsert 返回的映射与独立 SELECT 逐一比对。
        /// </summary>
        [TestMethod]
        public async Task P0_1_BatchUpsert_ReturningGuidId_IsCorrect_ForMixedInsertAndUpdate()
        {
            const int existing = 100;
            const int fresh = 150;
            const int total = existing + fresh;

            // ---- 预置 100 条已存在行 ----
            var preExistingGuids = Enumerable.Range(0, existing)
                .Select(i => Guid.NewGuid())
                .ToArray();

            await using (var conn = await OpenAsync())
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO media_items (guid, type, name, sort_name) " +
                "SELECT u.g, 2, u.n, u.n FROM UNNEST(@g, @n) AS u(g, n)", conn))
            {
                cmd.Parameters.Add(new NpgsqlParameter("g", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = preExistingGuids });
                cmd.Parameters.Add(new NpgsqlParameter("n", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = Enumerable.Range(0, existing).Select(i => "old-" + i).ToArray()
                });
                await cmd.ExecuteNonQueryAsync();
            }

            // 记录预置行的 id，稍后验证 UPDATE 不改 id
            var originalIds = new Dictionary<Guid, long>();
            await using (var conn = await OpenAsync())
            await using (var cmd = new NpgsqlCommand("SELECT guid, id FROM media_items", conn))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    originalIds[r.GetGuid(0)] = r.GetInt64(1);
                }
            }

            Assert.AreEqual(existing, originalIds.Count, "预置行数不符");

            // ---- 构造混合批次：前 100 条已存在（UPDATE），后 150 条新（INSERT）----
            var allGuids = preExistingGuids
                .Concat(Enumerable.Range(0, fresh).Select(_ => Guid.NewGuid()))
                .ToArray();

            // 故意打乱顺序，模拟 PostgreSQL 不保证 RETURNING 行序
            var shuffled = allGuids.OrderBy(_ => Guid.NewGuid()).ToArray();

            var returned = new Dictionary<Guid, long>();
            await using (var conn = await OpenAsync())
            await using (var cmd = new NpgsqlCommand(UpsertSql, conn))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = shuffled });
                cmd.Parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                {
                    Value = Enumerable.Repeat(2, total).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("type_name", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = Enumerable.Repeat("Movie", total).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = Enumerable.Range(0, total).Select(i => "new-" + i).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("path", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = Enumerable.Range(0, total).Select(i => "/m/" + i + ".mkv").ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("year", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                {
                    Value = Enumerable.Range(0, total).Select(i => (object)(2000 + (i % 20))).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("rating", NpgsqlDbType.Array | NpgsqlDbType.Double)
                {
                    Value = Enumerable.Range(0, total).Select(i => (object)(5.0 + (i % 5))).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("puk", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = Enumerable.Range(0, total).Select(i => "puk-" + i).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("is_folder", NpgsqlDbType.Array | NpgsqlDbType.Boolean)
                {
                    Value = Enumerable.Repeat(false, total).ToArray()
                });

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    returned[reader.GetGuid(0)] = reader.GetInt64(1);
                }
            }

            Assert.AreEqual(total, returned.Count, "RETURNING 行数与输入不一致");

            // ---- 断言：每条 guid 的映射 == 数据库真实 id ----
            var actual = new Dictionary<Guid, long>();
            await using (var conn = await OpenAsync())
            await using (var cmd = new NpgsqlCommand(
                "SELECT guid, id FROM media_items WHERE guid = ANY(@g)", conn))
            {
                cmd.Parameters.Add(new NpgsqlParameter("g", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = allGuids });
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    actual[r.GetGuid(0)] = r.GetInt64(1);
                }
            }

            Assert.AreEqual(total, actual.Count, "数据库实际行数不符");

            foreach (var g in allGuids)
            {
                Assert.IsTrue(returned.ContainsKey(g), $"RETURNING 缺少 guid {g}");
                Assert.IsTrue(actual.ContainsKey(g), $"数据库缺少 guid {g}");
                Assert.AreEqual(actual[g], returned[g],
                    $"guid {g} 的 RETURNING id 与数据库不一致（下标错位回归）");
            }

            // ---- 断言：已存在行的 id 未被 UPDATE 改写 ----
            foreach (var g in preExistingGuids)
            {
                Assert.AreEqual(originalIds[g], returned[g],
                    $"已存在行 {g} 的 id 在 UPDATE 后发生变化");
            }
        }

        /// <summary>
        /// P0-2：子项 GUID 排序在前、父项排序在后，跨 chunk（每块 250，共 300 条）。
        /// 真实 SaveItemsAsync 只调用一次，单阶段写入后 parent_id 必须正确，
        /// ancestor_ids 层级必须正确——不存在独立的回填步骤。
        /// </summary>
        [TestMethod]
        public async Task P0_2_CrossChunkParentAndAncestors_AreFullyResolved()
        {
            await EnsureProductionSchemaAsync().ConfigureAwait(false);
            EnsureRepositoryStatics();

            const int childCount = 290;
            var rootGuid = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
            var folderGuid = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
            var childGuids = Enumerable.Range(0, childCount)
                .Select(i => Guid.Parse((i + 1).ToString("x8") + "-0000-0000-0000-000000000000"))
                .ToArray();

            Assert.IsTrue(childGuids.All(g => g.CompareTo(folderGuid) < 0), "子项 GUID 必须小于父项");

            // 子项 GUID 小于父项，且子项数量超过一个 chunk：父项先落库，
            // 部分子项与父项处于不同 chunk（250 行一块）。
            var items = childGuids.Select(g => NewSeason(g, folderGuid))
                .Concat(new[] { NewSeason(folderGuid, rootGuid), NewSeason(rootGuid, Guid.Empty) })
                .ToList();

            await CreateRepository().SaveItemsAsync(items, CancellationToken.None).ConfigureAwait(false);

            var stored = await ReadReferencesAsync(childGuids.Concat(new[] { folderGuid, rootGuid }).ToArray()).ConfigureAwait(false);
            var folderId = stored[folderGuid].Id;
            var rootId = stored[rootGuid].Parent;

            Assert.IsNull(rootId, "真正无父项的 root 必须保持 parent_id IS NULL");
            Assert.AreEqual(stored[rootGuid].Id, stored[folderGuid].Parent, "folder 的 parent_id 必须指向 root");
            foreach (var child in childGuids)
            {
                Assert.AreEqual(folderId, stored[child].Parent, $"交叉块子项 {child} 的 parent_id 未解析");
            }

            // 祖先闭包：每个 child 应有 folder(distance=1) 和 root(distance=2)。
            await using var conn = await OpenAsync().ConfigureAwait(false);
            await using (var cmd = new NpgsqlCommand(@"
                SELECT count(*) FROM (
                    SELECT a.item_id, count(*) AS c, min(a.distance) AS dmin, max(a.distance) AS dmax
                    FROM ancestor_ids a
                    JOIN media_items m ON m.id = a.item_id
                    WHERE m.guid = ANY(@guids)
                    GROUP BY a.item_id
                ) t WHERE t.c <> 2 OR t.dmin <> 1 OR t.dmax <> 2", conn))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
                {
                    Value = childGuids
                });
                Assert.AreEqual(0L, Convert.ToInt64(await cmd.ExecuteScalarAsync().ConfigureAwait(false)),
                    "祖先闭包行数或 distance 层级不正确");
            }
        }

        /// <summary>
        /// 顺序无关性：同一个批次（同一 chunk）内父项 GUID 排在子项之后，
        /// 且 series GUID 也排在子项之后。单阶段写入必须靠拓扑顺序让两者先落库。
        /// </summary>
        [TestMethod]
        public async Task SaveItems_ParentAndSeriesAfterChildInBatch_StillResolve()
        {
            await EnsureProductionSchemaAsync().ConfigureAwait(false);
            EnsureRepositoryStatics();

            var childGuid = Guid.Parse("00000001-0000-0000-0000-000000000001");
            var parentGuid = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
            var seriesGuid = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
            var rootGuid = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");

            // 故意以 child → series → parent → root 的顺序传入（父项全部在后）。
            var items = new[]
            {
                NewSeason(childGuid, parentGuid, seriesGuid),
                NewSeason(seriesGuid, rootGuid),
                NewSeason(parentGuid, rootGuid),
                NewSeason(rootGuid, Guid.Empty)
            };

            await CreateRepository().SaveItemsAsync(items.ToList(), CancellationToken.None).ConfigureAwait(false);

            var stored = await ReadReferencesAsync(new[] { childGuid, parentGuid, seriesGuid, rootGuid }).ConfigureAwait(false);
            Assert.AreEqual(stored[parentGuid].Id, stored[childGuid].Parent, "同批次父项出现在子项之后时 parent_id 未解析");
            Assert.AreEqual(stored[seriesGuid].Id, stored[childGuid].Series, "同批次 series 出现在子项之后时 series_id 未解析");
            Assert.AreEqual(stored[rootGuid].Id, stored[parentGuid].Parent, "中间父项的 parent_id 未解析");
            Assert.IsNull(stored[rootGuid].Parent, "root 必须保持 parent_id IS NULL");
        }

        /// <summary>
        /// 崩溃窗口属性（用「一次调用即完整」来模拟，不真的杀进程）：
        /// 跨多个 chunk 的单次 SaveItemsAsync 返回后，凡是提供了父项 GUID 的行
        /// 都不能留下 parent_id IS NULL；只有真正无父项的条目允许为 NULL。
        /// </summary>
        [TestMethod]
        public async Task SaveItems_SingleCall_LeavesNoNullParentIdForProvidedParent()
        {
            await EnsureProductionSchemaAsync().ConfigureAwait(false);
            EnsureRepositoryStatics();

            const int childCount = 300;
            var rootGuid = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
            var virtualGuid = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var childGuids = Enumerable.Range(0, childCount)
                .Select(i => Guid.Parse((i + 2).ToString("x8") + "-0000-0000-0000-000000000000"))
                .ToArray();

            var items = childGuids.Select(g => NewSeason(g, rootGuid))
                .Concat(new[] { NewSeason(rootGuid, Guid.Empty), NewSeason(virtualGuid, Guid.Empty) })
                .ToList();

            await CreateRepository().SaveItemsAsync(items, CancellationToken.None).ConfigureAwait(false);

            await using (var conn = await OpenAsync().ConfigureAwait(false))
            {
                await using (var cmd = new NpgsqlCommand(@"
                    SELECT count(*) FROM media_items
                    WHERE parent_id IS NULL AND guid NOT IN (@virtual, @root)", conn))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("virtual", NpgsqlDbType.Uuid) { Value = virtualGuid });
                    cmd.Parameters.Add(new NpgsqlParameter("root", NpgsqlDbType.Uuid) { Value = rootGuid });
                    Assert.AreEqual(0L, Convert.ToInt64(await cmd.ExecuteScalarAsync().ConfigureAwait(false)),
                        "一次保存后仍有提供了父项 GUID 却 parent_id IS NULL 的行（崩溃窗口回归）");
                }

                await using (var cmd = new NpgsqlCommand(@"
                    SELECT count(*) FROM media_items m
                    WHERE m.guid = ANY(@guids)
                      AND m.parent_id = (SELECT p.id FROM media_items p WHERE p.guid = @root)", conn))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = childGuids });
                    cmd.Parameters.Add(new NpgsqlParameter("root", NpgsqlDbType.Uuid) { Value = rootGuid });
                    Assert.AreEqual((long)childCount, Convert.ToInt64(await cmd.ExecuteScalarAsync().ConfigureAwait(false)),
                        "跨块子项的 parent_id 未全部指向 root");
                }
            }
        }

        private static MediaBrowser.Controller.Entities.BaseItem NewSeason(Guid id, Guid parentGuid, Guid seriesGuid = default)
        {
            return new MediaBrowser.Controller.Entities.TV.Season
            {
                Id = id,
                ParentId = parentGuid,
                SeriesId = seriesGuid,
                Name = "item-" + id.ToString("N"),
                Path = "/p/" + id.ToString("N"),
                DateCreated = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DateModified = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
        }

        private PgItemRepository CreateRepository()
        {
            var logger = new MediaBrowser.Model.Logging.NullLogger();
            var envInfo = new Emby.Server.Implementations.EnvironmentInfo.EnvironmentInfo();
            var fs = new Emby.Server.Implementations.IO.ManagedFileSystem(
                logger, envInfo, ".", System.IO.Path.GetTempPath(), false);

            return new PgItemRepository(
                new FakeConfigManager(),
                new FakeAppHost(),
                new Emby.Common.Implementations.Serialization.JsonSerializer(fs, logger),
                logger,
                new Emby.Server.Implementations.Reflection.AssemblyInfo(),
                fs,
                TestConnectionString);
        }

        /// <summary>
        /// BuildBatchRow 会调用 GetTopParent/GetInheritedTags，它们经由静态 LibraryManager
        /// 回溯父项；测试里只需让它返回 null（引用解析由 SQL 完成，不依赖内存对象图）。
        /// </summary>
        private static void EnsureRepositoryStatics()
        {
            MediaBrowser.Controller.Extensions.StringExtensions.LocalizationManager = new FakeLocalizationManager();
            MediaBrowser.Controller.Entities.BaseItem.LibraryManager =
                System.Reflection.DispatchProxy.Create<MediaBrowser.Controller.Library.ILibraryManager, NullLibraryManagerProxy>();
        }

        private class NullLibraryManagerProxy : System.Reflection.DispatchProxy
        {
            protected override object Invoke(System.Reflection.MethodInfo targetMethod, object[] args) => null;
        }

        private async Task<Dictionary<Guid, (long Id, long? Parent, long? Series)>> ReadReferencesAsync(Guid[] guids)
        {
            var result = new Dictionary<Guid, (long, long?, long?)>();
            await using var conn = await OpenAsync().ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("SELECT guid, id, parent_id, series_id FROM media_items WHERE guid = ANY(@guids)", conn);
            cmd.Parameters.Add(new NpgsqlParameter("guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = guids });
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result[reader.GetGuid(0)] = (
                    reader.GetInt64(1),
                    await reader.IsDBNullAsync(2).ConfigureAwait(false) ? null : reader.GetInt64(2),
                    await reader.IsDBNullAsync(3).ConfigureAwait(false) ? null : reader.GetInt64(3));
            }

            return result;
        }

        /// <summary>
        /// P1-5：SaveAllUserData 的 UNNEST UPSERT 必须正确处理
        /// null rating / null lastPlayedDate（object[] 内装 DBNull 的路径）。
        /// </summary>
        [TestMethod]
        public async Task SaveAllUserData_HandlesNullRatingAndNullLastPlayedDate()
        {
            int keyId;
            await using (var conn = await OpenAsync())
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO user_data_keys (user_data_key) VALUES ('k-null-test') RETURNING id", conn))
            {
                keyId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            const string sql = @"
                INSERT INTO user_datas
                    (user_data_key_id, user_id, rating, played, play_count, is_favorite,
                     playback_position_ticks, last_played_date, audio_stream_index, subtitle_stream_index)
                SELECT t.k_id, t.u_id, t.r, t.p, t.pc, t.fav, t.pos, t.lpd, t.asi, t.ssi
                FROM UNNEST(@k_ids, @u_ids, @rs, @ps, @pcs, @favs, @poss, @lpds, @asis, @ssis)
                     AS t(k_id, u_id, r, p, pc, fav, pos, lpd, asi, ssi)
                ON CONFLICT (user_data_key_id, user_id) DO UPDATE SET
                    rating = EXCLUDED.rating,
                    played = EXCLUDED.played,
                    play_count = EXCLUDED.play_count,
                    is_favorite = EXCLUDED.is_favorite,
                    playback_position_ticks = EXCLUDED.playback_position_ticks,
                    last_played_date = EXCLUDED.last_played_date,
                    audio_stream_index = EXCLUDED.audio_stream_index,
                    subtitle_stream_index = EXCLUDED.subtitle_stream_index,
                    playstate_last_modified = now()";

            // 第一轮：全 null 可空列（rating / lastPlayedDate / audio / subtitle）
            await using (var conn = await OpenAsync())
            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.Add(new NpgsqlParameter("k_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = new[] { keyId } });
                cmd.Parameters.Add(new NpgsqlParameter("u_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = new[] { 1 } });
                cmd.Parameters.Add(new NpgsqlParameter("rs", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = new object[] { DBNull.Value } });
                cmd.Parameters.Add(new NpgsqlParameter("ps", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = new[] { true } });
                cmd.Parameters.Add(new NpgsqlParameter("pcs", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = new[] { 3 } });
                cmd.Parameters.Add(new NpgsqlParameter("favs", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = new[] { true } });
                cmd.Parameters.Add(new NpgsqlParameter("poss", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = new[] { 12345L } });
                cmd.Parameters.Add(new NpgsqlParameter("lpds", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = new object[] { DBNull.Value } });
                cmd.Parameters.Add(new NpgsqlParameter("asis", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = new object[] { DBNull.Value } });
                cmd.Parameters.Add(new NpgsqlParameter("ssis", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = new object[] { DBNull.Value } });
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var conn = await OpenAsync())
            await using (var cmd = new NpgsqlCommand(
                "SELECT rating, last_played_date, audio_stream_index, subtitle_stream_index, play_count " +
                "FROM user_datas WHERE user_data_key_id = @k AND user_id = 1", conn))
            {
                cmd.Parameters.Add(new NpgsqlParameter("k", NpgsqlDbType.Integer) { Value = keyId });
                await using var r = await cmd.ExecuteReaderAsync();
                Assert.IsTrue(await r.ReadAsync(), "首轮 UPSERT 未写入行");
                Assert.IsTrue(await r.IsDBNullAsync(0).ConfigureAwait(false), "rating 应为 NULL");
                Assert.IsTrue(await r.IsDBNullAsync(1).ConfigureAwait(false), "last_played_date 应为 NULL");
                Assert.IsTrue(await r.IsDBNullAsync(2).ConfigureAwait(false), "audio_stream_index 应为 NULL");
                Assert.IsTrue(await r.IsDBNullAsync(3).ConfigureAwait(false), "subtitle_stream_index 应为 NULL");
                Assert.AreEqual(3, r.GetInt32(4));
            }

            // 第二轮：UPDATE 路径把 null 覆盖为具体值
            await using (var conn = await OpenAsync())
            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.Add(new NpgsqlParameter("k_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = new[] { keyId } });
                cmd.Parameters.Add(new NpgsqlParameter("u_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = new[] { 1 } });
                cmd.Parameters.Add(new NpgsqlParameter("rs", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = new object[] { 9.5 } });
                cmd.Parameters.Add(new NpgsqlParameter("ps", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = new[] { false } });
                cmd.Parameters.Add(new NpgsqlParameter("pcs", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = new[] { 7 } });
                cmd.Parameters.Add(new NpgsqlParameter("favs", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = new[] { false } });
                cmd.Parameters.Add(new NpgsqlParameter("poss", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = new[] { 0L } });
                cmd.Parameters.Add(new NpgsqlParameter("lpds", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
                {
                    Value = new object[] { DateTime.UtcNow }
                });
                cmd.Parameters.Add(new NpgsqlParameter("asis", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = new object[] { (short)2 } });
                cmd.Parameters.Add(new NpgsqlParameter("ssis", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = new object[] { (short)5 } });
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var conn = await OpenAsync())
            await using (var cmd = new NpgsqlCommand(
                "SELECT rating, last_played_date, audio_stream_index, subtitle_stream_index, play_count, played " +
                "FROM user_datas WHERE user_data_key_id = @k AND user_id = 1", conn))
            {
                cmd.Parameters.Add(new NpgsqlParameter("k", NpgsqlDbType.Integer) { Value = keyId });
                await using var r = await cmd.ExecuteReaderAsync();
                Assert.IsTrue(await r.ReadAsync());
                Assert.AreEqual(9.5, r.GetDouble(0), 1e-9);
                Assert.IsFalse(await r.IsDBNullAsync(1).ConfigureAwait(false), "last_played_date 应已被覆盖");
                Assert.AreEqual((short)2, r.GetInt16(2));
                Assert.AreEqual((short)5, r.GetInt16(3));
                Assert.AreEqual(7, r.GetInt32(4));
                Assert.IsFalse(r.GetBoolean(5));
            }

            // 断言只有一行（UPSERT 而非重复插入）
            await using (var conn = await OpenAsync())
            await using (var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM user_datas WHERE user_data_key_id = @k AND user_id = 1", conn))
            {
                cmd.Parameters.Add(new NpgsqlParameter("k", NpgsqlDbType.Integer) { Value = keyId });
                Assert.AreEqual(1L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));
            }
        }
    }
}
