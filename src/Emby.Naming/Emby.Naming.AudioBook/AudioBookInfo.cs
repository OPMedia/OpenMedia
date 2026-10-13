using System.Collections.Generic;

namespace Emby.Naming.AudioBook;

public class AudioBookInfo
{
	public string Name { get; set; }

	public int? Year { get; set; }

	public List<AudioBookFileInfo> Files { get; set; }

	public List<AudioBookFileInfo> Extras { get; set; }

	public List<AudioBookFileInfo> AlternateVersions { get; set; }

	public AudioBookInfo()
	{
		Files = new List<AudioBookFileInfo>();
		Extras = new List<AudioBookFileInfo>();
		AlternateVersions = new List<AudioBookFileInfo>();
	}
}
