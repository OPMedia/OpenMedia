using System;
using System.Text;

namespace Emby.XmlTv.Entities;

public class XmlTvChannel : IEquatable<XmlTvChannel>
{
	public string Id { get; set; }

	public string DisplayName { get; set; }

	public string Number { get; set; }

	public string Url { get; set; }

	public XmlTvIcon Icon { get; set; }

	public bool Equals(XmlTvChannel other)
	{
		if (this == other)
		{
			return true;
		}
		if (other == null)
		{
			return false;
		}
		return Id == other.Id;
	}

	public override int GetHashCode()
	{
		return Id.GetHashCode() * 17;
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendFormat("{0} - {1} ", new object[2] { Id, DisplayName });
		if (!string.IsNullOrEmpty(Url))
		{
			stringBuilder.AppendFormat(" ({0})", new object[1] { Url });
		}
		return stringBuilder.ToString();
	}
}
