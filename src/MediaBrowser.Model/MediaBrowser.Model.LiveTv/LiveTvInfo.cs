using System;

namespace MediaBrowser.Model.LiveTv;

public class LiveTvInfo
{
	public LiveTvServiceInfo[] Services { get; set; }

	public bool IsEnabled { get; set; }

	public string[] EnabledUsers { get; set; }

	public LiveTvInfo()
	{
		Services = new LiveTvServiceInfo[0];
		EnabledUsers = Array.Empty<string>();
	}
}
