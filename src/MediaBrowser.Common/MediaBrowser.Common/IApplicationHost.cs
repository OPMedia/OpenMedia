using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Updates;

namespace MediaBrowser.Common;

public interface IApplicationHost
{
	string OperatingSystemDisplayName { get; }

	string Name { get; }

	string SystemId { get; }

	bool HasPendingRestart { get; }

	bool IsShuttingDown { get; }

	bool CanSelfRestart { get; }

	Version ApplicationVersion { get; }

	bool CanSelfUpdate { get; }

	IPlugin[] Plugins { get; }

	PackageVersionClass SystemUpdateLevel { get; }

	event EventHandler<GenericEventArgs<PackageVersionInfo>> ApplicationUpdated;

	event EventHandler HasPendingRestartChanged;

	void NotifyPendingRestart();

	void Restart();

	IEnumerable<T> GetExports<T>(bool manageLiftime = true);

	Task<CheckForUpdateResult> CheckForApplicationUpdate(CancellationToken cancellationToken, IProgress<double> progress);

	Task UpdateApplication(PackageVersionInfo package, CancellationToken cancellationToken, IProgress<double> progress);

	T Resolve<T>();

	T TryResolve<T>();

	Task Shutdown();

	void RemovePlugin(IPlugin plugin);

	void Init();

	object CreateInstance(Type type);

	string GetValue(string name);
}
