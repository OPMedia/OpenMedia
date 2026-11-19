using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Emby.Server.HttpServer.Observability
{
    /// <summary>
    /// readiness 的数据库探针：一条 SELECT 1，使用独立、非池化连接。
    /// 不共享仓储的 NpgsqlDataSource，避免探针自己把业务池占满或掩盖池耗尽。
    /// </summary>
    public sealed class PgHealthProbe
    {
        private const int ProbeTimeoutMilliseconds = 2000;

        private readonly string _connectionString;

        public PgHealthProbe(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<DatabaseHealthResult> ProbeAsync(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ProbeTimeoutMilliseconds);

            try
            {
                var builder = new NpgsqlConnectionStringBuilder(_connectionString)
                {
                    Pooling = false,
                    Timeout = 2,
                    CommandTimeout = 2
                };

                await using var connection = new NpgsqlConnection(builder.ConnectionString);
                await connection.OpenAsync(timeoutCts.Token).ConfigureAwait(false);
                using var command = new NpgsqlCommand("SELECT 1", connection);
                var value = await command.ExecuteScalarAsync(timeoutCts.Token).ConfigureAwait(false);

                if (value == null || Convert.ToInt32(value, CultureInfo.InvariantCulture) != 1)
                {
                    return DatabaseHealthResult.Fail("SELECT 1 返回了非预期结果");
                }

                stopwatch.Stop();
                return DatabaseHealthResult.Ok(
                    "SELECT 1 ok (" + stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms)");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 客户端断开连接：不写响应，让 ASP.NET Core 自行清理。
                throw;
            }
            catch (Exception ex)
            {
                var message = ex.Message ?? string.Empty;
                if (message.Length > 200)
                {
                    message = message.Substring(0, 200);
                }

                return DatabaseHealthResult.Fail(ex.GetType().Name + ": " + message);
            }
        }
    }
}
