using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.LiveTv;

public class TimerInfo
{
	public Dictionary<string, string> ProviderIds { get; set; }

	public Dictionary<string, string> SeriesProviderIds { get; set; }

	public string[] Tags { get; set; }

	public string Id { get; set; }

	public string SeriesTimerId { get; set; }

	public string ChannelId { get; set; }

	public string ProgramId { get; set; }

	public string ShowId { get; set; }

	public string Name { get; set; }

	public string Overview { get; set; }

	public string SeriesId { get; set; }

	public DateTime StartDate { get; set; }

	public DateTime EndDate { get; set; }

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

	public int PrePaddingSeconds { get; set; }

	public int PostPaddingSeconds { get; set; }

	public bool IsPrePaddingRequired { get; set; }

	public bool IsPostPaddingRequired { get; set; }

	public bool IsManual { get; set; }

	public int Priority { get; set; }

	public int RetryCount { get; set; }

	public int? SeasonNumber { get; set; }

	public int? EpisodeNumber { get; set; }

	public bool IsMovie { get; set; }

	public bool IsKids => Tags.Contains("Kids", StringComparer.OrdinalIgnoreCase);

	public bool IsSports => Tags.Contains("Sports", StringComparer.OrdinalIgnoreCase);

	public bool IsNews => Tags.Contains("News", StringComparer.OrdinalIgnoreCase);

	public bool IsSeries { get; set; }

	[IgnoreDataMember]
	public bool IsLive => Tags.Contains("Live", StringComparer.OrdinalIgnoreCase);

	[IgnoreDataMember]
	public bool IsPremiere => Tags.Contains("Premiere", StringComparer.OrdinalIgnoreCase);

	public int? ProductionYear { get; set; }

	public string EpisodeTitle { get; set; }

	public DateTime? OriginalAirDate { get; set; }

	public bool IsProgramSeries { get; set; }

	public bool IsRepeat { get; set; }

	public float? CommunityRating { get; set; }

	public string OfficialRating { get; set; }

	public string[] Genres { get; set; }

	public string RecordingPath { get; set; }

	public KeepUntil KeepUntil
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

	public TimerInfo()
	{
		Genres = Array.Empty<string>();
		KeepUntil = (KeepUntil)0;
		ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		SeriesProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Tags = Array.Empty<string>();
	}
}
