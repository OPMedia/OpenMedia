using System.Threading.Tasks;

namespace MediaBrowser.Controller.Net;

public interface IWebSocketListener
{
	Task ProcessMessage(WebSocketMessageInfo message);
}
