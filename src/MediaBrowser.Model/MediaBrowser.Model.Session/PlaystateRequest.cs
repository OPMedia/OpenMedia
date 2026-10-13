namespace MediaBrowser.Model.Session;

public class PlaystateRequest
{
	public PlaystateCommand Command { get; set; }

	public long? SeekPositionTicks { get; set; }

	public string ControllingUserId { get; set; }
}
