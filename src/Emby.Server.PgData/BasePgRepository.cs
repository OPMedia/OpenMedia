using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
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
    /// - 并发控制完全交还 Npgsql 连接池 + PG 内核，不设应用层人为信号量。
    /// </summary>
    public abstract class BasePgRepository : IDisposable
    {
        /// <summary>
        /// 默认连接串（可被 OPENMEDIA_PGCONN 环境变量覆盖——ApplicationHost.GetPostgresConnectionString）。
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
                    // 连接池等待上限：由 Npgsql 内部排队，超时才失败（不设应用层信号量）
                    Timeout = 15,
                    MaxPoolSize = 100,
                    // 长事务/慢查询保护：单条命令 30s 上限，防止连接被无限期占用
                    CommandTimeout = 30
                };
                return NpgsqlDataSource.Create(builder.ConnectionString);
            });
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
