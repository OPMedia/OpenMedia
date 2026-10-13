using System;
using MediaBrowser.Model.Plugins;

namespace MediaBrowser.Common.Plugins;

public interface IPlugin
{
	string Name { get; }

	string Description { get; }

	Guid Id { get; }

	Version Version { get; }

	string AssemblyFilePath { get; }

	string DataFolderPath { get; }

	PluginInfo GetPluginInfo();

	void OnUninstalling();
}
