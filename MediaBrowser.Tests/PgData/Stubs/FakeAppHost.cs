using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.System;
using MediaBrowser.Model.Updates;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// IServerApplicationHost 最小 stub：PgItemRepository 只读 ExpandVirtualPath /
    /// ReverseVirtualPath，其余成员按"未实现"返回空值。
    /// </summary>
    internal sealed class FakeAppHost : IServerApplicationHost
    {
        public string OperatingSystemDisplayName => "Test";

        public string Name => "OpenMedia.Test";

        public string SystemId => "test-system";

        public bool HasPendingRestart => false;

        public bool IsShuttingDown => false;

        public bool CanSelfRestart => false;

        public Version ApplicationVersion => new Version(1, 0, 0);

        public bool CanSelfUpdate => false;

        public IPlugin[] Plugins => Array.Empty<IPlugin>();

        public PackageVersionClass SystemUpdateLevel => PackageVersionClass.Release;

        public event EventHandler<GenericEventArgs<PackageVersionInfo>> ApplicationUpdated;

        public event EventHandler HasPendingRestartChanged;

        public bool SupportsAutoRunAtStartup => false;

        public bool CanLaunchWebBrowser => false;

        public int HttpPort => 8096;

        public int HttpsPort => 8920;

        public bool EnableHttps => false;

        public bool HasUpdateAvailable => false;

        public string FriendlyName => "OpenMedia Test";

        public string PackageRuntime => "Test";

        public event EventHandler HasUpdateAvailableChanged;

        public Task<SystemInfo> GetSystemInfo(CancellationToken cancellationToken)
        {
            return Task.FromResult(new SystemInfo());
        }

        public Task<PublicSystemInfo> GetPublicSystemInfo(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PublicSystemInfo());
        }

        public Task<List<IpAddressInfo>> GetLocalIpAddresses(CancellationToken cancellationToken)
        {
            return Task.FromResult(new List<IpAddressInfo>());
        }

        public Task<string> GetLocalApiUrl(CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Empty);
        }

        public string GetLocalApiUrl(string host)
        {
            return string.Empty;
        }

        public string GetLocalApiUrl(IpAddressInfo address)
        {
            return string.Empty;
        }

        public void LaunchUrl(string url)
        {
        }

        public void EnableLoopback(string appName)
        {
        }

        public WakeOnLanInfo[] GetWakeOnLanInfo()
        {
            return Array.Empty<WakeOnLanInfo>();
        }

        public string ExpandVirtualPath(string path)
        {
            return path;
        }

        public string ReverseVirtualPath(string path)
        {
            return path;
        }

        public void NotifyPendingRestart()
        {
        }

        public void Restart()
        {
        }

        public IEnumerable<T> GetExports<T>(bool manageLiftime = true)
        {
            return Array.Empty<T>();
        }

        public Task<CheckForUpdateResult> CheckForApplicationUpdate(CancellationToken cancellationToken, IProgress<double> progress)
        {
            return Task.FromResult(new CheckForUpdateResult());
        }

        public Task UpdateApplication(PackageVersionInfo package, CancellationToken cancellationToken, IProgress<double> progress)
        {
            return Task.CompletedTask;
        }

        public T Resolve<T>()
        {
            return default(T);
        }

        public T TryResolve<T>()
        {
            return default(T);
        }

        public Task Shutdown()
        {
            return Task.CompletedTask;
        }

        public void RemovePlugin(IPlugin plugin)
        {
        }

        public void Init()
        {
        }

        public object CreateInstance(Type type)
        {
            return Activator.CreateInstance(type);
        }

        public string GetValue(string name)
        {
            return null;
        }
    }
}