using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// 批量写入正确性集成测试（对应评审 P0-1 / P0-2）。
    ///
    /// 说明：这里直接驱动 PgItemRepository 实际使用的 SQL 形态
    /// （INSERT ... SELECT FROM UNNEST ... ON CONFLICT ... RETURNING guid, id，
    ///  以及全局 parent/series 回填、ancestor_ids 闭包写入），
    /// 在真实 PostgreSQL 上验证行序、跨块父子引用与层级 distance。
    /// </summary>
    [TestClass]
    public class BatchWriteIntegrationTests : PgTestBase
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
        /// 全局两阶段写入后，parent_id 必须非空，ancestor_ids 层级必须正确。
        /// </summary>
        [TestMethod]
        public async Task P0_2_CrossChunkParentAndAncestors_AreFullyResolved()
        {
            // 构造 300 条：子项 GUID 全部小于父项 GUID，保证按 Guid 升序分块时
            // 子项必然落在第 1 块、父项落在第 2 块。
            const int childCount = 290;
            var rootGuid = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");   // 最大，落在最后
            var folderGuid = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"); // 次大

            var childGuids = Enumerable.Range(0, childCount)
                .Select(i => Guid.Parse((i + 1).ToString("x8") + "-0000-0000-0000-000000000000"))
                .ToArray();

            Assert.IsTrue(childGuids.All(g => g.CompareTo(folderGuid) < 0), "子项 GUID 必须小于父项");

            // ---- 阶段 1：分块 upsert（引用列留 NULL，与生产 UpsertItemsChunkAsync 一致）----
            var all = childGuids
                .Select(g => (Guid: g, Type: 2, Name: "child", Parent: folderGuid))
                .Concat(new[] { (Guid: folderGuid, Type: 6, Name: "folder", Parent: rootGuid) })
                .Concat(new[] { (Guid: rootGuid, Type: 6, Name: "root", Parent: Guid.Empty) })
                .OrderBy(x => x.Guid)
                .ToList();

            const int chunkSize = 250;
            for (int start = 0; start < all.Count; start += chunkSize)
            {
                var chunk = all.Skip(start).Take(chunkSize).ToList();
                var n = chunk.Count;

                await using var conn = await OpenAsync();
                await using var cmd = new NpgsqlCommand(UpsertSql, conn);
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
                {
                    Value = chunk.Select(x => x.Guid).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                {
                    Value = chunk.Select(x => x.Type).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("type_name", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = Enumerable.Repeat("Item", n).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = chunk.Select(x => x.Name).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("path", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = chunk.Select(x => "/p/" + x.Guid.ToString("N")).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("year", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                {
                    Value = Enumerable.Repeat((object)DBNull.Value, n).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("rating", NpgsqlDbType.Array | NpgsqlDbType.Double)
                {
                    Value = Enumerable.Repeat((object)DBNull.Value, n).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("puk", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = chunk.Select(x => "puk-" + x.Guid.ToString("N")).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("is_folder", NpgsqlDbType.Array | NpgsqlDbType.Boolean)
                {
                    Value = chunk.Select(x => x.Type == 6).ToArray()
                });

                await cmd.ExecuteNonQueryAsync();
            }

            // ---- 阶段 2：全局 parent_id 回填（所有块已 upsert 完毕）----
            await using (var conn = await OpenAsync())
            await using (var cmd = new NpgsqlCommand(@"
                UPDATE media_items m SET
                    parent_id = CASE WHEN u.pg = '00000000-0000-0000-0000-000000000000'::uuid
                                     THEN NULL
                                     ELSE (SELECT p.id FROM media_items p WHERE p.guid = u.pg LIMIT 1) END
                FROM UNNEST(@guids, @parents) AS u(g, pg)
                WHERE m.guid = u.g", conn))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
                {
                    Value = all.Select(x => x.Guid).ToArray()
                });
                cmd.Parameters.Add(new NpgsqlParameter("parents", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
                {
                    Value = all.Select(x => x.Parent).ToArray()
                });
                await cmd.ExecuteNonQueryAsync();
            }

            // ---- 阶段 3：全局 ancestor_ids 闭包（与生产 RebuildAncestorsAsync 同款递归 CTE）----
            await using (var conn = await OpenAsync())
            await using (var cmd = new NpgsqlCommand(@"
                WITH RECURSIVE walk AS (
                    SELECT m.id AS item_id, m.parent_id AS ancestor_id, 1 AS distance
                    FROM media_items m
                    WHERE m.guid = ANY(@guids) AND m.parent_id IS NOT NULL
                    UNION ALL
                    SELECT w.item_id, p.parent_id, w.distance + 1
                    FROM walk w
                    JOIN media_items p ON p.id = w.ancestor_id
                    WHERE p.parent_id IS NOT NULL AND w.distance < 64
                )
                INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
                SELECT item_id, ancestor_id, min(distance)
                FROM walk
                GROUP BY item_id, ancestor_id
                ON CONFLICT (item_id, ancestor_id) DO UPDATE SET distance = EXCLUDED.distance", conn))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
                {
                    Value = childGuids.Concat(new[] { folderGuid, rootGuid }).ToArray()
                });
                await cmd.ExecuteNonQueryAsync();
            }

            // ---- 断言 1：所有子项 parent_id 非空且指向 folder ----
            await using (var conn = await OpenAsync())
            {
                await using (var cmd = new NpgsqlCommand(@"
                    SELECT count(*) FROM media_items
                    WHERE type = 2 AND (parent_id IS NULL OR parent_id <> (SELECT id FROM media_items WHERE guid = @fg))", conn))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("fg", NpgsqlDbType.Uuid) { Value = folderGuid });
                    var bad = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                    Assert.AreEqual(0L, bad, "存在 parent_id 未回填或回填错误的子项（跨块丢失回归）");
                }

                await using (var cmd = new NpgsqlCommand(
                    "SELECT count(*) FROM media_items WHERE type = 2 AND parent_id IS NOT NULL", conn))
                {
                    var ok = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                    Assert.AreEqual((long)childCount, ok, "parent_id 非空的子项数量不符");
                }

                // ---- 断言 2：每个子项恰好 2 条祖先，distance 分别为 1 和 2 ----
                await using (var cmd = new NpgsqlCommand(@"
                    SELECT count(*) FROM (
                        SELECT a.item_id, count(*) AS c, min(a.distance) AS dmin, max(a.distance) AS dmax
                        FROM ancestor_ids a
                        JOIN media_items m ON m.id = a.item_id
                        WHERE m.type = 2
                        GROUP BY a.item_id
                    ) t
                    WHERE t.c <> 2 OR t.dmin <> 1 OR t.dmax <> 2", conn))
                {
                    var bad = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                    Assert.AreEqual(0L, bad, "祖先闭包行数或 distance 层级不正确");
                }

                await using (var cmd = new NpgsqlCommand(@"
                    SELECT count(*) FROM ancestor_ids a
                    JOIN media_items m ON m.id = a.item_id WHERE m.type = 2", conn))
                {
                    var rows = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                    Assert.AreEqual((long)childCount * 2, rows, "祖先闭包总行数不符");
                }
            }
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
                Assert.IsTrue(r.IsDBNull(0), "rating 应为 NULL");
                Assert.IsTrue(r.IsDBNull(1), "last_played_date 应为 NULL");
                Assert.IsTrue(r.IsDBNull(2), "audio_stream_index 应为 NULL");
                Assert.IsTrue(r.IsDBNull(3), "subtitle_stream_index 应为 NULL");
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
                Assert.IsFalse(r.IsDBNull(1), "last_played_date 应已被覆盖");
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
