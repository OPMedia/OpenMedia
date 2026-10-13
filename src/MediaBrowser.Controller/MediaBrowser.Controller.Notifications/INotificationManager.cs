using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Notifications;

namespace MediaBrowser.Controller.Notifications;

public interface INotificationManager
{
	Task SendNotification(NotificationRequest request, CancellationToken cancellationToken);

	Task SendNotification(NotificationRequest request, BaseItem relatedItem, CancellationToken cancellationToken);

	void AddParts(IEnumerable<INotificationService> services, IEnumerable<INotificationTypeFactory> notificationTypeFactories);

	List<NotificationTypeInfo> GetNotificationTypes();

	IEnumerable<NameIdPair> GetNotificationServices();
}
