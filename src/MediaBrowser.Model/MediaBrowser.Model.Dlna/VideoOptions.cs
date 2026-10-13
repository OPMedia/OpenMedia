namespace MediaBrowser.Model.Dlna;

public class VideoOptions : AudioOptions
{
	public int? AudioStreamIndex { get; set; }

	public int? SubtitleStreamIndex { get; set; }
}
