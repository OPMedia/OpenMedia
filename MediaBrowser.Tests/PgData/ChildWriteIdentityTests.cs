using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.PgData;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// 子表写入不再依赖进程内 GUID→id 缓存：item_id 必须由写语句自己从数据库解析。
    /// </summary>
    [TestClass]
    public class ChildWriteIdentityTests : ProductionSchemaTestBase
    {
        private static readonly string[] ChildTables = { "chapters", "media_streams", "item_people" };

        /// <summary>
        /// 全新 PgItemRepository 从未保存过任何东西；DB 中已有的 item 仍应正确接上子行。
        /// </summary>
        [TestMethod]
        public async Task ChildWrites_WorkWithoutWarmCache()
        {
            await EnsureProductionSchemaAsync().ConfigureAwait(false);

            var itemGuid = Guid.NewGuid();
            long itemId;
            await using (var conn = await OpenAsync().ConfigureAwait(false))
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO media_items (guid, type, name, sort_name) " +
                "VALUES (@guid, 2, @name, @name) RETURNING id", conn))
            {
                cmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlDbType.Uuid) { Value = itemGuid });
                cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = "parent-item" });
                itemId = Convert.ToInt64(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
            }

            var repository = CreateRepository();

            await repository.SaveChaptersAsync(
                itemGuid,
                new List<ChapterInfo> { new ChapterInfo { Name = "chapter-1", StartPositionTicks = 1234 } },
                CancellationToken.None).ConfigureAwait(false);

            await repository.SaveMediaStreamsAsync(
                itemGuid,
                new List<MediaStream> { new MediaStream { Index = 0, Type = MediaStreamType.Video, Codec = "h264" } },
                CancellationToken.None).ConfigureAwait(false);

            await repository.UpdatePeopleAsync(
                itemGuid,
                new List<PersonInfo> { new PersonInfo { Name = "Actor One", Role = "Lead", Type = "Actor" } },
                CancellationToken.None).ConfigureAwait(false);

            foreach (var table in ChildTables)
            {
                var matching = await ScalarAsync(
                    "SELECT count(*) FROM " + table + " WHERE item_id = @id",
                    new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = itemId }).ConfigureAwait(false);
                var orphaned = await ScalarAsync(
                    "SELECT count(*) FROM " + table + " WHERE item_id <> @id",
                    new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = itemId }).ConfigureAwait(false);

                Assert.AreEqual(1L, matching.Value, table + " 未写入 item_id 正确的行");
                Assert.AreEqual(0L, orphaned.Value, table + " 出现了指向其它 item_id 的行");
            }

            // 旧实现对未解析的 guid 直接早退；新实现也必须整批 no-op（包括不 upsert Person 项）。
            var missingGuid = Guid.NewGuid();
            var itemsBefore = (await ScalarAsync("SELECT count(*) FROM media_items").ConfigureAwait(false)).Value;
            await repository.SaveChaptersAsync(
                missingGuid,
                new List<ChapterInfo> { new ChapterInfo { Name = "ghost-chapter", StartPositionTicks = 1 } },
                CancellationToken.None).ConfigureAwait(false);
            await repository.SaveMediaStreamsAsync(
                missingGuid,
                new List<MediaStream> { new MediaStream { Index = 0, Type = MediaStreamType.Audio, Codec = "aac" } },
                CancellationToken.None).ConfigureAwait(false);
            await repository.UpdatePeopleAsync(
                missingGuid,
                new List<PersonInfo> { new PersonInfo { Name = "Ghost Actor", Role = "Ghost", Type = "Actor" } },
                CancellationToken.None).ConfigureAwait(false);

            var itemsAfter = (await ScalarAsync("SELECT count(*) FROM media_items").ConfigureAwait(false)).Value;
            Assert.AreEqual(itemsBefore, itemsAfter, "未解析的 guid 不应写入 Person 项");
            foreach (var table in ChildTables)
            {
                var total = (await ScalarAsync("SELECT count(*) FROM " + table).ConfigureAwait(false)).Value;
                Assert.AreEqual(1L, total, table + " 对未解析的 guid 不应留下子行");
            }
        }

        /// <summary>
        /// 结构性检查：三条子项保存路径里不能有独立的
        /// SELECT ... FROM media_items WHERE guid 语句（那会额外产生一次往返）。
        /// guid 解析只能出现在 DELETE 子查询或 INSERT 的 CTE 里。
        /// </summary>
        [TestMethod]
        public void ChildSavePaths_DoNotIssueSeparateGuidLookup()
        {
            var targets = new[]
            {
                new[] { "PgItemRepository.ChaptersStreams.cs", "SaveChaptersAsync" },
                new[] { "PgItemRepository.ChaptersStreams.cs", "SaveMediaStreamsAsync" },
                new[] { "PgItemRepository.People.cs", "UpdatePeopleAsync" }
            };

            foreach (var target in targets)
            {
                var file = target[0];
                var method = target[1];
                var body = ExtractMethodBody(ReadEmbeddedSource(file), method);

                Assert.IsFalse(body.Contains("GetInternalIdAsync"), method + " 仍在调用已删除的 id 查找方法");
                Assert.IsFalse(body.Contains("ExecuteScalarAsync"), method + " 仍在发独立的 SELECT 往返");

                var standaloneSelects = Regex.Matches(body, "@\"[\\s\\S]*?\"")
                    .Cast<Match>()
                    .Select(m => m.Value.Trim())
                    .Where(sql => Regex.IsMatch(sql, @"(?is)^\s*SELECT\s+[\s\S]*\bFROM\s+media_items\b[\s\S]*\bWHERE\s+guid\b"))
                    .ToArray();
                Assert.AreEqual(0, standaloneSelects.Length,
                    method + " 存在独立的 guid 查询：" + string.Join(" | ", standaloneSelects));

                StringAssert.Contains(body, "resolved_item AS (", method + " 的 INSERT 未按 guid 解析 item_id");
                StringAssert.Contains(body, "FROM media_items WHERE guid = @", method + " 缺少按 guid 解析 item_id 的语句");
            }
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            var signature = Regex.Match(
                source,
                @"(?m)^\s*public\s+(?:async\s+)?Task\s+" + Regex.Escape(methodName) + @"\s*\(");
            Assert.IsTrue(signature.Success, "找不到方法 " + methodName);

            var open = source.IndexOf('{', signature.Index);
            Assert.IsTrue(open >= 0, "找不到方法体 " + methodName);

            var depth = 0;
            for (var i = open; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(open, i - open + 1);
                    }
                }
            }

            Assert.Fail("方法体未闭合：" + methodName);
            return null;
        }

        private static string ReadEmbeddedSource(string fileName)
        {
            var assembly = typeof(ChildWriteIdentityTests).Assembly;
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.Ordinal));
            Assert.IsNotNull(name, "找不到嵌入的生产源码：" + fileName);

            using var stream = assembly.GetManifestResourceStream(name);
            Assert.IsNotNull(stream, "无法读取嵌入的生产源码：" + fileName);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private PgItemRepository CreateRepository()
        {
            var logger = new MediaBrowser.Model.Logging.NullLogger();
            var envInfo = new Emby.Server.Implementations.EnvironmentInfo.EnvironmentInfo();
            var fs = new Emby.Server.Implementations.IO.ManagedFileSystem(
                logger, envInfo, ".", Path.GetTempPath(), false);

            return new PgItemRepository(
                new FakeConfigManager(),
                new FakeAppHost(),
                new Emby.Common.Implementations.Serialization.JsonSerializer(fs, logger),
                logger,
                new Emby.Server.Implementations.Reflection.AssemblyInfo(),
                fs,
                TestConnectionString);
        }
    }
}
