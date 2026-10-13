using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Controller.Sync;

public class SyncedFileInfo
{
	public string Path { get; set; }

	public string[] PathParts { get; set; }

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

	public Dictionary<string, string> RequiredHttpHeaders { get; set; }

	public string Id { get; set; }

	public SyncedFileInfo()
	{
		RequiredHttpHeaders = new Dictionary<string, string>();
	}
}
