using System;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class UserItemData
{
	public const double MinLikeValue = 6.5;

	public string Key { get; set; }

	public double? Rating { get; set; }

	public long PlaybackPositionTicks { get; set; }

	public int PlayCount { get; set; }

	public bool IsFavorite { get; set; }

	public DateTime? LastPlayedDate { get; set; }

	public bool Played { get; set; }

	public int? AudioStreamIndex { get; set; }

	public int? SubtitleStreamIndex { get; set; }

	[IgnoreDataMember]
	public bool? Likes
	{
		get
		{
			if (Rating.HasValue)
			{
				return Rating >= 6.5;
			}
			return null;
		}
		set
		{
			if (value.HasValue)
			{
				Rating = ((!value.Value) ? 1 : 10);
			}
			else
			{
				Rating = null;
			}
		}
	}
}
