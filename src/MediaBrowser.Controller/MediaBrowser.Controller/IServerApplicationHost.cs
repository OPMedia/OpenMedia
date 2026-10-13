using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.System;

namespace MediaBrowser.Controller;

public interface IServerApplicationHost : IApplicationHost
{
	bool SupportsAutoRunAtStartup { get; }

	bool CanLaunchWebBrowser { get; }

	int HttpPort { get; }

	int HttpsPort { get; }

	bool EnableHttps { get; }

	bool HasUpdateAvailable { get; }

	string FriendlyName { get; }

	string PackageRuntime { get; }

	event EventHandler HasUpdateAvailableChanged;

	Task<SystemInfo> GetSystemInfo(CancellationToken cancellationToken);

	Task<PublicSystemInfo> GetPublicSystemInfo(CancellationToken cancellationToken);

	Task<List<IpAddressInfo>> GetLocalIpAddresses(CancellationToken cancellationToken);

	Task<string> GetLocalApiUrl(CancellationToken cancellationToken);

	string GetLocalApiUrl(string host);

	string GetLocalApiUrl(IpAddressInfo address);

	void LaunchUrl(string url);

	void EnableLoopback(string appName);

	WakeOnLanInfo[] GetWakeOnLanInfo();

	string ExpandVirtualPath(string path);

	string ReverseVirtualPath(string path);
}
