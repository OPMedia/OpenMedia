using System.Collections.Generic;
using MediaBrowser.Model.Notifications;

namespace MediaBrowser.Controller.Notifications;

public interface INotificationTypeFactory
{
	IEnumerable<NotificationTypeInfo> GetNotificationTypes();
}
