using System;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class ItemImageInfo
{
	public string Path { get; set; }

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

	public DateTime DateModified { get; set; }

	public int Width { get; set; }

	public int Height { get; set; }

	[IgnoreDataMember]
	public bool IsLocalFile
	{
		get
		{
			if (Path != null && Path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			return true;
		}
	}
}
