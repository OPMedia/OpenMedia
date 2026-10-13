using System.Runtime.CompilerServices;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Providers;

public class DynamicImageInfo
{
	public string ImageId { get; set; }

	public ImageType Type
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
}
