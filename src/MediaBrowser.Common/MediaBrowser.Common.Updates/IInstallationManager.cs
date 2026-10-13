using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Updates;

namespace MediaBrowser.Common.Updates;

public interface IInstallationManager : IDisposable
{
	List<Tuple<InstallationInfo, CancellationTokenSource>> CurrentInstallations { get; set; }

	IEnumerable<InstallationInfo> CompletedInstallations { get; }

	event EventHandler<InstallationEventArgs> PackageInstalling;

	event EventHandler<InstallationEventArgs> PackageInstallationCompleted;

	event EventHandler<InstallationFailedEventArgs> PackageInstallationFailed;

	event EventHandler<InstallationEventArgs> PackageInstallationCancelled;

	event EventHandler<GenericEventArgs<IPlugin>> PluginUninstalled;

	event EventHandler<GenericEventArgs<Tuple<IPlugin, PackageVersionInfo>>> PluginUpdated;

	event EventHandler<GenericEventArgs<PackageVersionInfo>> PluginInstalled;

	Task<List<PackageInfo>> GetAvailablePackages(CancellationToken cancellationToken, bool withRegistration = true, string packageType = null, Version applicationVersion = null);

	Task<List<PackageInfo>> GetAvailablePackagesWithoutRegistrationInfo(CancellationToken cancellationToken);

	Task<PackageVersionInfo> GetPackage(string name, string guid, PackageVersionClass classification, Version version);

	Task<PackageVersionInfo> GetLatestCompatibleVersion(string name, string guid, Version currentServerVersion, PackageVersionClass classification = PackageVersionClass.Release);

	PackageVersionInfo GetLatestCompatibleVersion(IEnumerable<PackageInfo> availablePackages, string name, string guid, Version currentServerVersion, PackageVersionClass classification = PackageVersionClass.Release);

	Task<IEnumerable<PackageVersionInfo>> GetAvailablePluginUpdates(Version applicationVersion, bool withAutoUpdateEnabled, CancellationToken cancellationToken);

	Task InstallPackage(PackageVersionInfo package, bool isPlugin, IProgress<double> progress, CancellationToken cancellationToken);

	void UninstallPlugin(IPlugin plugin);
}
