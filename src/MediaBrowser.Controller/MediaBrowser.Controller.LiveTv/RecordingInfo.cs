using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.LiveTv;

namespace MediaBrowser.Controller.LiveTv;

public class RecordingInfo
{
	public string Id { get; set; }

	public string SeriesTimerId { get; set; }

	public string TimerId { get; set; }

	public string ChannelId { get; set; }

	public ChannelType ChannelType
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	public string Name { get; set; }

	public string Path { get; set; }

	public string Url { get; set; }

	public string Overview { get; set; }

	public DateTime StartDate { get; set; }

	public DateTime EndDate { get; set; }

	public string ProgramId { get; set; }

	public RecordingStatus Status
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	public List<string> Genres { get; set; }

	public bool IsRepeat { get; set; }

	public string EpisodeTitle { get; set; }

	public bool? IsHD { get; set; }

	public ProgramAudio? Audio { get; set; }

	public DateTime? OriginalAirDate { get; set; }

	public bool IsMovie { get; set; }

	public bool IsSports { get; set; }

	public bool IsSeries { get; set; }

	public bool IsLive { get; set; }

	public bool IsNews { get; set; }

	public bool IsKids { get; set; }

	public bool IsPremiere { get; set; }

	public string OfficialRating { get; set; }

	public float? CommunityRating { get; set; }

	public string ImagePath { get; set; }

	public string ImageUrl { get; set; }

	public bool? HasImage { get; set; }

	public string ShowId { get; set; }

	public DateTime DateLastUpdated { get; set; }

	public RecordingInfo()
	{
		Genres = new List<string>();
	}
}
