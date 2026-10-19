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
    /// PgItemRepository — 继承标签重建（全异步）。
    ///
    /// 注：ancestor_ids 闭包表由 <see cref="PgItemRepository.RebuildAncestorsAsync"/>
    /// 在 SaveItems 的全局阶段用递归 CTE 统一重建（含真实层级 distance），
    /// 本文件不再提供逐项闭包写入入口。
    /// </summary>
    public partial class PgItemRepository
    {
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
