using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Services;

namespace MediaBrowser.Controller.Net;

public interface IWebSocketConnection : IDisposable
{
	Guid Id { get; }

	DateTime LastActivityDate { get; }

	string Url { get; set; }

	QueryParamCollection QueryString { get; set; }

	Func<WebSocketMessageInfo, Task> OnReceive { get; set; }

	WebSocketState State { get; }

	string RemoteEndPoint { get; }

	event EventHandler<EventArgs> Closed;

	Task SendAsync<T>(WebSocketMessage<T> message, CancellationToken cancellationToken);

	Task SendAsync(byte[] buffer, CancellationToken cancellationToken);

	Task SendAsync(string text, CancellationToken cancellationToken);
}
