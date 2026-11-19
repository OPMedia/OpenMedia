using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Emby.Server.PgData;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// 「全新部署」门禁：在一个**空数据库**上跑一遍真正的 <see cref="PgSchemaInitializer"/>。
    ///
    /// 为什么需要这个测试：初始化的 DDL 分两段 —— 嵌入的 dump 建基础表，然后
    /// ApplyAlignmentDdl 补列、补索引。两段之间的依赖是隐含的：
    /// 只要有人在第二段里给一个「没被第一段建过、也没被 ADD COLUMN 补过」的列建索引，
    /// 已存在的老库毫无感觉（列是历史上手工加过的），全新部署却会在启动时直接炸。
    ///
    /// 这正是 item_extradata.clean_value 发生过的事：
    ///   PgSchemaInitializer 给它建索引，但 dump 里没有、ALTER 里也没有，
    ///   PostgreSQL 抛 UndefinedColumn，整个 schema 初始化失败。
    ///   而 openmedia_dev/openmedia_perf 因为列是手工加过的，一直看不出问题。
    ///
    /// 用临时数据库而不是独立 schema，是因为 <see cref="PgSchemaInitializer.Initialize"/>
    /// 用 to_regclass('public.media_items') 判断是否需要全量 DDL —— 只有真正的空库
    /// 才会走完整路径，也才是「新装」的真实形状。
    /// </summary>
    [TestClass]
    public class SchemaInitializerTests
    {
        private const string DefaultConn =
            "Host=localhost;Port=5432;Database=openmedia;Username=postgres;Password=123456";

        /// <summary>
        /// ApplyAlignmentDdl 里所有 ADD COLUMN 的 (表, 列)。初始化跑完后必须全部存在。
        /// 新增 ALTER 时同步加到这里 —— 漏了不会漏测「索引引用不存在的列」，
        /// 因为那种情况初始化本身就会抛。
        /// </summary>
        private static readonly (string Table, string Column)[] RuntimeColumns =
        {
            ("media_items", "data"),
            ("media_items", "type_name"),
            ("media_items", "is_folder"),
            ("media_items", "is_virtual_item"),
            ("media_items", "media_type"),
            ("media_items", "clean_name"),
            ("media_items", "top_parent_id"),
            ("media_items", "channel_id"),
            ("media_items", "album"),
            ("media_items", "extra_ids"),
            ("media_items", "unrated_type"),
            ("media_items", "trailer_types"),
            ("media_items", "external_series_id"),
            ("media_items", "owner_id"),
            ("media_items", "date_last_media_added"),
            ("item_people", "person_type_name"),
            ("item_people", "sort_order"),
            ("media_streams", "codec_time_base"),
            ("media_streams", "is_avc"),
            ("item_extradata", "clean_value"),
        };

        [TestMethod]
        public async Task Initialize_OnFreshDatabase_CreatesEveryRuntimeColumn()
        {
            var adminConnectionString = Environment.GetEnvironmentVariable("OPENMEDIA_TEST_PGCONN") ?? DefaultConn;
            var databaseName = "pgdrift_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var adminBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString);

            try
            {
                await using (var admin = new NpgsqlConnection(adminConnectionString))
                {
                    await admin.OpenAsync().ConfigureAwait(false);
                    await using var create = new NpgsqlCommand("CREATE DATABASE " + databaseName, admin);
                    await create.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            catch (PostgresException ex) when (ex.SqlState == "42501" || ex.SqlState == "55006")
            {
                Assert.Inconclusive("当前角色无权创建数据库，跳过全新部署门禁：" + ex.MessageText);
                return;
            }
            catch (NpgsqlException ex) when (IsConnectivityFailure(ex))
            {
                Assert.Inconclusive("PostgreSQL 不可达（docker compose up -d postgres 后重试）：" + ex.Message);
                return;
            }

            try
            {
                var freshBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString)
                {
                    Database = databaseName
                };

                await using (var connection = new NpgsqlConnection(freshBuilder.ConnectionString))
                {
                    await connection.OpenAsync().ConfigureAwait(false);

                    // 空库 → 走 ExecuteFullSchema + ApplyAlignmentDdl。
                    // 任何一条 DDL 引用不存在的列，这里就会抛 UndefinedColumn。
                    PgSchemaInitializer.Initialize(connection);

                    var present = await ReadColumnsAsync(connection).ConfigureAwait(false);

                    Assert.IsTrue(present.Contains(("media_items", "id")), "全量 dump 没有被应用（media_items 不存在）");

                    var missing = new List<string>();
                    foreach (var (table, column) in RuntimeColumns)
                    {
                        if (!present.Contains((table, column)))
                        {
                            missing.Add(table + "." + column);
                        }
                    }

                    Assert.AreEqual(
                        0,
                        missing.Count,
                        "初始化跑完后仍然缺少这些运行期列：" + string.Join(", ", missing));

                    // 这条索引是当初缺失列的直接受害者，单独钉住。
                    Assert.IsTrue(
                        await IndexExistsAsync(connection, "idx_item_extradata_type_clean_value").ConfigureAwait(false),
                        "idx_item_extradata_type_clean_value 未创建");
                }
            }
            finally
            {
                await using var admin = new NpgsqlConnection(adminConnectionString);
                try
                {
                    await admin.OpenAsync().ConfigureAwait(false);
                    await using var drop = new NpgsqlCommand(
                        "DROP DATABASE IF EXISTS " + databaseName + " WITH (FORCE)",
                        admin);
                    await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 清理失败不应掩盖测试本身的结论
                }
            }
        }

        private static async Task<HashSet<(string, string)>> ReadColumnsAsync(NpgsqlConnection connection)
        {
            var result = new HashSet<(string, string)>();
            const string Sql = @"
                SELECT table_name, column_name
                FROM information_schema.columns
                WHERE table_schema = 'public'";

            await using var cmd = new NpgsqlCommand(Sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.Add((reader.GetString(0), reader.GetString(1)));
            }

            return result;
        }

        private static async Task<bool> IndexExistsAsync(NpgsqlConnection connection, string indexName)
        {
            await using var cmd = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL", connection);
            cmd.Parameters.AddWithValue("name", "public." + indexName);
            return (bool)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;
        }

        private static bool IsConnectivityFailure(NpgsqlException ex)
        {
            return ex.InnerException is System.Net.Sockets.SocketException
                || ex.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("failed to connect", StringComparison.OrdinalIgnoreCase);
        }
    }
}
