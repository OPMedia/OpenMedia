namespace MediaBrowser.Model.Net;

public class WebSocketMessage<T>
{
	public string MessageType { get; set; }

	public string MessageId { get; set; }

	public string ServerId { get; set; }

	public T Data { get; set; }
}
