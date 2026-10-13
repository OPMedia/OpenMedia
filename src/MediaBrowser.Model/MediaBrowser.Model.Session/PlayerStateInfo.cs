namespace MediaBrowser.Model.Session;

public class PlayerStateInfo
{
	public long? PositionTicks { get; set; }

	public bool CanSeek { get; set; }

	public bool IsPaused { get; set; }

	public bool IsMuted { get; set; }

	public int? VolumeLevel { get; set; }

	public int? AudioStreamIndex { get; set; }

	public int? SubtitleStreamIndex { get; set; }

	public string MediaSourceId { get; set; }

	public PlayMethod? PlayMethod { get; set; }

	public RepeatMode RepeatMode { get; set; }
}
