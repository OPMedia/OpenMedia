namespace MediaBrowser.Model.Plugins;

public class PluginInfo
{
	public string Name { get; set; }

	public string Version { get; set; }

	public string ConfigurationFileName { get; set; }

	public string Description { get; set; }

	public string Id { get; set; }

	public string ImageUrl { get; set; }

	public string ImageTag { get; set; }
}
