using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Emby.Server.HttpServer.RequestResponse;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.HttpServer
{
    /// <summary>
    /// 反向代理头可信边界与 BaseUrl 绝对 URL 回归测试。
    /// 基线（484ae47b）下前两个用例是红的：X-Forwarded-* 被无条件采信。
    /// </summary>
    [TestClass]
    public class ForwardedHeadersSecurityTests
    {
        private static KestrelRequest CreateRequest(
            string remoteIp,
            string forwardedFor = null,
            string forwardedProto = null,
            string path = "/emby/System/Info/Public",
            string[] knownProxies = null,
            INetworkManager networkManager = null,
            string baseUrl = null,
            string realIp = null)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost:8096");
            context.Request.Path = path;

            if (forwardedFor != null)
            {
                context.Request.Headers["X-Forwarded-For"] = forwardedFor;
            }

            if (forwardedProto != null)
            {
                context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
            }

            if (realIp != null)
            {
                context.Request.Headers["X-Real-IP"] = realIp;
            }

            return new KestrelRequest(context, "Test", knownProxies, networkManager, baseUrl);
        }

        [TestMethod]
        public void SpoofedForwardedFor_IsIgnored_WhenPeerIsNotInWhitelist()
        {
            var request = CreateRequest(
                "203.0.113.10",
                forwardedFor: "10.0.0.1",
                knownProxies: new[] { "10.0.0.1" },
                networkManager: new LocalNetworkStub());

            Assert.AreEqual("203.0.113.10", request.RemoteIp);
            Assert.IsFalse(request.IsLocal);
            Assert.IsNull(request.XForwardedFor);
        }

        [TestMethod]
        public void SpoofedForwardedProto_IsIgnored_WhenPeerIsNotInWhitelist()
        {
            var request = CreateRequest("203.0.113.10", forwardedProto: "https", knownProxies: new[] { "10.0.0.1" });

            Assert.AreEqual("http", request.GetUri().Scheme);
            Assert.IsFalse(request.IsSecureConnection);
            Assert.IsNull(request.XForwardedProtocol);
        }

        [TestMethod]
        public void SpoofedRealIp_IsIgnored_WhenPeerIsNotInWhitelist()
        {
            var request = CreateRequest("203.0.113.10", realIp: "10.0.0.1");

            Assert.AreEqual("203.0.113.10", request.RemoteIp);
            Assert.IsNull(request.XRealIp);
        }

        [TestMethod]
        public void ForwardedFor_IsTrusted_WhenPeerIsInWhitelist()
        {
            var request = CreateRequest(
                "10.0.0.1",
                forwardedFor: "10.0.0.1",
                knownProxies: new[] { "10.0.0.1" },
                networkManager: new LocalNetworkStub());

            Assert.AreEqual("10.0.0.1", request.RemoteIp);
            Assert.IsTrue(request.IsLocal);
        }

        [TestMethod]
        public void MultiHopForwardedFor_UsesFirstUntrustedAddress()
        {
            var request = CreateRequest(
                "10.0.0.1",
                forwardedFor: "8.8.8.8, 10.0.0.1",
                knownProxies: new[] { "10.0.0.1" },
                networkManager: new LocalNetworkStub());

            Assert.AreEqual("8.8.8.8", request.RemoteIp);
            Assert.IsFalse(request.IsLocal);
        }

        [TestMethod]
        public void ForwardedProto_IsTrusted_WhenPeerIsInWhitelist()
        {
            var request = CreateRequest(
                "10.0.0.1",
                forwardedProto: "https",
                knownProxies: new[] { "10.0.0.1" });

            Assert.AreEqual("https", request.GetUri().Scheme);
            Assert.IsTrue(request.IsSecureConnection);
        }

        [TestMethod]
        public void Ipv6ProxyWithBracketedPort_IsTrusted_AndClientPortIsStripped()
        {
            var request = CreateRequest(
                "2001:db8::1",
                forwardedFor: "2001:db8::2, [2001:db8::1]:8443",
                knownProxies: new[] { "[2001:db8::1]:8443" });

            Assert.AreEqual("2001:db8::2", request.RemoteIp);
        }

        [TestMethod]
        public void Ipv4MappedPeerMatchesCidr_AndForwardedPortIsStripped()
        {
            var request = CreateRequest(
                "::ffff:10.0.0.1",
                forwardedFor: "203.0.113.7:5555",
                knownProxies: new[] { "10.0.0.0/8" });

            Assert.AreEqual("203.0.113.7", request.RemoteIp);
        }

        [TestMethod]
        public void BaseUrl_IsAddedToAbsoluteUri_WhenProxyStripsPrefix()
        {
            var stripped = CreateRequest("127.0.0.1", baseUrl: "/media");
            var prefixed = CreateRequest(
                "127.0.0.1",
                path: "/media/emby/System/Info/Public",
                baseUrl: "/media");

            Assert.AreEqual("http://localhost:8096/media/emby/System/Info/Public", stripped.AbsoluteUri);
            Assert.AreEqual("http://localhost:8096/media/emby/System/Info/Public", prefixed.AbsoluteUri);
        }

        [TestMethod]
        public void DefaultConfigurationWithoutForwardedHeaders_KeepsExistingBehavior()
        {
            var request = CreateRequest("127.0.0.1");

            Assert.AreEqual("127.0.0.1", request.RemoteIp);
            Assert.IsTrue(request.IsLocal);
            Assert.AreEqual("http", request.GetUri().Scheme);
            Assert.AreEqual("http://localhost:8096/emby/System/Info/Public", request.AbsoluteUri);
        }

        private sealed class LocalNetworkStub : INetworkManager
        {
            public Func<string[]> LocalSubnetsFn { get; set; }

            public event EventHandler NetworkChanged
            {
                add { }
                remove { }
            }

            public int GetRandomUnusedTcpPort() => 0;

            public int GetRandomUnusedUdpPort() => 0;

            public List<string> GetMacAddresses() => new List<string>();

            public bool IsInPrivateAddressSpace(string endpoint) => true;

            public IEnumerable<NetworkShare> GetNetworkShares(string path) => new List<NetworkShare>();

            public IEnumerable<FileSystemEntryInfo> GetNetworkDevices() => new List<FileSystemEntryInfo>();

            public bool IsInLocalNetwork(string endpoint)
            {
                if (!IPAddress.TryParse(endpoint, out var address))
                {
                    return false;
                }

                return address.AddressFamily == AddressFamily.InterNetwork
                    ? address.GetAddressBytes()[0] == 10
                    : IPAddress.IsLoopback(address);
            }

            public IpAddressInfo[] GetLocalIpAddresses() => new IpAddressInfo[0];

            public IpAddressInfo ParseIpAddress(string ipAddress) => null;

            public bool TryParseIpAddress(string ipAddress, out IpAddressInfo ipAddressInfo)
            {
                ipAddressInfo = null;
                return false;
            }

            public Task<IpAddressInfo[]> GetHostAddressesAsync(string host) => Task.FromResult(new IpAddressInfo[0]);

            public bool IsAddressInSubnets(string addressString, string[] subnets) => false;
        }
    }
}
