using System;

namespace MediaBrowser.Common.Configuration;

public class ConfigurationStore
{
	public string Key { get; set; }

	public Type ConfigurationType { get; set; }
}
