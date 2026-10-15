using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using Npgsql;

namespace Emby.Server.PgData
{
    /// <summary>
    /// PostgreSQL 仓储基类：NpgsqlDataSource 管理（按连接串共享、内置连接池）、
    /// 连接获取、日志与事务助手。替代旧 BaseSqliteRepository + ManagedConnection。
    /// </summary>
    public abstract class BasePgRepository : IDisposable
    {
        /// <summary>
        /// P8 并发防护（docs/p8-analysis.md ③方案，Chief 拍板 + Reviewer/Architect 护栏）：
        /// 全局单门信号量——所有仓储共用同一个池（同连接串=同一 NpgsqlDataSource），
        /// 门也必须全局一个（Architect 护栏①：不能按仓储×N，否则 N×50 > 池 100 自己打穿）。
        /// 只守 DB 执行点（护栏②），不包请求管道。
        /// 嵌套连接检查（护栏③）已做：GetItem 的 connection 参数零使用、
        /// GetItems/GetItemList 执行体内无嵌套仓储调用——持门槽位内不开第二条连接。
        /// </summary>
        private static readonly SemaphoreSlim QueryGate = new SemaphoreSlim(QueryGateCapacity, QueryGateCapacity);
        private const int QueryGateCapacity = 50;

        /// <summary>
        /// 池等待快速失败（P8 ③方案 B）：15s 默认收窄到 3s——池耗尽时快速失败
        /// 不雪崩（U2 的 60s 超时级联止损层）。
        /// </summary>
        private const int PoolWaitTimeoutMs = 3000;

        /// <summary>
        /// 查询门：获取槽位（等待 3s 后 503），执行查询，释放。
        /// 覆盖：GetItems/GetItemList/GetItemValues（Reviewer 备注②：不留第二个 U2 形态）。
        /// 不覆盖：单条 RetrieveItem/认证/写入（信号量 50 < 池 100 留 50 余量）。
        /// </summary>
        protected static T WithQueryGate<T>(Func<T> action)
        {
            if (!QueryGate.Wait(PoolWaitTimeoutMs))
            {
                throw new PoolExhaustedException(QueryGateCapacity);
            }

            try
            {
                return action();
            }
            finally
            {
                QueryGate.Release();
            }
        }

        protected static void WithQueryGate(Action action)
        {
            WithQueryGate<object>(() =>
            {
                action();
                return null;
            });
        }
        /// <summary>
        /// 默认连接串（方案 §5.1；可被 OPENMEDIA_PGCONN 环境变量覆盖——ApplicationHost.GetPostgresConnectionString）。
        /// 端口 15432：2026-10-15 起本机安全软件屏蔽数据库端口（5432/5433/3306 回环全堵，高位端口正常），
        /// PG 已迁移至 15432。若日后解除屏蔽可改回 5432。
        /// </summary>
        public const string DefaultConnectionString =
            "Host=localhost;Port=15432;Database=openmedia;Username=postgres;Password=123456";

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
        /// 从连接池获取并打开一个新连接。
        /// </summary>
        protected NpgsqlConnection CreateConnection()
        {
            CheckDisposed();

            var dataSource = DataSources.GetOrAdd(_connectionString, cs =>
            {
                var builder = new NpgsqlConnectionStringBuilder(cs)
                {
                    // 与旧 ManagedConnection 语义对齐：池化由 Npgsql 自带，默认即可。
                    // P8：池等待超时 15s→3s（快速失败不雪崩——U2 60s 级联的止损层）
                    Pooling = true,
                    Timeout = 3
                };
                return NpgsqlDataSource.Create(builder.ConnectionString);
            });

            var connection = dataSource.CreateConnection();
            connection.Open();
            return connection;
        }

        /// <summary>
        /// 在事务中执行操作（异常时回滚）。
        /// </summary>
        protected T RunInTransaction<T>(Func<NpgsqlConnection, NpgsqlTransaction, T> action)
        {
            using (var connection = CreateConnection())
            {
                return RunInTransaction(connection, action);
            }
        }

        protected T RunInTransaction<T>(NpgsqlConnection connection, Func<NpgsqlConnection, NpgsqlTransaction, T> action)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    var result = action(connection, transaction);
                    transaction.Commit();
                    return result;
                }
                catch
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch
                    {
                        // 回滚失败：连接归还连接池时会重置状态
                    }

                    throw;
                }
            }
        }

        protected void RunInTransaction(Action<NpgsqlConnection, NpgsqlTransaction> action)
        {
            RunInTransaction<object>((connection, transaction) =>
            {
                action(connection, transaction);
                return null;
            });
        }

        protected void RunInTransaction(NpgsqlConnection connection, Action<NpgsqlConnection, NpgsqlTransaction> action)
        {
            RunInTransaction(connection, (conn, transaction) =>
            {
                action(conn, transaction);
                return (object)null;
            });
        }

        /// <summary>
        /// 判断表是否存在（幂等 DDL 门卫用）。
        /// </summary>
        protected static bool TableExists(NpgsqlConnection connection, string tableName)
        {
            using (var cmd = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL", connection))
            {
                cmd.Parameters.AddWithValue("table", "public." + tableName);
                return (bool)cmd.ExecuteScalar();
            }
        }

        /// <summary>
        /// 读取表的主键列数（schema 对齐迁移判断用）。
        /// 数 indkey 数组长度（列数）；不能数 pg_index 行数（那是索引个数，恒为 1）。
        /// </summary>
        protected static int GetPrimaryKeyColumnCount(NpgsqlConnection connection, string tableName)
        {
            const string sql = @"
                SELECT COALESCE(array_length(i.indkey, 1), 0)
                FROM pg_index i
                WHERE i.indrelid = @table::regclass
                  AND i.indisprimary";

            using (var cmd = new NpgsqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("table", "public." + tableName);
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
            }
        }

        /// <summary>
        /// 获取表的列名列表（小写）。
        /// </summary>
        protected static List<string> GetColumnNames(NpgsqlConnection connection, string tableName)
        {
            var list = new List<string>();

            const string sql = @"
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @table
                ORDER BY ordinal_position";

            using (var cmd = new NpgsqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("table", tableName);

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
