using System;

namespace MediaBrowser.Model.LiveTv;

public class LiveTvServiceInfo
{
	public string Name { get; set; }

	public string HomePageUrl { get; set; }

	public LiveTvServiceStatus Status { get; set; }

	public string StatusMessage { get; set; }

	public string Version { get; set; }

	public bool HasUpdateAvailable { get; set; }

	public bool IsVisible { get; set; }

	public string[] Tuners { get; set; }

	public LiveTvServiceInfo()
	{
		Tuners = Array.Empty<string>();
	}
}
