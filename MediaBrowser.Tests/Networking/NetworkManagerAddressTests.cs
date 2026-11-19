using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Emby.Server.Implementations.Networking;
using MediaBrowser.Model.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.Networking
{
    /// <summary>
    /// NetworkManager 的地址判定回归测试：私网分类、本地子网包含和自定义子网 ACL。
    /// </summary>
    [TestClass]
    public class NetworkManagerAddressTests
    {
        private sealed class RecordingLogger : ILogger
        {
            public List<string> Warnings { get; } = new List<string>();

            public void Info(string message, params object[] paramList)
            {
            }

            public void Error(string message, params object[] paramList)
            {
            }

            public void Warn(string message, params object[] paramList)
            {
                Warnings.Add(string.Format(message, paramList));
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

        private static NetworkManager CreateManager(RecordingLogger logger = null)
        {
            var manager = (NetworkManager)RuntimeHelpers.GetUninitializedObject(typeof(NetworkManager));
            var loggerProperty = typeof(NetworkManager).GetProperty("Logger", BindingFlags.Instance | BindingFlags.NonPublic);
            loggerProperty.SetValue(manager, logger ?? new RecordingLogger());
            return manager;
        }

        private static bool InvokeSubnets(NetworkManager manager, string address, params string[] subnets)
        {
            var method = typeof(NetworkManager).GetMethod(
                "IsAddressInSubnets",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(IPAddress), typeof(string), typeof(string[]) },
                null);
            return (bool)method.Invoke(manager, new object[] { IPAddress.Parse(address), address, subnets });
        }

        private static bool InvokePrivateCheck(NetworkManager manager, string endpoint, bool checkSubnets)
        {
            var method = typeof(NetworkManager).GetMethod(
                "IsInPrivateAddressSpace",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(bool) },
                null);
            return (bool)method.Invoke(manager, new object[] { endpoint, checkSubnets });
        }

        private static void SetLocalSubnets(NetworkManager manager, string key, params string[] networks)
        {
            var field = typeof(NetworkManager).GetField("_subnetLookup", BindingFlags.Instance | BindingFlags.NonPublic);
            var parsed = networks.Select(n => global::System.Net.IPNetwork.Parse(n)).ToList();
            var lookup = new Dictionary<string, List<global::System.Net.IPNetwork>>(StringComparer.Ordinal)
            {
                [key] = parsed
            };
            field.SetValue(manager, lookup);
        }

        [DataTestMethod]
        [DataRow("10.0.0.1", true)]
        [DataRow("10.255.255.255", true)]
        [DataRow("11.0.0.1", false)]
        [DataRow("9.255.255.255", false)]
        [DataRow("172.15.255.255", false)]
        [DataRow("172.16.0.0", true)]
        [DataRow("172.20.13.7", true)]
        [DataRow("172.31.255.255", true)]
        [DataRow("172.32.0.1", false)]
        [DataRow("172.16.evil.com", false)]
        [DataRow("172.255.1.1", false)]
        [DataRow("192.168.0.0", true)]
        [DataRow("192.168.255.255", true)]
        [DataRow("192.169.0.1", false)]
        [DataRow("192.168.evil.com", false)]
        [DataRow("192.168", false)]
        [DataRow("127.0.0.1", true)]
        [DataRow("127.255.255.255", true)]
        [DataRow("126.255.255.255", false)]
        [DataRow("127.evil.com", false)]
        [DataRow("169.254.0.1", true)]
        [DataRow("169.254.255.255", true)]
        [DataRow("169.253.1.1", false)]
        [DataRow("169.255.1.1", false)]
        [DataRow("169.evil.com", false)]
        [DataRow("::1", true)]
        [DataRow("0:0:0:0:0:0:0:1", true)]
        [DataRow("fc00::1", true)]
        [DataRow("fd00::1234", true)]
        [DataRow("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", true)]
        [DataRow("fe00::1", false)]
        [DataRow("fe80::1", false)]
        [DataRow("::ffff:192.168.1.5", true)]
        [DataRow("::ffff:10.1.2.3", true)]
        [DataRow("::ffff:8.8.8.8", false)]
        [DataRow("8.8.8.8", false)]
        [DataRow("100.64.0.1", false)]
        [DataRow("224.0.0.1", false)]
        [DataRow("0.0.0.0", false)]
        [DataRow("localhost", true)]
        [DataRow("LOCALHOST", true)]
        [DataRow("localhost.evil.com", false)]
        [DataRow("not-an-ip", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        public void IsInPrivateAddressSpace_ClassifiesByNetworkContainment(string endpoint, bool expected)
        {
            Assert.AreEqual(expected, CreateManager().IsInPrivateAddressSpace(endpoint));
        }

        [DataTestMethod]
        [DataRow("10.1.2.3", false, true)]
        [DataRow("192.168.1.1", false, true)]
        [DataRow("172.20.1.1", false, true)]
        [DataRow("8.8.8.8", false, false)]
        [DataRow("10.1.2.3", true, true)]
        [DataRow("192.168.1.1", true, true)]
        public void IsInPrivateAddressSpace_CheckSubnetsOnlyControlsLocalSubnetLookup(string endpoint, bool checkSubnets, bool expected)
        {
            Assert.AreEqual(expected, InvokePrivateCheck(CreateManager(), endpoint, checkSubnets));
        }

        [DataTestMethod]
        [DataRow("10.1.2.3", "10.1.2.0/24", true)]
        [DataRow("10.1.3.3", "10.1.2.0/24", false)]
        [DataRow("10.1.2.3", "10.0.0.0/8", true)]
        [DataRow("10.1.2.3", "192.168.1.0/24", false)]
        [DataRow("192.168.1.5", "192.168.1.0/24", false)]
        [DataRow("8.8.8.8", "8.8.8.0/24", false)]
        [DataRow("10.1.2.3", "10.1.2.255/24", true)]
        [DataRow("::ffff:10.1.2.3", "10.1.2.0/24", false)]
        public void IsInPrivateAddressSpaceAndLocalSubnet_UsesCidrContainment(string endpoint, string network, bool expected)
        {
            var manager = CreateManager();
            SetLocalSubnets(manager, endpoint.StartsWith("10.", StringComparison.Ordinal) ? "10" : endpoint.StartsWith("8.", StringComparison.Ordinal) ? "8" : "192", network);
            Assert.AreEqual(expected, manager.IsInPrivateAddressSpaceAndLocalSubnet(endpoint));
        }

        [DataTestMethod]
        [DataRow("255.255.255.0", 24)]
        [DataRow("255.255.240.0", 20)]
        [DataRow("255.0.0.0", 8)]
        [DataRow("0.0.0.0", 0)]
        [DataRow("255.255.255.255", 32)]
        public void GetPrefixLength_ConvertsInterfaceNetmask(string netmask, int expected)
        {
            var method = typeof(NetworkManager).GetMethod("GetPrefixLength", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.AreEqual(expected, method.Invoke(null, new object[] { IPAddress.Parse(netmask) }));
        }

        [DataTestMethod]
        [DataRow("10.1.2.3", "10.0.0.0/8", true)]
        [DataRow("11.1.2.3", "10.0.0.0/8", false)]
        [DataRow("10.0.0.0", "10.0.0.0/8", true)]
        [DataRow("10.255.255.255", "10.0.0.0/8", true)]
        [DataRow("10.0.0.1", "10.1.2.3/8", true)]
        [DataRow("192.168.1.255", "192.168.1.0/24", true)]
        [DataRow("192.168.2.0", "192.168.1.0/24", false)]
        [DataRow("192.168.1.0", "192.168.1.0/31", true)]
        [DataRow("192.168.1.1", "192.168.1.0/31", true)]
        [DataRow("192.168.1.2", "192.168.1.0/31", false)]
        [DataRow("192.168.1.1", "192.168.1.1/32", true)]
        [DataRow("192.168.1.2", "192.168.1.1/32", false)]
        [DataRow("fe80::1234", "fe80::/10", true)]
        [DataRow("febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff", "fe80::/10", true)]
        [DataRow("fec0::1234", "fe80::/10", false)]
        [DataRow("::1", "::1/128", true)]
        [DataRow("::2", "::1/128", false)]
        [DataRow("127.0.0.1", "::1/128", false)]
        [DataRow("::ffff:192.168.1.5", "192.168.1.0/24", true)]
        [DataRow("8.8.8.8", "8.8.8.8", true)]
        [DataRow("8.8.8.8", " 8.8.8.8 ", true)]
        [DataRow("8.8.4.4", "8.8.8.8", false)]
        public void IsAddressInSubnets_UsesBclNetworkContainment(string address, string subnet, bool expected)
        {
            Assert.AreEqual(expected, InvokeSubnets(CreateManager(), address, subnet));
        }

        [TestMethod]
        public void IsAddressInSubnets_MatchesAnyOfSeveralSubnets()
        {
            var manager = CreateManager();

            Assert.IsTrue(InvokeSubnets(manager, "10.1.2.3", "8.8.8.8/8", "10.1.2.3", "10.0.0.0/8"));
            Assert.IsTrue(InvokeSubnets(manager, "10.1.2.3", "10.0.0.0/8", "8.8.8.8/8"));
            Assert.IsTrue(InvokeSubnets(manager, "8.8.8.8", "10.0.0.0/8", "8.8.8.8/8"));
            Assert.IsFalse(InvokeSubnets(manager, "9.8.4.4", "10.0.0.0/8", "8.8.8.8/8"));
            Assert.IsFalse(InvokeSubnets(manager, "8.8.4.4"));
        }

        [DataTestMethod]
        [DataRow("not-a-network", false)]
        [DataRow("10.0.0.0/33", true)]
        [DataRow("10.0.0.0/999", true)]
        [DataRow("::1/129", true)]
        [DataRow("192.168.1.0/255.255.255.0", true)]
        [DataRow("10.0.0.0/abc", true)]
        [DataRow("10.0.0.0/", true)]
        [DataRow("", false)]
        [DataRow("   ", false)]
        public void IsAddressInSubnets_MalformedSubnetIsSkippedAndLogged(string subnet, bool expectWarning)
        {
            var logger = new RecordingLogger();
            var manager = CreateManager(logger);

            Assert.IsFalse(InvokeSubnets(manager, "10.1.2.3", subnet));
            Assert.AreEqual(expectWarning ? 1 : 0, logger.Warnings.Count);
        }

        [TestMethod]
        public void IsAddressInSubnets_ContinuesAfterMalformedSubnet()
        {
            var logger = new RecordingLogger();
            var manager = CreateManager(logger);

            Assert.IsTrue(InvokeSubnets(manager, "10.1.2.3", "10.0.0.0/33", "10.0.0.0/8"));
            Assert.AreEqual(1, logger.Warnings.Count);
        }

        [TestMethod]
        public void IsAddressInSubnets_PublicOverload_UsesSameLogic()
        {
            var manager = CreateManager();

            Assert.IsTrue(manager.IsAddressInSubnets("10.1.2.3", new[] { "10.0.0.0/8" }));
            Assert.IsFalse(manager.IsAddressInSubnets("11.1.2.3", new[] { "10.0.0.0/8" }));
        }

        [TestMethod]
        public void IsAddressInSubnets_PublicOverload_DoesNotThrowOnMalformedSubnet()
        {
            var manager = CreateManager();

            Assert.IsFalse(manager.IsAddressInSubnets("10.1.2.3", new[] { "10.0.0.0/33" }));
        }
    }
}
