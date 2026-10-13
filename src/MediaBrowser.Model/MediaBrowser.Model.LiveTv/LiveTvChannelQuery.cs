using System;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.LiveTv;

public class LiveTvChannelQuery
{
	public ChannelType? ChannelType { get; set; }

	public bool? IsFavorite { get; set; }

	public bool? IsLiked { get; set; }

	public bool? IsDisliked { get; set; }

	public bool EnableFavoriteSorting { get; set; }

	public Guid UserId { get; set; }

	public int? StartIndex { get; set; }

	public int? Limit { get; set; }

	public bool AddCurrentProgram { get; set; }

	public bool EnableUserData { get; set; }

	public bool? IsNews { get; set; }

	public bool? IsMovie { get; set; }

	public bool? IsKids { get; set; }

	public bool? IsSports { get; set; }

	public bool? IsSeries { get; set; }

	public string[] SortBy { get; set; }

	public SortOrder? SortOrder { get; set; }

	public LiveTvChannelQuery()
	{
		EnableUserData = true;
		SortBy = Array.Empty<string>();
	}
}
