using System;
using System.Collections.Generic;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Services;

namespace MediaBrowser.Controller.Net;

public interface IHttpServer : IDisposable
{
	string[] UrlPrefixes { get; }

	string GlobalResponse { get; set; }

	event EventHandler<GenericEventArgs<IWebSocketConnection>> WebSocketConnected;

	void Stop();

	void Init(IEnumerable<IService> services, IEnumerable<IWebSocketListener> listeners);
}
