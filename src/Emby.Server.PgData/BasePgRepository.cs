using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using Npgsql;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PostgreSQL 仓储基类：NpgsqlDataSource 生命周期（按连接串共享、进程级单例）、
    /// 原生异步连接获取与异步事务助手。
    ///
    /// 设计约束（工业级）：
    /// - 唯一数据访问入口为 async（CreateConnectionAsync / RunInTransactionAsync / ExecuteNonQueryAsync ...），
    ///   仓储实现内不再提供同步模板，从类型系统上杜绝“异步外壳里同步阻塞线程池”。
    /// - 并发控制在「应用层查询门（P8 全局单门）」与「Npgsql 连接池」之间分层：
    ///   池保护单条连接的获取/释放，门保护长查询对 PG 与池的总并发压力。
    /// </summary>
    public abstract class BasePgRepository : IDisposable
    {
        // ===========================================================
        // P8 雪崩防护：应用层查询门（docs/p8-analysis.md ③ 方案 A + B）
        // ===========================================================

        /// <summary>
        /// 查询门容量（默认 50）。可被环境变量 OPENMEDIA_QUERY_GATE_CAPACITY 覆盖。
        /// 依据 docs/p8-analysis.md ①：Npgsql MaxPoolSize 默认 100，门取 50
        /// 给写入/认证等非受门路径留出同等余量。
        /// </summary>
        private static readonly int QueryGateCapacity = ReadIntSetting(
            "OPENMEDIA_QUERY_GATE_CAPACITY", 50, 1, 1000);

        /// <summary>
        /// P8 并发防护的查询门本体（docs/p8-analysis.md ③方案 A，Chief 拍板 + Reviewer/Architect 护栏）：
        /// 全局单门信号量——所有仓储共用同一个池（同连接串 = 同一 NpgsqlDataSource），
        /// 门也必须是全局一个（护栏①：不能按仓储 ×N，否则 N×50 > 池 100 自己打穿）。
        ///
        /// 历史（ADR-0016）：59e842af07（10-16）以阻塞形态 WithQueryGate 落地；
        /// 86735fd5ef（10-18）重写本类为“真异步”时把整个门连同同步模板一起删掉，
        /// 属连带误删——该 commit 的意图是消除同步阻塞（VSTHRD002 门禁对象），
        /// 而非撤下 P8 定案的防护语义；docs/p8-analysis.md 与 docs/scale-design.md §2 #8
        /// 仍写着“✅ 定案（P8）”，与删除后的代码长期矛盾。此处用 WaitAsync 恢复。
        ///
        /// 只守 DB 执行点（护栏②），不包请求管道；嵌套连接检查（护栏③）靠 unwrapped
        /// core 模式（公共入口包门、内部委托走 *CoreAsync）保证持槽位内不开第二条连接；
        /// 护栏④（503 语义）靠 PoolExhaustedException 经 ServiceRoutingMiddleware 映射 503。
        ///
        /// 注意：必须在 QueryGateCapacity 之后声明——C# 静态字段按声明顺序初始化，
        /// 颠倒会得到容量 0 的永闭门。
        /// </summary>
        private static readonly SemaphoreSlim QueryGate = CreateQueryGate();

        /// <summary>
        /// 查询门等待上限（默认 3000ms）。可被 OPENMEDIA_QUERY_GATE_TIMEOUT_MS 覆盖。
        /// 依据 docs/p8-analysis.md ③方案 B：池等待 15s 收窄到 3s，快速失败不雪崩。
        /// 最坏路径有界：门等 3s + 池等 3s = 6s 上限（Reviewer 记录在案②）。
        /// </summary>
        private static readonly int QueryGateTimeoutMs = ReadIntSetting(
            "OPENMEDIA_QUERY_GATE_TIMEOUT_MS", 3000, 0, 60000);

        /// <summary>
        /// 查询门饱和计数（可被测试/运维读取的静态计数器，Task D 轻量观测点）。
        /// </summary>
        private static long _queryGateRejected;

        /// <summary>
        /// 查询门拒绝次数（门等待超时抛 <see cref="PoolExhaustedException"/> 的次数）。
        /// Npgsql 自身还有 MetricsReporter 可采，这里只补 P8 门这一层的计数。
        /// </summary>
        internal static long QueryGateRejectedCount => Interlocked.Read(ref _queryGateRejected);

        /// <summary>
        /// 查询门当前已占用槽位数（测试与运维可直接读取，无需反射 Npgsql 内部池计数）。
        /// </summary>
        internal static int QueryGateInUse => QueryGateCapacity - QueryGate.CurrentCount;

        internal static int QueryGateCapacityValue => QueryGateCapacity;

        internal static int QueryGateTimeoutValue => QueryGateTimeoutMs;

        private static SemaphoreSlim CreateQueryGate()
        {
            return new SemaphoreSlim(QueryGateCapacity, QueryGateCapacity);
        }

        private static int ReadIntSetting(string name, int fallback, int min, int max)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            int value;
            if (!string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                && value >= min
                && value <= max)
            {
                return value;
            }

            return fallback;
        }

        /// <summary>
        /// 查询门：异步等待槽位（超时抛 <see cref="PoolExhaustedException"/> → 中间件 503），
        /// 执行查询，释放。
        /// 覆盖：GetItems/GetItemList/GetItemValues（Reviewer 备注②：不留第二个 U2 形态）。
        /// 不覆盖：单条 RetrieveItem/GetCount/GetItemIds/认证/写入（门 50 < 池 100 留余量）。
        ///
        /// 真异步约束：只允许 <see cref="SemaphoreSlim.WaitAsync(int, CancellationToken)"/>，
        /// 禁止 <see cref="SemaphoreSlim.Wait(int)"/> / .Result（本工程 VSTHRD002/100/103/110 为 error）。
        ///
        /// 嵌套连接检查（护栏③）：调用方必须是公共入口，内部委托一律走 *CoreAsync，
        /// 否则同一线程第二次 WaitAsync 等自己释放即自死锁。
        /// </summary>
        protected static async Task<T> WithQueryGateAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            if (!await QueryGate.WaitAsync(QueryGateTimeoutMs, cancellationToken).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _queryGateRejected);
                throw new PoolExhaustedException(QueryGateCapacity);
            }

            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                QueryGate.Release();
            }
        }

        /// <summary>
        /// 默认连接串（可被 OPENMEDIA_PGCONN 环境变量覆盖——ApplicationHost.GetPostgresConnectionString）。
        /// 端口 5432 对应本机 PostgreSQL 的默认端口（见 docs/setup-local-env.md）；
        /// 走 docker-compose 或非标准端口时，用 OPENMEDIA_PGCONN 覆盖。
        /// </summary>
        /// <remarks>
        /// 以下连接池三个参数原为写死值且无依据注释。Task D 的可配置化：
        /// 环境变量优先、默认值不变，依据全部落在 docs/p8-analysis.md §① 实测表
        /// （Npgsql 10.0.3 零覆盖默认值：MaxPoolSize=100 / Timeout=15s / CommandTimeout=30s）。
        /// P8 只改 Timeout（15s→3s），另两个保持默认——②测算过扩池必须同步扩
        /// PG max_connections，属部署窗口操作，不在代码里单方面改。
        /// </remarks>
        private static readonly int MaxPoolSizeSetting = ReadIntSetting(
            "OPENMEDIA_PG_MAX_POOL_SIZE", 100, 1, 10000);

        private static readonly int PoolTimeoutSecondsSetting = ReadIntSetting(
            "OPENMEDIA_PG_POOL_TIMEOUT_SEC", 3, 1, 300);

        private static readonly int CommandTimeoutSecondsSetting = ReadIntSetting(
            "OPENMEDIA_PG_COMMAND_TIMEOUT_SEC", 30, 1, 3600);

        public const string DefaultConnectionString =
            "Host=localhost;Port=5432;Database=openmedia;Username=postgres;Password=123456";

        /// <summary>
        /// 按连接串共享的 DataSource 缓存（NpgsqlDataSource 自带连接池，进程内只需一份）。
        /// </summary>
        private static readonly ConcurrentDictionary<string, NpgsqlDataSource> DataSources =
            new ConcurrentDictionary<string, NpgsqlDataSource>(StringComparer.OrdinalIgnoreCase);

        private readonly string _connectionString;

        protected ILogger Logger { get; private set; }

        private bool _disposed;

        protected BasePgRepository(ILogger logger, string connectionString)
        {
            Logger = logger;
            _connectionString = string.IsNullOrWhiteSpace(connectionString)
                ? DefaultConnectionString
                : connectionString;
        }

        /// <summary>
        /// 仓储名称（IRepository.Name 的默认实现，子类可覆写）。
        /// </summary>
        public virtual string Name
        {
            get { return "PostgreSQL"; }
        }

        /// <summary>
        /// 获取（或惰性创建）进程级共享的 NpgsqlDataSource。
        /// </summary>
        protected NpgsqlDataSource GetDataSource()
        {
            CheckDisposed();

            return DataSources.GetOrAdd(_connectionString, cs =>
            {
                var builder = new NpgsqlConnectionStringBuilder(cs)
                {
                    Pooling = true,
                    // P8 ③方案 B：池等待 3s 快速失败（默认值与依据见上方常数注释）
                    Timeout = PoolTimeoutSecondsSetting,
                    MaxPoolSize = MaxPoolSizeSetting,
                    // 长事务/慢查询保护：单条命令 30s 上限，防止连接被无限期占用
                    CommandTimeout = CommandTimeoutSecondsSetting
                };
                var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
                WarnIfPoolBudgetExceedsServerLimit(dataSource);
                return dataSource;
            });
        }

        /// <summary>
        /// 连接预算自检：应用池上限（MaxPoolSize，默认 100）vs PG 服务端 max_connections。
        ///
        /// 背景（2026-10-20 全量回归取证）：本机 PG max_connections=100、应用池默认上限也是 100，
        /// 两者相等时没有任何余量留给 psql / 其它客户端；并发认证负载下服务端直接报
        ///   Npgsql.PostgresException 53300: sorry, too many clients already
        /// 表现为零星 500（本轮 L4 档位 10077 请求里 4 个 500，日志里就是这条）。
        ///
        /// 这里只告警不改默认值——池容量是容量承诺的一部分（docs/p8-analysis.md ① 记录
        /// MaxPoolSize=100 的依据），要不要下调/上调 PG max_connections 属于部署决策，
        /// 由运维按实际并发定。改代码默认值会惄惄改掉所有部署的容量口径。
        ///
        /// 异步探测：失败只记 Debug，不影响启动。
        /// </summary>
        private void WarnIfPoolBudgetExceedsServerLimit(NpgsqlDataSource dataSource)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var conn = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
                    await using var cmd = new NpgsqlCommand("SELECT current_setting('max_connections')::int", conn);
                    var maxConnections = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));

                    // PG 还需给 superuser 保留连接（superuser_reserved_connections，默认 3）
                    // 以及 psql / 监控 / 其它客户端留余量，这里按 10 个连接当安全垫。
                    const int ReservedForOtherClients = 10;
                    const int SuperuserReserved = 3;

                    if (MaxPoolSizeSetting >= maxConnections)
                    {
                        Logger.Error(
                            "PostgreSQL 连接预算不足：应用连接池上限 {0} ≥ 服务端 max_connections {1}，并发下必然出现 53300 (too many clients)。建议：下调 OPENMEDIA_PG_MAX_POOL_SIZE 或上调 PG max_connections（推荐 ≥ {2}）。",
                            MaxPoolSizeSetting,
                            maxConnections,
                            MaxPoolSizeSetting + ReservedForOtherClients + SuperuserReserved);
                    }
                    else if (MaxPoolSizeSetting + ReservedForOtherClients + SuperuserReserved > maxConnections)
                    {
                        Logger.Warn(
                            "PostgreSQL 连接预算偏紧：应用连接池上限 {0}，服务端 max_connections {1}（含保留 {2} + 其它客户端垫 {3}），余量不足。建议上调 PG max_connections 至 {4} 或下调 OPENMEDIA_PG_MAX_POOL_SIZE。",
                            MaxPoolSizeSetting,
                            maxConnections,
                            SuperuserReserved,
                            ReservedForOtherClients,
                            MaxPoolSizeSetting + ReservedForOtherClients + SuperuserReserved);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug("PostgreSQL 连接预算自检失败（忽略）: {0}", ex.Message);
                }
            });
        }

        /// <summary>
        /// 连接池占用快照（Task D 轻量观测点；不引新依赖、不用 OpenTelemetry）。
       /// 形态：(active, idle, total)：
        /// 形态：(active, idle, total)：
        /// - active：本进程借出且尚未归还池的连接数（应用层计数，Disposed 事件回收）；
        /// - idle：池中空闲连接数。Npgsql 10 的 PoolingDataSource 不再公开空闲计数，
        ///   这里以 total - active 下界估计（仅供参考）；精确侧写走 PG 的 pg_stat_activity
        ///   或 Npgsql 自带的 NpgsqlDataSource.MetricsReporter（未启用 OpenTelemetry 时不开采）。
        /// - total：本进程经本基类借出的连接峰值（重启归零）。
        /// 之所以保留这个形状：docs/spec/acceptance.md §4 的 /metrics 契约要的就是
        /// active/idle/total 三元组；这里只做 DB 侧观测点，接口形状不变即可平移到 Prometheus。
        /// </summary>
        internal static (int Active, int Idle, int Total) PoolSnapshot()
        {
            var active = Interlocked.Read(ref _activeConnections);
            var lifetime = Interlocked.Read(ref _lifetimeConnections);
            return ((int)active, (int)Math.Max(0, lifetime - active), (int)lifetime);
        }

        /// <summary>
        /// 连接池最大容量（与 DataSource 生效值一致，供指标标签使用）。
        /// </summary>
        internal static int PoolMaxSize => MaxPoolSizeSetting;

        internal static int PoolTimeoutSeconds => PoolTimeoutSecondsSetting;

        internal static int PoolCommandTimeoutSeconds => CommandTimeoutSecondsSetting;

        /// <summary>
        /// 累计借出连接数（池占用峰值的上界估计，配合 active 可看 idle）。
        /// </summary>
        private static long _lifetimeConnections;

        /// <summary>
        /// 当前借出未归还的连接数（Disposed 事件里归还）。
        /// </summary>
        private static long _activeConnections;

        /// <summary>
        /// 登记一条借出的连接：active +1，并挂 Disposed 回调做归还记账。
        /// U2 雪崩定位靠人肉 dotnet-counters（docs/p8-analysis.md）；这里把同口径的数字
        /// 变成可被测试/运维代码读取的计数器，使"池被打穿"不再只能事后从 PG 侧反推。
        /// </summary>
        private static void TrackPoolConnection(NpgsqlConnection connection)
        {
            Interlocked.Increment(ref _lifetimeConnections);
            Interlocked.Increment(ref _activeConnections);

            // NpgsqlConnection 是 sealed，无法包饰器；且 Npgsql 明确不支持 Disposed 事件
            // （抛 NotSupportedException，请用 StateChange）。连接归还池时状态必然落到
            // Closed，故用 StateChange 回收计数——await using 与异常路径都走这条路。
            connection.StateChange += OnConnectionStateChange;
        }

        private static void OnConnectionStateChange(object sender, StateChangeEventArgs e)
        {
            if (e.CurrentState == ConnectionState.Closed)
            {
                Interlocked.Decrement(ref _activeConnections);
            }
        }

        /// <summary>
        /// 异步从连接池获取并打开一个新连接（调用方负责 await using 释放）。
        /// </summary>
        protected async Task<NpgsqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = GetDataSource().CreateConnection();
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                TrackPoolConnection(connection);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// 原生异步事务执行器：异常自动回滚，成功自动提交。
        /// </summary>
        protected async Task<T> RunInTransactionAsync<T>(
            Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var result = await action(connection, transaction, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // 回滚失败：连接归还池时由 Npgsql 重置会话状态
                }

                throw;
            }
        }

        /// <summary>
        /// 原生异步事务执行器（无返回值）。
        /// </summary>
        protected Task RunInTransactionAsync(
            Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> action,
            CancellationToken cancellationToken = default)
        {
            return RunInTransactionAsync<object>(async (conn, tx, ct) =>
            {
                await action(conn, tx, ct).ConfigureAwait(false);
                return null;
            }, cancellationToken);
        }

        // ===========================================================
        // 异步命令助手（统一参数化 + CancellationToken 传递）
        // ===========================================================

        /// <summary>
        /// 执行非查询语句（异步）。
        /// </summary>
        protected static async Task<int> ExecuteNonQueryAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string sql,
            CancellationToken cancellationToken,
            params NpgsqlParameter[] parameters)
        {
            await using var cmd = new NpgsqlCommand(sql, connection, transaction);
            if (parameters != null && parameters.Length > 0)
            {
                cmd.Parameters.AddRange(parameters);
            }

            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 执行标量查询（异步）。
        /// </summary>
        protected static async Task<object> ExecuteScalarAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string sql,
            CancellationToken cancellationToken,
            params NpgsqlParameter[] parameters)
        {
            await using var cmd = new NpgsqlCommand(sql, connection, transaction);
            if (parameters != null && parameters.Length > 0)
            {
                cmd.Parameters.AddRange(parameters);
            }

            return await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 判断表是否存在（异步；幂等 DDL 门卫用）。
        /// </summary>
        protected static async Task<bool> TableExistsAsync(
            NpgsqlConnection connection,
            string tableName,
            CancellationToken cancellationToken)
        {
            var result = await ExecuteScalarAsync(
                connection,
                null,
                "SELECT to_regclass(@table) IS NOT NULL",
                cancellationToken,
                new NpgsqlParameter("table", "public." + tableName)).ConfigureAwait(false);

            return result != null && result != DBNull.Value && (bool)result;
        }

        /// <summary>
        /// DateTime → timestamptz 写入值：统一为 UTC（Npgsql 6+ 要求 timestamptz 参数为 UTC）。
        /// </summary>
        protected static DateTime ToUtcDateTime(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
            {
                return value;
            }

            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        /// <summary>
        /// 可空 DateTime → 参数值（UTC 归一或 DBNull）。
        /// </summary>
        protected static object ToDbValue(DateTime? value)
        {
            return value.HasValue ? (object)ToUtcDateTime(value.Value) : DBNull.Value;
        }

        /// <summary>
        /// 可空值类型 → 参数值（null 转 DBNull）。
        /// </summary>
        protected static object ToDbValue<T>(T? value) where T : struct
        {
            return value.HasValue ? (object)value.Value : DBNull.Value;
        }

        /// <summary>
        /// 可空字符串 → 参数值（null/空 转 DBNull）。
        /// </summary>
        protected static object ToDbValue(string value)
        {
            return string.IsNullOrEmpty(value) ? (object)DBNull.Value : value;
        }

        protected void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name + " has been disposed and cannot be accessed.");
            }
        }

        public void Dispose()
        {
            _disposed = true;
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool dispose)
        {
            // NpgsqlDataSource 为进程级共享，不在此处释放
        }
    }
}
