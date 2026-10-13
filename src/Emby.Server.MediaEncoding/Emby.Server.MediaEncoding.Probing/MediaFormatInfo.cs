using System.Collections.Generic;

namespace Emby.Server.MediaEncoding.Probing;

public class MediaFormatInfo
{
	public string filename { get; set; }

	public int nb_streams { get; set; }

	public string format_name { get; set; }

	public string format_long_name { get; set; }

	public string start_time { get; set; }

	public string duration { get; set; }

	public string size { get; set; }

	public string bit_rate { get; set; }

	public int probe_score { get; set; }

	public Dictionary<string, string> tags { get; set; }
}
