using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.MediaInfo;

public class BlurayDiscInfo
{
	public MediaStream[] MediaStreams { get; set; }

	public long? RunTimeTicks { get; set; }

	public string[] Files { get; set; }

	public string PlaylistName { get; set; }

	public double[] Chapters { get; set; }
}
