using System;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.LiveTv;

namespace MediaBrowser.Controller.LiveTv;

public class RecordingStatusChangedEventArgs : EventArgs
{
	public string RecordingId { get; set; }

	public RecordingStatus NewStatus
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
