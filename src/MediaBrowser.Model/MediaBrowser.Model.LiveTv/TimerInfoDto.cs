using MediaBrowser.Model.Dto;

namespace MediaBrowser.Model.LiveTv;

public class TimerInfoDto : BaseTimerInfoDto
{
	public RecordingStatus Status { get; set; }

	public string SeriesTimerId { get; set; }

	public string ExternalSeriesTimerId { get; set; }

	public long? RunTimeTicks { get; set; }

	public BaseItemDto ProgramInfo { get; set; }

	public TimerInfoDto()
	{
		Type = "Timer";
	}
}
