using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Npgsql;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PostgreSQL schema 初始化器。
    /// 1) 幂等门卫：SELECT to_regclass('public.media_items') 为 NULL 才执行全量 DDL
    ///    （SQL 文件里的分区子表 CREATE TABLE ... PARTITION OF 没有 IF NOT EXISTS，重复执行会失败）。
    /// 2) 全量 DDL 来自嵌入资源 emby_pgsql_dump_schema_optimized.sql（单一事实来源，含 sync 三表照建留空）。
    ///    文件中曾存在的非法单列外键 REFERENCES media_items (id, type)（引用列数与复合主键不匹配，
    ///    PostgreSQL 报 "number of referencing and referenced columns for foreign key disagree"）
    ///    已直接在 SQL 文件内移除；引用完整性由仓储层维护（与旧 SQLite 行为一致，
    ///    DeleteItem 显式删除子行）。
    /// 3) 补充对齐 DDL（docs/upgrade-net10-plan.md §5.2 + 查询引擎语义保留所需的列/索引），全部幂等。
    /// </summary>
    public static class PgSchemaInitializer
    {
        private const string SchemaResourceName = "Emby.Server.PgData.emby_pgsql_dump_schema_optimized.sql";

        /// <summary>
        /// 初始化数据库结构（幂等，可重复调用）。
        /// </summary>
        public static void Initialize(NpgsqlConnection connection)
        {
            if (connection == null)
            {
                throw new ArgumentNullException("connection");
            }

            var mediaItemsExists = TableExists(connection, "media_items");

            if (!mediaItemsExists)
            {
                ExecuteFullSchema(connection);
            }

            ApplyAlignmentDdl(connection);
        }

        private static bool TableExists(NpgsqlConnection connection, string tableName)
        {
            using (var cmd = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL", connection))
            {
                cmd.Parameters.AddWithValue("table", "public." + tableName);
                return (bool)cmd.ExecuteScalar();
            }
        }

        /// <summary>
        /// 执行嵌入 SQL 文件的全量 DDL。
        /// 文件含 BEGIN;/COMMIT; 包裹（在此剥离，改由调用方事务保证原子性）与物化视图。
        /// 逐条执行以便失败时能定位到具体语句。
        /// </summary>
        private static void ExecuteFullSchema(NpgsqlConnection connection)
        {
            var sql = ReadEmbeddedSchema();

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    foreach (var statement in SplitSqlStatements(sql))
                    {
                        using (var cmd = new NpgsqlCommand(statement, connection, transaction))
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                }
                catch
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch
                    {
                        // 回滚失败时连接状态由连接池清理
                    }

                    throw;
                }
            }
        }

        private static string ReadEmbeddedSchema()
        {
            var assembly = typeof(PgSchemaInitializer).GetTypeInfo().Assembly;

            using (var stream = assembly.GetManifestResourceStream(SchemaResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        "Embedded schema resource not found: " + SchemaResourceName);
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// 将 SQL 文本拆分为可独立执行的语句：
        /// 先剥离行注释（本 schema 文件只含 -- 注释），再按分号切分，
        /// 跳过空语句与 BEGIN/COMMIT 包裹语句。
        /// </summary>
        internal static IEnumerable<string> SplitSqlStatements(string sql)
        {
            // 去掉 -- 行注释（文件不含块注释与含分号的字符串字面量）
            var withoutComments = Regex.Replace(sql, @"--[^\r\n]*", string.Empty);

            var parts = withoutComments.Split(';');

            foreach (var raw in parts)
            {
                var statement = raw.Trim();

                if (statement.Length == 0)
                {
                    continue;
                }

                if (string.Equals(statement, "BEGIN", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(statement, "COMMIT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return statement;
            }
        }

        /// <summary>
        /// 补充对齐 DDL，全部幂等可重复执行。
        /// §5.2 的 5 条 + 查询引擎语义保留所需的补充列/索引
        /// （旧 TypedBaseItems 的 IsFolder/IsVirtualItem/MediaType/TopParentId/CleanName 等过滤列
        ///  在优化后的 media_items 中不存在，WHERE 语义无法保留，故补齐）。
        /// </summary>
        private static void ApplyAlignmentDdl(NpgsqlConnection connection)
        {
            // ========== §5.2 核心 5 条 ==========

            // §5.2-1: media_items.data — 旧 TypedBaseItems 靠它存整个 BaseItem 序列化 JSON
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS data JSONB");

            // §5.2-2: media_items.type_name — .NET 完整类型名（反序列化必需；type INTEGER 是分区键，语义不同）
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS type_name TEXT");

            // §5.2-3: guid 唯一索引（分区表唯一索引必须含分区键 type；guid 为最左前缀可服务 GUID 查找）
            Execute(connection, "CREATE UNIQUE INDEX IF NOT EXISTS idx_media_items_guid ON media_items (guid, type)");

            // §5.2-4: item_extradata 主键修正为 (item_id, extradata_type_id, value) + clean_value 列
            FixItemExtradata(connection);

            // §5.2-5: display_preferences（旧 userdisplaypreferences 的 PG 对应表）
            Execute(connection, @"
                CREATE TABLE IF NOT EXISTS display_preferences (
                    item_id UUID    NOT NULL,
                    user_id INTEGER NOT NULL,
                    client  TEXT    NOT NULL,
                    data    JSONB   NOT NULL,
                    PRIMARY KEY (item_id, user_id, client)
                )");

            // ========== 查询引擎语义保留所需的 media_items 补充列 ==========
            // （旧 TypedBaseItems 的过滤/排序列，优化 schema 中缺失）

            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS is_folder BOOLEAN DEFAULT FALSE");
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS is_virtual_item BOOLEAN DEFAULT FALSE");
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS media_type TEXT");
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS channel_id TEXT");
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS top_parent_id TEXT");
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS clean_name TEXT");
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS album TEXT");
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS extra_ids TEXT");
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS unrated_type TEXT");
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS trailer_types TEXT");
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS external_series_id TEXT");
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS owner_id TEXT");
            Execute(connection, "ALTER TABLE media_items ADD COLUMN IF NOT EXISTS date_last_media_added TIMESTAMPTZ");

            // ========== 查询引擎所需索引（对照旧 idx_CleanNameType / idx_TopParentIdGuid 等） ==========

            Execute(connection, "CREATE INDEX IF NOT EXISTS idx_media_items_clean_name_type ON media_items (clean_name, type)");
            Execute(connection, "CREATE INDEX IF NOT EXISTS idx_media_items_top_parent_id ON media_items (top_parent_id)");
            Execute(connection, "CREATE INDEX IF NOT EXISTS idx_media_items_media_type ON media_items (media_type) WHERE media_type IS NOT NULL");
            Execute(connection, "CREATE INDEX IF NOT EXISTS idx_media_items_is_virtual_item ON media_items (is_virtual_item) WHERE is_virtual_item = TRUE");

            // ========== item_extradata 查询索引（对照旧 idx_ItemValues6/7/8） ==========

            Execute(connection, "CREATE INDEX IF NOT EXISTS idx_item_extradata_type_clean_value ON item_extradata (extradata_type_id, clean_value)");
            Execute(connection, "CREATE INDEX IF NOT EXISTS idx_item_extradata_item_type ON item_extradata (item_id, extradata_type_id)");

            // ========== item_extradata_types 字典种子（关键！） ==========
            // item_extradata.extradata_type_id 有外键指向本字典表；字典为空时
            // 任何带 extradata 值的保存（genres/studios/tags/artists）都会
            // 23503 外键违规 → 整个保存事务回滚 → 元数据永不落库。
            // 类型码对照旧 ItemValues：0=Artists 1=AlbumArtists 2=Genres
            // 3=Studios 4=Tags 6=InheritedTags
            Execute(connection, @"INSERT INTO item_extradata_types (extradata_type_id, name) VALUES
                (0, 'Artists'), (1, 'AlbumArtists'), (2, 'Genres'), (3, 'Studios'),
                (4, 'Tags'), (6, 'InheritedTags')
                ON CONFLICT (extradata_type_id) DO NOTHING");

            // ========== item_people 补充列（person_type SMALLINT 存不下任意字符串类型；sort_order 旧 People 表有） ==========

            Execute(connection, "ALTER TABLE item_people ADD COLUMN IF NOT EXISTS person_type_name TEXT");
            Execute(connection, "ALTER TABLE item_people ADD COLUMN IF NOT EXISTS sort_order INTEGER");

            // ========== media_streams 补充列（旧 mediastreams 的 IsAvc/CodecTimeBase 在优化 schema 中缺失） ==========

            Execute(connection, "ALTER TABLE media_streams ADD COLUMN IF NOT EXISTS is_avc BOOLEAN");
            Execute(connection, "ALTER TABLE media_streams ADD COLUMN IF NOT EXISTS codec_time_base TEXT");

            // ========== 仓储运行所需字典表唯一索引（upsert 的 ON CONFLICT 依赖） ==========

            // PgActivityRepository 的活动类型字典 upsert
            Execute(connection, "CREATE UNIQUE INDEX IF NOT EXISTS idx_activity_log_types_name ON activity_log_types (name)");

            // PgAuthenticationRepository 的设备 upsert
            Execute(connection, "CREATE UNIQUE INDEX IF NOT EXISTS idx_devices_reported_device_id_unique ON devices (reported_device_id)");
        }

        /// <summary>
        /// item_extradata 主键修正：
        /// 原 PK (item_id, extradata_type_id) 存不下多值 Genres/Studios/Tags/Artists，
        /// 修正为 (item_id, extradata_type_id, value)，并补充 clean_value 列
        /// （旧 ItemValues.CleanValue，Name/Genre/Tag 匹配与聚合分组依赖）。
        /// 若表已按旧定义存在 → 新建修正表 + 拷数据 + 替换；初始化场景下表为空，拷贝为空操作。
        /// </summary>
        private static void FixItemExtradata(NpgsqlConnection connection)
        {
            if (!TableExists(connection, "item_extradata"))
            {
                // 全量 DDL 已建表（旧 PK 定义），走迁移路径
                return;
            }

            var pkColumnCount = GetPrimaryKeyColumnCount(connection, "item_extradata");

            var hasCleanValueColumn = false;

            using (var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='item_extradata' AND column_name='clean_value'",
                connection))
            {
                hasCleanValueColumn = Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }

            if (pkColumnCount >= 3 && hasCleanValueColumn)
            {
                // 已是修正后的定义
                return;
            }

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Execute(connection, transaction, @"
                        CREATE TABLE item_extradata_fixed (
                            item_id            BIGINT  NOT NULL,
                            extradata_type_id  INTEGER NOT NULL,
                            value              TEXT    NOT NULL,
                            clean_value        TEXT,
                            PRIMARY KEY (item_id, extradata_type_id, value),
                            FOREIGN KEY (extradata_type_id) REFERENCES item_extradata_types (extradata_type_id)
                        ) WITH (fillfactor = 90)");

                    Execute(connection, transaction, @"
                        INSERT INTO item_extradata_fixed (item_id, extradata_type_id, value)
                        SELECT item_id, extradata_type_id, value FROM item_extradata");

                    Execute(connection, transaction, "DROP TABLE item_extradata");

                    Execute(connection, transaction, "ALTER TABLE item_extradata_fixed RENAME TO item_extradata");

                    Execute(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_item_extradata_type_id ON item_extradata (extradata_type_id)");

                    transaction.Commit();
                }
                catch
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch
                    {
                    }

                    throw;
                }
            }
        }

        private static int GetPrimaryKeyColumnCount(NpgsqlConnection connection, string tableName)
        {
            const string sql = @"
                SELECT count(*)
                FROM pg_index i
                WHERE i.indrelid = @table::regclass
                  AND i.indisprimary";

            using (var cmd = new NpgsqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("table", "public." + tableName);
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
            }
        }

        private static void Execute(NpgsqlConnection connection, string sql)
        {
            using (var cmd = new NpgsqlCommand(sql, connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private static void Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
        {
            using (var cmd = new NpgsqlCommand(sql, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
        }
    }
}
