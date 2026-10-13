using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Providers;

namespace Emby.Server.MediaEncoding.Subtitles;

public class SubtitleConfigurationFactory : IConfigurationFactory
{
	public IEnumerable<ConfigurationStore> GetConfigurations()
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected Obj, but got Unknown
		return new ConfigurationStore[1]
		{
			new ConfigurationStore
			{
				Key = "subtitles",
				ConfigurationType = typeof(SubtitleOptions)
			}
		};
	}
}
