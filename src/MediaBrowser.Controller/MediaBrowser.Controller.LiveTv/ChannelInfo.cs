using System.Runtime.CompilerServices;
using MediaBrowser.Model.LiveTv;

namespace MediaBrowser.Controller.LiveTv;

public class ChannelInfo
{
	public string Name { get; set; }

	public string Number { get; set; }

	public string Id { get; set; }

	public string Path { get; set; }

	public string TunerChannelId { get; set; }

	public string CallSign { get; set; }

	public string TunerHostId { get; set; }

	public ChannelType ChannelType
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	public string ImagePath { get; set; }

	public string ImageUrl { get; set; }

	public bool? HasImage { get; set; }

	public bool? IsFavorite { get; set; }

	public bool? IsHD { get; set; }

	public string AudioCodec { get; set; }

	public string VideoCodec { get; set; }

	public string[] Tags { get; set; }
}
