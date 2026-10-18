using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PostgreSQL 显示偏好仓储（display_preferences：item_id UUID + user_id INTEGER + client + data JSONB）。
    /// 全链路原生异步；批量保存走单条 UNNEST UPSERT。
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
        /// 初始化存在性校验。
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            if (!await TableExistsAsync(connection, "display_preferences", cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("display_preferences table does not exist. Schema initialization failed.");
            }
        }

        /// <summary>
        /// 保存单条显示偏好（UPSERT）。
        /// </summary>
        public Task SaveDisplayPreferencesAsync(DisplayPreferences displayPreferences, Guid userId, string client, CancellationToken cancellationToken)
        {
            if (displayPreferences == null)
            {
                throw new ArgumentNullException(nameof(displayPreferences));
            }
            if (string.IsNullOrEmpty(displayPreferences.Id))
            {
                throw new ArgumentNullException(nameof(displayPreferences.Id));
            }

            return SaveAllDisplayPreferencesAsync(new[] { displayPreferences }, userId, cancellationToken);
        }

        /// <summary>
        /// 批量保存显示偏好（单条 UNNEST UPSERT，1 次往返）。
        /// </summary>
        public async Task SaveAllDisplayPreferencesAsync(IEnumerable<DisplayPreferences> displayPreferences, Guid userId, CancellationToken cancellationToken)
        {
            if (displayPreferences == null)
            {
                throw new ArgumentNullException(nameof(displayPreferences));
            }

            var items = new List<DisplayPreferences>();
            foreach (var dp in displayPreferences)
            {
                if (dp == null || string.IsNullOrEmpty(dp.Id))
                {
                    continue;
                }
                items.Add(dp);
            }

            if (items.Count == 0)
            {
                return;
            }

            // 确定性排序（item_id, client），保证并发事务加锁顺序一致
            items.Sort((a, b) =>
            {
                var c = string.CompareOrdinal(a.Id, b.Id);
                return c != 0 ? c : string.CompareOrdinal(a.Client ?? string.Empty, b.Client ?? string.Empty);
            });

            await RunInTransactionAsync(async (connection, transaction, ct) =>
            {
                var internalUserId = await ResolveInternalUserIdAsync(connection, transaction, userId, ct).ConfigureAwait(false);

                if (internalUserId == null)
                {
                    throw new InvalidOperationException("User not found: " + userId.ToString("N"));
                }

                var count = items.Count;
                var itemIds = new Guid[count];
                var userIds = new int[count];
                var clients = new string[count];
                var datas = new string[count];

                for (int i = 0; i < count; i++)
                {
                    itemIds[i] = new Guid(items[i].Id);
                    userIds[i] = internalUserId.Value;
                    clients[i] = items[i].Client ?? string.Empty;
                    datas[i] = _jsonSerializer.SerializeToString(items[i]);
                }

                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    @"INSERT INTO display_preferences (item_id, user_id, client, data)
                      SELECT u.item_id, u.user_id, u.client, u.data
                      FROM UNNEST(@item_ids, @user_ids, @clients, @datas) AS u(item_id, user_id, client, data)
                      ON CONFLICT (item_id, user_id, client) DO UPDATE SET data = EXCLUDED.data",
                    ct,
                    new NpgsqlParameter("item_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = itemIds },
                    new NpgsqlParameter("user_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = userIds },
                    new NpgsqlParameter("clients", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = clients },
                    new NpgsqlParameter("datas", NpgsqlDbType.Array | NpgsqlDbType.Jsonb) { Value = datas }).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 读取单条显示偏好（不存在时返回 Id=MD5(id) 的默认对象）。
        /// </summary>
        public async Task<DisplayPreferences> GetDisplayPreferencesAsync(string displayPreferencesId, Guid userId, string client, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(displayPreferencesId))
            {
                throw new ArgumentNullException(nameof(displayPreferencesId));
            }

            var guidId = GetMd5(displayPreferencesId);

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            var internalUserId = await ResolveInternalUserIdAsync(connection, null, userId, cancellationToken).ConfigureAwait(false);
            if (internalUserId != null)
            {
                await using var cmd = new NpgsqlCommand(
                    @"SELECT data::text FROM display_preferences
                      WHERE item_id = @item_id AND user_id = @user_id AND client = @client",
                    connection);
                cmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Uuid) { Value = guidId });
                cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = internalUserId });
                cmd.Parameters.Add(new NpgsqlParameter("client", NpgsqlDbType.Text) { Value = client });

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return _jsonSerializer.DeserializeFromString<DisplayPreferences>(reader.GetString(0));
                }
            }

            return new DisplayPreferences
            {
                Id = guidId.ToString("N")
            };
        }

        /// <summary>
        /// 读取用户全部显示偏好。
        /// </summary>
        public async Task<List<DisplayPreferences>> GetAllDisplayPreferencesAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var list = new List<DisplayPreferences>();

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            var internalUserId = await ResolveInternalUserIdAsync(connection, null, userId, cancellationToken).ConfigureAwait(false);
            if (internalUserId == null)
            {
                return list;
            }

            await using var cmd = new NpgsqlCommand(
                "SELECT data::text FROM display_preferences WHERE user_id = @user_id",
                connection);
            cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = internalUserId });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(_jsonSerializer.DeserializeFromString<DisplayPreferences>(reader.GetString(0)));
            }

            return list;
        }

        /// <summary>
        /// 旧 BaseExtensions.GetMD5 本地实现（MD5(UTF-16LE) 前 16 字节构造 Guid）。
        /// </summary>
        private static Guid GetMd5(string str)
        {
            using var provider = MD5.Create();
            var hash = provider.ComputeHash(Encoding.Unicode.GetBytes(str));
            return new Guid(hash);
        }

        /// <summary>
        /// 用户 GUID → local_users.id（异步）。
        /// </summary>
        private static async Task<int?> ResolveInternalUserIdAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid userId,
            CancellationToken cancellationToken)
        {
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
