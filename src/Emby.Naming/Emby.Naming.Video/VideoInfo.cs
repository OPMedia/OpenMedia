using System.Collections.Generic;

namespace Emby.Naming.Video;

public class VideoInfo
{
	public string Name { get; set; }

	public int? Year { get; set; }

	public List<VideoFileInfo> Files { get; set; }

	public List<VideoFileInfo> Extras { get; set; }

	public List<VideoFileInfo> AlternateVersions { get; set; }

	public VideoInfo()
	{
		Files = new List<VideoFileInfo>();
		Extras = new List<VideoFileInfo>();
		AlternateVersions = new List<VideoFileInfo>();
	}
}
