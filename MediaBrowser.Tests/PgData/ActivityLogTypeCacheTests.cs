using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Emby.Server.PgData;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// PgActivityRepository 的类型字典缓存（name → type_id）回归测试。
    ///
    /// 背景（2026-10-20 全量回归取证，pg_stat_statements Top-1）：
    ///   INSERT INTO activity_log_types (name) VALUES ($1)
    ///     ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name RETURNING type_id
    ///   —— 36567 次调用 / mean 77.19ms / max 8715.3ms / 累计 2822.7s。
    /// DO UPDATE 的排他更新锁让并发写同一 type 名的事务互相排队。
    /// 修法：进程内缓存 name → type_id（按 search_path 分桶），未命中时走无锁 SELECT +
    /// INSERT ... ON CONFLICT DO NOTHING。
    ///
    /// 覆盖两个要点：
    ///   ① 同名 type 多次写入 → 字典表只增 1 行，activity_log 各行 type_id 相同（缓存复用）
    ///   ② 不同 schema 各自写入同名 type → type_id 互相独立（缓存分桶，不串味）
    ///      这一条是缓存化引入的新风险：集成测试每个用例用独立 schema，各 schema 的
    ///      activity_log_types 序列独立，不分桶会拿到另一个 schema 的 type_id。
    /// </summary>
    [TestClass]
    public sealed class ActivityLogTypeCacheTests
    {
        private const string DefaultConn =
            "Host=localhost;Port=5432;Database=openmedia;Username=postgres;Password=123456";

        private readonly List<string> _schemas = new List<string>();

        private string _adminConn;

        [TestInitialize]
        public async Task SetUpAsync()
        {
            _adminConn = Environment.GetEnvironmentVariable("OPENMEDIA_TEST_PGCONN") ?? DefaultConn;

            try
            {
                await using var conn = new NpgsqlConnection(_adminConn);
                await conn.OpenAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is NpgsqlException || ex is System.Net.Sockets.SocketException)
            {
                Assert.Inconclusive("PostgreSQL 不可达（连接串 " + _adminConn + "）：" + ex.Message);
            }
        }

        [TestCleanup]
        public async Task TearDownAsync()
        {
            if (_schemas.Count == 0)
            {
                return;
            }

            try
            {
                await using var conn = new NpgsqlConnection(_adminConn);
                await conn.OpenAsync().ConfigureAwait(false);

                foreach (var schema in _schemas)
                {
                    await using var cmd = new NpgsqlCommand("DROP SCHEMA IF EXISTS " + schema + " CASCADE", conn);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // 清理失败不影响测试结论
            }
        }

        [TestMethod]
        public async Task SameTypeName_ReusesOneDictionaryRow()
        {
            var schema = await CreateSchemaAsync().ConfigureAwait(false);
            var connString = BuildConnString(schema);

            using (var repo = new PgActivityRepository(new NullLogger(), connString))
            {
                await repo.CreateAsync(NewEntry("UserActivity", "第一条")).ConfigureAwait(false);
                await repo.CreateAsync(NewEntry("UserActivity", "第二条")).ConfigureAwait(false);
                await repo.CreateAsync(NewEntry("UserActivity", "第三条")).ConfigureAwait(false);
            }

            var typeRows = await ScalarAsync(connString, "SELECT count(*) FROM activity_log_types WHERE name = 'UserActivity'").ConfigureAwait(false);
            var distinctTypes = await ScalarAsync(connString, "SELECT count(DISTINCT type_id) FROM activity_log").ConfigureAwait(false);
            var logRows = await ScalarAsync(connString, "SELECT count(*) FROM activity_log").ConfigureAwait(false);

            Assert.AreEqual(1, typeRows, "同名 type 在字典表只应有一行");
            Assert.AreEqual(3, logRows, "activity_log 应有 3 行");
            Assert.AreEqual(1, distinctTypes, "同一 type 的多行 activity_log 应共用同一个 type_id");
        }

        [TestMethod]
        public async Task DistinctSchemas_UseDistinctTypeIds()
        {
            var schemaA = await CreateSchemaAsync().ConfigureAwait(false);
            var schemaB = await CreateSchemaAsync().ConfigureAwait(false);
            var connA = BuildConnString(schemaA);
            var connB = BuildConnString(schemaB);

            using (var repo = new PgActivityRepository(new NullLogger(), connA))
            {
                // 先在 A 里插一个别的 type，把 A 的 UserActivity id 顶到 2；
                // B 的 UserActivity 会是 1（各 schema 序列独立）。
                // 这样缓存若不按 schema 分桶，B 会拿到 A 的 id=2（B 表里不存在 → 后续断言炸）。
                await repo.CreateAsync(NewEntry("OtherType", "占位")).ConfigureAwait(false);
                await repo.CreateAsync(NewEntry("UserActivity", "A1")).ConfigureAwait(false);
                await repo.CreateAsync(NewEntry("UserActivity", "A2")).ConfigureAwait(false);
            }

            using (var repo = new PgActivityRepository(new NullLogger(), connB))
            {
                await repo.CreateAsync(NewEntry("UserActivity", "B1")).ConfigureAwait(false);
            }

            var typeIdA = await ScalarAsync(connA, "SELECT type_id FROM activity_log_types WHERE name = 'UserActivity'").ConfigureAwait(false);
            var typeIdB = await ScalarAsync(connB, "SELECT type_id FROM activity_log_types WHERE name = 'UserActivity'").ConfigureAwait(false);

            // 各 schema 序列独立 → id 不同；关键是 B 拿到的 id 必须在 B 自己的字典表里存在
            // （若缓存不按 schema 分桶，B 会拿到 A 的 id，写入时外键不匹配 → 直接失败）
            Assert.AreNotEqual(typeIdA, typeIdB, "不同 schema 的同名 type 应各自独立取 id");

            var resolvedInB = await ScalarAsync(connB, "SELECT type_id FROM activity_log_types WHERE type_id = " + typeIdB).ConfigureAwait(false);
            Assert.AreEqual(typeIdB, resolvedInB, "B 的 type_id 必须在 B 自己的字典表里存在");

            var logTypeIdInB = await ScalarAsync(connB, "SELECT type_id FROM activity_log").ConfigureAwait(false);
            Assert.AreEqual(typeIdB, logTypeIdInB, "B 的 activity_log 行的 type_id 必须指向 B 自己的字典行");
        }

        [TestMethod]
        public async Task CacheHit_DoesNotInsertDuplicateDictionaryRows()
        {
            var schema = await CreateSchemaAsync().ConfigureAwait(false);
            var connString = BuildConnString(schema);

            using (var repo = new PgActivityRepository(new NullLogger(), connString))
            {
                // 首写走慢路径（建字典行），后续走缓存快路径
                await repo.CreateAsync(NewEntry("PlaybackStart", "冷启动")).ConfigureAwait(false);

                var rowsAfterCold = await ScalarAsync(connString, "SELECT count(*) FROM activity_log_types").ConfigureAwait(false);

                for (var i = 0; i < 20; i++)
                {
                    await repo.CreateAsync(NewEntry("PlaybackStart", "热路径" + i)).ConfigureAwait(false);
                }

                var rowsAfterWarm = await ScalarAsync(connString, "SELECT count(*) FROM activity_log_types").ConfigureAwait(false);
                var logRows = await ScalarAsync(connString, "SELECT count(*) FROM activity_log").ConfigureAwait(false);

                Assert.AreEqual(1, rowsAfterCold, "首写应只建 1 行字典");
                Assert.AreEqual(rowsAfterCold, rowsAfterWarm, "缓存命中路径不应再写字典表");
                Assert.AreEqual(21, logRows, "21 次写入应产生 21 行 activity_log");
            }
        }

        private static ActivityLogEntry NewEntry(string type, string name)
        {
            return new ActivityLogEntry
            {
                Type = type,
                Name = name,
                Date = DateTime.UtcNow
            };
        }

        private string BuildConnString(string schema)
        {
            var builder = new NpgsqlConnectionStringBuilder(_adminConn)
            {
                SearchPath = schema
            };

            return builder.ConnectionString;
        }

        private async Task<string> CreateSchemaAsync()
        {
            var schema = "pgtest_act_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            _schemas.Add(schema);

            await using var conn = new NpgsqlConnection(_adminConn);
            await conn.OpenAsync().ConfigureAwait(false);

            await using (var cmd = new NpgsqlCommand("CREATE SCHEMA " + schema, conn))
            {
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // 活动日志最小 schema：字典表 + 日志表（生产为 RANGE 分区表，测试用平表即可）
            var ddl = @"
                CREATE TABLE activity_log_types (
                    type_id SERIAL PRIMARY KEY,
                    name   TEXT NOT NULL UNIQUE
                );

                CREATE TABLE activity_log (
                    id             BIGSERIAL PRIMARY KEY,
                    name           TEXT,
                    overview       TEXT,
                    short_overview TEXT,
                    type_id        INTEGER REFERENCES activity_log_types(type_id),
                    item_id        BIGINT,
                    user_id        INTEGER,
                    date_created   TIMESTAMPTZ,
                    log_severity   TEXT
                );";

            await using (var cmd = new NpgsqlCommand("SET search_path TO " + schema + "; " + ddl, conn))
            {
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            return schema;
        }

        private static async Task<int> ScalarAsync(string connString, string sql)
        {
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync().ConfigureAwait(false);

            await using var cmd = new NpgsqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);

            return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }
    }
}
