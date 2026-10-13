using System;

namespace MediaBrowser.Model.Notifications;

public class NotificationOption
{
	public string Type { get; set; }

	public string[] DisabledMonitorUsers { get; set; }

	public string[] SendToUsers { get; set; }

	public bool Enabled { get; set; }

	public string[] DisabledServices { get; set; }

	public SendToUserType SendToUserMode { get; set; }

	public NotificationOption()
	{
		DisabledServices = Array.Empty<string>();
		DisabledMonitorUsers = Array.Empty<string>();
		SendToUsers = Array.Empty<string>();
	}
}
