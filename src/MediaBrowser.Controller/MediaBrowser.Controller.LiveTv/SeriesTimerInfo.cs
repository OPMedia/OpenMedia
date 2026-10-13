using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.LiveTv;

namespace MediaBrowser.Controller.LiveTv;

public class SeriesTimerInfo
{
	public string Id { get; set; }

	public string ChannelId { get; set; }

	public string ProgramId { get; set; }

	public string Name { get; set; }

	public string ServiceName { get; set; }

	public string Overview { get; set; }

	public DateTime StartDate { get; set; }

	public DateTime EndDate { get; set; }

	public bool RecordAnyTime { get; set; }

	public bool RecordAnyChannel { get; set; }

	public int KeepUpTo { get; set; }

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

	public bool SkipEpisodesInLibrary { get; set; }

	public bool RecordNewOnly { get; set; }

	public List<DayOfWeek> Days { get; set; }

	public int Priority { get; set; }

	public int PrePaddingSeconds { get; set; }

	public int PostPaddingSeconds { get; set; }

	public bool IsPrePaddingRequired { get; set; }

	public bool IsPostPaddingRequired { get; set; }

	public string SeriesId { get; set; }

	public SeriesTimerInfo()
	{
		Days = new List<DayOfWeek>();
		SkipEpisodesInLibrary = true;
		KeepUntil = (KeepUntil)0;
	}
}
