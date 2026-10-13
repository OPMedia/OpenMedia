using MediaBrowser.Model.Updates;

namespace MediaBrowser.Model.System;

public class SystemInfo : PublicSystemInfo
{
	public PackageVersionClass SystemUpdateLevel { get; set; }

	public string OperatingSystemDisplayName { get; set; }

	public string PackageName { get; set; }

	public bool HasPendingRestart { get; set; }

	public bool IsShuttingDown { get; set; }

	public bool SupportsLibraryMonitor { get; set; }

	public int WebSocketPortNumber { get; set; }

	public InstallationInfo[] CompletedInstallations { get; set; }

	public bool CanSelfRestart { get; set; }

	public bool CanSelfUpdate { get; set; }

	public bool CanLaunchWebBrowser { get; set; }

	public string ProgramDataPath { get; set; }

	public string ItemsByNamePath { get; set; }

	public string CachePath { get; set; }

	public string LogPath { get; set; }

	public string InternalMetadataPath { get; set; }

	public string TranscodingTempPath { get; set; }

	public int HttpServerPortNumber { get; set; }

	public bool SupportsHttps { get; set; }

	public int HttpsPortNumber { get; set; }

	public bool HasUpdateAvailable { get; set; }

	public bool SupportsAutoRunAtStartup { get; set; }

	public string EncoderLocationType { get; set; }

	public Architecture SystemArchitecture { get; set; }

	public SystemInfo()
	{
		CompletedInstallations = new InstallationInfo[0];
	}
}
