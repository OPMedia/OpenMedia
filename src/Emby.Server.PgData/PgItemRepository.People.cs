using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PgItemRepository — item_people 读写（含 Person 项 upsert 到 media_items）。
    /// 对照旧 SqliteItemRepository 的 GetPeopleNames/GetPeople/UpdatePeople 逐方法保留语义。
    ///
    /// 列映射：
    /// - 旧 People (ItemId GUID, Name, Role, PersonType TEXT, SortOrder, ListOrder)
    ///   → item_people (item_id BIGINT, person_id BIGINT→media_items, role, person_type SMALLINT,
    ///     person_type_name TEXT 补充列, list_order, sort_order 补充列)
    /// - 旧 People.Name → Person 项的 media_items.name（Person 项由 UpdatePeople upsert，
    ///   GUID 复刻 LibraryManager.GetItemByNameId&lt;Person&gt; 算法，保证与真实 Person 项同 GUID 不重复）
    /// - 旧 People.PersonType 字符串 → person_type_name（原样字符串）+ person_type（已知类型码）
    /// </summary>
    public partial class PgItemRepository
    {
        /// <summary>
        /// 已知 PersonType 字符串 → person_type 码（未知类型 → 99，靠 person_type_name 还原）。
        /// </summary>
        private static readonly Dictionary<string, int> PersonTypeCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Actor", 0 },
            { "Director", 1 },
            { "Writer", 2 },
            { "Producer", 3 },
            { "Composer", 4 },
            { "GuestStar", 5 },
            { "Conductor", 6 },
            { "Lyricist", 7 },
            { "Arranger", 8 }
        };

        private static int MapPersonType(string personType)
        {
            int code;
            return PersonTypeCodes.TryGetValue(personType ?? string.Empty, out code) ? code : 99;
        }

        /// <summary>
        /// 获取去重的人物名列表（旧 GetPeopleNames：Distinct Name + ListOrder 排序；
        /// PG 用 GROUP BY name ORDER BY MIN(list_order) 等价"按首次出现顺序"）。
        /// </summary>
        public List<string> GetPeopleNames(InternalPeopleQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            var commandText = @"
                SELECT p.name
                FROM item_people ip
                JOIN media_items p ON p.id = ip.person_id";

            var whereClauses = GetPeopleWhereClauses(query, null);

            if (whereClauses.Count > 0)
            {
                commandText += " WHERE " + string.Join(" AND ", whereClauses.ToArray());
            }

            commandText += " GROUP BY p.name ORDER BY MIN(ip.list_order)";

            using (var connection = CreateConnection())
            {
                var list = new List<string>();

                using (var cmd = new NpgsqlCommand(commandText, connection))
                {
                    // 再跑一遍以注册参数（旧实现模式）
                    GetPeopleWhereClauses(query, cmd);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(reader.GetString(0));
                        }
                    }
                }

                return list;
            }
        }

        /// <summary>
        /// 获取人物列表（旧 GetPeople：ItemId/Name/Role/PersonType/SortOrder + ListOrder 排序）。
        /// </summary>
        public List<PersonInfo> GetPeople(InternalPeopleQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            var commandText = @"
                SELECT m.guid, p.name, ip.role, ip.person_type_name, ip.sort_order
                FROM item_people ip
                JOIN media_items p ON p.id = ip.person_id
                JOIN media_items m ON m.id = ip.item_id";

            var whereClauses = GetPeopleWhereClauses(query, null);

            if (whereClauses.Count > 0)
            {
                commandText += " WHERE " + string.Join(" AND ", whereClauses.ToArray());
            }

            commandText += " ORDER BY ip.list_order";

            using (var connection = CreateConnection())
            {
                var list = new List<PersonInfo>();

                using (var cmd = new NpgsqlCommand(commandText, connection))
                {
                    // 再跑一遍以注册参数（旧实现模式）
                    GetPeopleWhereClauses(query, cmd);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(GetPerson(reader));
                        }
                    }
                }

                return list;
            }
        }

        private PersonInfo GetPerson(NpgsqlDataReader reader)
        {
            var item = new PersonInfo();

            item.ItemId = reader.GetGuid(0);
            item.Name = reader.GetString(1);

            if (!reader.IsDBNull(2))
            {
                item.Role = reader.GetString(2);
            }

            if (!reader.IsDBNull(3))
            {
                item.Type = reader.GetString(3);
            }

            if (!reader.IsDBNull(4))
            {
                item.SortOrder = reader.GetInt32(4);
            }

            return item;
        }

        /// <summary>
        /// 构建 WHERE 子句；cmd 非空时同时注册参数（旧模式：先传 null 取文本，再传 statement 绑参）。
        /// </summary>
        private List<string> GetPeopleWhereClauses(InternalPeopleQuery query, NpgsqlCommand cmd)
        {
            var whereClauses = new List<string>();

            if (!query.ItemId.Equals(Guid.Empty))
            {
                whereClauses.Add("ip.item_id = (SELECT id FROM media_items WHERE guid = @ItemId)");
                if (cmd != null)
                {
                    cmd.Parameters.Add(new NpgsqlParameter("ItemId", NpgsqlDbType.Uuid) { Value = query.ItemId });
                }
            }

            if (!query.AppearsInItemId.Equals(Guid.Empty))
            {
                whereClauses.Add("p.id IN (SELECT person_id FROM item_people WHERE item_id = (SELECT id FROM media_items WHERE guid = @AppearsInItemId))");
                if (cmd != null)
                {
                    cmd.Parameters.Add(new NpgsqlParameter("AppearsInItemId", NpgsqlDbType.Uuid) { Value = query.AppearsInItemId });
                }
            }

            var queryPersonTypes = query.PersonTypes.Where(IsValidPersonType).ToList();

            if (queryPersonTypes.Count == 1)
            {
                whereClauses.Add("ip.person_type_name = @PersonType");
                if (cmd != null)
                {
                    cmd.Parameters.Add(new NpgsqlParameter("PersonType", NpgsqlDbType.Text) { Value = queryPersonTypes[0] });
                }
            }
            else if (queryPersonTypes.Count > 1)
            {
                var placeholders = new List<string>();
                for (var i = 0; i < queryPersonTypes.Count; i++)
                {
                    placeholders.Add("@PersonType" + i);
                    if (cmd != null)
                    {
                        cmd.Parameters.Add(new NpgsqlParameter("PersonType" + i, NpgsqlDbType.Text) { Value = queryPersonTypes[i] });
                    }
                }

                whereClauses.Add("ip.person_type_name IN (" + string.Join(",", placeholders) + ")");
            }

            var queryExcludePersonTypes = query.ExcludePersonTypes.Where(IsValidPersonType).ToList();

            if (queryExcludePersonTypes.Count == 1)
            {
                whereClauses.Add("ip.person_type_name <> @ExcludePersonType");
                if (cmd != null)
                {
                    cmd.Parameters.Add(new NpgsqlParameter("ExcludePersonType", NpgsqlDbType.Text) { Value = queryExcludePersonTypes[0] });
                }
            }
            else if (queryExcludePersonTypes.Count > 1)
            {
                var placeholders = new List<string>();
                for (var i = 0; i < queryExcludePersonTypes.Count; i++)
                {
                    placeholders.Add("@ExcludePersonType" + i);
                    if (cmd != null)
                    {
                        cmd.Parameters.Add(new NpgsqlParameter("ExcludePersonType" + i, NpgsqlDbType.Text) { Value = queryExcludePersonTypes[i] });
                    }
                }

                whereClauses.Add("ip.person_type_name NOT IN (" + string.Join(",", placeholders) + ")");
            }

            if (query.MaxListOrder.HasValue)
            {
                whereClauses.Add("ip.list_order <= @MaxListOrder");
                if (cmd != null)
                {
                    cmd.Parameters.Add(new NpgsqlParameter("MaxListOrder", NpgsqlDbType.Integer) { Value = query.MaxListOrder.Value });
                }
            }

            if (!string.IsNullOrWhiteSpace(query.NameContains))
            {
                whereClauses.Add("p.name LIKE @NameContains");
                if (cmd != null)
                {
                    cmd.Parameters.Add(new NpgsqlParameter("NameContains", NpgsqlDbType.Text) { Value = "%" + query.NameContains + "%" });
                }
            }

            return whereClauses;
        }

        /// <summary>
        /// 更新媒体项的人物（旧 UpdatePeople：先删后插，ListOrder 按列表顺序编号）。
        /// Person 项不存在时 upsert 到 media_items（GUID 复刻 LibraryManager 算法）。
        /// </summary>
        public void UpdatePeople(Guid itemId, List<PersonInfo> people)
        {
            if (itemId.Equals(Guid.Empty))
            {
                throw new ArgumentNullException("itemId");
            }

            if (people == null)
            {
                throw new ArgumentNullException("people");
            }

            CheckDisposed();

            RunInTransaction((connection, transaction) =>
            {
                var internalId = GetInternalId(connection, transaction, itemId);

                if (internalId == null)
                {
                    return;
                }

                var itemInternalId = internalId.Value;

                using (var deleteCmd = new NpgsqlCommand(
                    "DELETE FROM item_people WHERE item_id = @item_id",
                    connection, transaction))
                {
                    deleteCmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = itemInternalId });
                    deleteCmd.ExecuteNonQuery();
                }

                var listIndex = 0;

                foreach (var person in people)
                {
                    if (string.IsNullOrWhiteSpace(person.Name))
                    {
                        continue;
                    }

                    var personId = GetOrInsertPersonItem(connection, transaction, person.Name);

                    using (var cmd = new NpgsqlCommand(
                        @"INSERT INTO item_people
                            (item_id, person_id, role, person_type, person_type_name, list_order, sort_order)
                          VALUES
                            (@item_id, @person_id, @role, @person_type, @person_type_name, @list_order, @sort_order)
                          ON CONFLICT DO NOTHING",
                        connection, transaction))
                    {
                        cmd.Parameters.Add(new NpgsqlParameter("item_id", NpgsqlDbType.Bigint) { Value = itemInternalId });
                        cmd.Parameters.Add(new NpgsqlParameter("person_id", NpgsqlDbType.Bigint) { Value = personId });
                        cmd.Parameters.Add(new NpgsqlParameter("role", NpgsqlDbType.Text) { Value = (object)person.Role ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("person_type", NpgsqlDbType.Smallint) { Value = MapPersonType(person.Type) });
                        cmd.Parameters.Add(new NpgsqlParameter("person_type_name", NpgsqlDbType.Text) { Value = (object)person.Type ?? DBNull.Value });
                        cmd.Parameters.Add(new NpgsqlParameter("list_order", NpgsqlDbType.Integer) { Value = listIndex });
                        cmd.Parameters.Add(new NpgsqlParameter("sort_order", NpgsqlDbType.Integer)
                        {
                            Value = person.SortOrder.HasValue ? (object)person.SortOrder.Value : DBNull.Value
                        });

                        cmd.ExecuteNonQuery();
                    }

                    listIndex++;
                }
            });
        }

        /// <summary>
        /// Person 项 upsert 到 media_items，返回其 id。
        /// GUID = GetItemByNameId(Person, Person.GetPath(name))（与 LibraryManager.CreateItemByName 一致），
        /// 已存在的真实 Person 项（含 data JSONB）不会被覆盖 data。
        /// </summary>
        internal long GetOrInsertPersonItem(NpgsqlConnection connection, NpgsqlTransaction transaction, string name)
        {
            var guid = GetItemByNameId(typeof(Person), GetPersonPath(name));
            var personTypeCode = GetTypeCode(typeof(Person));

            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO media_items
                    (guid, type, type_name, name, clean_name, sort_name, path, is_folder, date_created, date_modified)
                  VALUES
                    (@guid, @type, @type_name, @name, @clean_name, @sort_name, @path, FALSE, now(), now())
                  ON CONFLICT (guid, type) DO UPDATE SET
                    type_name = EXCLUDED.type_name,
                    name = EXCLUDED.name,
                    clean_name = EXCLUDED.clean_name,
                    sort_name = EXCLUDED.sort_name,
                    path = EXCLUDED.path,
                    date_modified = now()
                  RETURNING id",
                connection, transaction))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = guid });
                cmd.Parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Integer) { Value = personTypeCode });
                cmd.Parameters.Add(new NpgsqlParameter("type_name", NpgsqlDbType.Text) { Value = typeof(Person).FullName });
                cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = name });
                cmd.Parameters.Add(new NpgsqlParameter("clean_name", NpgsqlDbType.Text) { Value = GetCleanValue(name) });
                cmd.Parameters.Add(new NpgsqlParameter("sort_name", NpgsqlDbType.Text) { Value = (object)name ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter("path", NpgsqlDbType.Text) { Value = (object)GetPathToSave(GetPersonPath(name)) ?? DBNull.Value });

                var id = Convert.ToInt64(cmd.ExecuteScalar());
                _guidToIdMap[guid] = id;
                return id;
            }
        }

        private static bool IsValidPersonType(string value)
        {
            return IsAlphaNumeric(value);
        }

        private static bool IsAlphaNumeric(string str)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return false;
            }

            for (int i = 0; i < str.Length; i++)
            {
                if (!(char.IsLetter(str[i])) && (!(char.IsNumber(str[i]))))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
