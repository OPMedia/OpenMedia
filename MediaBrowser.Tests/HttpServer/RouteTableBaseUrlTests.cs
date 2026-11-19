using System;
using System.Text;
using Emby.Server.HttpServer.Routing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.HttpServer
{
    /// <summary>
    /// BaseUrl 路由前缀回归测试：默认配置与基线逐条一致；配置前缀后仅新增带前缀路由。
    /// </summary>
    [TestClass]
    public class RouteTableBaseUrlTests
    {
        [TestMethod]
        public void WithoutBaseUrl_ExistingThreePrefixesAreUnchanged()
        {
            var table = CreateTable(null);

            Assert.AreEqual(4, table.RestPathCount);
            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/System/Info/Public", NullLogger.Instance));
            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/emby/System/Info/Public", NullLogger.Instance));
            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/mediabrowser/System/Info/Public", NullLogger.Instance));
            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/emby/emby/System/Info/Public", NullLogger.Instance));
            Assert.IsNull(table.GetRestPathForRequest("GET", "/media/emby/System/Info/Public", NullLogger.Instance));
        }

        [TestMethod]
        public void WithBaseUrl_BasePrefixedRoutesAreAdded_AndOldPathsStillWork()
        {
            var table = CreateTable("/media");

            Assert.AreEqual(8, table.RestPathCount);
            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/media/emby/System/Info/Public", NullLogger.Instance));
            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/media/mediabrowser/System/Info/Public", NullLogger.Instance));
            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/media/System/Info/Public", NullLogger.Instance));
            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/media/emby/emby/System/Info/Public", NullLogger.Instance));

            // 现有三前缀的行为不能因为 BaseUrl 而消失。
            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/emby/System/Info/Public", NullLogger.Instance));
            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/mediabrowser/System/Info/Public", NullLogger.Instance));
            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/System/Info/Public", NullLogger.Instance));
        }

        [TestMethod]
        public void WithBaseUrl_SpaWildcardRouteMatchesBasePrefixedDeepLink()
        {
            var table = new RouteTable(t => Activator.CreateInstance(t), TypeParser.GetParseFn, "/media");
            table.RegisterService(typeof(WebAppStub));

            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/media/app/library/3", NullLogger.Instance));
            Assert.IsNotNull(table.GetRestPathForRequest("GET", "/app/library/3", NullLogger.Instance));
        }

        private static RouteTable CreateTable(string baseUrl)
        {
            var table = new RouteTable(t => Activator.CreateInstance(t), TypeParser.GetParseFn, baseUrl);
            table.RegisterService(typeof(InfoStub));
            return table;
        }

        [Route("/System/Info/Public", "GET")]
        private sealed class InfoRequest
        {
        }

        private sealed class InfoStub
        {
            public object Get(InfoRequest request) => null;
        }

        [Route("/app/{ResourceName*}", "GET")]
        private sealed class WebAppRequest
        {
            public string ResourceName { get; set; }
        }

        private sealed class WebAppStub
        {
            public object Get(WebAppRequest request) => null;
        }

        private sealed class NullLogger : ILogger
        {
            public static readonly NullLogger Instance = new NullLogger();

            public void Info(string message, params object[] paramList)
            {
            }

            public void Error(string message, params object[] paramList)
            {
            }

            public void Warn(string message, params object[] paramList)
            {
            }

            public void Debug(string message, params object[] paramList)
            {
            }

            public void Fatal(string message, params object[] paramList)
            {
            }

            public void FatalException(string message, Exception exception, params object[] paramList)
            {
            }

            public void ErrorException(string message, Exception exception, params object[] paramList)
            {
            }

            public void LogMultiline(string message, LogSeverity severity, StringBuilder additionalContent)
            {
            }

            public void Log(LogSeverity severity, string message, params object[] paramList)
            {
            }
        }
    }
}
