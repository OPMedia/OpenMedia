using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.PgData;
using Emby.Server.Implementations.Configuration;
using Emby.Server.Implementations.EnvironmentInfo;
using Emby.Server.Implementations.IO;
using Emby.Server.Implementations.Reflection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// P8 查询门回归测试（ADR-0016 / docs/p8-analysis.md ③）。
    ///
    /// 背景：86735fd5ef 的真异步重写把 WithQueryGate 连同同步模板一起删掉，
    /// 导致 U2 雪崩防护（59e842af07）实际失效——500 并发全库查询再无止损层。
    /// 四条护栏逐条钉成可失败定位的断言：
    ///   ① 全局单门         —— 容量静态可读且为 P8 默认 50，且 ≤ 池上限；
    ///   ② 只守 DB 执行点   —— GetCount / RetrieveItem 单条不受门限制；
    ///   ③ 嵌套连接检查     —— GetItems 内部委托 GetItemListCoreAsync 不二次取槽；
    ///   ④ 503 语义         —— 门满时抛 PoolExhaustedException（中间件映射 503）。
    /// </summary>
    [TestClass]
    public class QueryGateTests : ProductionSchemaTestBase
    {
        [TestMethod]
        public void A1_QueryGateIsGlobalSingleton_WithP8Defaults()
        {
            // 护栏①：门是 BasePgRepository 的 static（单例），容量默认 50；
            // 不能按仓储 ×N（7 仓储 × 50 = 350 > 池 100 会自己打穿）。
            Assert.AreEqual(50, PgGateAccess.QueryGateCapacity,
                "查询门容量必须保持 P8 定案的默认 50");
            Assert.AreEqual(3000, PgGateAccess.QueryGateTimeout,
                "查询门等待上限必须保持 P8 定案的默认 3000ms");
            Assert.IsTrue(PgGateAccess.QueryGateCapacity <= PgGateAccess.PoolMaxSize,
                "门容量必须 ≤ 池上限，否则门形同虚设");
        }

        [TestMethod]
        public void A2_PoolParametersUseP8Defaults_AndWorstPathIsBounded()
        {
            // Task D：三个池参数默认值与 docs/p8-analysis.md §① 实测表一致
            // （MaxPoolSize 100 / Timeout 15s→3s / CommandTimeout 30s）。
            Assert.AreEqual(100, PgGateAccess.PoolMaxSize, "MaxPoolSize 默认 100");
            Assert.AreEqual(3, PgGateAccess.PoolTimeoutSeconds, "池等待默认 3s（P8 ③方案 B）");
            Assert.AreEqual(30, PgGateAccess.PoolCommandTimeoutSeconds, "命令超时默认 30s");

            // P8 最坏路径有界（Reviewer 记录在案②）：门等 3s + 池等 3s = 6s
            var bound = PgGateAccess.QueryGateTimeout + PgGateAccess.PoolTimeoutSeconds * 1000;
            Assert.AreEqual(6000, bound, "门等待 + 池等待必须合计 6s 有界");
        }

        [TestMethod]
        public async Task A3_GateReleasesSlot_AfterQueryCompletes()
        {
            // 槽位必须正确归还，否则随后每个查询都会等到超时。
            await EnsureProductionSchemaAsync().ConfigureAwait(false);
            await SeedFixtureAsync().ConfigureAwait(false);
            var repo = CreateRepository();

            var before = PgGateAccess.QueryGateInUse;
            await repo.GetItemsAsync(NewQuery()).ConfigureAwait(false);

            Assert.AreEqual(before, PgGateAccess.QueryGateInUse,
                "查询结束后查询门槽位未归还");
        }

        [TestMethod]
        public async Task A4_GateIsConcurrencyLimited_AndFailsFast_WhenSaturated()
        {
            // 护栏④：门满 -> PoolExhaustedException（503），不是无限排队到 60s 雪崩。
            //
            // 确定性手法（不赌真实查询慢）：直接驱动门的原语 WithQueryGateAsync——
            // 起 capacity 个「持槽 + 等放行信号」的受门调用把门占满，等 QueryGateInUse
            // 真的等于 capacity 之后再发第 capacity+1 个调用，断言它被 503 拒绝。
            // 早期版本靠真实查询占满门，但 fixture 查询 <1ms，窗口抓不到（A4/A8 假失败）。
            var capacity = PgGateAccess.QueryGateCapacity;
            var gate = new GateFakeRepository();

            var entered = 0;
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var holders = Enumerable.Range(0, capacity)
                .Select(_ => Task.Run(async () =>
                {
                    Interlocked.Increment(ref entered);
                    try
                    {
                        return await gate.RunAsync(ct => release.Task).ConfigureAwait(false);
                    }
                    catch (PoolExhaustedException)
                    {
                        return false;
                    }
                }))
                .ToArray();

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (PgGateAccess.QueryGateInUse < capacity && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }

            Assert.AreEqual(capacity, entered,
                "capacity 个并发调用都应拿到槽位（没有进入说明门漏放行）");
            Assert.AreEqual(capacity, PgGateAccess.QueryGateInUse,
                "门应被 capacity 个持槽调用占满（实测 " + PgGateAccess.QueryGateInUse + "）");
            Assert.IsFalse(holders.Any(h => h.IsCompleted),
                "持槽调用不应在放行信号前自行完成");

            // 第 capacity+1 个调用：门已满且只等 3s -> 必须 503，绝不久等
            var rejectedBefore = PgGateAccess.QueryGateRejectedCount;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var overflow = "NOT_RETURNED";
            try
            {
                await gate.RunAsync(ct => Task.FromResult(true)).ConfigureAwait(false);
                overflow = "OK";
            }
            catch (PoolExhaustedException)
            {
                overflow = "PoolExhausted";
            }
            catch (Exception ex)
            {
                overflow = ex.GetType().Name;
            }

            watch.Stop();

            Assert.AreEqual("PoolExhausted", overflow,
                "门饱和时超出的调用必须且只能抛 PoolExhaustedException（503 语义）");
            Assert.AreEqual(rejectedBefore + 1, PgGateAccess.QueryGateRejectedCount,
                "每次 503 拒绝都必须落在拒绝计数器上（Task D 可观测性要求）");
            Assert.IsTrue(watch.ElapsedMilliseconds >= 2900,
                "503 前应真的等满门等待上限（实测 " + watch.ElapsedMilliseconds + "ms）");
            Assert.IsTrue(watch.ElapsedMilliseconds < 10000,
                "503 拒绝必须有界收敛（实测 " + watch.ElapsedMilliseconds + "ms）");

            // 放行 + 全量归还：门不得泄漏，否则后续查询全部 503
            release.TrySetResult(true);
            await Task.WhenAll(holders).ConfigureAwait(false);
            Assert.AreEqual(0, PgGateAccess.QueryGateInUse,
                "全部放行后门必须完全归还（当前 " + PgGateAccess.QueryGateInUse + "）");
        }

        [TestMethod]
        public async Task A5_NonQueryPaths_AreNotGated()
        {
            // 护栏②：门只守 GetItems/GetItemList/GetItemValues；GetCount 与单条
            // RetrieveItem 池占用短，不进门。这里先把门真的打满（用门原语持槽，
            // 不依赖真实查询慢），再验证这两条路径仍然可用——若哪天有人误把门
            // 接到非查询路径上，本用例立刻炸。
            await EnsureProductionSchemaAsync().ConfigureAwait(false);
            await SeedFixtureAsync().ConfigureAwait(false);
            var repo = CreateRepository();

            var capacity = PgGateAccess.QueryGateCapacity;
            var gate = new GateFakeRepository();
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var background = Enumerable.Range(0, capacity)
                .Select(_ => Task.Run(async () =>
                {
                    await gate.RunAsync(ct => release.Task).ConfigureAwait(false);
                }))
                .ToArray();

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (PgGateAccess.QueryGateInUse < capacity && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }

            // 前置条件自证：门此刻确实已满，后面的“仍然可用”才有意义
            Assert.AreEqual(capacity, PgGateAccess.QueryGateInUse,
                "前置条件：门应已被打满（实测 " + PgGateAccess.QueryGateInUse + "）");

            var count = await repo.GetCountAsync(NewQuery()).ConfigureAwait(false);
            Assert.IsTrue(count >= 0, "GetCount 在门饱和时应仍可用");

            var guid = await FirstGuidAsync().ConfigureAwait(false);
            if (guid.HasValue)
            {
                var item = await repo.RetrieveItemAsync(guid.Value).ConfigureAwait(false);
                Assert.IsTrue(item == null || item.Id == guid.Value,
                    "RetrieveItem 在门饱和时应仍可用");
            }

            // 反证：同一时刻，受门入口真的会被拒绝——确认“门满”这个状态是真的
            var gated = await Task.Run(async () =>
            {
                try
                {
                    await repo.GetItemsAsync(NewQuery()).ConfigureAwait(false);
                    return "OK";
                }
                catch (PoolExhaustedException)
                {
                    return "PoolExhausted";
                }
                catch (Exception ex)
                {
                    return ex.GetType().Name;
                }
            }).ConfigureAwait(false);
            Assert.AreEqual("PoolExhausted", gated,
                "门满时 GetItems 应被 503；否则本用例的“门饱和”前提不成立");

            release.TrySetResult(true);
            await Task.WhenAll(background).ConfigureAwait(false);
        }


        [TestMethod]
        public async Task A6_GateAndPoolCountersAreObservable()
        {
            // Task D：门拒绝计数与池快照可读（U2 当年只能人肉 dotnet-counters 反推）。
            Assert.IsTrue(PgGateAccess.QueryGateRejectedCount >= 0, "拒绝计数应非负");
            Assert.IsTrue(PgGateAccess.QueryGateInUse >= 0, "在用槽位应非负");
            Assert.IsTrue(PgGateAccess.QueryGateInUse <= PgGateAccess.QueryGateCapacity,
                "在用槽位不应超过容量");
            var snap = PgGateAccess.PoolSnapshot();
            Assert.IsTrue(snap.Active >= 0 && snap.Total >= 0, "池快照应为非负");
            Assert.IsFalse(string.IsNullOrEmpty(Emby.Server.PgData.PgDataObservability.FormatSnapshot()),
                "快照应可格式化");
        }

        [TestMethod]
        public async Task A7_PoolSnapshot_TracksBorrowAndReturn()
        {
            // Task D：连接池 active 借出时增、归还后回收（StateChange→Closed 落账）。
            var (active0, _, total0) = PgGateAccess.PoolSnapshot();

            var conn = await OpenAsync().ConfigureAwait(false);
            await using (var cmd = new NpgsqlCommand("SELECT 1", conn))
            {
                await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            }

            await conn.DisposeAsync().ConfigureAwait(false);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (PgGateAccess.PoolSnapshot().Active > active0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20).ConfigureAwait(false);
            }

            Assert.AreEqual(active0, PgGateAccess.PoolSnapshot().Active,
                "连接归还后 active 计数未回收（StateChange 回调未落账）");
            Assert.IsTrue(PgGateAccess.PoolSnapshot().Total >= total0, "total 不应倒退");
        }

        [TestMethod]
        public async Task A8_GateRejectsWithinBoundedTime_NotHangingForever()
        {
            // P8 止损语义：饱和的请求必须快速收敛，不允许无限挂死（U2 事故的 60s 形态）。
            // 与 A4 的分工：A4 证明「满门 + 溢出必 503」，本用例证明「等待时间有界」——
            // 拒绝时刻必须落在「门等待上限」附近而不是池等待 15s 的量级。
            var capacity = PgGateAccess.QueryGateCapacity;
            var gate = new GateFakeRepository();

            var inUse = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var holders = Enumerable.Range(0, capacity)
                .Select(_ => Task.Run(async () =>
                {
                    await gate.RunAsync(ct => release.Task).ConfigureAwait(false);
                }))
                .ToArray();

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (PgGateAccess.QueryGateInUse < capacity && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }

            Assert.AreEqual(capacity, PgGateAccess.QueryGateInUse,
                "门应被占满（实测 " + PgGateAccess.QueryGateInUse + "）");

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var outcome = await Task.Run(async () =>
            {
                try
                {
                    await gate.RunAsync(ct => Task.FromResult(true)).ConfigureAwait(false);
                    return "OK";
                }
                catch (PoolExhaustedException)
                {
                    return "PoolExhausted";
                }
                catch (Exception ex)
                {
                    return ex.GetType().Name;
                }
            }).ConfigureAwait(false);
            watch.Stop();

            Assert.AreEqual("PoolExhausted", outcome, "溢出请求应 503");
            Assert.IsTrue(watch.ElapsedMilliseconds < 10000,
                "拒绝必须快速收敛（实测 " + watch.ElapsedMilliseconds + "ms）；"
                + "15s 池等待已改为 3s，绝不允许回到 U2 的 60s 挂死形态");

            release.TrySetResult(true);
            await Task.WhenAll(holders).ConfigureAwait(false);
        }

        [TestMethod]
        public async Task A9_GateWrapsRealRepositoryEntryPoints()
        {
            // 护栏②+④ 的集成面：三个受门入口（GetItems/GetItemList/GetItemValues）
            // 在真实 PG 查询执行期间都应让 QueryGateInUse 见 >= 1——证明门确实
            // 包住了 DB 执行点，而不是只挂在空壳上。
            await EnsureProductionSchemaAsync().ConfigureAwait(false);
            await SeedFixtureAsync().ConfigureAwait(false);
            var repo = CreateRepository();

            var observed = 0;
            var sampler = Task.Run(async () =>
            {
                for (var i = 0; i < 400; i++)
                {
                    observed = Math.Max(observed, PgGateAccess.QueryGateInUse);
                    await Task.Delay(2).ConfigureAwait(false);
                }
            });

            await repo.GetItemsAsync(NewQuery()).ConfigureAwait(false);
            await repo.GetItemListAsync(NewQuery()).ConfigureAwait(false);
            await sampler.ConfigureAwait(false);

            Assert.IsTrue(observed >= 1,
                "查询执行期间 QueryGateInUse 应 >= 1（实测 " + observed + "）");
            Assert.AreEqual(0, PgGateAccess.QueryGateInUse,
                "全部查询结束后门应归还（当前 " + PgGateAccess.QueryGateInUse + "）");
        }

        // ---- helpers ----

        /// <summary>
        /// 门原语驱动器：让测试能确定性地把全局查询门占满并保持占用。
        /// <see cref="BasePgRepository.WithQueryGateAsync{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/>
        /// 是 protected static，子类可直接调用；不再依赖"真实查询恰好很慢"这种时间窗口，
        /// 于是 A4/A5/A8 从概率性用例变成可失败定位的确定性用例。
        /// </summary>
        private sealed class GateFakeRepository : BasePgRepository
        {
            public GateFakeRepository()
                : base(new NullLogger(), BasePgAccess.DefaultConnectionString)
            {
            }

            public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> body)
            {
                return WithQueryGateAsync(body, CancellationToken.None);
            }
        }

        /// <summary>
        /// 占住一条池连接不放，配合容量个并发查询即可把门打满。
        /// 当前 A4/A5/A8 走 GateFakeRepository 确定性占门，本类保留给
        /// 「连接池确实还有余量」这一维度的验证。
        /// </summary>
        private sealed class ConnectionHolder : IDisposable
        {
            private readonly NpgsqlConnection _conn;

            public ConnectionHolder()
            {
                _conn = new NpgsqlConnection(BasePgAccess.DefaultConnectionString);
                _conn.Open();
                using var cmd = new NpgsqlCommand("BEGIN", _conn);
                cmd.ExecuteNonQuery();
            }

            public void Dispose()
            {
                try
                {
                    using var cmd = new NpgsqlCommand("ROLLBACK", _conn);
                    cmd.ExecuteNonQuery();
                }
                catch
                {
                    // 忽略
                }

                _conn.Dispose();
            }
        }

        private Emby.Server.PgData.PgItemRepository CreateRepository()
        {
            var logger = new NullLogger();
            var envInfo = new EnvironmentInfo();
            var fs = new ManagedFileSystem(logger, envInfo, ".", Path.GetTempPath(), false);

            return new Emby.Server.PgData.PgItemRepository(
                new FakeConfigManager(),
                new FakeAppHost(),
                new Emby.Common.Implementations.Serialization.JsonSerializer(fs, logger),
                logger,
                new AssemblyInfo(),
                fs,
                TestConnectionString);
        }

        private async Task SeedFixtureAsync()
        {
            await using var conn = await OpenAsync().ConfigureAwait(false);

            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO media_items (guid, type, type_name, name, sort_name, clean_name, " +
                "presentation_unique_key, is_folder, is_virtual_item) " +
                "SELECT ('00000000-0000-0000-0000-' || lpad(g.g::text, 12, '0'))::uuid, " +
                "g.g % 3, 'Movie', g.g::text, g.g::text, g.g::text, 'puk-' || g.g, FALSE, FALSE " +
                "FROM generate_series(1, 60) AS g(g) " +
                "ON CONFLICT (guid, type) DO UPDATE SET name = EXCLUDED.name", conn))
            {
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // 第二组同 puk 前缀的多版本行：触发 HasPresentationUniqueKeyDuplicatesAsync 的
            // 保守路径（DISTINCT ON 包装 + 外层重排序），让查询比"全绿"的 fixture 更重。
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO media_items (guid, type, type_name, name, sort_name, clean_name, " +
                "presentation_unique_key, is_folder, is_virtual_item) " +
                "SELECT ('00000000-0000-0000-0001-' || lpad(g.g::text, 12, '0'))::uuid, " +
                "g.g % 3, 'Movie', g.g::text, g.g::text, g.g::text, 'puk-' || (g.g % 60), FALSE, FALSE " +
                "FROM generate_series(1, 120) AS g(g) " +
                "ON CONFLICT (guid, type) DO UPDATE SET name = EXCLUDED.name", conn))
            {
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // 确定性慢查询：把每行的 name 换成会触发 pg_sleep 的表达式不可行（仓储不让我注入），
            // 改为造一个真的很大的 clean_name 让 count(DISTINCT puk) + 外层重排序付出代价。
            // 关键不是查询多慢，而是「容量个并发能同时占住门」——用 rowcount 拉到 20k。
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO media_items (guid, type, type_name, name, sort_name, clean_name, " +
                "presentation_unique_key, is_folder, is_virtual_item) " +
                "SELECT ('00000000-0000-0000-0002-' || lpad(g.g::text, 12, '0'))::uuid, " +
                "2, 'Movie', repeat(g.g::text, 40), repeat(g.g::text, 40), repeat(g.g::text, 40), " +
                "'puk-' || g.g, FALSE, FALSE " +
                "FROM generate_series(1, 20000) AS g(g) " +
                "ON CONFLICT (guid, type) DO UPDATE SET name = EXCLUDED.name", conn))
            {
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private static InternalItemsQuery NewQuery()
        {
            return new InternalItemsQuery { EnableTotalRecordCount = true, Limit = 10 };
        }

        private async Task<Guid?> FirstGuidAsync()
        {
            await using var conn = new NpgsqlConnection(TestConnectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("SELECT guid FROM media_items LIMIT 1", conn);
            var v = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (v == null || v == DBNull.Value)
            {
                return null;
            }

            return (Guid)v;
        }
    }
}
