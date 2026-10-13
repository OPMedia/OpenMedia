namespace MediaBrowser.Common.Configuration;

public static class ConfigurationManagerExtensions
{
	public static T GetConfiguration<T>(this IConfigurationManager manager, string key)
	{
		return (T)manager.GetConfiguration(key);
	}
}
