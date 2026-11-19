using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HttpManager = Emby.Server.Implementations.HttpClientManager.HttpClientManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.HttpClientManager
{
    /// <summary>
    /// HttpClientManager 用 host 作为 HttpClient 缓存键：userinfo、端口和 IPv6 不能进入键。
    /// </summary>
    [TestClass]
    public class GetHostFromUrlTests
    {
        private static string Invoke(string url)
        {
            var manager = RuntimeHelpers.GetUninitializedObject(typeof(HttpManager));
            var method = typeof(HttpManager).GetMethod("GetHostFromUrl", BindingFlags.Instance | BindingFlags.NonPublic);
            return (string)method.Invoke(manager, new object[] { url });
        }

        [DataTestMethod]
        [DataRow("http://host/x", "host")]
        [DataRow("http://user:secret@host/x", "host")]
        [DataRow("http://user:secret@host:8080/x", "host")]
        [DataRow("http://[::1]:8080/x", "[::1]")]
        [DataRow("https://[2001:db8::1]/x", "[2001:db8::1]")]
        [DataRow("http://host", "host")]
        [DataRow("ftp://host/x/y", "host")]
        [DataRow("host/x", "host/x")]
        [DataRow("http://", "http://")]
        public void GetHostFromUrl_ReturnsHostOnly(string url, string expected)
        {
            Assert.AreEqual(expected, Invoke(url));
        }

        [TestMethod]
        public void GetHostFromUrl_QueryIsNotPartOfHost()
        {
            Assert.AreEqual("host", Invoke("http://host?x=1"));
        }
    }
}
