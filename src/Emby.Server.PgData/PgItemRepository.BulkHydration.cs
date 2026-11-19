using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PgItemRepository — DTO 批量水合：一页条目的 People 及 Person 实体各一次查询，
    /// 替代 DtoService 逐条 GetPeople/GetPerson 造成的 N+1。
    /// </summary>
    public partial class PgItemRepository
    {
        public async Task<Dictionary<Guid, List<PersonInfo>>> GetPeopleForItemsAsync(
            IReadOnlyList<Guid> itemIds,
            CancellationToken cancellationToken = default)
        {
            if (itemIds == null)
            {
                throw new ArgumentNullException(nameof(itemIds));
            }

            var result = new Dictionary<Guid, List<PersonInfo>>();
            foreach (var itemId in itemIds.Distinct())
            {
                result[itemId] = new List<PersonInfo>();
            }

            if (result.Count == 0)
            {
                return result;
            }

            CheckDisposed();

            const string commandText = @"
                SELECT m.guid, p.name, ip.role, ip.person_type_name, ip.sort_order
                FROM item_people ip
                JOIN media_items p ON p.id = ip.person_id
                JOIN media_items m ON m.id = ip.item_id
                WHERE ip.item_id IN (SELECT id FROM media_items WHERE guid = ANY(@ids))
                ORDER BY m.guid, ip.list_order";

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(commandText, connection);
            cmd.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = result.Keys.ToArray()
            });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var itemId = reader.GetGuid(0);
                if (result.TryGetValue(itemId, out var people))
                {
                    people.Add(GetPerson(reader));
                }
            }

            return result;
        }

        public async Task<Dictionary<Guid, BaseItem>> RetrieveItemsAsync(
            IReadOnlyList<Guid> ids,
            CancellationToken cancellationToken = default)
        {
            if (ids == null)
            {
                throw new ArgumentNullException(nameof(ids));
            }

            var result = new Dictionary<Guid, BaseItem>();
            foreach (var id in ids.Distinct())
            {
                result[id] = null;
            }

            if (result.Count == 0)
            {
                return result;
            }

            CheckDisposed();

            var commandText = BuildItemSelectText(null) + " FROM media_items A WHERE A.guid = ANY(@ids)";

            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(commandText, connection);
            cmd.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = result.Keys.ToArray()
            });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var item = GetItem(reader, connection);
                if (result.ContainsKey(item.Id))
                {
                    result[item.Id] = item;
                }
            }

            return result;
        }
    }
}
