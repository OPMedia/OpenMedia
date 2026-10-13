using System.Text;

namespace Emby.XmlTv.Entities;

public class XmlTvEpisode
{
	public int? Series { get; set; }

	public int? SeriesCount { get; set; }

	public int? Episode { get; set; }

	public int? EpisodeCount { get; set; }

	public string Title { get; set; }

	public int? Part { get; set; }

	public int? PartCount { get; set; }

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (Series.HasValue || SeriesCount.HasValue)
		{
			stringBuilder.AppendFormat("Series {0}", new object[1] { Series.HasValue ? Series.Value.ToString() : "?" });
			if (SeriesCount.HasValue)
			{
				stringBuilder.AppendFormat(" of {0}", new object[1] { SeriesCount });
			}
		}
		if (Episode.HasValue || EpisodeCount.HasValue)
		{
			stringBuilder.Append((stringBuilder.Length > 0) ? ", " : string.Empty);
			stringBuilder.AppendFormat("Episode {0}", new object[1] { Episode.HasValue ? Episode.Value.ToString() : "?" });
			if (EpisodeCount.HasValue)
			{
				stringBuilder.AppendFormat(" of {0}", new object[1] { EpisodeCount });
			}
		}
		if (Part.HasValue || PartCount.HasValue)
		{
			stringBuilder.Append((stringBuilder.Length > 0) ? ", " : string.Empty);
			stringBuilder.AppendFormat("Part {0}", new object[1] { Part.HasValue ? Part.Value.ToString() : "?" });
			if (PartCount.HasValue)
			{
				stringBuilder.AppendFormat(" of {0}", new object[1] { PartCount });
			}
		}
		return stringBuilder.ToString();
	}
}
