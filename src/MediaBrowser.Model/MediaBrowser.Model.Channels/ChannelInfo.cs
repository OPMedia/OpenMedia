namespace MediaBrowser.Model.Channels;

public class ChannelInfo
{
	public string Name { get; set; }

	public string Id { get; set; }

	public string HomePageUrl { get; set; }

	public ChannelFeatures Features { get; set; }
}
