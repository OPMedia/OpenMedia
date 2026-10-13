using System;
using System.Collections.Generic;
using System.Text;

namespace Emby.XmlTv.Entities;

public class XmlTvProgram : IEquatable<XmlTvProgram>
{
	public string ChannelId { get; set; }

	public DateTime StartDate { get; set; }

	public DateTime EndDate { get; set; }

	public string Title { get; set; }

	public string Description { get; set; }

	public string ProgramId { get; set; }

	public List<string> Categories { get; set; }

	public List<string> Countries { get; set; }

	public DateTime? PreviouslyShown { get; set; }

	public bool IsPreviouslyShown { get; set; }

	public bool IsNew { get; set; }

	public DateTime? CopyrightDate { get; set; }

	public XmlTvEpisode Episode { get; set; }

	public List<XmlTvCredit> Credits { get; set; }

	public XmlTvRating Rating { get; set; }

	public float? StarRating { get; set; }

	public XmlTvIcon Icon { get; set; }

	public XmlTvPremiere Premiere { get; set; }

	public Dictionary<string, string> ProviderIds { get; set; }

	public Dictionary<string, string> SeriesProviderIds { get; set; }

	public XmlTvProgram()
	{
		Credits = new List<XmlTvCredit>();
		Categories = new List<string>();
		Countries = new List<string>();
		Episode = new XmlTvEpisode();
		ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		SeriesProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public bool Equals(XmlTvProgram other)
	{
		if (this == other)
		{
			return true;
		}
		if (other == null)
		{
			return false;
		}
		if (ChannelId == other.ChannelId && StartDate == other.StartDate)
		{
			return EndDate == other.EndDate;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return ChannelId.GetHashCode() * 17 + StartDate.GetHashCode() * 17 + EndDate.GetHashCode() * 17;
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendFormat("ChannelId: \t\t{0}\r\n", new object[1] { ChannelId });
		stringBuilder.AppendFormat("Title: \t\t{0}\r\n", new object[1] { Title });
		stringBuilder.AppendFormat("StartDate: \t\t{0}\r\n", new object[1] { StartDate });
		stringBuilder.AppendFormat("EndDate: \t\t{0}\r\n", new object[1] { EndDate });
		return stringBuilder.ToString();
	}
}
