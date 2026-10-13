using System;
using System.Runtime.CompilerServices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Notifications;

namespace MediaBrowser.Controller.Notifications;

public class UserNotification
{
	public string Name { get; set; }

	public string Description { get; set; }

	public string Url { get; set; }

	public NotificationLevel Level
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

	public DateTime Date { get; set; }

	public User User { get; set; }
}
