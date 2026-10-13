using System;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Users;

namespace MediaBrowser.Model.Notifications;

public class NotificationOptions
{
	public NotificationOption[] Options { get; set; }

	public NotificationOptions()
	{
		Options = new NotificationOption[10]
		{
			new NotificationOption
			{
				Type = NotificationType.TaskFailed.ToString(),
				Enabled = true,
				SendToUserMode = SendToUserType.Admins
			},
			new NotificationOption
			{
				Type = NotificationType.ServerRestartRequired.ToString(),
				Enabled = true,
				SendToUserMode = SendToUserType.Admins
			},
			new NotificationOption
			{
				Type = NotificationType.ApplicationUpdateAvailable.ToString(),
				Enabled = true,
				SendToUserMode = SendToUserType.Admins
			},
			new NotificationOption
			{
				Type = NotificationType.ApplicationUpdateInstalled.ToString(),
				Enabled = true,
				SendToUserMode = SendToUserType.Admins
			},
			new NotificationOption
			{
				Type = NotificationType.PluginUpdateInstalled.ToString(),
				Enabled = true,
				SendToUserMode = SendToUserType.Admins
			},
			new NotificationOption
			{
				Type = NotificationType.PluginUninstalled.ToString(),
				Enabled = true,
				SendToUserMode = SendToUserType.Admins
			},
			new NotificationOption
			{
				Type = NotificationType.InstallationFailed.ToString(),
				Enabled = true,
				SendToUserMode = SendToUserType.Admins
			},
			new NotificationOption
			{
				Type = NotificationType.PluginInstalled.ToString(),
				Enabled = true,
				SendToUserMode = SendToUserType.Admins
			},
			new NotificationOption
			{
				Type = NotificationType.PluginError.ToString(),
				Enabled = true,
				SendToUserMode = SendToUserType.Admins
			},
			new NotificationOption
			{
				Type = NotificationType.UserLockedOut.ToString(),
				Enabled = true,
				SendToUserMode = SendToUserType.Admins
			}
		};
	}

	public NotificationOption GetOptions(string type)
	{
		NotificationOption[] options = Options;
		foreach (NotificationOption notificationOption in options)
		{
			if (StringHelper.EqualsIgnoreCase(type, notificationOption.Type))
			{
				return notificationOption;
			}
		}
		return null;
	}

	public bool IsEnabled(string type)
	{
		return GetOptions(type)?.Enabled ?? false;
	}

	public bool IsServiceEnabled(string service, string notificationType)
	{
		NotificationOption options = GetOptions(notificationType);
		if (options != null)
		{
			return !ListHelper.ContainsIgnoreCase(options.DisabledServices, service);
		}
		return true;
	}

	public bool IsEnabledToMonitorUser(string type, Guid userId)
	{
		NotificationOption options = GetOptions(type);
		if (options != null && options.Enabled)
		{
			return !ListHelper.ContainsIgnoreCase(options.DisabledMonitorUsers, userId.ToString("N"));
		}
		return false;
	}

	public bool IsEnabledToSendToUser(string type, string userId, UserPolicy userPolicy)
	{
		NotificationOption options = GetOptions(type);
		if (options != null && options.Enabled)
		{
			if (options.SendToUserMode == SendToUserType.All)
			{
				return true;
			}
			if (options.SendToUserMode == SendToUserType.Admins && userPolicy.IsAdministrator)
			{
				return true;
			}
			return ListHelper.ContainsIgnoreCase(options.SendToUsers, userId);
		}
		return false;
	}
}
