using System;
using System.Collections.Generic;
using MediaBrowser.Model.Configuration;

namespace MediaBrowser.Common.Configuration;

public interface IConfigurationManager
{
	IApplicationPaths CommonApplicationPaths { get; }

	BaseApplicationConfiguration CommonConfiguration { get; }

	event EventHandler<ConfigurationUpdateEventArgs> NamedConfigurationUpdating;

	event EventHandler<EventArgs> ConfigurationUpdated;

	event EventHandler<ConfigurationUpdateEventArgs> NamedConfigurationUpdated;

	void SaveConfiguration();

	void ReplaceConfiguration(BaseApplicationConfiguration newConfiguration);

	object GetConfiguration(string key);

	Type GetConfigurationType(string key);

	void SaveConfiguration(string key, object configuration);

	void AddParts(IEnumerable<IConfigurationFactory> factories);
}
