using System.Text;

namespace Emby.XmlTv.Entities;

public class XmlTvRating
{
	public string System { get; set; }

	public string Value { get; set; }

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (!string.IsNullOrEmpty(Value))
		{
			stringBuilder.Append(Value);
		}
		if (!string.IsNullOrEmpty(System))
		{
			stringBuilder.AppendFormat(" ({0})", new object[1] { System });
		}
		return stringBuilder.ToString();
	}
}
