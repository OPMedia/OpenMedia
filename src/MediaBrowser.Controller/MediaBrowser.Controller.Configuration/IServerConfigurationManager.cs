using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;

namespace MediaBrowser.Controller.Configuration;

public interface IServerConfigurationManager : IConfigurationManager
{
	IServerApplicationPaths ApplicationPaths { get; }

	ServerConfiguration Configuration { get; }

	bool SetOptimalValues();
}
