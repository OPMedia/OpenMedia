using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PostgreSQL 用户仓储（local_users 表：id BIGINT IDENTITY + guid UUID + data JSONB）。
    /// 全链路原生异步（无同步 I/O）。
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
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            if (!await TableExistsAsync(connection, "local_users", cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("local_users table does not exist. Schema initialization failed.");
            }
        }

        /// <summary>
        /// 创建用户（插入后回读 id 填充 InternalId）。
        /// </summary>
        public Task CreateUserAsync(User user, CancellationToken cancellationToken = default)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var serialized = _jsonSerializer.SerializeToString(user);

            return RunInTransactionAsync(async (connection, transaction, ct) =>
            {
                var id = await ExecuteScalarAsync(
                    connection,
                    transaction,
                    "INSERT INTO local_users (guid, data) VALUES (@guid, @data) RETURNING id",
                    ct,
                    new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = user.Id },
                    new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = serialized }).ConfigureAwait(false);

                user.InternalId = Convert.ToInt64(id);
            }, cancellationToken);
        }

        /// <summary>
        /// 更新用户（按 InternalId 更新 data）。
        /// </summary>
        public Task UpdateUserAsync(User user, CancellationToken cancellationToken = default)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var serialized = _jsonSerializer.SerializeToString(user);

            return RunInTransactionAsync((connection, transaction, ct) =>
                ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    "UPDATE local_users SET data = @data WHERE id = @id",
                    ct,
                    new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = serialized },
                    new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = user.InternalId }),
                cancellationToken);
        }

        /// <summary>
        /// 检索全部用户（按 id 排序，等价旧 rowid 顺序）。
        /// </summary>
        public async Task<List<User>> RetrieveAllUsersAsync(CancellationToken cancellationToken = default)
        {
            var list = new List<User>();

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = new NpgsqlCommand(
                "SELECT id, guid, data::text FROM local_users ORDER BY id",
                connection);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(ReadUser(reader));
            }

            return list;
        }

        /// <summary>
        /// 删除用户（按 InternalId）。
        /// </summary>
        public Task DeleteUserAsync(User user, CancellationToken cancellationToken = default)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            return RunInTransactionAsync((connection, transaction, ct) =>
                ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    "DELETE FROM local_users WHERE id = @id",
                    ct,
                    new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = user.InternalId }),
                cancellationToken);
        }

        /// <summary>
        /// 按 GUID 查询单个用户。
        /// </summary>
        public async Task<User> GetUserAsync(Guid guid, CancellationToken cancellationToken = default)
        {
            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = new NpgsqlCommand(
                "SELECT id, guid, data::text FROM local_users WHERE guid = @guid",
                connection);
            cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = guid });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return ReadUser(reader);
            }

            return null;
        }

        /// <summary>
        /// GUID → local_users.id 翻译（供其他仓储解析整数外键）。
        /// </summary>
        public async Task<long?> GetInternalIdAsync(Guid guid, CancellationToken cancellationToken = default)
        {
            if (guid.Equals(Guid.Empty))
            {
                return null;
            }

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            var result = await ExecuteScalarAsync(
                connection,
                null,
                "SELECT id FROM local_users WHERE guid = @guid",
                cancellationToken,
                new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = guid }).ConfigureAwait(false);

            return result == null || result == DBNull.Value ? (long?)null : Convert.ToInt64(result);
        }

        private User ReadUser(NpgsqlDataReader reader)
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
