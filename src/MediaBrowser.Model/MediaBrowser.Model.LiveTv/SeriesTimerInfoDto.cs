using System;
using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.LiveTv;

public class SeriesTimerInfoDto : BaseTimerInfoDto
{
	public bool RecordAnyTime { get; set; }

	public bool SkipEpisodesInLibrary { get; set; }

	public bool RecordAnyChannel { get; set; }

	public int KeepUpTo { get; set; }

	public bool RecordNewOnly { get; set; }

	public DayOfWeek[] Days { get; set; }

	public DayPattern? DayPattern { get; set; }

	public Dictionary<ImageType, string> ImageTags { get; set; }

	public string ParentThumbItemId { get; set; }

	public string ParentThumbImageTag { get; set; }

	public string ParentPrimaryImageItemId { get; set; }

	public string ParentPrimaryImageTag { get; set; }

	public SeriesTimerInfoDto()
	{
		ImageTags = new Dictionary<ImageType, string>();
		Days = new DayOfWeek[0];
		Type = "SeriesTimer";
	}
}
