using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PostgreSQL 显示偏好仓储（display_preferences 表：item_id UUID + user_id INTEGER + client + data JSONB）。
    /// 对照旧 SqliteDisplayPreferencesRepository（userdisplaypreferences 表）逐方法保留语义：
    /// - 旧 id/userId 均为 GUID blob → 新 item_id 仍为 UUID，userId 翻译为 local_users.id
    /// - 旧 GetDisplayPreferences 对不存在的 id 返回 Id=GetMD5(id) 的新对象（保留）
    /// </summary>
    public class PgDisplayPreferencesRepository : BasePgRepository, IDisplayPreferencesRepository
    {
        private readonly IJsonSerializer _jsonSerializer;

        public PgDisplayPreferencesRepository(ILogger logger, IJsonSerializer jsonSerializer, string connectionString)
            : base(logger, connectionString)
        {
            _jsonSerializer = jsonSerializer;
        }

        /// <summary>
        /// 初始化：schema 由 PgSchemaInitializer 统一建好（补充 DDL §5.2-5），这里只做存在性校验。
        /// </summary>
        public void Initialize()
        {
            using (var connection = CreateConnection())
            {
                if (!TableExists(connection, "display_preferences"))
                {
                    throw new InvalidOperationException("display_preferences table does not exist. Schema initialization failed.");
                }
            }
        }

        /// <summary>
        /// 保存单条显示偏好（旧 SaveDisplayPreferences：replace into (id, userId, client, data)）。
        /// </summary>
        public void SaveDisplayPreferences(DisplayPreferences displayPreferences, string userId, string client, CancellationToken cancellationToken)
        {
            SaveDisplayPreferences(displayPreferences, new Guid(userId), client, cancellationToken);
        }

        public void SaveDisplayPreferences(DisplayPreferences displayPreferences, Guid userId, string client, CancellationToken cancellationToken)
        {
            if (displayPreferences == null)
            {
                throw new ArgumentNullException("displayPreferences");
            }
            if (string.IsNullOrEmpty(displayPreferences.Id))
            {
                throw new ArgumentNullException("displayPreferences.Id");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var serialized = _jsonSerializer.SerializeToString(displayPreferences);
            var itemId = new Guid(displayPreferences.Id);

            using (var connection = CreateConnection())
            {
                var internalUserId = ResolveInternalUserId(connection, userId);

                if (internalUserId == null)
                {
                    throw new InvalidOperationException("User not found: " + userId.ToString("N"));
                }

                using (var cmd = new NpgsqlCommand(
                    @"INSERT INTO display_preferences (item_id, user_id, client, data)
                      VALUES (@item_id, @user_id, @client, @data)
                      ON CONFLICT (item_id, user_id, client) DO UPDATE SET data = EXCLUDED.data",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Uuid) { Value = itemId });
                    cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = internalUserId });
                    cmd.Parameters.Add(new NpgsqlParameter("client", NpgsqlDbType.Text) { Value = client });
                    cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = serialized });

                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 批量保存（旧 SaveAllDisplayPreferences：单事务逐条 replace，client 取自对象自身）。
        /// </summary>
        public void SaveAllDisplayPreferences(IEnumerable<DisplayPreferences> displayPreferences, Guid userId, CancellationToken cancellationToken)
        {
            if (displayPreferences == null)
            {
                throw new ArgumentNullException("displayPreferences");
            }

            cancellationToken.ThrowIfCancellationRequested();

            RunInTransaction((connection, transaction) =>
            {
                var internalUserId = ResolveInternalUserId(connection, transaction, userId);

                if (internalUserId == null)
                {
                    throw new InvalidOperationException("User not found: " + userId.ToString("N"));
                }

                foreach (var displayPreference in displayPreferences)
                {
                    var serialized = _jsonSerializer.SerializeToString(displayPreference);
                    var itemId = new Guid(displayPreference.Id);
                    var client = displayPreference.Client;

                    using (var cmd = new NpgsqlCommand(
                        @"INSERT INTO display_preferences (item_id, user_id, client, data)
                          VALUES (@item_id, @user_id, @client, @data)
                          ON CONFLICT (item_id, user_id, client) DO UPDATE SET data = EXCLUDED.data",
                        connection, transaction))
                    {
                        cmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Uuid) { Value = itemId });
                        cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = internalUserId });
                        cmd.Parameters.Add(new NpgsqlParameter("client", NpgsqlDbType.Text) { Value = client ?? string.Empty });
                        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = serialized });

                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// 读取单条显示偏好（旧 GetDisplayPreferences：不存在时返回 Id=GetMD5(id) 的默认对象）。
        /// </summary>
        public DisplayPreferences GetDisplayPreferences(string displayPreferencesId, string userId, string client)
        {
            return GetDisplayPreferences(displayPreferencesId, new Guid(userId), client);
        }

        public DisplayPreferences GetDisplayPreferences(string displayPreferencesId, Guid userId, string client)
        {
            if (string.IsNullOrEmpty(displayPreferencesId))
            {
                throw new ArgumentNullException("displayPreferencesId");
            }

            var guidId = GetMd5(displayPreferencesId);

            using (var connection = CreateConnection())
            {
                var internalUserId = ResolveInternalUserId(connection, userId);

                if (internalUserId != null)
                {
                    using (var cmd = new NpgsqlCommand(
                        @"SELECT data::text FROM display_preferences
                          WHERE item_id = @item_id AND user_id = @user_id AND client = @client",
                        connection))
                    {
                        cmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Uuid) { Value = guidId });
                        cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = internalUserId });
                        cmd.Parameters.Add(new NpgsqlParameter("client", NpgsqlDbType.Text) { Value = client });

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return _jsonSerializer.DeserializeFromString<DisplayPreferences>(reader.GetString(0));
                            }
                        }
                    }
                }
            }

            return new DisplayPreferences
            {
                Id = guidId.ToString("N")
            };
        }

        /// <summary>
        /// 读取用户全部显示偏好（旧 GetAllDisplayPreferences）。
        /// </summary>
        public IEnumerable<DisplayPreferences> GetAllDisplayPreferences(Guid userId)
        {
            var list = new List<DisplayPreferences>();

            using (var connection = CreateConnection())
            {
                var internalUserId = ResolveInternalUserId(connection, userId);

                if (internalUserId == null)
                {
                    return list;
                }

                using (var cmd = new NpgsqlCommand(
                    "SELECT data::text FROM display_preferences WHERE user_id = @user_id",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = internalUserId });

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(_jsonSerializer.DeserializeFromString<DisplayPreferences>(reader.GetString(0)));
                        }
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// 旧 BaseExtensions.GetMD5 的本地实现（CryptographyProvider.GetMD5：
        /// MD5(UTF-16LE 字节) 前 16 字节构造 Guid），避免依赖进程级静态注入。
        /// </summary>
        private static Guid GetMd5(string str)
        {
            using (var provider = MD5.Create())
            {
                var hash = provider.ComputeHash(Encoding.Unicode.GetBytes(str));
                return new Guid(hash);
            }
        }

        /// <summary>
        /// 用户 GUID → local_users.id。
        /// </summary>
        private static int? ResolveInternalUserId(NpgsqlConnection connection, Guid userId)
        {
            using (var cmd = new NpgsqlCommand("SELECT id FROM local_users WHERE guid = @guid", connection))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = userId });

                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? (int?)null : (int)result;
            }
        }

        private static int? ResolveInternalUserId(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId)
        {
            using (var cmd = new NpgsqlCommand("SELECT id FROM local_users WHERE guid = @guid", connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = userId });

                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? (int?)null : (int)result;
            }
        }
    }
}
