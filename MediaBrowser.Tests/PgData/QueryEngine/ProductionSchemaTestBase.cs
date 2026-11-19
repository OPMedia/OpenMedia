using System.Threading.Tasks;
using Emby.Server.PgData;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// 用真正的 <see cref="PgSchemaInitializer.Initialize"/> 派生测试 schema。
    ///
    /// 测试库是独立数据库，其 public 就是本用例的测试 schema（<see cref="PgTestBase.UseDedicatedDatabase"/>）。
    /// 初始化器用 to_regclass('public.media_items') 判断是否走全量 DDL；在此之前必须先删掉
    /// PgTestBase 建的最小 media_items，否则会走跳过分支，差分又跑在错误 fixture 上。
    ///
    /// 不再维护「嵌入 dump + 手写 ALTER 子集」的测试副本：生产列、索引、item_extradata
    /// 主键修正（FixItemExtradata）全部由初始化器一处提供，测试没有第二份事实来源。
    /// </summary>
    public abstract class ProductionSchemaTestBase : PgTestBase
    {
        /// <summary>
        /// 初始化器按 public 判断分支，测试必须让 public 成为独立库里的测试 schema。
        /// </summary>
        protected override bool UseDedicatedDatabase => true;

        protected async Task EnsureProductionSchemaAsync()
        {
            await using var conn = await OpenAsync().ConfigureAwait(false);

            await DropBaseTablesAsync(conn).ConfigureAwait(false);

            // 单一事实来源：嵌入 dump（全量分支） + ApplyAlignmentDdl（含 FixItemExtradata）。
            PgSchemaInitializer.Initialize(conn);

            // 防止门卫误判后静默跳过：必须真的在测试 schema 里建出 media_items。
            await using (var cmd = new NpgsqlCommand(
                "SELECT to_regclass(@qualified) IS NOT NULL", conn))
            {
                cmd.Parameters.AddWithValue("qualified", Schema + ".media_items");
                var exists = (bool)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;
                Assert.IsTrue(exists,
                    "初始化后测试 schema（" + Schema + "）里没有 media_items：初始化器走了跳过分支");
            }

            // FixItemExtradata 的端到端证据：clean_value 不是测试手写的 ALTER，
            // 只能由初始化器重建 item_extradata 时带来。
            await using (var cmd = new NpgsqlCommand(
                @"SELECT count(*) FROM information_schema.columns
                  WHERE table_schema = @s AND table_name = 'item_extradata' AND column_name = 'clean_value'", conn))
            {
                cmd.Parameters.AddWithValue("s", Schema);
                var count = System.Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
                Assert.AreEqual(1, count,
                    "测试 schema 的 item_extradata.clean_value 缺失：FixItemExtradata 没有生效");
            }
        }

        private static async Task DropBaseTablesAsync(NpgsqlConnection conn)
        {
            var tables = new[] { "user_datas", "ancestor_ids", "media_items", "user_data_keys" };

            foreach (var t in tables)
            {
                await using var cmd = new NpgsqlCommand("DROP TABLE IF EXISTS " + t + " CASCADE", conn);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }
}
