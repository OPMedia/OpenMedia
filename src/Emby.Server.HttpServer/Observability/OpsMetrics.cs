using Prometheus;

namespace Emby.Server.HttpServer.Observability
{
    /// <summary>
    /// OpenMedia 自己的指标系列。全部挂在默认 registry，和 prometheus-net 自带的
    /// process/dotnet 系列一起由 /metrics 输出；前缀 openmedia_ 便于抓取端区分。
    /// </summary>
    public static class OpsMetrics
    {
        public const string HttpRequestsName = "openmedia_http_requests_total";
        public const string HttpDurationName = "openmedia_http_request_duration_seconds";
        public const string TranscodingActiveName = "openmedia_transcoding_sessions_active";
        public const string TranscodingTotalName = "openmedia_transcoding_sessions_total";

        private static readonly Counter HttpRequests =
            Metrics.CreateCounter(HttpRequestsName, "HTTP requests handled by Kestrel.", "method", "status");

        private static readonly Histogram HttpDuration =
            Metrics.CreateHistogram(HttpDurationName, "HTTP request duration in seconds.", new HistogramConfiguration
            {
                Buckets = new[] { 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10 },
                LabelNames = new[] { "method", "status" }
            });

        static OpsMetrics()
        {
            // 采集瞬间才刷新进程计数：不需要常驻定时器，也不会在关闭后再开线程。
            // 首次调用同时触发转码系列在 registry 中注册，保证 /metrics 首次抓取就能看到。
            TranscodeProcessProbe.Refresh();
            Metrics.DefaultRegistry.AddBeforeCollectCallback(TranscodeProcessProbe.Refresh);
        }

        public static void RecordHttp(string method, int status, double durationSeconds)
        {
            var labels = new[] { method ?? "UNKNOWN", status.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            HttpRequests.WithLabels(labels).Inc();
            HttpDuration.WithLabels(labels).Observe(durationSeconds);
        }
    }
}
