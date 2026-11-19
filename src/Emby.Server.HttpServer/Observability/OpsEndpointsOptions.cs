using System;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Server.HttpServer.Observability
{
    /// <summary>
    /// 运维端点（/health*、/metrics）所需的最小依赖。用委托注入，避免把
    /// ApplicationHost / PgData 的依赖拉进 HTTP 层，也方便单元测试替代探针。
    /// </summary>
    public sealed class OpsEndpointsOptions
    {
        public bool EnableMetrics { get; set; }

        public string BaseUrl { get; set; } = string.Empty;

        public Func<CancellationToken, Task<DatabaseHealthResult>> DatabaseProbe { get; set; }

        public Func<bool> IsApplicationReady { get; set; } = () => true;

        public Func<bool> IsShuttingDown { get; set; } = () => false;
    }

    public sealed class DatabaseHealthResult
    {
        public bool Healthy { get; set; }

        public string Detail { get; set; } = string.Empty;

        public static DatabaseHealthResult Ok(string detail) => new DatabaseHealthResult { Healthy = true, Detail = detail };

        public static DatabaseHealthResult Fail(string detail) => new DatabaseHealthResult { Healthy = false, Detail = detail };
    }
}
