using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.HttpServer.Observability;
using Emby.Server.HttpServer.Routing;
using Microsoft.AspNetCore.Http;
using Prometheus;

namespace Emby.Server.HttpServer.Middleware
{
    /// <summary>
    /// 运维端点：在自研 RouteTable 之前短路处理 /health、/health/live、
    /// /health/ready 和 /metrics。不引入 ASP.NET 端点路由，也不经过认证过滤器
    /// （探针不带 token）。/metrics 受 EnableMetrics 开关控制，关闭时 404。
    /// </summary>
    public sealed class OpsEndpointsMiddleware
    {
        private const string LivePath = "/health/live";
        private const string ReadyPath = "/health/ready";
        private const string HealthPath = "/health";
        private const string MetricsPath = "/metrics";

        private readonly RequestDelegate _next;
        private readonly OpsEndpointsOptions _options;

        public OpsEndpointsMiddleware(RequestDelegate next, OpsEndpointsOptions options)
        {
            _next = next;
            _options = options;
        }

        public async Task Invoke(HttpContext context)
        {
            var path = BaseUrlHelper.Strip(context.Request.Path.Value ?? string.Empty, _options.BaseUrl);
            var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);

            if (isRead && string.Equals(path, LivePath, StringComparison.OrdinalIgnoreCase))
            {
                // 故意不调用 DB 探针：DB 故障应该让 readiness 摘流量，而不是让容器被重启。
                await WriteJsonAsync(context, 200, new
                {
                    status = "Healthy",
                    checks = new { liveness = new { status = "Healthy" } }
                }, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            if (isRead && string.Equals(path, ReadyPath, StringComparison.OrdinalIgnoreCase))
            {
                await WriteReadinessAsync(context, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            if (isRead && string.Equals(path, HealthPath, StringComparison.OrdinalIgnoreCase))
            {
                await WriteReadinessAsync(context, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, MetricsPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!_options.EnableMetrics)
                {
                    context.Response.StatusCode = 404;
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    return;
                }

                if (isRead)
                {
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
                    await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
                    return;
                }
            }

            await _next(context).ConfigureAwait(false);
        }

        private async Task WriteReadinessAsync(HttpContext context, CancellationToken cancellationToken)
        {
            var applicationStatus = _options.IsApplicationReady() && !_options.IsShuttingDown();
            DatabaseHealthResult database;
            try
            {
                database = await _options.DatabaseProbe(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var healthy = applicationStatus && database.Healthy;
            await WriteJsonAsync(context, healthy ? 200 : 503, new
            {
                status = healthy ? "Healthy" : "Unhealthy",
                checks = new
                {
                    application = new
                    {
                        status = applicationStatus ? "Healthy" : "Unhealthy"
                    },
                    database = new
                    {
                        status = database.Healthy ? "Healthy" : "Unhealthy",
                        detail = database.Detail
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        private static async Task WriteJsonAsync(HttpContext context, int statusCode, object payload, CancellationToken cancellationToken)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
        }
    }
}
