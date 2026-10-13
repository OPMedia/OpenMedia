using System;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Controller.MediaEncoding;

public class MediaInfoRequest
{
	public MediaSourceInfo MediaSource { get; set; }

	public bool ExtractChapters { get; set; }

	public DlnaProfileType MediaType
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

	public IIsoMount MountedIso { get; set; }

	public string[] PlayableStreamFileNames { get; set; }

	public MediaInfoRequest()
	{
		PlayableStreamFileNames = Array.Empty<string>();
	}
}
