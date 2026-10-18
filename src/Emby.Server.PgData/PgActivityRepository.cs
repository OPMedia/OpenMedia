using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PostgreSQL 活动日志仓储（activity_log RANGE 分区表 + activity_log_types 字典表）。
    /// 对照旧 ActivityRepository（SQLite ActivityLog 表）逐方法保留语义。
    ///
    /// 列映射：
    /// - Type TEXT        → activity_log_types 字典翻译（type_id 整数外键）
    /// - ItemId TEXT (guid "N") → item_id 整数（media_items.id，经 GUID↔int 翻译；项不存在则 NULL）
    /// - UserId TEXT (guid "N") → user_id 整数（local_users.id；空 GUID 或用户不存在则 NULL）
    /// - LogSeverity TEXT → log_severity（schema CHECK 枚举 Trace/Debug/Information/Warning/Error/Critical，
    ///                       与 LogSeverity 枚举 Info/Warn/Fatal 做双向映射）
    /// </summary>
    public class PgActivityRepository : BasePgRepository, IActivityRepository
    {
        public PgActivityRepository(ILogger logger, string connectionString)
            : base(logger, connectionString)
        {
        }

        /// <summary>
        /// 初始化：schema 由 PgSchemaInitializer 统一建好，这里只做存在性校验。
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            var hasLog = await TableExistsAsync(connection, "activity_log", cancellationToken).ConfigureAwait(false);
            var hasTypes = await TableExistsAsync(connection, "activity_log_types", cancellationToken).ConfigureAwait(false);

            if (!hasLog || !hasTypes)
            {
                throw new InvalidOperationException("activity_log tables do not exist. Schema initialization failed.");
            }
        }

        /// <summary>
        /// 写入活动日志（旧 Create：插入 ActivityLog 行）。
        /// </summary>
        public Task CreateAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            return RunInTransactionAsync(async (connection, transaction, ct) =>
            {
                var typeId = await GetOrInsertActivityLogTypeAsync(connection, transaction, entry.Type, ct).ConfigureAwait(false);
                var itemId = await ResolveMediaItemIdAsync(connection, transaction, entry.ItemId, ct).ConfigureAwait(false);
                var userId = await ResolveInternalUserIdAsync(connection, transaction, entry.UserId, ct).ConfigureAwait(false);

                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    @"INSERT INTO activity_log
                        (name, overview, short_overview, type_id, item_id, user_id, date_created, log_severity)
                      VALUES
                        (@name, @overview, @short_overview, @type_id, @item_id, @user_id, @date_created, @log_severity)",
                    ct,
                    new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = entry.Name },
                    new NpgsqlParameter("overview", NpgsqlDbType.Text) { Value = ToDbValue(entry.Overview) },
                    new NpgsqlParameter("short_overview", NpgsqlDbType.Text) { Value = ToDbValue(entry.ShortOverview) },
                    new NpgsqlParameter("type_id", NpgsqlDbType.Integer) { Value = typeId },
                    new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = ToDbValue(itemId) },
                    new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = ToDbValue(userId) },
                    new NpgsqlParameter("date_created", NpgsqlDbType.TimestampTz) { Value = ToUtcDateTime(entry.Date) },
                    new NpgsqlParameter("log_severity", NpgsqlDbType.Text) { Value = ToSchemaSeverity(entry.Severity) }).ConfigureAwait(false);
            }, cancellationToken);
        }

        /// <summary>
        /// 查询活动日志（旧 GetActivityLogEntries：minDate/hasUserId 过滤 + DateCreated 倒序 + 分页 + 总数）。
        /// 旧分页用 "Id NOT IN (子查询 LIMIT startIndex)"，PG 用等价的 OFFSET。
        /// </summary>
        public async Task<QueryResult<ActivityLogEntry>> GetActivityLogEntriesAsync(DateTime? minDate, bool? hasUserId, int? startIndex, int? limit, CancellationToken cancellationToken = default)
        {
            var whereClauses = new List<string>();

            if (minDate.HasValue)
            {
                whereClauses.Add("a.date_created >= @date_created");
            }

            if (hasUserId.HasValue)
            {
                whereClauses.Add(hasUserId.Value ? "a.user_id IS NOT NULL" : "a.user_id IS NULL");
            }

            var whereTextWithoutPaging = whereClauses.Count == 0
                ? string.Empty
                : " WHERE " + string.Join(" AND ", whereClauses);

            var commandText = BaseActivitySelectText + whereTextWithoutPaging + " ORDER BY a.date_created DESC";

            if (limit.HasValue)
            {
                commandText += " LIMIT " + limit.Value.ToString(CultureInfo.InvariantCulture);
            }

            var offset = startIndex ?? 0;

            if (offset > 0)
            {
                commandText += " OFFSET " + offset.ToString(CultureInfo.InvariantCulture);
            }

            var list = new List<ActivityLogEntry>();
            var result = new QueryResult<ActivityLogEntry>();

            await using (var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false))
            {
                await using (var cmd = new NpgsqlCommand(commandText, connection))
                {
                    BindActivityQueryParams(cmd, minDate);

                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        list.Add(GetEntry(reader));
                    }
                }

                await using (var countCmd = new NpgsqlCommand(
                    "SELECT count(*) FROM activity_log a" + whereTextWithoutPaging,
                    connection))
                {
                    BindActivityQueryParams(countCmd, minDate);

                    result.TotalRecordCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                }
            }

            result.Items = list.ToArray();
            return result;
        }

        private const string BaseActivitySelectText = @"
            SELECT a.id,
                   a.name,
                   a.overview,
                   a.short_overview,
                   alt.name,
                   m.guid,
                   lu.guid,
                   a.date_created,
                   a.log_severity
            FROM activity_log a
            LEFT JOIN activity_log_types alt ON alt.type_id = a.type_id
            LEFT JOIN media_items m ON m.id = a.item_id
            LEFT JOIN local_users lu ON lu.id = a.user_id";

        private static void BindActivityQueryParams(NpgsqlCommand cmd, DateTime? minDate)
        {
            if (minDate.HasValue)
            {
                cmd.Parameters.Add(new NpgsqlParameter("date_created", NpgsqlDbType.TimestampTz)
                {
                    Value = ToUtcDateTime(minDate.Value)
                });
            }
        }

        /// <summary>
        /// 行 → ActivityLogEntry（旧 GetEntry 逐字段对照；item/user 整数 id 翻译回 GUID）。
        /// </summary>
        private static ActivityLogEntry GetEntry(NpgsqlDataReader reader)
        {
            var info = new ActivityLogEntry
            {
                Id = reader.GetInt64(0)
            };

            if (!reader.IsDBNull(1))
            {
                info.Name = reader.GetString(1);
            }

            if (!reader.IsDBNull(2))
            {
                info.Overview = reader.GetString(2);
            }

            if (!reader.IsDBNull(3))
            {
                info.ShortOverview = reader.GetString(3);
            }

            if (!reader.IsDBNull(4))
            {
                info.Type = reader.GetString(4);
            }

            if (!reader.IsDBNull(5))
            {
                // 旧 ItemId 为 item.Id.ToString("N")
                info.ItemId = reader.GetGuid(5).ToString("N");
            }

            if (!reader.IsDBNull(6))
            {
                info.UserId = reader.GetGuid(6);
            }

            info.Date = reader.GetDateTime(7);

            if (!reader.IsDBNull(8))
            {
                info.Severity = FromSchemaSeverity(reader.GetString(8));
            }

            return info;
        }

        /// <summary>
        /// LogSeverity 枚举（Info/Debug/Warn/Error/Fatal）→ schema CHECK 枚举
        /// （Trace/Debug/Information/Warning/Error/Critical）。
        /// </summary>
        private static string ToSchemaSeverity(LogSeverity severity)
        {
            switch (severity)
            {
                case LogSeverity.Debug:
                    return "Debug";
                case LogSeverity.Warn:
                    return "Warning";
                case LogSeverity.Error:
                    return "Error";
                case LogSeverity.Fatal:
                    return "Critical";
                default:
                    return "Information";
            }
        }

        private static LogSeverity FromSchemaSeverity(string severity)
        {
            switch (severity)
            {
                case "Trace":
                case "Debug":
                    return LogSeverity.Debug;
                case "Warning":
                    return LogSeverity.Warn;
                case "Error":
                    return LogSeverity.Error;
                case "Critical":
                    return LogSeverity.Fatal;
                default:
                    return LogSeverity.Info;
            }
        }

        /// <summary>
        /// 类型字典 upsert（activity_log_types.name 唯一索引支持 ON CONFLICT）。
        /// </summary>
        private static async Task<int> GetOrInsertActivityLogTypeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string name, CancellationToken cancellationToken)
        {
            var result = await ExecuteScalarAsync(
                connection,
                transaction,
                @"INSERT INTO activity_log_types (name)
                  VALUES (@name)
                  ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name
                  RETURNING type_id",
                cancellationToken,
                new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = name ?? string.Empty }).ConfigureAwait(false);

            return Convert.ToInt32(result);
        }

        /// <summary>
        /// item GUID 字符串 → media_items.id（解析失败或项不存在返回 null）。
        /// </summary>
        private static async Task<long?> ResolveMediaItemIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string itemId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(itemId) || !Guid.TryParse(itemId, out var guid))
            {
                return null;
            }

            var result = await ExecuteScalarAsync(
                connection,
                transaction,
                "SELECT id FROM media_items WHERE guid = @guid LIMIT 1",
                cancellationToken,
                new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = guid }).ConfigureAwait(false);

            return result == null || result == DBNull.Value ? (long?)null : Convert.ToInt64(result);
        }

        /// <summary>
        /// 用户 GUID → local_users.id（空 GUID 或用户不存在返回 null）。
        /// </summary>
        private static async Task<int?> ResolveInternalUserIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, CancellationToken cancellationToken)
        {
            if (userId.Equals(Guid.Empty))
            {
                return null;
            }

            var result = await ExecuteScalarAsync(
                connection,
                transaction,
                "SELECT id FROM local_users WHERE guid = @guid",
                cancellationToken,
                new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = userId }).ConfigureAwait(false);

            return result == null || result == DBNull.Value ? (int?)null : Convert.ToInt32(result);
        }
    }
}
