using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// PostgreSQL 集成测试基类。
    ///
    /// 连接目标：环境变量 OPENMEDIA_TEST_PGCONN，缺省为 docker-compose 暴露的 15432。
    /// 若数据库不可达，所有用例 Assert.Inconclusive（而不是静默通过）。
    ///
    /// 每个用例在独立 schema（pgdata_test_xxx）中运行，结束即 DROP SCHEMA CASCADE，
    /// 不污染共享库、可并行、可重复。
    /// </summary>
    public abstract class PgTestBase
    {
        private const string DefaultConn =
            "Host=localhost;Port=15432;Database=openmedia;Username=postgres;Password=123456";

        protected string Schema { get; private set; }

        protected string AdminConnectionString { get; private set; }

        protected string TestConnectionString { get; private set; }

        [TestInitialize]
        public async Task SetUp()
        {
            AdminConnectionString = Environment.GetEnvironmentVariable("OPENMEDIA_TEST_PGCONN") ?? DefaultConn;
            Schema = "pgtest_" + Guid.NewGuid().ToString("N").Substring(0, 12);

            try
            {
                await using var conn = new NpgsqlConnection(AdminConnectionString);
                await conn.OpenAsync();

                await using (var cmd = new NpgsqlCommand("CREATE SCHEMA " + Schema, conn))
                {
                    await cmd.ExecuteNonQueryAsync();
                }

                // 关键：DDL 必须落在测试 schema 内，否则会撞上 public 里的同名表
                await using (var cmd = new NpgsqlCommand("SET search_path TO " + Schema, conn))
                {
                    await cmd.ExecuteNonQueryAsync();
                }

                await CreateSchemaObjectsAsync(conn);
            }
            catch (Exception ex)
            {
                // 只有连接层失败才 Inconclusive；DDL/权限错误必须让测试失败并暴露真实原因
                var isConnectivity = ex is NpgsqlException ne
                    ? ne.InnerException is System.Net.Sockets.SocketException
                      || ne.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
                      || ne.Message.Contains("failed to connect", StringComparison.OrdinalIgnoreCase)
                      || ne.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                    : ex is System.Net.Sockets.SocketException;

                if (isConnectivity)
                {
                    Assert.Inconclusive("PostgreSQL 不可达（docker compose up -d postgres 后重试）：" + ex.Message);
                }

                throw new AssertFailedException("测试 schema 初始化失败：" + ex);
            }

            var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString)
            {
                SearchPath = Schema
            };
            TestConnectionString = builder.ConnectionString;
        }

        [TestCleanup]
        public async Task TearDown()
        {
            if (string.IsNullOrEmpty(Schema))
            {
                return;
            }

            try
            {
                await using var conn = new NpgsqlConnection(AdminConnectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("DROP SCHEMA IF EXISTS " + Schema + " CASCADE", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // 清理失败不影响测试结论
            }
        }

        /// <summary>
        /// 建最小可用的 media_items / user_data_keys / user_datas / ancestor_ids。
        /// 只建被测 SQL 需要的列，避免依赖完整 schema-deploy.sql。
        /// </summary>
        private static async Task CreateSchemaObjectsAsync(NpgsqlConnection conn)
        {
            var ddl = @"
                CREATE TABLE user_data_keys (
                    id            SERIAL PRIMARY KEY,
                    user_data_key TEXT NOT NULL UNIQUE
                );

                CREATE TABLE media_items (
                    id                         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    guid                       UUID NOT NULL,
                    type                       INTEGER NOT NULL,
                    type_name                  TEXT,
                    data                       JSONB,
                    parent_id                  BIGINT,
                    series_id                  BIGINT,
                    path                       TEXT,
                    name                       TEXT,
                    sort_name                  TEXT,
                    clean_name                 TEXT,
                    presentation_unique_key    TEXT,
                    production_year            INTEGER,
                    community_rating           DOUBLE PRECISION,
                    is_folder                  BOOLEAN DEFAULT FALSE,
                    is_virtual_item            BOOLEAN DEFAULT FALSE,
                    user_data_key_id           INTEGER REFERENCES user_data_keys(id),
                    date_created               TIMESTAMPTZ DEFAULT now(),
                    date_modified              TIMESTAMPTZ DEFAULT now(),
                    date_last_media_added      TIMESTAMPTZ,
                    CONSTRAINT uq_media_guid_type UNIQUE (guid, type)
                );

                CREATE TABLE ancestor_ids (
                    item_id     BIGINT NOT NULL,
                    ancestor_id BIGINT NOT NULL,
                    distance    INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT ancestor_ids_pkey PRIMARY KEY (item_id, ancestor_id),
                    CONSTRAINT ancestor_ids_distance_check CHECK (distance >= 0)
                );

                CREATE TABLE user_datas (
                    user_data_key_id          INTEGER NOT NULL REFERENCES user_data_keys(id),
                    user_id                   INTEGER NOT NULL,
                    rating                    DOUBLE PRECISION,
                    played                    BOOLEAN NOT NULL DEFAULT FALSE,
                    play_count                INTEGER NOT NULL DEFAULT 0,
                    is_favorite               BOOLEAN NOT NULL DEFAULT FALSE,
                    playback_position_ticks   BIGINT NOT NULL DEFAULT 0,
                    last_played_date          TIMESTAMPTZ,
                    audio_stream_index        SMALLINT,
                    subtitle_stream_index     SMALLINT,
                    playstate_last_modified   TIMESTAMPTZ NOT NULL DEFAULT now(),
                    CONSTRAINT user_datas_pkey PRIMARY KEY (user_data_key_id, user_id)
                );";

            await using var cmd = new NpgsqlCommand(ddl, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        protected async Task<NpgsqlConnection> OpenAsync()
        {
            var conn = new NpgsqlConnection(TestConnectionString);
            await conn.OpenAsync();
            return conn;
        }

        protected async Task<long?> ScalarAsync(string sql, params NpgsqlParameter[] ps)
        {
            await using var conn = await OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            if (ps != null)
            {
                cmd.Parameters.AddRange(ps);
            }

            var r = await cmd.ExecuteScalarAsync();
            return r == null || r == DBNull.Value ? (long?)null : Convert.ToInt64(r);
        }
    }
}
