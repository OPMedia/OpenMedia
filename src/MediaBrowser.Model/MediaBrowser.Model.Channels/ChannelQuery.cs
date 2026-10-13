using System;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Model.Channels;

public class ChannelQuery
{
	public ItemFields[] Fields { get; set; }

	public bool? EnableImages { get; set; }

	public int? ImageTypeLimit { get; set; }

	public ImageType[] EnableImageTypes { get; set; }

	public Guid UserId { get; set; }

	public int? StartIndex { get; set; }

	public int? Limit { get; set; }

	public bool? SupportsLatestItems { get; set; }

	public bool? SupportsMediaDeletion { get; set; }

	public bool? IsFavorite { get; set; }

	public bool? IsRecordingsFolder { get; set; }

	public bool RefreshLatestChannelItems { get; set; }
}
