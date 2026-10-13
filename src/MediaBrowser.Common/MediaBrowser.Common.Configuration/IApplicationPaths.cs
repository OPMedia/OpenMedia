namespace MediaBrowser.Common.Configuration;

public interface IApplicationPaths
{
	string ProgramDataPath { get; }

	string ProgramSystemPath { get; }

	string DataPath { get; }

	string VirtualDataPath { get; }

	string ImageCachePath { get; }

	string PluginsPath { get; }

	string PluginConfigurationsPath { get; }

	string TempUpdatePath { get; }

	string LogDirectoryPath { get; }

	string ConfigurationDirectoryPath { get; }

	string SystemConfigurationFilePath { get; }

	string CachePath { get; }

	string TempDirectory { get; }
}
