using System;
using System.Collections.Generic;
using System.Threading;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PgItemRepository — ancestor_ids 闭包表维护 + UpdateInheritedValues。
    /// 对照旧 SqliteItemRepository 的 UpdateAncestors/UpdateInheritedTags 保留语义。
    ///
    /// 列映射：
    /// - 旧 AncestorIds (ItemId GUID, AncestorId GUID, AncestorIdText) →
    ///   ancestor_ids (item_id BIGINT, ancestor_id BIGINT, distance)
    /// - 旧 AncestorIdText（"N" 字符串，用于 IN 过滤）→ 直接用整数 ancestor_id + 子查询翻译
    /// </summary>
    public partial class PgItemRepository
    {
        /// <summary>
        /// 更新祖先闭包表（旧 UpdateAncestors：先删后插）。
        /// 祖先尚未入库时跳过该条（新表为整数外键语义；旧表存 GUID 无此限制，
        /// 批量保存场景下祖先总在同事务内先行 upsert，正常路径不受影响）。
        /// </summary>
        internal void UpdateAncestors(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid itemId, List<Guid> ancestorIds)
        {
            if (itemId.Equals(Guid.Empty))
            {
                throw new ArgumentNullException("itemId");
            }

            if (ancestorIds == null)
            {
                throw new ArgumentNullException("ancestorIds");
            }

            CheckDisposed();

            var internalId = GetInternalId(connection, transaction, itemId);

            if (internalId == null)
            {
                return;
            }

            var itemInternalId = internalId.Value;

            using (var deleteCmd = new NpgsqlCommand(
                "DELETE FROM ancestor_ids WHERE item_id = @item_id",
                connection, transaction))
            {
                deleteCmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = itemInternalId });
                deleteCmd.ExecuteNonQuery();
            }

            if (ancestorIds.Count == 0)
            {
                return;
            }

            foreach (var ancestorId in ancestorIds)
            {
                var ancestorInternalId = GetInternalId(connection, transaction, ancestorId);

                if (ancestorInternalId == null)
                {
                    continue;
                }

                using (var cmd = new NpgsqlCommand(
                    @"INSERT INTO ancestor_ids (item_id, ancestor_id, distance)
                      VALUES (@item_id, @ancestor_id, @distance)
                      ON CONFLICT (item_id, ancestor_id) DO NOTHING",
                    connection, transaction))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = itemInternalId });
                    cmd.Parameters.Add(new NpgsqlParameter("ancestor_id", NpgsqlDbType.Bigint) { Value = ancestorInternalId.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("distance", NpgsqlDbType.Integer) { Value = 0 });

                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 更新继承值（旧 UpdateInheritedValues → UpdateInheritedTags）：
        /// 重建 extradata_type_id=6（继承标签）= 自身 Type=4 标签 + 祖先的 Type=4 标签。
        /// </summary>
        public void UpdateInheritedValues(CancellationToken cancellationToken)
        {
            CheckDisposed();

            RunInTransaction((connection, transaction) =>
            {
                Execute(connection, transaction, "DELETE FROM item_extradata WHERE extradata_type_id = 6");

                // 自身标签 → 继承标签
                Execute(connection, transaction, @"
                    INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
                    SELECT item_id, 6, value, clean_value
                    FROM item_extradata
                    WHERE extradata_type_id = 4
                    ON CONFLICT (item_id, extradata_type_id, value) DO NOTHING");

                // 祖先标签 → 继承标签（旧：AncestorIds join ItemValues on AncestorId=ItemId and Type=4）
                Execute(connection, transaction, @"
                    INSERT INTO item_extradata (item_id, extradata_type_id, value, clean_value)
                    SELECT a.item_id, 6, iv.value, iv.clean_value
                    FROM ancestor_ids a
                    LEFT JOIN item_extradata iv ON (a.ancestor_id = iv.item_id AND iv.extradata_type_id = 4)
                    WHERE iv.value IS NOT NULL
                    ON CONFLICT (item_id, extradata_type_id, value) DO NOTHING");
            });
        }

        private static void Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
        {
            using (var cmd = new NpgsqlCommand(sql, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
        }
    }
}
