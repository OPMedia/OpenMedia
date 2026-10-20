using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Emby.Server.PgData;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// 从生产 schema（Emby.Server.PgData 嵌入的 emby_pgsql_dump_schema_optimized.sql，
    /// 即 <see cref="PgSchemaInitializer"/> 用的同一份单一事实来源）派生测试 schema。
    ///
    /// 做法：读嵌入资源 → 去注释 → 去分区（PARTITION BY / PARTITION OF / 分区子表调参）→
    /// 去扩展/物化视图/权限语句 → 在独立 schema 内执行。
    /// 这样差分测试的 fixture 形状永远与生产一致，不会两边各漂一份。
    ///
    /// 注意：PgTestBase 先建了最小 media_items（20 列），必须先删掉再灌生产 schema——
    /// 否则 CREATE TABLE IF NOT EXISTS 变成空操作，差分就跑在错误 fixture 上。
    /// </summary>
    public abstract class ProductionSchemaTestBase : PgTestBase
    {
        private const string SchemaResourceName =
            "Emby.Server.PgData.emby_pgsql_dump_schema_optimized.sql";

        protected async Task EnsureProductionSchemaAsync()
        {
            await using var conn = await OpenAsync().ConfigureAwait(false);

            await DropBaseTablesAsync(conn).ConfigureAwait(false);

            var sql = ReadSchemaResource();

            var failed = new List<string>();
            foreach (var statement in SplitSchemaStatements(sql))
            {
                try
                {
                    await using var cmd = new NpgsqlCommand(statement, conn);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                catch (PostgresException ex)
                {
                    failed.Add(ex.SqlState + ": " + ex.Message.Replace(Environment.NewLine, " ")
                        + " :: " + statement.Substring(0, Math.Min(160, statement.Length))
                            .Replace(Environment.NewLine, " "));
                }
            }

            // 表结构语句失败必须让测试失败——否则差分跑在错误的 fixture 上会得出假结论。
            Assert.AreEqual(0, failed.Count,
                "生产 schema 派生失败：" + string.Join(" | ", failed));

            await ApplyQueryEngineColumnsAsync(conn).ConfigureAwait(false);
        }

        private static string ReadSchemaResource()
        {
            var assembly = typeof(PgSchemaInitializer).Assembly;
            using var stream = assembly.GetManifestResourceStream(SchemaResourceName);
            if (stream == null)
            {
                throw new InvalidOperationException("schema 资源不存在：" + SchemaResourceName);
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// 把生产 schema 切成可在测试 schema 内执行的语句。
        /// </summary>
        internal static IEnumerable<string> SplitSchemaStatements(string sql)
        {
            var withoutComments = Regex.Replace(sql, @"--[^\r\n]*", string.Empty);

            var parts = withoutComments.Split(';');

            foreach (var raw in parts)
            {
                var statement = raw.Trim();

                if (statement.Length == 0)
                {
                    continue;
                }

                if (statement.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) ||
                    statement.Equals("COMMIT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 分区子表：CREATE TABLE media_items_xxx PARTITION OF media_items FOR VALUES ...
                if (Regex.IsMatch(statement, @"(?is)^\s*CREATE\s+TABLE\s+\w+\s+PARTITION\s+OF\b"))
                {
                    continue;
                }

                // 分区父表的 PARTITION BY ...; 尾子句
                statement = Regex.Replace(
                    statement, @"(?is)\s*PARTITION\s+BY\s+(LIST|HASH|RANGE)\s*\([^)]*\)\s*$", string.Empty);

                // 扩展：测试库可能未预加载 pg_stat_statements
                if (Regex.IsMatch(statement, @"(?is)^\s*CREATE\s+EXTENSION\b"))
                {
                    continue;
                }

                // 物化视图依赖分区父表
                if (Regex.IsMatch(statement, @"(?is)^\s*CREATE\s+MATERIALIZED\s+VIEW\b"))
                {
                    continue;
                }

                // 物化视图的索引/注释（视图本体已跳过）
                if (Regex.IsMatch(statement, @"(?is)^\s*CREATE\s+(UNIQUE\s+)?INDEX\s+\w+\s+ON\s+mv_"))
                {
                    continue;
                }

                if (Regex.IsMatch(statement, @"(?is)^\s*COMMENT\s+ON\s+MATERIALIZED\s+VIEW\b"))
                {
                    continue;
                }

                // 权限语句
                if (Regex.IsMatch(statement, @"(?is)^\s*(CREATE\s+ROLE|GRANT|REVOKE|ALTER\s+ROLE)\b"))
                {
                    continue;
                }

                // 分区子表的 autovacuum 调参（子表已跳过，必然失败）
                if (Regex.IsMatch(statement, @"(?is)^\s*ALTER\s+TABLE\s+\w+\s+SET\s*\(\s*autovacuum"))
                {
                    continue;
                }

                // 分区子表注释
                if (Regex.IsMatch(statement, @"(?is)^\s*COMMENT\s+ON\s+TABLE\s+\w*_p\d"))
                {
                    continue;
                }

                yield return statement;
            }
        }

        private static async Task DropBaseTablesAsync(NpgsqlConnection conn)
        {
            var tables = new[] { "user_datas", "ancestor_ids", "media_items", "user_data_keys" };

            foreach (var t in tables)
            {
                await using var cmd = new NpgsqlCommand("DROP TABLE IF EXISTS " + t + " CASCADE", conn);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// PgSchemaInitializer.ApplyAlignmentDdl 的等价子集（Task C 漂移清单的
        /// 「初始化器补丁」侧）：只补查询引擎跑得起来必需的列/索引/字典种子。
        /// 与 PgSchemaInitializer 保持语义一致——这里是测试副本，不替代生产路径。
        /// </summary>
        private static async Task ApplyQueryEngineColumnsAsync(NpgsqlConnection conn)
        {
            var statements = new[]
            {
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS data JSONB",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS type_name TEXT",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS is_folder BOOLEAN DEFAULT FALSE",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS is_virtual_item BOOLEAN DEFAULT FALSE",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS media_type TEXT",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS channel_id TEXT",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS top_parent_id TEXT",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS clean_name TEXT",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS album TEXT",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS extra_ids TEXT",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS unrated_type TEXT",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS trailer_types TEXT",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS external_series_id TEXT",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS owner_id TEXT",
                "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS date_last_media_added TIMESTAMPTZ",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_media_items_guid ON media_items (guid, type)",
                // FixItemExtradata 等价：根 SQL 的 PK 是 (item_id, extradata_type_id)，
                // 存不下多值 Genres/Studios/Tags/Artists，与生产运行末态不符；
                // PgSchemaInitializer.FixItemExtradata 会换成 3 列 PK + clean_value。
                // 差分 fixture 必须建在生产真实末态上，否则 ON CONFLICT 直接报 42P10。
                "ALTER TABLE item_extradata ADD COLUMN IF NOT EXISTS clean_value TEXT",
                @"DROP TABLE IF EXISTS item_extradata CASCADE;
                  CREATE TABLE item_extradata (
                      item_id            BIGINT  NOT NULL,
                      extradata_type_id  INTEGER NOT NULL,
                      value              TEXT    NOT NULL,
                      clean_value        TEXT,
                      PRIMARY KEY (item_id, extradata_type_id, value),
                      FOREIGN KEY (extradata_type_id) REFERENCES item_extradata_types (extradata_type_id)
                  )",
                "CREATE INDEX IF NOT EXISTS idx_media_items_clean_name_type ON media_items (clean_name, type)",
                "CREATE INDEX IF NOT EXISTS idx_media_items_top_parent_id ON media_items (top_parent_id)",
                "CREATE INDEX IF NOT EXISTS idx_media_items_media_type ON media_items (media_type) WHERE media_type IS NOT NULL",
                "CREATE INDEX IF NOT EXISTS idx_media_items_is_virtual_item ON media_items (is_virtual_item) WHERE is_virtual_item = TRUE",
                "CREATE INDEX IF NOT EXISTS idx_item_extradata_type_clean_value ON item_extradata (extradata_type_id, clean_value)",
                "CREATE INDEX IF NOT EXISTS idx_item_extradata_item_type ON item_extradata (item_id, extradata_type_id)",
                @"INSERT INTO item_extradata_types (extradata_type_id, name) VALUES
                    (0, 'Artists'), (1, 'AlbumArtists'), (2, 'Genres'), (3, 'Studios'),
                    (4, 'Tags'), (6, 'InheritedTags')
                    ON CONFLICT (extradata_type_id) DO NOTHING",
                "ALTER TABLE item_people ADD COLUMN IF NOT EXISTS person_type_name TEXT",
                "ALTER TABLE item_people ADD COLUMN IF NOT EXISTS sort_order INTEGER",
                "ALTER TABLE media_streams ADD COLUMN IF NOT EXISTS is_avc BOOLEAN",
                "ALTER TABLE media_streams ADD COLUMN IF NOT EXISTS codec_time_base TEXT",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_activity_log_types_name ON activity_log_types (name)",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_devices_reported_device_id_unique ON devices (reported_device_id)",
                "CREATE INDEX IF NOT EXISTS idx_media_items_user_data_key_id ON media_items (user_data_key_id) WHERE user_data_key_id IS NOT NULL",
                "CREATE INDEX IF NOT EXISTS idx_user_settings_key_id ON user_settings (user_settings_key_id)"
            };

            foreach (var s in statements)
            {
                try
                {
                    await using var cmd = new NpgsqlCommand(s, conn);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                catch (PostgresException)
                {
                    // 该表在生产 schema 里不存在（如同步表），跳过——测试不需要
                }
            }
        }
    }
}
