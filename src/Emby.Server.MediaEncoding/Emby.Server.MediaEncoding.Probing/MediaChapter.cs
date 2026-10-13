using System.Collections.Generic;

namespace Emby.Server.MediaEncoding.Probing;

public class MediaChapter
{
	public int id { get; set; }

	public string time_base { get; set; }

	public long start { get; set; }

	public string start_time { get; set; }

	public long end { get; set; }

	public string end_time { get; set; }

	public Dictionary<string, string> tags { get; set; }
}
