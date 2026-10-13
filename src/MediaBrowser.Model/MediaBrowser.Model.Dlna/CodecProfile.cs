using System.Xml.Serialization;
using MediaBrowser.Model.Extensions;

namespace MediaBrowser.Model.Dlna;

public class CodecProfile
{
	[XmlAttribute("type")]
	public CodecType Type { get; set; }

	public ProfileCondition[] Conditions { get; set; }

	public ProfileCondition[] ApplyConditions { get; set; }

	[XmlAttribute("codec")]
	public string Codec { get; set; }

	[XmlAttribute("container")]
	public string Container { get; set; }

	public CodecProfile()
	{
		Conditions = new ProfileCondition[0];
		ApplyConditions = new ProfileCondition[0];
	}

	public string[] GetCodecs()
	{
		return ContainerProfile.SplitValue(Codec);
	}

	private bool ContainsContainer(string container)
	{
		return ContainerProfile.ContainsContainer(Container, container);
	}

	public bool ContainsAnyCodec(string codec, string container)
	{
		return ContainsAnyCodec(ContainerProfile.SplitValue(codec), container);
	}

	public bool ContainsAnyCodec(string[] codec, string container)
	{
		if (!ContainsContainer(container))
		{
			return false;
		}
		string[] codecs = GetCodecs();
		if (codecs.Length == 0)
		{
			return true;
		}
		foreach (string value in codec)
		{
			if (ListHelper.ContainsIgnoreCase(codecs, value))
			{
				return true;
			}
		}
		return false;
	}
}
