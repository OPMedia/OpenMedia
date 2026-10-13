using System.Collections.Generic;

namespace MediaBrowser.Common.Configuration;

public interface IConfigurationFactory
{
	IEnumerable<ConfigurationStore> GetConfigurations();
}
