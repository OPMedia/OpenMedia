using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.HttpServer.Middleware;
using Emby.Server.HttpServer.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.HttpServer
{
    /// <summary>
    /// 运维端点的短路行为：liveness 不查 DB、readiness 反映 DB 与初始化状态、
    /// metrics 受开关控制。用 DefaultHttpContext 直接驱动中间件，不需要起端口。
    /// </summary>
    [TestClass]
    public class OpsEndpointsTests
    {
        [TestMethod]
        public async Task Liveness_DoesNotTouchDatabase_AndReturns200()
        {
            var probeCalled = false;
            var options = CreateOptions(databaseProbe: _ =>
            {
                probeCalled = true;
                throw new InvalidOperationException("liveness must not probe the database");
            });

            var result = await InvokeAsync("/health/live", options).ConfigureAwait(false);

            Assert.AreEqual(200, result.StatusCode);
            Assert.IsFalse(probeCalled);
            StringAssert.Contains(result.Body, "Healthy");
            Assert.IsFalse(result.NextCalled);
        }

        [TestMethod]
        public async Task Readiness_Returns200_WithDatabaseDetail()
        {
            var result = await InvokeAsync("/health/ready", CreateOptions()).ConfigureAwait(false);

            Assert.AreEqual(200, result.StatusCode);
            StringAssert.Contains(result.Body, "SELECT 1 ok");
            StringAssert.Contains(result.Body, "application");
        }

        [TestMethod]
        public async Task Readiness_Returns503_WhenDatabaseFails()
        {
            var options = CreateOptions(databaseProbe: _ => Task.FromResult(DatabaseHealthResult.Fail("connection refused")));

            var result = await InvokeAsync("/health/ready", options).ConfigureAwait(false);

            Assert.AreEqual(503, result.StatusCode);
            StringAssert.Contains(result.Body, "Unhealthy");
            StringAssert.Contains(result.Body, "connection refused");
        }

        [TestMethod]
        public async Task Readiness_Returns503_WhenStartupNotComplete()
        {
            var result = await InvokeAsync("/health/ready", CreateOptions(applicationReady: false)).ConfigureAwait(false);

            Assert.AreEqual(503, result.StatusCode);
            StringAssert.Contains(result.Body, "Unhealthy");
        }

        [TestMethod]
        public async Task Readiness_Returns503_DuringShutdown()
        {
            var result = await InvokeAsync("/health/ready", CreateOptions(shuttingDown: true)).ConfigureAwait(false);

            Assert.AreEqual(503, result.StatusCode);
        }

        [TestMethod]
        public async Task Health_AggregateIncludesDatabaseStatus()
        {
            var result = await InvokeAsync("/health", CreateOptions()).ConfigureAwait(false);

            Assert.AreEqual(200, result.StatusCode);
            StringAssert.Contains(result.Body, "database");
            StringAssert.Contains(result.Body, "SELECT 1 ok");
        }

        [TestMethod]
        public async Task BaseUrlPrefixedHealthEndpoint_IsRecognized()
        {
            var result = await InvokeAsync("/media/health/live", CreateOptions(baseUrl: "/media")).ConfigureAwait(false);

            Assert.AreEqual(200, result.StatusCode);
            Assert.IsFalse(result.NextCalled);
        }

        [TestMethod]
        public async Task Metrics_Returns404_WhenDisabled()
        {
            var result = await InvokeAsync("/metrics", CreateOptions(enableMetrics: false)).ConfigureAwait(false);

            Assert.AreEqual(404, result.StatusCode);
            Assert.IsFalse(result.NextCalled);
        }

        [TestMethod]
        public async Task Metrics_ExposesOurHttpAndTranscodeSeries_WhenEnabled()
        {
            OpsMetrics.RecordHttp("GET", 200, 0.01);
            var result = await InvokeAsync("/metrics", CreateOptions(enableMetrics: true)).ConfigureAwait(false);

            Assert.AreEqual(200, result.StatusCode);
            StringAssert.Contains(result.Body, OpsMetrics.HttpRequestsName);
            StringAssert.Contains(result.Body, OpsMetrics.HttpDurationName);
            StringAssert.Contains(result.Body, OpsMetrics.TranscodingActiveName);
            StringAssert.Contains(result.Body, OpsMetrics.TranscodingTotalName);
        }

        [TestMethod]
        public void FfmpegDetection_IgnoresOtherProcesses()
        {
            Assert.IsTrue(TranscodeProcessProbe.IsFfmpegCommandLine("/usr/bin/ffmpeg -i input.mkv"));
            Assert.IsTrue(TranscodeProcessProbe.IsFfmpegCommandLine("ffmpeg.exe -i input.mkv"));
            Assert.IsFalse(TranscodeProcessProbe.IsFfmpegCommandLine("/usr/bin/ffprobe -i input.mkv"));
            Assert.IsFalse(TranscodeProcessProbe.IsFfmpegCommandLine(string.Empty));
        }

        private static OpsEndpointsOptions CreateOptions(
            bool enableMetrics = false,
            Func<CancellationToken, Task<DatabaseHealthResult>> databaseProbe = null,
            bool applicationReady = true,
            bool shuttingDown = false,
            string baseUrl = "")
        {
            return new OpsEndpointsOptions
            {
                EnableMetrics = enableMetrics,
                BaseUrl = baseUrl,
                DatabaseProbe = databaseProbe ?? (_ => Task.FromResult(DatabaseHealthResult.Ok("SELECT 1 ok (1 ms)"))),
                IsApplicationReady = () => applicationReady,
                IsShuttingDown = () => shuttingDown
            };
        }

        private static async Task<InvokeResult> InvokeAsync(string path, OpsEndpointsOptions options, string method = "GET")
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Request.Method = method;
            var body = new MemoryStream();
            context.Response.Body = body;

            var nextCalled = false;
            var middleware = new OpsEndpointsMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }, options);

            await middleware.Invoke(context).ConfigureAwait(false);

            body.Position = 0;
            using var reader = new StreamReader(body);
            return new InvokeResult(context.Response.StatusCode, await reader.ReadToEndAsync().ConfigureAwait(false), nextCalled);
        }

        private sealed class InvokeResult
        {
            public InvokeResult(int statusCode, string body, bool nextCalled)
            {
                StatusCode = statusCode;
                Body = body;
                NextCalled = nextCalled;
            }

            public int StatusCode { get; }

            public string Body { get; }

            public bool NextCalled { get; }
        }
    }
}
