namespace MediaBrowser.Model.LiveTv;

public class TimerQuery
{
	public string ChannelId { get; set; }

	public string Id { get; set; }

	public string SeriesTimerId { get; set; }

	public bool? IsActive { get; set; }

	public bool? IsScheduled { get; set; }
}
