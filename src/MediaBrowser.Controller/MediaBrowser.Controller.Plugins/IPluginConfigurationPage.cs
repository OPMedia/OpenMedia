using System.IO;
using MediaBrowser.Common.Plugins;

namespace MediaBrowser.Controller.Plugins;

public interface IPluginConfigurationPage
{
	string Name { get; }

	ConfigurationPageType ConfigurationPageType { get; }

	IPlugin Plugin { get; }

	Stream GetHtmlStream();
}
