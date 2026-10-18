using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PostgreSQL 用户数据仓储（user_datas + user_data_keys）。
    /// 全链路原生异步：所有命令走 ExecuteNonQueryAsync / ExecuteReaderAsync / ExecuteScalarAsync，
    /// 无任何同步 I/O 阻塞点。
    /// </summary>
    public class PgUserDataRepository : BasePgRepository, IUserDataRepository
    {
        public PgUserDataRepository(ILogger logger, string connectionString)
            : base(logger, connectionString)
        {
        }

        /// <summary>
        /// 初始化：schema 由 PgSchemaInitializer 统一建好，这里只做存在性校验。
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            var hasUserDatas = await TableExistsAsync(connection, "user_datas", cancellationToken).ConfigureAwait(false);
            var hasKeys = await TableExistsAsync(connection, "user_data_keys", cancellationToken).ConfigureAwait(false);

            if (!hasUserDatas || !hasKeys)
            {
                throw new InvalidOperationException("user_datas/user_data_keys tables do not exist. Schema initialization failed.");
            }
        }

        /// <summary>
        /// 保存单条用户数据（UPSERT，确定性加锁顺序由单行天然保证）。
        /// </summary>
        public Task SaveUserDataAsync(long internalUserId, string key, UserItemData userData, CancellationToken cancellationToken)
        {
            if (userData == null)
            {
                throw new ArgumentNullException(nameof(userData));
            }
            if (internalUserId <= 0)
            {
                throw new ArgumentNullException(nameof(internalUserId));
            }
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key));
            }

            return SaveAllUserDataAsync(internalUserId, new[] { userData }, cancellationToken);
        }

        /// <summary>
        /// 批量保存用户数据：单语句参数化 UNNEST 批量 UPSERT，1 次网络往返完成整批入库。
        /// 批次内按 Key 字典序排序，保证并发事务加锁顺序一致，杜绝 40P01 交叉死锁。
        /// </summary>
        public async Task SaveAllUserDataAsync(long internalUserId, UserItemData[] userData, CancellationToken cancellationToken)
        {
            if (userData == null)
            {
                throw new ArgumentNullException(nameof(userData));
            }
            if (internalUserId <= 0)
            {
                throw new ArgumentNullException(nameof(internalUserId));
            }

            var validUserData = userData
                .Where(u => u != null && !string.IsNullOrEmpty(u.Key))
                .OrderBy(u => u.Key, StringComparer.Ordinal)
                .ToList();

            if (validUserData.Count == 0)
            {
                return;
            }

            await RunInTransactionAsync(async (connection, transaction, ct) =>
            {
                // 1. 批量解析 user_data_key → id（单次 UNNEST 往返）
                var distinctKeys = validUserData
                    .Select(u => u.Key)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToArray();

                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    @"INSERT INTO user_data_keys (user_data_key)
                      SELECT unnest_key
                      FROM UNNEST(@keys) AS unnest_key
                      ON CONFLICT (user_data_key) DO UPDATE SET user_data_key = EXCLUDED.user_data_key",
                    ct,
                    new NpgsqlParameter("keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = distinctKeys }).ConfigureAwait(false);

                var keyMap = new Dictionary<string, int>(StringComparer.Ordinal);
                await using (var mapCmd = new NpgsqlCommand(
                    "SELECT id, user_data_key FROM user_data_keys WHERE user_data_key = ANY(@keys)",
                    connection,
                    transaction))
                {
                    mapCmd.Parameters.Add(new NpgsqlParameter("keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = distinctKeys });
                    await using var reader = await mapCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        keyMap[reader.GetString(1)] = reader.GetInt32(0);
                    }
                }

                // 2. 向量化 UNNEST 批量 UPSERT（网络往返严格为 1）
                var count = validUserData.Count;
                var keyIds = new int[count];
                var userIds = new int[count];
                var ratings = new object[count];
                var playeds = new bool[count];
                var playCounts = new int[count];
                var isFavorites = new bool[count];
                var positions = new long[count];
                var lastPlayedDates = new object[count];
                var audioStreamIndices = new object[count];
                var subtitleStreamIndices = new object[count];

                for (int i = 0; i < count; i++)
                {
                    var item = validUserData[i];
                    keyIds[i] = keyMap[item.Key];
                    userIds[i] = (int)internalUserId;
                    ratings[i] = item.Rating.HasValue ? (object)item.Rating.Value : DBNull.Value;
                    playeds[i] = item.Played;
                    playCounts[i] = item.PlayCount;
                    isFavorites[i] = item.IsFavorite;
                    positions[i] = item.PlaybackPositionTicks;
                    lastPlayedDates[i] = ToDbValue(item.LastPlayedDate);
                    audioStreamIndices[i] = item.AudioStreamIndex.HasValue ? (object)(short)item.AudioStreamIndex.Value : DBNull.Value;
                    subtitleStreamIndices[i] = item.SubtitleStreamIndex.HasValue ? (object)(short)item.SubtitleStreamIndex.Value : DBNull.Value;
                }

                const string batchSql = @"
                    INSERT INTO user_datas
                        (user_data_key_id, user_id, rating, played, play_count, is_favorite,
                         playback_position_ticks, last_played_date, audio_stream_index, subtitle_stream_index)
                    SELECT
                        t.k_id, t.u_id, t.r, t.p, t.pc, t.fav, t.pos, t.lpd, t.asi, t.ssi
                    FROM UNNEST(
                        @k_ids, @u_ids, @rs, @ps, @pcs, @favs, @poss, @lpds, @asis, @ssis
                    ) AS t(k_id, u_id, r, p, pc, fav, pos, lpd, asi, ssi)
                    ON CONFLICT (user_data_key_id, user_id) DO UPDATE SET
                        rating = EXCLUDED.rating,
                        played = EXCLUDED.played,
                        play_count = EXCLUDED.play_count,
                        is_favorite = EXCLUDED.is_favorite,
                        playback_position_ticks = EXCLUDED.playback_position_ticks,
                        last_played_date = EXCLUDED.last_played_date,
                        audio_stream_index = EXCLUDED.audio_stream_index,
                        subtitle_stream_index = EXCLUDED.subtitle_stream_index,
                        playstate_last_modified = now()";

                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    batchSql,
                    ct,
                    new NpgsqlParameter("k_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = keyIds },
                    new NpgsqlParameter("u_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = userIds },
                    new NpgsqlParameter("rs", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = ratings },
                    new NpgsqlParameter("ps", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = playeds },
                    new NpgsqlParameter("pcs", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = playCounts },
                    new NpgsqlParameter("favs", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = isFavorites },
                    new NpgsqlParameter("poss", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = positions },
                    new NpgsqlParameter("lpds", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = lastPlayedDates },
                    new NpgsqlParameter("asis", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = audioStreamIndices },
                    new NpgsqlParameter("ssis", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = subtitleStreamIndices }).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 读取单条用户数据（key + userId 精确匹配）。
        /// </summary>
        public async Task<UserItemData> GetUserDataAsync(long internalUserId, string key, CancellationToken cancellationToken = default)
        {
            if (internalUserId <= 0)
            {
                throw new ArgumentNullException(nameof(internalUserId));
            }
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key));
            }

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = new NpgsqlCommand(
                @"SELECT udk.user_data_key, ud.rating, ud.played, ud.play_count, ud.is_favorite,
                         ud.playback_position_ticks, ud.last_played_date, ud.audio_stream_index, ud.subtitle_stream_index
                  FROM user_datas ud
                  JOIN user_data_keys udk ON udk.id = ud.user_data_key_id
                  WHERE udk.user_data_key = @key AND ud.user_id = @user_id",
                connection);
            cmd.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Text) { Value = key });
            cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = (int)internalUserId });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return ReadRow(reader);
            }

            return null;
        }

        /// <summary>
        /// 旧语义：多 key 查询只取第一个 key。
        /// </summary>
        public Task<UserItemData> GetUserDataAsync(long internalUserId, List<string> keys, CancellationToken cancellationToken = default)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            if (keys.Count == 0)
            {
                return Task.FromResult<UserItemData>(null);
            }

            return GetUserDataAsync(internalUserId, keys[0], cancellationToken);
        }

        /// <summary>
        /// 读取用户全部用户数据（流式读取，不预分配大数组）。
        /// </summary>
        public async Task<List<UserItemData>> GetAllUserDataAsync(long internalUserId, CancellationToken cancellationToken = default)
        {
            if (internalUserId <= 0)
            {
                throw new ArgumentNullException(nameof(internalUserId));
            }

            var list = new List<UserItemData>();

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = new NpgsqlCommand(
                @"SELECT udk.user_data_key, ud.rating, ud.played, ud.play_count, ud.is_favorite,
                         ud.playback_position_ticks, ud.last_played_date, ud.audio_stream_index, ud.subtitle_stream_index
                  FROM user_datas ud
                  JOIN user_data_keys udk ON udk.id = ud.user_data_key_id
                  WHERE ud.user_id = @user_id",
                connection);
            cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = (int)internalUserId });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(ReadRow(reader));
            }

            return list;
        }

        /// <summary>
        /// 行 → UserItemData（逐字段对照旧 ReadRow）。
        /// </summary>
        private static UserItemData ReadRow(NpgsqlDataReader reader)
        {
            var userData = new UserItemData
            {
                Key = reader.GetString(0)
            };

            if (!reader.IsDBNull(1))
            {
                userData.Rating = reader.GetDouble(1);
            }

            userData.Played = reader.GetBoolean(2);
            userData.PlayCount = reader.GetInt32(3);
            userData.IsFavorite = reader.GetBoolean(4);
            userData.PlaybackPositionTicks = reader.GetInt64(5);

            if (!reader.IsDBNull(6))
            {
                userData.LastPlayedDate = reader.GetDateTime(6);
            }

            if (!reader.IsDBNull(7))
            {
                userData.AudioStreamIndex = reader.GetInt16(7);
            }

            if (!reader.IsDBNull(8))
            {
                userData.SubtitleStreamIndex = reader.GetInt16(8);
            }

            return userData;
        }
    }
}
