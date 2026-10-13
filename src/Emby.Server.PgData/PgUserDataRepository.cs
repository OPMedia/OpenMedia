using System;
using System.Collections.Generic;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PostgreSQL 用户数据仓储（user_datas + user_data_keys）。
    /// 对照旧 SqliteUserDataRepository（UserDatas 表）逐方法保留语义：
    /// 旧 (key, userId) 主键 → 新 (user_data_key_id, user_id)，key 字符串经字典表翻译为整数 id。
    /// 高频更新（播放进度）走 INSERT ... ON CONFLICT DO UPDATE（UPSERT，等价旧 replace into）。
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
        public void Initialize()
        {
            using (var connection = CreateConnection())
            {
                if (!TableExists(connection, "user_datas") || !TableExists(connection, "user_data_keys"))
                {
                    throw new InvalidOperationException("user_datas/user_data_keys tables do not exist. Schema initialization failed.");
                }
            }
        }

        /// <summary>
        /// 保存单条用户数据（旧 SaveUserData → PersistUserData → replace into）。
        /// </summary>
        public void SaveUserData(long internalUserId, string key, UserItemData userData, CancellationToken cancellationToken)
        {
            if (userData == null)
            {
                throw new ArgumentNullException("userData");
            }
            if (internalUserId <= 0)
            {
                throw new ArgumentNullException("internalUserId");
            }
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException("key");
            }

            cancellationToken.ThrowIfCancellationRequested();

            RunInTransaction((connection, transaction) =>
            {
                SaveUserData(connection, transaction, internalUserId, key, userData);
            });
        }

        /// <summary>
        /// 批量保存用户数据（旧 SaveAllUserData → 单事务逐条 replace）。
        /// </summary>
        public void SaveAllUserData(long internalUserId, UserItemData[] userData, CancellationToken cancellationToken)
        {
            if (userData == null)
            {
                throw new ArgumentNullException("userData");
            }
            if (internalUserId <= 0)
            {
                throw new ArgumentNullException("internalUserId");
            }

            cancellationToken.ThrowIfCancellationRequested();

            RunInTransaction((connection, transaction) =>
            {
                foreach (var userItemData in userData)
                {
                    SaveUserData(connection, transaction, internalUserId, userItemData.Key, userItemData);
                }
            });
        }

        private void SaveUserData(NpgsqlConnection connection, NpgsqlTransaction transaction, long internalUserId, string key, UserItemData userData)
        {
            var keyId = GetOrInsertUserDataKey(connection, transaction, key);

            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO user_datas
                    (user_data_key_id, user_id, rating, played, play_count, is_favorite,
                     playback_position_ticks, last_played_date, audio_stream_index, subtitle_stream_index)
                  VALUES
                    (@user_data_key_id, @user_id, @rating, @played, @play_count, @is_favorite,
                     @playback_position_ticks, @last_played_date, @audio_stream_index, @subtitle_stream_index)
                  ON CONFLICT (user_data_key_id, user_id) DO UPDATE SET
                    rating = EXCLUDED.rating,
                    played = EXCLUDED.played,
                    play_count = EXCLUDED.play_count,
                    is_favorite = EXCLUDED.is_favorite,
                    playback_position_ticks = EXCLUDED.playback_position_ticks,
                    last_played_date = EXCLUDED.last_played_date,
                    audio_stream_index = EXCLUDED.audio_stream_index,
                    subtitle_stream_index = EXCLUDED.subtitle_stream_index,
                    playstate_last_modified = now()",
                connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("user_data_key_id", NpgsqlDbType.Integer) { Value = keyId });
                cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = (int)internalUserId });
                cmd.Parameters.Add(new NpgsqlParameter("rating", NpgsqlDbType.Double)
                {
                    Value = userData.Rating.HasValue ? (object)userData.Rating.Value : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("played", NpgsqlDbType.Boolean) { Value = userData.Played });
                cmd.Parameters.Add(new NpgsqlParameter("play_count", NpgsqlDbType.Integer) { Value = userData.PlayCount });
                cmd.Parameters.Add(new NpgsqlParameter("is_favorite", NpgsqlDbType.Boolean) { Value = userData.IsFavorite });
                cmd.Parameters.Add(new NpgsqlParameter("playback_position_ticks", NpgsqlDbType.Bigint) { Value = userData.PlaybackPositionTicks });
                cmd.Parameters.Add(new NpgsqlParameter("last_played_date", NpgsqlDbType.TimestampTz)
                {
                    Value = userData.LastPlayedDate.HasValue ? (object)ToUtcDateTime(userData.LastPlayedDate.Value) : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("audio_stream_index", NpgsqlDbType.Smallint)
                {
                    Value = userData.AudioStreamIndex.HasValue ? (object)userData.AudioStreamIndex.Value : DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter("subtitle_stream_index", NpgsqlDbType.Smallint)
                {
                    Value = userData.SubtitleStreamIndex.HasValue ? (object)userData.SubtitleStreamIndex.Value : DBNull.Value
                });

                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// key 字符串 → user_data_keys.id（不存在则插入；DO UPDATE 保证 RETURNING 始终返回行）。
        /// </summary>
        private static int GetOrInsertUserDataKey(NpgsqlConnection connection, NpgsqlTransaction transaction, string key)
        {
            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO user_data_keys (user_data_key)
                  VALUES (@user_data_key)
                  ON CONFLICT (user_data_key) DO UPDATE SET user_data_key = EXCLUDED.user_data_key
                  RETURNING id",
                connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("user_data_key", NpgsqlDbType.Text) { Value = key });
                return (int)cmd.ExecuteScalar();
            }
        }

        private static int? GetUserDataKeyId(NpgsqlConnection connection, string key)
        {
            using (var cmd = new NpgsqlCommand(
                "SELECT id FROM user_data_keys WHERE user_data_key = @user_data_key",
                connection))
            {
                cmd.Parameters.Add(new NpgsqlParameter("user_data_key", NpgsqlDbType.Text) { Value = key });

                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? (int?)null : (int)result;
            }
        }

        /// <summary>
        /// 读取单条用户数据（旧 GetUserData：key + userId 精确匹配）。
        /// </summary>
        public UserItemData GetUserData(long internalUserId, string key)
        {
            if (internalUserId <= 0)
            {
                throw new ArgumentNullException("internalUserId");
            }
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException("key");
            }

            using (var connection = CreateConnection())
            {
                var keyId = GetUserDataKeyId(connection, key);

                if (keyId == null)
                {
                    return null;
                }

                using (var cmd = new NpgsqlCommand(
                    @"SELECT udk.user_data_key, ud.rating, ud.played, ud.play_count, ud.is_favorite,
                             ud.playback_position_ticks, ud.last_played_date, ud.audio_stream_index, ud.subtitle_stream_index
                      FROM user_datas ud
                      JOIN user_data_keys udk ON udk.id = ud.user_data_key_id
                      WHERE ud.user_data_key_id = @user_data_key_id AND ud.user_id = @user_id",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("user_data_key_id", NpgsqlDbType.Integer) { Value = keyId });
                    cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = (int)internalUserId });

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return ReadRow(reader);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 旧语义：多 key 查询只取第一个 key。
        /// </summary>
        public UserItemData GetUserData(long internalUserId, List<string> keys)
        {
            if (keys == null)
            {
                throw new ArgumentNullException("keys");
            }

            if (keys.Count == 0)
            {
                return null;
            }

            return GetUserData(internalUserId, keys[0]);
        }

        /// <summary>
        /// 读取用户全部用户数据（旧 GetAllUserData）。
        /// </summary>
        public List<UserItemData> GetAllUserData(long internalUserId)
        {
            if (internalUserId <= 0)
            {
                throw new ArgumentNullException("internalUserId");
            }

            var list = new List<UserItemData>();

            using (var connection = CreateConnection())
            {
                using (var cmd = new NpgsqlCommand(
                    @"SELECT udk.user_data_key, ud.rating, ud.played, ud.play_count, ud.is_favorite,
                             ud.playback_position_ticks, ud.last_played_date, ud.audio_stream_index, ud.subtitle_stream_index
                      FROM user_datas ud
                      JOIN user_data_keys udk ON udk.id = ud.user_data_key_id
                      WHERE ud.user_id = @user_id",
                    connection))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Integer) { Value = (int)internalUserId });

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(ReadRow(reader));
                        }
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// 行 → UserItemData（旧 ReadRow 逐字段对照）。
        /// </summary>
        private static UserItemData ReadRow(NpgsqlDataReader reader)
        {
            var userData = new UserItemData();

            userData.Key = reader.GetString(0);

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
