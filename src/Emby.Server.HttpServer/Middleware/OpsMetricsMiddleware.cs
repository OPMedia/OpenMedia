using System.Diagnostics;
using System.Threading.Tasks;
using Emby.Server.HttpServer.Observability;
using Microsoft.AspNetCore.Http;

namespace Emby.Server.HttpServer.Middleware
{
    /// <summary>
    /// 自研的 HTTP 指标埋点：prometheus-net.AspNetCore 的 UseHttpMetrics 需要
    /// 不同的管线/端点注册方式，这里直接包一层 middleware，保持 RouteTable 不变。
    /// 关闭 EnableMetrics 时是纯粹的直传，不产生指标标签基数。
    /// </summary>
    public sealed class OpsMetricsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly OpsEndpointsOptions _options;

        public OpsMetricsMiddleware(RequestDelegate next, OpsEndpointsOptions options)
        {
            _next = next;
            _options = options;
        }

        public async Task Invoke(HttpContext context)
        {
            if (!_options.EnableMetrics)
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            var start = Stopwatch.GetTimestamp();
            var status = 500;
            try
            {
                await _next(context).ConfigureAwait(false);
                status = context.Response.StatusCode;
            }
            finally
            {
                OpsMetrics.RecordHttp(context.Request.Method, status, Stopwatch.GetElapsedTime(start).TotalSeconds);
            }
        }
    }
}
