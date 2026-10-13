using System.Collections.Generic;
using MediaBrowser.Model.Channels;

namespace MediaBrowser.Controller.Channels;

public class InternalChannelFeatures
{
	public List<ChannelMediaType> MediaTypes { get; set; }

	public List<ChannelMediaContentType> ContentTypes { get; set; }

	public int? MaxPageSize { get; set; }

	public List<ChannelItemSortField> DefaultSortFields { get; set; }

	public bool SupportsSortOrderToggle { get; set; }

	public int? AutoRefreshLevels { get; set; }

	public int? DailyDownloadLimit { get; set; }

	public bool SupportsContentDownloading { get; set; }

	public InternalChannelFeatures()
	{
		MediaTypes = new List<ChannelMediaType>();
		ContentTypes = new List<ChannelMediaContentType>();
		DefaultSortFields = new List<ChannelItemSortField>();
	}
}
