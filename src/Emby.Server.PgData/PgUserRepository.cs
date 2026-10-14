using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PostgreSQL 用户仓储（local_users 表：id SERIAL + guid UUID + data JSONB）。
    /// 域模型 User 的 GUID ↔ 整数 id 翻译在此完成。
    /// 对照旧 SqliteUserRepository（LocalUsersv2 表）逐方法保留语义。
    /// </summary>
    public class PgUserRepository : BasePgRepository, IUserRepository
    {
        private readonly IJsonSerializer _jsonSerializer;

        public PgUserRepository(ILogger logger, IJsonSerializer jsonSerializer, string connectionString)
            : base(logger, connectionString)
        {
            _jsonSerializer = jsonSerializer;
        }

        /// <summary>
        /// 初始化：schema 由 PgSchemaInitializer 统一建好，这里只做存在性校验。
        /// </summary>
        public void Initialize()
        {
            using (var connection = CreateConnection())
            {
                if (!TableExists(connection, "local_users"))
                {
                    throw new InvalidOperationException("local_users table does not exist. Schema initialization failed.");
                }
            }
        }

        /// <summary>
        /// 创建用户（旧 CreateUser：插入后回读并填充 InternalId）。
        /// </summary>
        public void CreateUser(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            var serialized = _jsonSerializer.SerializeToString(user);

            RunInTransaction((connection, transaction) =>
            {
                using (var cmd = new NpgsqlCommand(
                    "INSERT INTO local_users (guid, data) VALUES (@guid, @data) RETURNING id",
                    connection, transaction))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = user.Id });
                    cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = serialized });

                    user.InternalId = Convert.ToInt64(cmd.ExecuteScalar());
                }
            });
        }

        /// <summary>
        /// 更新用户（旧 UpdateUser：按 InternalId 更新 data）。
        /// </summary>
        public void UpdateUser(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            var serialized = _jsonSerializer.SerializeToString(user);

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(
                    "UPDATE local_users SET data = @data WHERE id = @id",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = serialized });
                    cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = user.InternalId });

                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 检索全部用户（旧 RetrieveAllUsers；SQLite 按 rowid 返回，这里按 id 排序等价）。
        /// </summary>
        public List<User> RetrieveAllUsers()
        {
            var list = new List<User>();

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(
                    "SELECT id, guid, data::text FROM local_users ORDER BY id",
                    connection))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(GetUser(reader));
                        }
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// 删除用户（旧 DeleteUser：按 InternalId 删除）。
        /// </summary>
        public void DeleteUser(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(
                    "DELETE FROM local_users WHERE id = @id",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = user.InternalId });

                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 按 GUID 查询单个用户（旧私有方法 GetUser）。
        /// </summary>
        public User GetUser(Guid guid)
        {
            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(
                    "SELECT id, guid, data::text FROM local_users WHERE guid = @guid",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = guid });

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return GetUser(reader);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// GUID → local_users.id 翻译（供其他仓储解析 user_id 整数外键用）。
        /// </summary>
        public long? GetInternalId(Guid guid)
        {
            if (guid.Equals(Guid.Empty))
            {
                return null;
            }

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(
                    "SELECT id FROM local_users WHERE guid = @guid",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = guid });

                    var result = cmd.ExecuteScalar();
                    return result == null || result == DBNull.Value ? (long?)null : Convert.ToInt64(result);
                }
            }
        }

        private User GetUser(NpgsqlDataReader reader)
        {
            var id = reader.GetInt64(0);
            var guid = reader.GetGuid(1);
            var data = reader.GetString(2);

            var user = _jsonSerializer.DeserializeFromString<User>(data);
            user.InternalId = id;
            user.Id = guid;
            return user;
        }
    }
}
