using MediaBrowser.Model.Net;

namespace MediaBrowser.Controller.Net;

public class WebSocketMessageInfo : WebSocketMessage<string>
{
	public IWebSocketConnection Connection { get; set; }
}
