using System;
using System.IO;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Controller.Providers;

public class DynamicImageResponse
{
	public string Path { get; set; }

	public MediaProtocol Protocol
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

	public Stream Stream { get; set; }

	public ImageFormat Format
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

	public bool HasImage { get; set; }

	public void SetFormatFromMimeType(string mimeType)
	{
		if (mimeType.EndsWith("gif", StringComparison.OrdinalIgnoreCase))
		{
			Format = (ImageFormat)1;
		}
		else if (mimeType.EndsWith("bmp", StringComparison.OrdinalIgnoreCase))
		{
			Format = (ImageFormat)0;
		}
		else if (mimeType.EndsWith("png", StringComparison.OrdinalIgnoreCase))
		{
			Format = (ImageFormat)3;
		}
		else
		{
			Format = (ImageFormat)2;
		}
	}
}
