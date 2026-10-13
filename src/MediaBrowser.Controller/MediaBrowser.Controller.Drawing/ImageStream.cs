using System;
using System.IO;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.Drawing;

namespace MediaBrowser.Controller.Drawing;

public class ImageStream : IDisposable
{
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

	public void Dispose()
	{
		if (Stream != null)
		{
			Stream.Dispose();
		}
	}
}
