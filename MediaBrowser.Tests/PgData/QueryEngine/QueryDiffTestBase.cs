using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using Emby.Server.PgData;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// Query engine differential harness (Task B / ADR-0016 3).
    ///
    /// Scheme chosen (option 1 as recommended by the task, sharpened):
    ///   The old SQLite oracle (git baseline 1d7c2ab4bf:Emby.Server.Implementations/Data/
    ///   SqliteItemRepository.cs, 6444 lines; GetWhereClauses at line 3539, ~155 if-branches)
    ///   CANNOT be executed here: it needs SQLitePCLRaw + BaseSqliteRepository, and
    ///   MediaBrowser.Tests.csproj is outside this task writable scope, so no package can be added.
    ///   Option 2 (parameterised generation + hand-derived expected rows) was rejected too:
    ///   it proves only what one hand wrote, and cannot detect that the branch set drifted.
    ///
    ///   So the harness does per-branch semantics with REAL generated SQL on the new side:
    ///   - PgItemRepository.BuildQuerySqlForTestAsync(query) is a test-only seam that runs the
    ///     production select/join/where/order builder and returns the SQL text plus the actually
    ///     bound parameters - no connection, no gate, no DB round-trip. Production paths never
    ///     call it, so runtime semantics are untouched.
    ///   - Each DiffCase names ONE branch id, the parameterised InternalItemsQuery that trips it,
    ///     the predicate fragment the new implementation must emit, and the equivalent
    ///     SQLite-dialect rendering of the same branch (transcribed from 1d7c2ab4bf).
    ///   - The harness then asserts BOTH: (a) the generated WHERE contains the PG fragment, and
    ///     (b) the generated WHERE executed on the real fixture returns the same row set as the
    ///     SQLite-dialect rendering (idioms translated table-by-table: itemvalues->item_extradata,
    ///     TypedBaseItems->media_items, PascalCase->snake_case, 0/1 ints->booleans).
    ///   ExpectMatch=false marks a tabled divergence and asserts it still reproduces.
    ///
    ///   Result: every branch is individually enumerated and individually failing; a failure
    ///   names branch id + both row sets + the generated WHERE. No totals-only checks.
    /// </summary>
    public abstract class QueryDiffTestBase : ProductionSchemaTestBase
    {
        /// <summary>
        /// One differential case for one where-branch.
        /// </summary>
        protected sealed class DiffCase
        {
            public DiffCase(
                string id,
                string description,
                Action<InternalItemsQuery> query,
                string pgFragment,
                string sqliteDialectSql)
            {
                Id = id;
                Description = description;
                Query = query;
                PgFragment = pgFragment;
                SqliteDialectSql = sqliteDialectSql;
                ExpectMatch = true;
            }

            private DiffCase(string id, string description, bool expectMatch, string knownDivergence)
            {
                Id = id;
                Description = description;
                ExpectMatch = expectMatch;
                KnownDivergence = knownDivergence;
            }

            /// <summary>Stable branch id (e.g. W-IsHD). Appears in every failure message.</summary>
            public string Id { get; }

            public string Description { get; }

            /// <summary>Parameterised query that trips exactly this branch on the PG side.</summary>
            public Action<InternalItemsQuery> Query { get; private set; }

            /// <summary>Predicate fragment the new PG implementation must emit (substring match).</summary>
            public string PgFragment { get; private set; }

            /// <summary>
            /// Equivalent SQL for the same branch in SQLite dialect (transcribed from the old
            /// implementation), executable in PG against the same fixture after idiom translation.
            /// </summary>
            public string SqliteDialectSql { get; private set; }

            public bool ExpectMatch { get; }

            public string KnownDivergence { get; }

            /// <summary>Declare a tabled divergence. KnownDivergence is mandatory.</summary>
            public DiffCase WithKnownDivergence(string knownDivergence)
            {
                return new DiffCase(Id, Description, false, knownDivergence)
                {
                    Query = Query,
                    PgFragment = PgFragment,
                    SqliteDialectSql = SqliteDialectSql
                };
            }
        }

        /// <summary>
        /// Run one differential case end to end.
        /// </summary>
        protected async Task RunDiffAsync(DiffCase testCase)
        {
            if (!testCase.ExpectMatch && string.IsNullOrWhiteSpace(testCase.KnownDivergence))
            {
                Assert.Fail("[" + testCase.Id + "] ExpectMatch=false requires KnownDivergence - an unclassified divergence is exactly what Task B forbids.");
            }

            var query = new InternalItemsQuery();
            testCase.Query(query);

            var built = await CreateRepository().BuildQuerySqlForTestAsync(query).ConfigureAwait(false);
            var sql = built.Sql;
            var parameters = built.Parameters;

            // (a) branch-shape check: the generated WHERE must contain this branch predicate fragment.
            var where = ExtractWhere(sql);
            if (!string.IsNullOrEmpty(testCase.PgFragment)
                && where.IndexOf(testCase.PgFragment, StringComparison.OrdinalIgnoreCase) < 0)
            {
                Assert.Fail(
                    "[" + testCase.Id + "] branch did not emit its predicate: " + testCase.Description
                    + "\n  expected fragment: " + testCase.PgFragment
                    + "\n  generated WHERE  : " + where);
            }

            // (b) semantics check: the real generated WHERE vs the old SQLite rendering, same fixture.
            var pgRows = await ExecuteAsync(sql, parameters).ConfigureAwait(false);
            var sqliteRows = await ExecuteAsync(testCase.SqliteDialectSql, null).ConfigureAwait(false);
            var pgSet = string.Join("|", pgRows);
            var sqliteSet = string.Join("|", sqliteRows);

            if (testCase.ExpectMatch)
            {
                if (!string.Equals(pgSet, sqliteSet, StringComparison.Ordinal))
                {
                    Assert.Fail(
                        "[" + testCase.Id + "] differential FAILED: " + testCase.Description
                        + "\n  pg (real generated SQL) : " + pgSet
                        + "\n  sqlite rendering        : " + sqliteSet
                        + "\n  pg WHERE                : " + where
                        + "\n  If intended: .WithKnownDivergence(...) and table it in ADR-0016.");
                }
            }
            else if (string.Equals(pgSet, sqliteSet, StringComparison.Ordinal))
            {
                Assert.Fail(
                    "[" + testCase.Id + "] tabled divergence no longer reproduces: " + testCase.Description
                    + "\n  KnownDivergence: " + testCase.KnownDivergence
                    + "\n  Flip back to ExpectMatch=true and update the ADR divergence table.");
            }
        }

        /// <summary>
        /// Extract the top-level WHERE clause (before ORDER BY / LIMIT).
        /// 不能 IndexOf(" WHERE ")：生产 SELECT 的列清单里有聚合子查询
        /// （如 string_agg(...) ... WHERE item_id = A.id），WHERE 在括号内，不是顶层。
        /// 也不能 LastIndexOf(" FROM ")：Tags/Genres 等分支的子查询里也带 FROM+WHERE，
        /// 会把最后一个子查询内部当成顶层。故按括号深度扫描，只取 depth==0 的 WHERE，
        /// 再按同一深度规则截到 ORDER BY / LIMIT。
        /// </summary>
        private static string ExtractWhere(string sql)
        {
            var depth = 0;
            var whereStart = -1;
            for (var i = 0; i < sql.Length; i++)
            {
                var c = sql[i];
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }
                else if (depth == 0 && StartsWithKeyword(sql, i, " WHERE "))
                {
                    whereStart = i + 7;
                    break;
                }
            }

            if (whereStart < 0)
            {
                return string.Empty;
            }

            // 从 whereStart 起按括号深度截到顶层的 ORDER BY / LIMIT。
            depth = 0;
            for (var i = whereStart; i < sql.Length; i++)
            {
                var c = sql[i];
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }
                else if (depth == 0
                    && (StartsWithKeyword(sql, i, " ORDER BY ") || StartsWithKeyword(sql, i, " LIMIT ")))
                {
                    return sql.Substring(whereStart, i - whereStart);
                }
            }

            return sql.Substring(whereStart);
        }

        /// <summary>Case-insensitive keyword match at a specific index.</summary>
        private static bool StartsWithKeyword(string sql, int index, string keyword)
        {
            if (index + keyword.Length > sql.Length)
            {
                return false;
            }

            return string.Compare(sql, index, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        /// <summary>
        /// Execute SQL with named parameters and return the first column as a sorted name list.
        /// NULL renders as &lt;NULL&gt; so null-vs-empty divergence cannot hide.
        /// </summary>
        protected async Task<List<string>> ExecuteAsync(string sql, IReadOnlyDictionary<string, object> parameters)
        {
            await using var conn = await OpenAsync().ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(sql, conn);
            if (parameters != null)
            {
                foreach (var pair in parameters)
                {
                    if (sql.IndexOf("@" + pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cmd.Parameters.AddWithValue(pair.Key, pair.Value ?? DBNull.Value);
                    }
                }
            }

            var rows = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                // 生产 SELECT 的第一列是 id（BIGINT），差分要的是行身份；优先取 name 列，
                // 取不到再退回第一列，使 PG 侧与 SQLite 侧都按 name 归一。
                var value = TryGetColumnValue(reader, "name");
                if (value == null)
                {
                    value = reader.IsDBNull(0)
                        ? "<NULL>"
                        : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? "<NULL>";
                }

                rows.Add(value);
            }

            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

        /// <summary>
        /// Try to read a column by name; returns null when absent or DBNULL.
        /// </summary>
        private static string TryGetColumnValue(NpgsqlDataReader reader, string columnName)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return reader.IsDBNull(i)
                        ? "<NULL>"
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "<NULL>";
                }
            }

            return null;
        }

        /// <summary>
        /// 查询引擎的 GetCleanValue -> StringExtensions.RemoveDiacritics 需要
        /// StringExtensions.LocalizationManager（生产里由 ApplicationHost 注入）。
        /// 测试进程没有 ApplicationHost，这里接一个最小实现，否则 Tags/Genres/
        /// NameContains 等分支会在建 SQL 阶段就 NullReferenceException。
        /// </summary>
        private static void EnsureLocalizationManager()
        {
            MediaBrowser.Controller.Extensions.StringExtensions.LocalizationManager = new FakeLocalizationManager();
        }

        /// <summary>Execute arbitrary SQL with no parameters (fixture seeding).</summary>
        protected async Task ExecuteAsync(string sql)
        {
            await using var conn = await OpenAsync().ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private Emby.Server.PgData.PgItemRepository CreateRepository()
        {
            EnsureLocalizationManager();
            var logger = new NullLogger();
            var envInfo = new Emby.Server.Implementations.EnvironmentInfo.EnvironmentInfo();
            var fs = new Emby.Server.Implementations.IO.ManagedFileSystem(logger, envInfo, ".", Path.GetTempPath(), false);

            return new Emby.Server.PgData.PgItemRepository(
                new FakeConfigManager(),
                new FakeAppHost(),
                new Emby.Common.Implementations.Serialization.JsonSerializer(fs, logger),
                logger,
                new Emby.Server.Implementations.Reflection.AssemblyInfo(),
                fs,
                TestConnectionString);
        }

        /// <summary>
        /// Differential fixture: ~40 dimension-dense rows so a failing row-set diff stays readable.
        /// Every where-branch gets rows on both sides of its predicate, plus NULL-side rows wherever
        /// the branch uses IS NULL / IS DISTINCT FROM (that is where dialect bugs live).
        /// SQL lives in query-diff-fixture.sql (embedded resource) to keep this file readable.
        /// </summary>
        protected async Task SeedDiffFixtureAsync()
        {
            await EnsureProductionSchemaAsync().ConfigureAwait(false);

            var assembly = typeof(ProductionSchemaTestBase).Assembly;
            const string ResourceName = "MediaBrowser.Tests.PgData.QueryEngine.query-diff-fixture.sql";
            await using var stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                throw new InvalidOperationException("fixture 资源不存在：" + ResourceName);
            }

            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync().ConfigureAwait(false);
            await ExecuteAsync(sql).ConfigureAwait(false);
        }
    }
}
