using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Notifications;

public interface INotificationService
{
	string Name { get; }

	Task SendNotification(UserNotification request, CancellationToken cancellationToken);

	bool IsEnabledForUser(User user);
}
