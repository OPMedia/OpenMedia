using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.LiveTv;

namespace MediaBrowser.Controller.LiveTv;

public class LiveTvServiceStatusInfo
{
	public LiveTvServiceStatus Status
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

	public string StatusMessage { get; set; }

	public string Version { get; set; }

	public bool HasUpdateAvailable { get; set; }

	public List<LiveTvTunerInfo> Tuners { get; set; }

	public bool IsVisible { get; set; }

	public LiveTvServiceStatusInfo()
	{
		Tuners = new List<LiveTvTunerInfo>();
		IsVisible = true;
	}
}
