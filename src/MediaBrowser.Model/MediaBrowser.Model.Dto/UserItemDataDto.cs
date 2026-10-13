using System;

namespace MediaBrowser.Model.Dto;

public class UserItemDataDto
{
	public double? Rating { get; set; }

	public double? PlayedPercentage { get; set; }

	public int? UnplayedItemCount { get; set; }

	public long PlaybackPositionTicks { get; set; }

	public int PlayCount { get; set; }

	public bool IsFavorite { get; set; }

	public bool? Likes { get; set; }

	public DateTime? LastPlayedDate { get; set; }

	public bool Played { get; set; }

	public string Key { get; set; }

	public string ItemId { get; set; }
}
