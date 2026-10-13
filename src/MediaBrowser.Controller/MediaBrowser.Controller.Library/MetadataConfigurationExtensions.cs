using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;

namespace MediaBrowser.Controller.Library;

public static class MetadataConfigurationExtensions
{
	public static MetadataConfiguration GetMetadataConfiguration(this IConfigurationManager config)
	{
		return ConfigurationManagerExtensions.GetConfiguration<MetadataConfiguration>(config, "metadata");
	}
}
