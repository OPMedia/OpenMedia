using System;

namespace MediaBrowser.Model.Channels;

public class ChannelFeatures
{
	public string Name { get; set; }

	public string Id { get; set; }

	public bool CanSearch { get; set; }

	public ChannelMediaType[] MediaTypes { get; set; }

	public ChannelMediaContentType[] ContentTypes { get; set; }

	public int? MaxPageSize { get; set; }

	public int? AutoRefreshLevels { get; set; }

	public ChannelItemSortField[] DefaultSortFields { get; set; }

	public bool SupportsSortOrderToggle { get; set; }

	public bool SupportsLatestMedia { get; set; }

	public bool CanFilter { get; set; }

	public bool SupportsContentDownloading { get; set; }

	public ChannelFeatures()
	{
		MediaTypes = Array.Empty<ChannelMediaType>();
		ContentTypes = Array.Empty<ChannelMediaContentType>();
		DefaultSortFields = Array.Empty<ChannelItemSortField>();
	}
}
