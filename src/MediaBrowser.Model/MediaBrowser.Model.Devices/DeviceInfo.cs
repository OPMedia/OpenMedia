using System;

namespace MediaBrowser.Model.Devices;

public class DeviceInfo
{
	public string Name { get; set; }

	public string Id { get; set; }

	public string LastUserName { get; set; }

	public string AppName { get; set; }

	public string AppVersion { get; set; }

	public Guid LastUserId { get; set; }

	public DateTime DateLastActivity { get; set; }

	public string IconUrl { get; set; }
}
