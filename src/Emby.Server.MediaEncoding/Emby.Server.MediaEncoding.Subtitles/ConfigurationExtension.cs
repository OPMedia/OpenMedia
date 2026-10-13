using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Providers;

namespace Emby.Server.MediaEncoding.Subtitles;

public static class ConfigurationExtension
{
	public static SubtitleOptions GetSubtitleConfiguration(this IConfigurationManager manager)
	{
		return ConfigurationManagerExtensions.GetConfiguration<SubtitleOptions>(manager, "subtitles");
	}
}
