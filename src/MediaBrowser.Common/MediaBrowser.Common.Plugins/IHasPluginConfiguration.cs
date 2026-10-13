using System;
using MediaBrowser.Model.Plugins;

namespace MediaBrowser.Common.Plugins;

public interface IHasPluginConfiguration
{
	Type ConfigurationType { get; }

	BasePluginConfiguration Configuration { get; }

	void UpdateConfiguration(BasePluginConfiguration configuration);

	void SetStartupInfo(Action<string> directoryCreateFn);
}
