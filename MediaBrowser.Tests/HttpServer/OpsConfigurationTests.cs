using MediaBrowser.Model.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.HttpServer
{
    /// <summary>
    /// 运维配置默认值：指标默认关闭（不向公网暴露），停机预算默认 10 秒。
    /// </summary>
    [TestClass]
    public class OpsConfigurationTests
    {
        [TestMethod]
        public void MetricsAreDisabledByDefault()
        {
            Assert.IsFalse(new ServerConfiguration().EnableMetrics);
        }

        [TestMethod]
        public void ShutdownTimeoutDefaultsToTenSeconds()
        {
            Assert.AreEqual(10, new ServerConfiguration().ShutdownTimeoutSeconds);
        }
    }
}
