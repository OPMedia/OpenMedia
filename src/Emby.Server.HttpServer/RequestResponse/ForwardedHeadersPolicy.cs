using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Emby.Server.HttpServer.RequestResponse
{
    /// <summary>
    /// X-Forwarded-* 的可信边界：只有直连对端命中 KnownProxies 时才解析转发头。
    /// 配置项支持单个 IP、CIDR 以及可选的端口后缀（IPv6 可带方括号）。
    /// </summary>
    public static class ForwardedHeadersPolicy
    {
        private static readonly IPNetwork[] NoProxies = new IPNetwork[0];

        public static IReadOnlyList<IPNetwork> ParseKnownProxies(IEnumerable<string> configuredProxies)
        {
            if (configuredProxies == null)
            {
                return NoProxies;
            }

            var proxies = new List<IPNetwork>();
            foreach (var entry in configuredProxies)
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                var value = entry.Trim();
                var slash = value.IndexOf('/');
                var addressPart = slash == -1 ? value : value.Substring(0, slash);
                var prefixPart = slash == -1 ? null : value.Substring(slash + 1);

                if (!TryNormalizeAddress(addressPart, out _, out var address))
                {
                    continue;
                }

                var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
                var prefixLength = maxPrefix;
                if (prefixPart != null
                    && (!int.TryParse(prefixPart, NumberStyles.None, CultureInfo.InvariantCulture, out prefixLength)
                        || prefixLength < 0
                        || prefixLength > maxPrefix))
                {
                    continue;
                }

                try
                {
                    proxies.Add(new IPNetwork(address, prefixLength));
                }
                catch (ArgumentException)
                {
                    // 配置里出现无法规范化为网络的条目时跳过，不放大信任范围。
                }
            }

            return proxies.Count == 0 ? NoProxies : proxies;
        }

        public static bool IsTrustedPeer(IPAddress peer, IReadOnlyList<IPNetwork> knownProxies)
        {
            if (peer == null || knownProxies == null || knownProxies.Count == 0)
            {
                return false;
            }

            var address = NormalizeAddress(peer);
            foreach (var proxy in knownProxies)
            {
                if (proxy.Contains(address))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从 X-Forwarded-For 链里取客户端地址：从右向左跳过所有可信代理，
        /// 遇到的第一个不可信地址就是客户端；若整条链都是代理则取最左侧地址。
        /// 直连对端不可信时一律返回 null（忽略转发头）。
        /// </summary>
        public static string GetClientAddress(
            string forwardedFor,
            string realIp,
            IPAddress peer,
            IReadOnlyList<IPNetwork> knownProxies)
        {
            if (!IsTrustedPeer(peer, knownProxies))
            {
                return null;
            }

            var candidates = (forwardedFor ?? string.Empty)
                .Split(',')
                .Select(token => token.Trim().Trim('"'))
                .Where(token => token.Length > 0)
                .Select(token => TryNormalizeAddress(token, out var normalized, out var address)
                    ? new Candidate(normalized, address)
                    : null)
                .Where(candidate => candidate != null)
                .ToList();

            if (candidates.Count == 0)
            {
                return TryNormalizeAddress(realIp, out var normalizedRealIp, out _) ? normalizedRealIp : null;
            }

            foreach (var candidate in candidates)
            {
                if (!IsTrustedPeer(candidate.Address, knownProxies))
                {
                    return candidate.Normalized;
                }
            }

            return candidates[candidates.Count - 1].Normalized;
        }

        private sealed class Candidate
        {
            public Candidate(string normalized, IPAddress address)
            {
                Normalized = normalized;
                Address = address;
            }

            public string Normalized { get; }

            public IPAddress Address { get; }
        }

        /// <summary>
        /// 去掉方括号、端口和 IPv6 zone id；IPv4-mapped IPv6 归一成 IPv4。
        /// </summary>
        private static bool TryNormalizeAddress(string value, out string normalized, out IPAddress address)
        {
            normalized = null;
            address = null;

            var candidate = value?.Trim();
            if (string.IsNullOrEmpty(candidate))
            {
                return false;
            }

            if (candidate[0] == '[')
            {
                var closeBracket = candidate.IndexOf(']');
                if (closeBracket <= 1)
                {
                    return false;
                }

                var remainder = candidate.Substring(closeBracket + 1);
                if (remainder.Length > 0 && (remainder[0] != ':' || remainder.Length == 1))
                {
                    return false;
                }

                candidate = candidate.Substring(1, closeBracket - 1);
            }
            else
            {
                var zone = candidate.IndexOf('%');
                if (zone >= 0)
                {
                    candidate = candidate.Substring(0, zone);
                }

                if (candidate.Count(c => c == ':') == 1)
                {
                    var colon = candidate.IndexOf(':');
                    if (int.TryParse(candidate.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                        && port <= 65535)
                    {
                        candidate = candidate.Substring(0, colon);
                    }
                }
            }

            if (!IPAddress.TryParse(candidate, out address))
            {
                return false;
            }

            address = NormalizeAddress(address);
            normalized = address.ToString();
            return true;
        }

        private static IPAddress NormalizeAddress(IPAddress address)
        {
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        }
    }
}
