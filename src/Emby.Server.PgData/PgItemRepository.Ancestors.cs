using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PgItemRepository — ancestor_ids 闭包表维护 + UpdateInheritedValues（全异步）。
    /// </summary>
    public partial class PgItemRepository
    {
        /// <summary>
        /// 更新祖先闭包表（异步；先删后插，单条 UNNEST 插入）。
        /// </summary>
        internal async Task UpdateAncestorsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid itemId,
            List<Guid> ancestorIds,
            CancellationToken cancellationToken)
        {
            if (itemId.Equals(Guid.Empty))
            {
                throw new ArgumentNullException(nameof(itemId));
            }

            if (ancestorIds == null)
            {
                throw new ArgumentNullException(nameof(ancestorIds));
            }

            CheckDisposed();

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                "DELETE FROM ancestor_ids WHERE item_id = (SELECT id FROM media_items WHERE guid = @guid LIMIT 1)",
                cancellationToken,
                new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = itemId }).ConfigureAwait(false);

            if (ancestorIds.Count == 0)
            {
                return;
            }

            // 单条语句完成：GUID 集合 → 整数 id 解析 → 批量插入
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                @"INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
                  SELECT c.id, p.id, 0
                  FROM UNNEST(@child_guids, @ancestor_guids) AS u(cg, ag)
                  JOIN media_items c ON c.guid = u.cg
                  JOIN media_items p ON p.guid = u.ag
                  ON CONFLICT (item_id, ancestor_id) DO NOTHING",
                cancellationToken,
                new NpgsqlParameter("child_guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
                {
                    Value = ancestorIds.Select(_ => itemId).ToArray()
                },
                new NpgsqlParameter("ancestor_guids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
                {
                    Value = ancestorIds.OrderBy(g => g).ToArray()
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// 更新继承值（异步）：重建 extradata_type_id=6（继承标签）。
        /// </summary>
        public Task UpdateInheritedValuesAsync(CancellationToken cancellationToken)
        {
            CheckDisposed();

            return RunInTransactionAsync(async (connection, transaction, ct) =>
            {
                await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM item_extradata WHERE extradata_type_id = 6", ct).ConfigureAwait(false);

                // 自身标签 → 继承标签
                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    @"INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
                      SELECT item_id, 6, value, clean_value
                      FROM item_extradata
                      WHERE extradata_type_id = 4
                      ON CONFLICT (item_id, extradata_type_id, value) DO NOTHING",
                    ct).ConfigureAwait(false);

                // 祖先标签 → 继承标签
                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    @"INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
                      SELECT a.item_id, 6, iv.value, iv.clean_value
                      FROM ancestor_ids a
                      JOIN item_extradata iv ON (a.ancestor_id = iv.item_id AND iv.extradata_type_id = 4)
                      WHERE iv.value IS NOT NULL
                      ON CONFLICT (item_id, extradata_type_id, value) DO NOTHING",
                    ct).ConfigureAwait(false);
            }, cancellationToken);
        }
    }
}
