using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Emby.Server.Implementations.Networking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.Networking
{
    /// <summary>
    /// Characterization of the vendored IPNetwork type. The type is resolved through
    /// reflection because the pre-migration name (System.Net.IPNetwork) collides with
    /// the BCL type; these tests must keep passing after the type is renamed.
    /// </summary>
    [TestClass]
    public class IpNetworkEquivalenceTests
    {
        private static readonly Assembly Implementation = typeof(NetworkManager).Assembly;

        private static Type NetworkType => Resolve("System.Net.IPNetwork", "Emby.Server.Implementations.Networking.IPNetwork");

        private static Type NetworkCollectionType => Resolve("System.Net.IPNetworkCollection", "Emby.Server.Implementations.Networking.IPNetworkCollection");

        private static Type AddressCollectionType => Resolve("System.Net.IPAddressCollection", "Emby.Server.Implementations.Networking.IPAddressCollection");

        private static Type Resolve(string legacyName, string migratedName)
        {
            return Implementation.GetType(legacyName) ?? Implementation.GetType(migratedName, true);
        }

        private static object InvokeStatic(string name, Type[] signature, params object[] args)
        {
            var method = NetworkType.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, signature, null);
            Assert.IsNotNull(method, "missing public static " + name);
            return method.Invoke(null, args);
        }

        private static object Call(object target, string name, Type[] signature, params object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, signature, null);
            Assert.IsNotNull(method, "missing public instance " + name);
            return method.Invoke(target, args);
        }

        private static object Get(object target, string name)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(property, "missing public property " + name);
            return property.GetValue(target);
        }

        private static object Parse(string text)
        {
            return InvokeStatic("Parse", new[] { typeof(string) }, text);
        }

        private static object Parse(string address, string netmask)
        {
            return InvokeStatic("Parse", new[] { typeof(string), typeof(string) }, address, netmask);
        }

        private static object Parse(string address, byte cidr)
        {
            return InvokeStatic("Parse", new[] { typeof(string), typeof(byte) }, address, cidr);
        }

        private static object Parse(IPAddress address, IPAddress netmask)
        {
            return InvokeStatic("Parse", new[] { typeof(IPAddress), typeof(IPAddress) }, address, netmask);
        }

        private static object[] TryParse(string text)
        {
            var args = new object[] { text, null };
            var result = InvokeStatic("TryParse", new[] { typeof(string), NetworkType.MakeByRefType() }, args);
            return new[] { result, args[1] };
        }

        private static object[] TryParse(string address, byte cidr)
        {
            var args = new object[] { address, cidr, null };
            var result = InvokeStatic("TryParse", new[] { typeof(string), typeof(byte), NetworkType.MakeByRefType() }, args);
            return new[] { result, args[2] };
        }

        private static object[] TryParse(string address, string netmask)
        {
            var args = new object[] { address, netmask, null };
            var result = InvokeStatic("TryParse", new[] { typeof(string), typeof(string), NetworkType.MakeByRefType() }, args);
            return new[] { result, args[2] };
        }

        private static object[] TryParse(IPAddress address, IPAddress netmask)
        {
            var args = new object[] { address, netmask, null };
            var result = InvokeStatic("TryParse", new[] { typeof(IPAddress), typeof(IPAddress), NetworkType.MakeByRefType() }, args);
            return new[] { result, args[2] };
        }

        private static Array TypedNetworks(params object[] networks)
        {
            var array = Array.CreateInstance(NetworkType, networks.Length);
            for (var i = 0; i < networks.Length; i++)
            {
                array.SetValue(networks[i], i);
            }

            return array;
        }

        private static string Describe(object network)
        {
            if (network == null)
            {
                return "<null>";
            }

            string Value(string name)
            {
                var value = Get(network, name);
                return value == null ? "<null>" : Convert.ToString(value);
            }

            return network + " | Network=" + Value("Network") + " | AddressFamily=" + Value("AddressFamily")
                + " | Netmask=" + Value("Netmask") + " | Broadcast=" + Value("Broadcast")
                + " | FirstUsable=" + Value("FirstUsable") + " | LastUsable=" + Value("LastUsable")
                + " | Usable=" + Value("Usable") + " | Total=" + Value("Total") + " | Cidr=" + Value("Cidr");
        }

        private static int ExpectedHash(string originalAddress, object network)
        {
            var original = InvokeStatic("ToBigInteger", new[] { typeof(IPAddress) }, IPAddress.Parse(originalAddress));
            var normalized = InvokeStatic("ToBigInteger", new[] { typeof(IPAddress) }, Get(network, "Network"));
            return string.Format("{0}|{1}|{2}", original, normalized, Get(network, "Cidr")).GetHashCode();
        }

        [TestMethod]
        public void PublicSurface_IsFullyPinned()
        {
            var actual = NetworkType.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(member => member.Name)
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var expected = new[]
            {
                "AddressFamily", "BitsSet", "Broadcast", "Cidr", "Compare", "CompareTo", "Contains",
                "Equals", "FirstUsable", "GetHashCode", "IANA_ABLK_RESERVED1", "IANA_BBLK_RESERVED1",
                "IANA_CBLK_RESERVED1", "IsIANAReserved", "LastUsable", "ListIPAddress", "Netmask",
                "Network", "op_Equality", "op_GreaterThan", "op_Inequality", "op_LessThan", "Overlap",
                "Parse", "Print", "Subnet", "Supernet", "ToBigInteger", "ToCidr", "ToIPAddress",
                "ToNetmask", "ToString", "ToUint", "Total", "TryGuessCidr", "TryParse", "TryParseCidr",
                "TrySubnet", "TrySupernet", "TryToBigInteger", "TryToCidr", "TryToNetmask", "TryToUint",
                "TryWideSubnet", "Usable", "ValidNetmask", "WideSubnet",
                "get_AddressFamily", "get_Broadcast", "get_Cidr", "get_FirstUsable",
                "get_IANA_ABLK_RESERVED1", "get_IANA_BBLK_RESERVED1", "get_IANA_CBLK_RESERVED1",
                "get_LastUsable", "get_Netmask", "get_Network", "get_Total", "get_Usable"
            }.OrderBy(name => name, StringComparer.Ordinal).ToArray();

            Assert.AreEqual(string.Join(",", expected), string.Join(",", actual));
        }

        [DataTestMethod]
        [DataRow("10.0.0.0/8", "10.0.0.0/8 | Network=10.0.0.0 | AddressFamily=InterNetwork | Netmask=255.0.0.0 | Broadcast=10.255.255.255 | FirstUsable=10.0.0.1 | LastUsable=10.255.255.254 | Usable=16777214 | Total=16777216 | Cidr=8")]
        [DataRow("10.1.2.3/8", "10.0.0.0/8 | Network=10.0.0.0 | AddressFamily=InterNetwork | Netmask=255.0.0.0 | Broadcast=10.255.255.255 | FirstUsable=10.0.0.1 | LastUsable=10.255.255.254 | Usable=16777214 | Total=16777216 | Cidr=8")]
        [DataRow("192.168.1.0/24", "192.168.1.0/24 | Network=192.168.1.0 | AddressFamily=InterNetwork | Netmask=255.255.255.0 | Broadcast=192.168.1.255 | FirstUsable=192.168.1.1 | LastUsable=192.168.1.254 | Usable=254 | Total=256 | Cidr=24")]
        [DataRow("192.168.1.42/24", "192.168.1.0/24 | Network=192.168.1.0 | AddressFamily=InterNetwork | Netmask=255.255.255.0 | Broadcast=192.168.1.255 | FirstUsable=192.168.1.1 | LastUsable=192.168.1.254 | Usable=254 | Total=256 | Cidr=24")]
        [DataRow("::1/128", "::1/128 | Network=::1 | AddressFamily=InterNetworkV6 | Netmask=ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff | Broadcast=<null> | FirstUsable=::1 | LastUsable=::1 | Usable=1 | Total=1 | Cidr=128")]
        [DataRow("fe80::/10", "fe80::/10 | Network=fe80:: | AddressFamily=InterNetworkV6 | Netmask=ffc0:: | Broadcast=<null> | FirstUsable=fe80:: | LastUsable=febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff | Usable=332306998946228968225951765070086144 | Total=332306998946228968225951765070086144 | Cidr=10")]
        [DataRow("fe80::1234/10", "fe80::/10 | Network=fe80:: | AddressFamily=InterNetworkV6 | Netmask=ffc0:: | Broadcast=<null> | FirstUsable=fe80:: | LastUsable=febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff | Usable=332306998946228968225951765070086144 | Total=332306998946228968225951765070086144 | Cidr=10")]
        [DataRow("2001:db8::1/32", "2001:db8::/32 | Network=2001:db8:: | AddressFamily=InterNetworkV6 | Netmask=ffff:ffff:: | Broadcast=<null> | FirstUsable=2001:db8:: | LastUsable=2001:db8:ffff:ffff:ffff:ffff:ffff:ffff | Usable=79228162514264337593543950336 | Total=79228162514264337593543950336 | Cidr=32")]
        [DataRow("10.0.0.0 255.0.0.0", "10.0.0.0/8 | Network=10.0.0.0 | AddressFamily=InterNetwork | Netmask=255.0.0.0 | Broadcast=10.255.255.255 | FirstUsable=10.0.0.1 | LastUsable=10.255.255.254 | Usable=16777214 | Total=16777216 | Cidr=8")]
        [DataRow("10.0.0.0/255.0.0.0", "10.0.0.0/8 | Network=10.0.0.0 | AddressFamily=InterNetwork | Netmask=255.0.0.0 | Broadcast=10.255.255.255 | FirstUsable=10.0.0.1 | LastUsable=10.255.255.254 | Usable=16777214 | Total=16777216 | Cidr=8")]
        [DataRow("192.168.1.1", "192.168.1.0/24 | Network=192.168.1.0 | AddressFamily=InterNetwork | Netmask=255.255.255.0 | Broadcast=192.168.1.255 | FirstUsable=192.168.1.1 | LastUsable=192.168.1.254 | Usable=254 | Total=256 | Cidr=24")]
        [DataRow("127.0.0.1", "127.0.0.0/8 | Network=127.0.0.0 | AddressFamily=InterNetwork | Netmask=255.0.0.0 | Broadcast=127.255.255.255 | FirstUsable=127.0.0.1 | LastUsable=127.255.255.254 | Usable=16777214 | Total=16777216 | Cidr=8")]
        [DataRow("8.8.8.8", "8.0.0.0/8 | Network=8.0.0.0 | AddressFamily=InterNetwork | Netmask=255.0.0.0 | Broadcast=8.255.255.255 | FirstUsable=8.0.0.1 | LastUsable=8.255.255.254 | Usable=16777214 | Total=16777216 | Cidr=8")]
        [DataRow("0.0.0.0", "0.0.0.0/8 | Network=0.0.0.0 | AddressFamily=InterNetwork | Netmask=255.0.0.0 | Broadcast=0.255.255.255 | FirstUsable=0.0.0.1 | LastUsable=0.255.255.254 | Usable=16777214 | Total=16777216 | Cidr=8")]
        [DataRow("192.168.1.0/31", "192.168.1.0/31 | Network=192.168.1.0 | AddressFamily=InterNetwork | Netmask=255.255.255.254 | Broadcast=192.168.1.1 | FirstUsable=192.168.1.0 | LastUsable=192.168.1.0 | Usable=0 | Total=2 | Cidr=31")]
        [DataRow("192.168.1.0/32", "192.168.1.0/32 | Network=192.168.1.0 | AddressFamily=InterNetwork | Netmask=255.255.255.255 | Broadcast=192.168.1.0 | FirstUsable=192.168.1.0 | LastUsable=192.168.1.0 | Usable=0 | Total=1 | Cidr=32")]
        [DataRow("0.0.0.0/0", "0.0.0.0/0 | Network=0.0.0.0 | AddressFamily=InterNetwork | Netmask=0.0.0.0 | Broadcast=255.255.255.255 | FirstUsable=0.0.0.1 | LastUsable=255.255.255.254 | Usable=4294967294 | Total=4294967296 | Cidr=0")]
        [DataRow("::/0", "::/0 | Network=:: | AddressFamily=InterNetworkV6 | Netmask=:: | Broadcast=<null> | FirstUsable=:: | LastUsable=ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff | Usable=340282366920938463463374607431768211456 | Total=340282366920938463463374607431768211456 | Cidr=0")]
        [DataRow("10.0.0.0/8/9", "10.0.0.0/8 | Network=10.0.0.0 | AddressFamily=InterNetwork | Netmask=255.0.0.0 | Broadcast=10.255.255.255 | FirstUsable=10.0.0.1 | LastUsable=10.255.255.254 | Usable=16777214 | Total=16777216 | Cidr=8")]
        [DataRow("10.0.0.0/-1", "0.0.0.0/1 | Network=0.0.0.0 | AddressFamily=InterNetwork | Netmask=128.0.0.0 | Broadcast=127.255.255.255 | FirstUsable=0.0.0.1 | LastUsable=127.255.255.254 | Usable=2147483646 | Total=2147483648 | Cidr=1")]
        [DataRow("010.0.0.0/8", "8.0.0.0/8 | Network=8.0.0.0 | AddressFamily=InterNetwork | Netmask=255.0.0.0 | Broadcast=8.255.255.255 | FirstUsable=8.0.0.1 | LastUsable=8.255.255.254 | Usable=16777214 | Total=16777216 | Cidr=8")]
        [DataRow("10.0.0.0/08", "10.0.0.0/8 | Network=10.0.0.0 | AddressFamily=InterNetwork | Netmask=255.0.0.0 | Broadcast=10.255.255.255 | FirstUsable=10.0.0.1 | LastUsable=10.255.255.254 | Usable=16777214 | Total=16777216 | Cidr=8")]
        public void Parse_SingleArgument_PinsEveryProperty(string input, string expected)
        {
            Assert.AreEqual(expected, Describe(Parse(input)));
        }

        [TestMethod]
        public void Parse_Overloads_AreEquivalentForCidrAndNetmaskForms()
        {
            Assert.AreEqual(Describe(Parse("10.1.2.3/8")), Describe(Parse("10.1.2.3", (byte)8)));
            Assert.AreEqual(Describe(Parse("10.1.2.3/8")), Describe(Parse("10.1.2.3", "255.0.0.0")));
            Assert.AreEqual(Describe(Parse("10.1.2.3/8")), Describe(Parse(IPAddress.Parse("10.1.2.3"), IPAddress.Parse("255.0.0.0"))));
            Assert.AreEqual("::1/128", Parse(IPAddress.Parse("::1"), IPAddress.Parse("ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")).ToString());
        }

        [DataTestMethod]
        [DataRow(null, false, "ArgumentNullException", "network")]
        [DataRow("", false, "ArgumentNullException", "network")]
        [DataRow("not-an-ip", false, "ArgumentException", null)]
        [DataRow("10.0.0.0/33", false, "ArgumentException", null)]
        [DataRow("10.0.0.0/999", false, "ArgumentException", null)]
        [DataRow("::1/129", false, "ArgumentException", null)]
        [DataRow(" fe80::1 / 10 ", false, "ArgumentNullException", "netmask")]
        public void MalformedInput_TryParseReturnsFalseAndParseThrows(string input, bool expectedTryParse, string expectedException, string expectedParamName)
        {
            var result = TryParse(input);
            Assert.AreEqual(expectedTryParse, result[0]);
            Assert.IsNull(result[1]);

            var error = Assert.ThrowsException<TargetInvocationException>(() => Parse(input));
            Assert.AreEqual(expectedException, error.InnerException.GetType().Name);
            Assert.AreEqual(expectedParamName, ((ArgumentException)error.InnerException).ParamName);
        }

        [TestMethod]
        public void TryParse_Overloads_MatchParseOnSuccess()
        {
            Assert.AreEqual(Describe(Parse("192.168.1.0/24")), Describe(TryParse("192.168.1.0/24")[1]));
            Assert.AreEqual(Describe(Parse("192.168.1.0/24")), Describe(TryParse("192.168.1.42", (byte)24)[1]));
            Assert.AreEqual(Describe(Parse("192.168.1.0/24")), Describe(TryParse("192.168.1.42", "255.255.255.0")[1]));
            Assert.AreEqual(Describe(Parse("192.168.1.0/24")), Describe(TryParse(IPAddress.Parse("192.168.1.42"), IPAddress.Parse("255.255.255.0"))[1]));

            Assert.IsFalse((bool)TryParse("192.168.1.0", (byte)33)[0]);
            Assert.IsNull(TryParse("192.168.1.0", (byte)33)[1]);
            Assert.IsFalse((bool)TryParse("192.168.1.0", "bad-mask")[0]);
            Assert.IsNull(TryParse("192.168.1.0", "bad-mask")[1]);
            Assert.IsFalse((bool)TryParse(IPAddress.Parse("192.168.1.0"), IPAddress.Parse("255.0.255.0"))[0]);
            Assert.IsNull(TryParse(IPAddress.Parse("192.168.1.0"), IPAddress.Parse("255.0.255.0"))[1]);
        }

        [DataTestMethod]
        [DataRow("192.168.1.0/24", "192.168.1.0", true)]
        [DataRow("192.168.1.0/24", "192.168.1.1", true)]
        [DataRow("192.168.1.0/24", "192.168.1.254", true)]
        [DataRow("192.168.1.0/24", "192.168.1.255", true)]
        [DataRow("192.168.1.0/24", "192.168.2.0", false)]
        [DataRow("192.168.1.0/24", "10.0.0.1", false)]
        [DataRow("10.0.0.0/8", "10.0.0.0", true)]
        [DataRow("10.0.0.0/8", "10.255.255.255", true)]
        [DataRow("10.0.0.0/8", "11.0.0.0", false)]
        [DataRow("10.0.0.0/8", "10.0.0.0", true)]
        [DataRow("fe80::/10", "fe80::", true)]
        [DataRow("fe80::/10", "febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff", true)]
        [DataRow("fe80::/10", "fec0::", false)]
        [DataRow("::1/128", "::1", true)]
        [DataRow("::1/128", "::2", false)]
        [DataRow("::1/128", "127.0.0.1", false)]
        [DataRow("192.168.1.0/31", "192.168.1.1", true)]
        [DataRow("192.168.1.0/31", "192.168.1.2", false)]
        public void Contains_Address_PinsFirstLastAndBroadcastBoundaries(string network, string address, bool expected)
        {
            var parsed = Parse(network);
            Assert.AreEqual(expected, Call(parsed, "Contains", new[] { typeof(IPAddress) }, IPAddress.Parse(address)));
            Assert.AreEqual(expected, InvokeStatic("Contains", new[] { NetworkType, typeof(IPAddress) }, parsed, IPAddress.Parse(address)));
        }

        [TestMethod]
        public void Contains_Null_ThrowsWithLegacyParameterName()
        {
            var parsed = Parse("10.0.0.0/8");
            var error = Assert.ThrowsException<TargetInvocationException>(() => Call(parsed, "Contains", new[] { typeof(IPAddress) }, new object[] { null }));
            Assert.AreEqual("ipaddress", ((ArgumentNullException)error.InnerException).ParamName);
        }

        [TestMethod]
        public void ContainsAndOverlap_Networks_PinsContainmentAndAdjacency()
        {
            var whole = Parse("10.0.0.0/8");
            var part = Parse("10.1.0.0/16");
            var adjacent = Parse("11.0.0.0/8");
            var firstHalf = Parse("10.0.0.0/9");
            var secondHalf = Parse("10.128.0.0/9");

            Assert.IsTrue((bool)Call(whole, "Contains", new[] { NetworkType }, part));
            Assert.IsFalse((bool)Call(part, "Contains", new[] { NetworkType }, whole));
            Assert.IsTrue((bool)InvokeStatic("Contains", new[] { NetworkType, NetworkType }, whole, part));
            Assert.IsFalse((bool)Call(firstHalf, "Overlap", new[] { NetworkType }, secondHalf));
            Assert.IsFalse((bool)Call(firstHalf, "Overlap", new[] { NetworkType }, adjacent));
            Assert.IsTrue((bool)Call(firstHalf, "Overlap", new[] { NetworkType }, whole));
            Assert.IsTrue((bool)Call(whole, "Overlap", new[] { NetworkType }, part));
            Assert.IsTrue((bool)InvokeStatic("Overlap", new[] { NetworkType, NetworkType }, whole, part));

            var nullError = Assert.ThrowsException<TargetInvocationException>(() => Call(whole, "Contains", new[] { NetworkType }, new object[] { null }));
            Assert.AreEqual("network2", ((ArgumentNullException)nullError.InnerException).ParamName);
        }

        [DataTestMethod]
        [DataRow("10.1.2.3/8")]
        [DataRow("192.168.1.42/24")]
        [DataRow("::1/128")]
        [DataRow("fe80::1234/10")]
        [DataRow("2001:db8::1/32")]
        [DataRow("10.0.0.0 255.0.0.0")]
        public void ToString_RoundTripsThroughParse(string input)
        {
            var parsed = Parse(input);
            var text = (string)Call(parsed, "ToString", Type.EmptyTypes);
            var reparsed = Parse(text);

            Assert.AreEqual(text, reparsed.ToString());
            Assert.AreEqual(true, Call(parsed, "Equals", new[] { NetworkType }, reparsed));
            Assert.AreEqual(true, InvokeStatic("Equals", new[] { NetworkType, NetworkType }, parsed, reparsed));
        }

        [TestMethod]
        public void EqualityComparisonAndHashCode_PinVendoredQuirks()
        {
            var withHostBits = Parse("10.1.2.3/8");
            var normalized = Parse("10.0.0.0/8");
            var narrower = Parse("10.0.0.0/9");

            Assert.AreEqual(true, Call(withHostBits, "Equals", new[] { NetworkType }, normalized));
            Assert.AreEqual(true, withHostBits.Equals((object)normalized));
            Assert.AreEqual(true, InvokeStatic("Equals", new[] { NetworkType, NetworkType }, withHostBits, normalized));
            Assert.AreEqual(false, withHostBits.Equals((object)narrower));
            Assert.AreEqual(0, Call(withHostBits, "CompareTo", new[] { NetworkType }, normalized));
            Assert.AreEqual(0, Call(withHostBits, "CompareTo", new[] { typeof(object) }, normalized));
            Assert.AreEqual(0, InvokeStatic("Compare", new[] { NetworkType, NetworkType }, withHostBits, normalized));
            Assert.AreEqual(-1, InvokeStatic("Compare", new[] { NetworkType, NetworkType }, null, normalized));
            Assert.AreEqual(1, InvokeStatic("Compare", new[] { NetworkType, NetworkType }, normalized, null));
            Assert.AreEqual(0, InvokeStatic("Compare", new[] { NetworkType, NetworkType }, null, null));
            Assert.AreEqual(1, Call(withHostBits, "CompareTo", new[] { typeof(object) }, new object[] { null }));
            Assert.IsTrue((bool)InvokeStatic("op_Equality", new[] { NetworkType, NetworkType }, withHostBits, normalized));
            Assert.IsFalse((bool)InvokeStatic("op_Inequality", new[] { NetworkType, NetworkType }, withHostBits, normalized));
            Assert.IsTrue((bool)InvokeStatic("op_GreaterThan", new[] { NetworkType, NetworkType }, narrower, withHostBits));
            Assert.IsTrue((bool)InvokeStatic("op_LessThan", new[] { NetworkType, NetworkType }, withHostBits, narrower));

            Assert.AreEqual(ExpectedHash("10.1.2.3", withHostBits), withHostBits.GetHashCode());
            Assert.AreEqual(ExpectedHash("10.0.0.0", normalized), normalized.GetHashCode());
            Assert.AreNotEqual(normalized.GetHashCode(), withHostBits.GetHashCode());

            var wrongType = Assert.ThrowsException<TargetInvocationException>(() => Call(withHostBits, "CompareTo", new[] { typeof(object) }, "not a network"));
            Assert.AreEqual("ArgumentException", wrongType.InnerException.GetType().Name);
        }

        [TestMethod]
        public void Converters_PinLegacyNumericAndMaskSemantics()
        {
            Assert.AreEqual(new BigInteger(0), InvokeStatic("ToBigInteger", new[] { typeof(IPAddress) }, IPAddress.Parse("0.0.0.0")));
            Assert.AreEqual(new BigInteger(256), InvokeStatic("ToBigInteger", new[] { typeof(IPAddress) }, IPAddress.Parse("0.0.1.0")));
            Assert.AreEqual(new BigInteger(4294967295L), InvokeStatic("ToBigInteger", new[] { typeof(IPAddress) }, IPAddress.Parse("255.255.255.255")));
            Assert.AreEqual(BigInteger.One, InvokeStatic("ToBigInteger", new[] { typeof(IPAddress) }, IPAddress.Parse("::1")));

            var bigArgs = new object[] { null, null };
            Assert.IsFalse((bool)InvokeStatic("TryToBigInteger", new[] { typeof(IPAddress), typeof(BigInteger?).MakeByRefType() }, bigArgs));
            Assert.IsNull(bigArgs[1]);
            var nullAddress = Assert.ThrowsException<TargetInvocationException>(() => InvokeStatic("ToBigInteger", new[] { typeof(IPAddress) }, new object[] { null }));
            Assert.AreEqual("ArgumentNullException", nullAddress.InnerException.GetType().Name);

            Assert.AreEqual(new BigInteger(4294967040L), InvokeStatic("ToUint", new[] { typeof(byte), typeof(AddressFamily) }, (byte)24, AddressFamily.InterNetwork));
            Assert.AreEqual(BigInteger.Zero, InvokeStatic("ToUint", new[] { typeof(byte), typeof(AddressFamily) }, (byte)0, AddressFamily.InterNetwork));
            Assert.AreEqual(BigInteger.Pow(2, 128) - 1, InvokeStatic("ToUint", new[] { typeof(byte), typeof(AddressFamily) }, (byte)128, AddressFamily.InterNetworkV6));
            var badUint = Assert.ThrowsException<TargetInvocationException>(() => InvokeStatic("ToUint", new[] { typeof(byte), typeof(AddressFamily) }, (byte)33, AddressFamily.InterNetwork));
            Assert.AreEqual("ArgumentOutOfRangeException", badUint.InnerException.GetType().Name);
            var badFamily = Assert.ThrowsException<TargetInvocationException>(() => InvokeStatic("ToUint", new[] { typeof(byte), typeof(AddressFamily) }, (byte)8, AddressFamily.AppleTalk));
            Assert.AreEqual("NotSupportedException", badFamily.InnerException.GetType().Name);

            Assert.AreEqual((byte)24, InvokeStatic("ToCidr", new[] { typeof(IPAddress) }, IPAddress.Parse("255.255.255.0")));
            Assert.AreEqual((byte)64, InvokeStatic("ToCidr", new[] { typeof(IPAddress) }, IPAddress.Parse("ffff:ffff:ffff:ffff::")));
            var badCidr = Assert.ThrowsException<TargetInvocationException>(() => InvokeStatic("ToCidr", new[] { typeof(IPAddress) }, IPAddress.Parse("255.0.255.0")));
            Assert.AreEqual("ArgumentException", badCidr.InnerException.GetType().Name);
            var cidrArgs = new object[] { IPAddress.Parse("255.0.255.0"), null };
            Assert.IsFalse((bool)InvokeStatic("TryToCidr", new[] { typeof(IPAddress), typeof(byte?).MakeByRefType() }, cidrArgs));
            Assert.IsNull(cidrArgs[1]);

            Assert.AreEqual("255.255.255.0", InvokeStatic("ToNetmask", new[] { typeof(byte), typeof(AddressFamily) }, (byte)24, AddressFamily.InterNetwork).ToString());
            Assert.AreEqual("ffc0::", InvokeStatic("ToNetmask", new[] { typeof(byte), typeof(AddressFamily) }, (byte)10, AddressFamily.InterNetworkV6).ToString());
            var maskArgs = new object[] { (byte)33, AddressFamily.InterNetwork, null };
            Assert.IsFalse((bool)InvokeStatic("TryToNetmask", new[] { typeof(byte), typeof(AddressFamily), typeof(IPAddress).MakeByRefType() }, maskArgs));
            Assert.IsNull(maskArgs[2]);

            Assert.AreEqual(20u, InvokeStatic("BitsSet", new[] { typeof(IPAddress) }, IPAddress.Parse("255.255.240.0")));
            Assert.IsTrue((bool)InvokeStatic("ValidNetmask", new[] { typeof(IPAddress) }, IPAddress.Parse("255.255.240.0")));
            Assert.IsFalse((bool)InvokeStatic("ValidNetmask", new[] { typeof(IPAddress) }, IPAddress.Parse("255.0.255.0")));
            Assert.AreEqual("192.168.0.0", InvokeStatic("ToIPAddress", new[] { typeof(BigInteger), typeof(AddressFamily) }, new BigInteger(3232235520L), AddressFamily.InterNetwork).ToString());
        }

        [TestMethod]
        public void IanaBlocks_AreUnchanged()
        {
            Assert.AreEqual("10.0.0.0/8", InvokeStatic("get_IANA_ABLK_RESERVED1", Type.EmptyTypes).ToString());
            Assert.AreEqual("172.16.0.0/12", InvokeStatic("get_IANA_BBLK_RESERVED1", Type.EmptyTypes).ToString());
            Assert.AreEqual("192.168.0.0/16", InvokeStatic("get_IANA_CBLK_RESERVED1", Type.EmptyTypes).ToString());

            Assert.IsTrue((bool)InvokeStatic("IsIANAReserved", new[] { typeof(IPAddress) }, IPAddress.Parse("10.1.2.3")));
            Assert.IsTrue((bool)InvokeStatic("IsIANAReserved", new[] { typeof(IPAddress) }, IPAddress.Parse("172.16.0.1")));
            Assert.IsTrue((bool)InvokeStatic("IsIANAReserved", new[] { typeof(IPAddress) }, IPAddress.Parse("172.31.255.255")));
            Assert.IsFalse((bool)InvokeStatic("IsIANAReserved", new[] { typeof(IPAddress) }, IPAddress.Parse("172.32.0.1")));
            Assert.IsTrue((bool)InvokeStatic("IsIANAReserved", new[] { typeof(IPAddress) }, IPAddress.Parse("192.168.1.1")));
            Assert.IsFalse((bool)InvokeStatic("IsIANAReserved", new[] { typeof(IPAddress) }, IPAddress.Parse("8.8.8.8")));
            Assert.IsFalse((bool)InvokeStatic("IsIANAReserved", new[] { typeof(IPAddress) }, IPAddress.Parse("fe80::1")));

            Assert.IsTrue((bool)Call(Parse("10.0.0.0/9"), "IsIANAReserved", Type.EmptyTypes));
            Assert.IsFalse((bool)Call(Parse("10.0.0.0/7"), "IsIANAReserved", Type.EmptyTypes));
            Assert.IsTrue((bool)InvokeStatic("IsIANAReserved", new[] { NetworkType }, Parse("10.0.0.0/9")));
        }

        [TestMethod]
        public void Subnet_AndAddressCollections_AreUnchanged()
        {
            var parent = Parse("192.168.0.0/24");
            var subnets = (IEnumerable)Call(parent, "Subnet", new[] { typeof(byte) }, (byte)25);
            CollectionAssert.AreEqual(
                new[] { "192.168.0.0/25", "192.168.0.128/25" },
                subnets.Cast<object>().Select(item => item.ToString()).ToArray());
            Assert.AreEqual(NetworkCollectionType, subnets.GetType());
            Assert.AreEqual(new BigInteger(2), Get(subnets, "Count"));
            Assert.AreEqual("192.168.0.0/25", subnets.GetType().GetProperty("Item").GetValue(subnets, new object[] { BigInteger.Zero }).ToString());

            var tryArgs = new object[] { (byte)23, null };
            Assert.IsFalse((bool)Call(parent, "TrySubnet", new[] { typeof(byte), NetworkCollectionType.MakeByRefType() }, tryArgs));
            Assert.IsNull(tryArgs[1]);
            var invalid = Assert.ThrowsException<TargetInvocationException>(() => Call(parent, "Subnet", new[] { typeof(byte) }, (byte)23));
            Assert.AreEqual("ArgumentException", invalid.InnerException.GetType().Name);

            var ipv6Subnets = (IEnumerable)Call(Parse("fe80::/10"), "Subnet", new[] { typeof(byte) }, (byte)11);
            CollectionAssert.AreEqual(new[] { "fe80::/11", "fea0::/11" }, ipv6Subnets.Cast<object>().Select(item => item.ToString()).ToArray());

            var addresses = (IEnumerable)InvokeStatic("ListIPAddress", new[] { NetworkType }, Parse("192.168.1.0/30"));
            CollectionAssert.AreEqual(
                new[] { "192.168.1.0", "192.168.1.1", "192.168.1.2", "192.168.1.3" },
                addresses.Cast<object>().Select(item => item.ToString()).ToArray());
            Assert.AreEqual(AddressCollectionType, addresses.GetType());
            Assert.AreEqual(new BigInteger(4), Get(addresses, "Count"));
        }

        [TestMethod]
        public void Supernet_PinsMergingRules()
        {
            var a = Parse("192.168.0.0/24");
            var b = Parse("192.168.1.0/24");
            Assert.AreEqual("192.168.0.0/23", Call(a, "Supernet", new[] { NetworkType }, b).ToString());
            Assert.AreEqual("192.168.0.0/24", Call(a, "Supernet", new[] { NetworkType }, Parse("192.168.0.128/25")).ToString());
            Assert.AreEqual("192.168.1.0/24", Call(b, "Supernet", new[] { NetworkType }, Parse("192.168.1.128/25")).ToString());

            var nonAdjacent = Assert.ThrowsException<TargetInvocationException>(() => Call(a, "Supernet", new[] { NetworkType }, Parse("192.168.2.0/24")));
            Assert.AreEqual("ArgumentOutOfRangeException", nonAdjacent.InnerException.GetType().Name);
            var mismatched = Assert.ThrowsException<TargetInvocationException>(() => Call(a, "Supernet", new[] { NetworkType }, Parse("192.168.1.0/25")));
            Assert.AreEqual("ArgumentException", mismatched.InnerException.GetType().Name);

            var tryArgs = new object[] { b, null };
            Assert.IsTrue((bool)Call(a, "TrySupernet", new[] { NetworkType, NetworkType.MakeByRefType() }, tryArgs));
            Assert.AreEqual("192.168.0.0/23", tryArgs[1].ToString());

            var list = TypedNetworks(a, b, Parse("192.168.2.0/24"), Parse("192.168.3.0/24"));
            var merged = (Array)InvokeStatic("Supernet", new[] { NetworkType.MakeArrayType() }, new object[] { list });
            CollectionAssert.AreEqual(new[] { "192.168.0.0/22" }, merged.Cast<object>().Select(item => item.ToString()).ToArray());
            var tryListArgs = new object[] { list, null };
            Assert.IsTrue((bool)InvokeStatic("TrySupernet", new[] { NetworkType.MakeArrayType(), NetworkType.MakeArrayType().MakeByRefType() }, tryListArgs));
            CollectionAssert.AreEqual(new[] { "192.168.0.0/22" }, ((Array)tryListArgs[1]).Cast<object>().Select(item => item.ToString()).ToArray());
        }

        [TestMethod]
        public void WideSubnet_PinsSmallestCoveringRange()
        {
            Assert.AreEqual("10.0.0.0/22", InvokeStatic("WideSubnet", new[] { typeof(string), typeof(string) }, "10.0.0.0", "10.0.3.255").ToString());
            Assert.AreEqual("192.168.1.0/27", InvokeStatic("WideSubnet", new[] { typeof(string), typeof(string) }, "192.168.1.10", "192.168.1.20").ToString());
            Assert.AreEqual("fe80::/10", InvokeStatic("WideSubnet", new[] { typeof(string), typeof(string) }, "fe80::", "febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff").ToString());

            var networks = TypedNetworks(Parse("192.168.0.0/24"), Parse("192.168.1.0/24"));
            Assert.AreEqual("192.168.0.0/23", InvokeStatic("WideSubnet", new[] { NetworkType.MakeArrayType() }, new object[] { networks }).ToString());
            var tryArgs = new object[] { networks, null };
            Assert.IsTrue((bool)InvokeStatic("TryWideSubnet", new[] { NetworkType.MakeArrayType(), NetworkType.MakeByRefType() }, tryArgs));
            Assert.AreEqual("192.168.0.0/23", tryArgs[1].ToString());
            var singleArgs = new object[] { TypedNetworks(Parse("10.0.0.0/8")), null };
            Assert.IsTrue((bool)InvokeStatic("TryWideSubnet", new[] { NetworkType.MakeArrayType(), NetworkType.MakeByRefType() }, singleArgs));
            Assert.AreEqual("10.0.0.0/8", singleArgs[1].ToString());
        }

        [TestMethod]
        public void PrintGuessAndParseCidr_AreUnchanged()
        {
            var expected = string.Join(Environment.NewLine,
                "IPNetwork   : 192.168.1.0/24",
                "Network     : 192.168.1.0",
                "Netmask     : 255.255.255.0",
                "Cidr        : 24",
                "Broadcast   : 192.168.1.255",
                "FirstUsable : 192.168.1.1",
                "LastUsable  : 192.168.1.254",
                "Usable      : 254",
                string.Empty);
            Assert.AreEqual(expected, Call(Parse("192.168.1.42/24"), "Print", Type.EmptyTypes));
            Assert.AreEqual(expected, InvokeStatic("Print", new[] { NetworkType }, Parse("192.168.1.42/24")));

            var guessArgs = new object[] { "10.1.2.3", (byte)0 };
            Assert.IsTrue((bool)InvokeStatic("TryGuessCidr", new[] { typeof(string), typeof(byte).MakeByRefType() }, guessArgs));
            Assert.AreEqual((byte)8, guessArgs[1]);
            guessArgs = new object[] { "172.16.0.1", (byte)0 };
            Assert.IsTrue((bool)InvokeStatic("TryGuessCidr", new[] { typeof(string), typeof(byte).MakeByRefType() }, guessArgs));
            Assert.AreEqual((byte)16, guessArgs[1]);
            guessArgs = new object[] { "192.168.1.1", (byte)0 };
            Assert.IsTrue((bool)InvokeStatic("TryGuessCidr", new[] { typeof(string), typeof(byte).MakeByRefType() }, guessArgs));
            Assert.AreEqual((byte)24, guessArgs[1]);
            guessArgs = new object[] { "::1", (byte)0 };
            Assert.IsTrue((bool)InvokeStatic("TryGuessCidr", new[] { typeof(string), typeof(byte).MakeByRefType() }, guessArgs));
            Assert.AreEqual((byte)64, guessArgs[1]);
            guessArgs = new object[] { "224.0.0.1", (byte)0 };
            Assert.IsFalse((bool)InvokeStatic("TryGuessCidr", new[] { typeof(string), typeof(byte).MakeByRefType() }, guessArgs));
            Assert.AreEqual((byte)0, guessArgs[1]);

            var cidrArgs = new object[] { "24", AddressFamily.InterNetwork, null };
            Assert.IsTrue((bool)InvokeStatic("TryParseCidr", new[] { typeof(string), typeof(AddressFamily), typeof(byte?).MakeByRefType() }, cidrArgs));
            Assert.AreEqual((byte)24, cidrArgs[2]);
            cidrArgs = new object[] { "33", AddressFamily.InterNetwork, null };
            Assert.IsFalse((bool)InvokeStatic("TryParseCidr", new[] { typeof(string), typeof(AddressFamily), typeof(byte?).MakeByRefType() }, cidrArgs));
            Assert.IsNull(cidrArgs[2]);
        }

        [TestMethod]
        public void NetworkManagerAcl_StillUsesTheSameRangeDecisions()
        {
            var manager = RuntimeHelpers.GetUninitializedObject(typeof(NetworkManager));
            var method = typeof(NetworkManager).GetMethod("IsAddressInSubnets", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            bool Check(string address, params string[] subnets)
            {
                return (bool)method.Invoke(manager, new object[] { IPAddress.Parse(address), address, subnets });
            }

            Assert.IsTrue(Check("10.1.2.3", "10.0.0.0/8"));
            Assert.IsFalse(Check("11.1.2.3", "10.0.0.0/8"));
            Assert.IsTrue(Check("192.168.1.255", "192.168.1.0/24"));
            Assert.IsTrue(Check("fe80::1234", "fe80::/10"));
            Assert.IsFalse(Check("fec0::1234", "fe80::/10"));
            Assert.IsTrue(Check("10.1.2.3", "8.8.8.8/8", "10.1.2.3", "10.0.0.0/8"));
        }
    }
}
