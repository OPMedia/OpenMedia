using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;

namespace MediaBrowser.Common.Net;

public interface INetworkManager
{
	Func<string[]> LocalSubnetsFn { get; set; }

	event EventHandler NetworkChanged;

	int GetRandomUnusedTcpPort();

	int GetRandomUnusedUdpPort();

	List<string> GetMacAddresses();

	bool IsInPrivateAddressSpace(string endpoint);

	IEnumerable<NetworkShare> GetNetworkShares(string path);

	IEnumerable<FileSystemEntryInfo> GetNetworkDevices();

	bool IsInLocalNetwork(string endpoint);

	IpAddressInfo[] GetLocalIpAddresses();

	IpAddressInfo ParseIpAddress(string ipAddress);

	bool TryParseIpAddress(string ipAddress, out IpAddressInfo ipAddressInfo);

	Task<IpAddressInfo[]> GetHostAddressesAsync(string host);

	bool IsAddressInSubnets(string addressString, string[] subnets);
}
