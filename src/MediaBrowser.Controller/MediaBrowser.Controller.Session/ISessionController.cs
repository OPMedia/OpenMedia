using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Session;

public interface ISessionController
{
	bool IsSessionActive { get; }

	bool SupportsMediaControl { get; }

	Task SendMessage<T>(string name, string messageId, T data, ISessionController[] allControllers, CancellationToken cancellationToken);
}
