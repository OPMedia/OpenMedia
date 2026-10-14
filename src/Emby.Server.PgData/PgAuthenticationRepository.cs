using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Security;
using MediaBrowser.Model.Devices;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PostgreSQL 认证仓储（devices/tokens 表）。
    /// 对照旧 AuthenticationRepository（SQLite Tokens/Devices 表）逐方法保留语义。
    ///
    /// 列映射：
    /// - Tokens.DeviceId (TEXT)   → devices.reported_device_id（tokens.device_id 整数外键指向 devices.id）
    /// - Tokens.DeviceName        → devices.reported_name（读取时 COALESCE(custom_name, reported_name)，
    ///                              与旧逻辑 "CustomName 非空则覆盖 DeviceName" 等价）
    /// - Tokens.UserId (GUID 字符串) → tokens.user_id 整数（local_users.id，经 GUID↔int 翻译层）
    /// - Tokens.UserName          → 读取时 JOIN local_users 取 data->>'Name'（旧表存创建时快照，改为实时值）
    /// </summary>
    public class PgAuthenticationRepository : BasePgRepository, IAuthenticationRepository
    {
        public PgAuthenticationRepository(ILogger logger, string connectionString)
            : base(logger, connectionString)
        {
        }

        /// <summary>
        /// 初始化：schema 由 PgSchemaInitializer 统一建好，这里只做存在性校验。
        /// </summary>
        public void Initialize()
        {
            using (var connection = CreateConnection())
            {
                if (!TableExists(connection, "tokens") || !TableExists(connection, "devices"))
                {
                    throw new InvalidOperationException("devices/tokens tables do not exist. Schema initialization failed.");
                }
            }
        }

        /// <summary>
        /// 创建令牌（旧 Create：插入 Tokens 行；IsActive 恒为 true）。
        /// 设备行不存在时自动注册（reported_device_id 唯一索引支持 ON CONFLICT upsert）。
        /// </summary>
        public void Create(AuthenticationInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException("info");
            }

            RunInTransaction((connection, transaction) =>
            {
                var deviceId = GetOrInsertDevice(connection, transaction, info.DeviceId, info.DeviceName);

                using (var cmd = new NpgsqlCommand(
                    @"INSERT INTO tokens
                        (access_token, device_id, app_name, app_version, user_id, is_active, date_created, date_last_activity)
                      VALUES (@access_token, @device_id, @app_name, @app_version, @user_id, @is_active, @date_created, @date_last_activity)
                      RETURNING id",
                    connection, transaction))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("access_token", NpgsqlDbType.Text) { Value = info.AccessToken });
                    cmd.Parameters.Add(new NpgsqlParameter("device_id", NpgsqlDbType.Integer) { Value = deviceId });
                    cmd.Parameters.Add(new NpgsqlParameter("app_name", NpgsqlDbType.Text) { Value = (object)info.AppName ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("app_version", NpgsqlDbType.Text) { Value = (object)info.AppVersion ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = (object)ResolveInternalUserId(connection, transaction, info.UserId) ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("is_active", NpgsqlDbType.Boolean) { Value = true });
                    cmd.Parameters.Add(new NpgsqlParameter("date_created", NpgsqlDbType.TimestampTz) { Value = ToUtcDateTime(info.DateCreated) });
                    cmd.Parameters.Add(new NpgsqlParameter("date_last_activity", NpgsqlDbType.TimestampTz) { Value = ToUtcDateTime(info.DateLastActivity) });

                    info.Id = Convert.ToInt64(cmd.ExecuteScalar());
                }
            });
        }

        /// <summary>
        /// 更新令牌（旧 Update：按 Id 更新 Tokens 全部字段；DeviceName 更新到设备行）。
        /// </summary>
        public void Update(AuthenticationInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException("info");
            }

            RunInTransaction((connection, transaction) =>
            {
                using (var cmd = new NpgsqlCommand(
                    @"UPDATE tokens SET
                        access_token = @access_token,
                        app_name = @app_name,
                        app_version = @app_version,
                        user_id = @user_id,
                        date_created = @date_created,
                        date_last_activity = @date_last_activity
                      WHERE id = @id",
                    connection, transaction))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = info.Id });
                    cmd.Parameters.Add(new NpgsqlParameter("access_token", NpgsqlDbType.Text) { Value = info.AccessToken });
                    cmd.Parameters.Add(new NpgsqlParameter("app_name", NpgsqlDbType.Text) { Value = (object)info.AppName ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("app_version", NpgsqlDbType.Text) { Value = (object)info.AppVersion ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = (object)ResolveInternalUserId(connection, transaction, info.UserId) ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("date_created", NpgsqlDbType.TimestampTz) { Value = ToUtcDateTime(info.DateCreated) });
                    cmd.Parameters.Add(new NpgsqlParameter("date_last_activity", NpgsqlDbType.TimestampTz) { Value = ToUtcDateTime(info.DateLastActivity) });

                    cmd.ExecuteNonQuery();
                }

                // 旧实现把 DeviceName 快照存进 Tokens；新 schema 设备名归 devices 管
                using (var cmd = new NpgsqlCommand(
                    @"UPDATE devices SET reported_name = @reported_name
                      WHERE id = (SELECT device_id FROM tokens WHERE id = @token_id)",
                    connection, transaction))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("token_id", NpgsqlDbType.Bigint) { Value = info.Id });
                    cmd.Parameters.Add(new NpgsqlParameter("reported_name", NpgsqlDbType.Text) { Value = (object)info.DeviceName ?? DBNull.Value });

                    cmd.ExecuteNonQuery();
                }
            });
        }

        /// <summary>
        /// 删除令牌（旧 Delete：按 Id 删除 Tokens 行，设备行保留）。
        /// </summary>
        public void Delete(AuthenticationInfo existing)
        {
            if (existing == null)
            {
                throw new ArgumentNullException("existing");
            }

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand("DELETE FROM tokens WHERE id = @id", connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = existing.Id });
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private const string BaseSelectText = @"
            SELECT t.id,
                   t.access_token,
                   d.reported_device_id,
                   t.app_name,
                   t.app_version,
                   COALESCE(d.custom_name, d.reported_name),
                   lu.guid,
                   lu.data->>'Name',
                   t.date_created,
                   t.date_last_activity,
                   d.custom_name
            FROM tokens t
            LEFT JOIN devices d ON d.id = t.device_id
            LEFT JOIN local_users lu ON lu.id = t.user_id";

        /// <summary>
        /// 查询令牌（旧 Get：AccessToken/DeviceId/UserId/HasUser 过滤 + DateLastActivity 倒序 + 分页 + 总数）。
        /// </summary>
        public QueryResult<AuthenticationInfo> Get(AuthenticationInfoQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            var whereClauses = new List<string>();

            if (!string.IsNullOrEmpty(query.AccessToken))
            {
                whereClauses.Add("t.access_token = @access_token");
            }

            if (!string.IsNullOrEmpty(query.DeviceId))
            {
                whereClauses.Add("d.reported_device_id = @device_id");
            }

            if (!query.UserId.Equals(Guid.Empty))
            {
                // GUID → local_users.id 翻译（用户不存在时子查询为 NULL，恒不匹配，与旧行为一致）
                whereClauses.Add("t.user_id = (SELECT id FROM local_users WHERE guid = @user_guid)");
            }

            if (query.HasUser.HasValue)
            {
                whereClauses.Add(query.HasUser.Value ? "t.user_id IS NOT NULL" : "t.user_id IS NULL");
            }

            var whereText = whereClauses.Count == 0
                ? string.Empty
                : " WHERE " + string.Join(" AND ", whereClauses);

            var commandText = BaseSelectText + whereText + " ORDER BY t.date_last_activity DESC";

            // 旧分页语义：LIMIT + OFFSET（StartIndex）
            var offset = query.StartIndex ?? 0;
            var limit = query.Limit;

            if (limit.HasValue)
            {
                commandText += " LIMIT " + limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (offset > 0)
            {
                commandText += " LIMIT " + int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (offset > 0)
            {
                commandText += " OFFSET " + offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var list = new List<AuthenticationInfo>();
            var result = new QueryResult<AuthenticationInfo>();

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(commandText, connection))
                {
                    BindAuthenticationQueryParams(cmd, query);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(Get(reader));
                        }
                    }
                }

                using (var countCmd = new NpgsqlCommand(
                    "SELECT count(*) FROM tokens t LEFT JOIN devices d ON d.id = t.device_id" + whereText,
                    connection))
                {
                    BindAuthenticationQueryParams(countCmd, query);

                    result.TotalRecordCount = Convert.ToInt32(countCmd.ExecuteScalar());
                }
            }

            result.Items = list.ToArray();
            return result;
        }

        private static void BindAuthenticationQueryParams(NpgsqlCommand cmd, AuthenticationInfoQuery query)
        {
            if (!string.IsNullOrEmpty(query.AccessToken))
            {
                cmd.Parameters.Add(new NpgsqlParameter("access_token", NpgsqlDbType.Text) { Value = query.AccessToken });
            }

            if (!string.IsNullOrEmpty(query.DeviceId))
            {
                cmd.Parameters.Add(new NpgsqlParameter("device_id", NpgsqlDbType.Text) { Value = query.DeviceId });
            }

            if (!query.UserId.Equals(Guid.Empty))
            {
                cmd.Parameters.Add(new NpgsqlParameter("user_guid", NpgsqlDbType.Uuid) { Value = query.UserId });
            }
        }

        private AuthenticationInfo Get(NpgsqlDataReader reader)
        {
            var info = new AuthenticationInfo
            {
                Id = reader.GetInt64(0),
                AccessToken = reader.IsDBNull(1) ? null : reader.GetString(1)
            };

            if (!reader.IsDBNull(2))
            {
                info.DeviceId = reader.GetString(2);
            }

            if (!reader.IsDBNull(3))
            {
                info.AppName = reader.GetString(3);
            }

            if (!reader.IsDBNull(4))
            {
                info.AppVersion = reader.GetString(4);
            }

            if (!reader.IsDBNull(5))
            {
                info.DeviceName = reader.GetString(5);
            }

            if (!reader.IsDBNull(6))
            {
                info.UserId = reader.GetGuid(6);
            }

            if (!reader.IsDBNull(7))
            {
                info.UserName = reader.GetString(7);
            }

            info.DateCreated = reader.GetDateTime(8);

            if (!reader.IsDBNull(9))
            {
                info.DateLastActivity = reader.GetDateTime(9);
            }
            else
            {
                info.DateLastActivity = info.DateCreated;
            }

            return info;
        }

        /// <summary>
        /// 读取设备自定义名（旧 GetDeviceOptions：按设备 ID 查 CustomName）。
        /// </summary>
        public DeviceOptions GetDeviceOptions(string deviceId)
        {
            var result = new DeviceOptions();

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(
                    "SELECT custom_name FROM devices WHERE reported_device_id = @device_id",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("device_id", NpgsqlDbType.Text) { Value = deviceId });

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read() && !reader.IsDBNull(0))
                        {
                            result.CustomName = reader.GetString(0);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 更新设备自定义名（旧 UpdateDeviceOptions：replace into 保留 Capabilities）。
        /// 设备行不存在时创建占位行（reported_name='Unknown'，与旧迁移默认值一致）。
        /// </summary>
        public void UpdateDeviceOptions(string deviceId, DeviceOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(
                    @"INSERT INTO devices (reported_device_id, reported_name, custom_name)
                      VALUES (@device_id, 'Unknown', @custom_name)
                      ON CONFLICT (reported_device_id) DO UPDATE SET custom_name = EXCLUDED.custom_name",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("device_id", NpgsqlDbType.Text) { Value = deviceId });
                    cmd.Parameters.Add(new NpgsqlParameter("custom_name", NpgsqlDbType.Text)
                    {
                        Value = string.IsNullOrWhiteSpace(options.CustomName) ? (object)DBNull.Value : options.CustomName
                    });

                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 设备 upsert：不存在则注册，存在则刷新 reported_name；返回 devices.id。
        /// </summary>
        private static int GetOrInsertDevice(NpgsqlConnection connection, NpgsqlTransaction transaction, string deviceId, string deviceName)
        {
            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO devices (reported_device_id, reported_name)
                  VALUES (@device_id, @reported_name)
                  ON CONFLICT (reported_device_id) DO UPDATE SET reported_name = EXCLUDED.reported_name
                  RETURNING id",
                connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("device_id", NpgsqlDbType.Text) { Value = deviceId });
                cmd.Parameters.Add(new NpgsqlParameter("reported_name", NpgsqlDbType.Text)
                {
                    Value = string.IsNullOrWhiteSpace(deviceName) ? (object)"Unknown" : deviceName
                });

                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// GUID → local_users.id（空 GUID 或用户不存在返回 null）。
        /// </summary>
        private static long? ResolveInternalUserId(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId)
        {
            if (userId.Equals(Guid.Empty))
            {
                return null;
            }

            using (var cmd = new NpgsqlCommand(
                "SELECT id FROM local_users WHERE guid = @guid",
                connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = userId });

                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? (long?)null : Convert.ToInt64(result);
            }
        }
    }
}
