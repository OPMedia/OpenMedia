using System;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Model.LiveTv;

public class RecordingQuery
{
	public string ChannelId { get; set; }

	public Guid UserId { get; set; }

	public string Id { get; set; }

	public int? StartIndex { get; set; }

	public int? Limit { get; set; }

	public RecordingStatus? Status { get; set; }

	public bool? IsInProgress { get; set; }

	public string SeriesTimerId { get; set; }

	public ItemFields[] Fields { get; set; }

	public bool? EnableImages { get; set; }

	public bool? IsLibraryItem { get; set; }

	public bool? IsNews { get; set; }

	public bool? IsMovie { get; set; }

	public bool? IsSeries { get; set; }

	public bool? IsKids { get; set; }

	public bool? IsSports { get; set; }

	public int? ImageTypeLimit { get; set; }

	public ImageType[] EnableImageTypes { get; set; }

	public bool EnableTotalRecordCount { get; set; }

	public RecordingQuery()
	{
		EnableTotalRecordCount = true;
	}
}
