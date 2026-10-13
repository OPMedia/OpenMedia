using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MediaBrowser.Model.Logging;
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
        /// 默认连接串（方案 §5.1；可被 system.xml 的 Postgres 节点或 OPENMEDIA_PGCONN 环境变量覆盖）。
        /// </summary>
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
        /// 从连接池获取并打开一个新连接。
        /// </summary>
        protected NpgsqlConnection CreateConnection()
        {
            CheckDisposed();

            var dataSource = DataSources.GetOrAdd(_connectionString, cs =>
            {
                var builder = new NpgsqlConnectionStringBuilder(cs)
                {
                    // 与旧 ManagedConnection 语义对齐：池化由 Npgsql 自带，默认即可
                    Pooling = true
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
        /// </summary>
        protected static int GetPrimaryKeyColumnCount(NpgsqlConnection connection, string tableName)
        {
            const string sql = @"
                SELECT count(*)
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
