using System;

namespace MediaBrowser.Model.Search;

public class SearchHint
{
	public Guid ItemId { get; set; }

	public Guid Id { get; set; }

	public string Name { get; set; }

	public string MatchedTerm { get; set; }

	public int? IndexNumber { get; set; }

	public int? ProductionYear { get; set; }

	public int? ParentIndexNumber { get; set; }

	public string PrimaryImageTag { get; set; }

	public string ThumbImageTag { get; set; }

	public string ThumbImageItemId { get; set; }

	public string BackdropImageTag { get; set; }

	public string BackdropImageItemId { get; set; }

	public string Type { get; set; }

	public bool? IsFolder { get; set; }

	public long? RunTimeTicks { get; set; }

	public string MediaType { get; set; }

	public DateTime? StartDate { get; set; }

	public DateTime? EndDate { get; set; }

	public string Series { get; set; }

	public string Status { get; set; }

	public string Album { get; set; }

	public Guid AlbumId { get; set; }

	public string AlbumArtist { get; set; }

	public string[] Artists { get; set; }

	public int? SongCount { get; set; }

	public int? EpisodeCount { get; set; }

	public Guid ChannelId { get; set; }

	public string ChannelName { get; set; }

	public double? PrimaryImageAspectRatio { get; set; }
}
