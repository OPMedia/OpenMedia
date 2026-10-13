using System.Text;

namespace Emby.XmlTv.Entities;

public class XmlTvIcon
{
	public string Source { get; set; }

	public int? Width { get; set; }

	public int? Height { get; set; }

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendFormat("Source: {0}", new object[1] { Source });
		if (Width.HasValue)
		{
			stringBuilder.AppendFormat(", Width: {0}", new object[1] { Width });
		}
		if (Height.HasValue)
		{
			stringBuilder.AppendFormat(", Height: {0}", new object[1] { Height });
		}
		return stringBuilder.ToString();
	}
}
