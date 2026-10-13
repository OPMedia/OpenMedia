using System;

namespace MediaBrowser.Controller.Net;

public class WebSocketListenerState
{
	public DateTime DateLastSendUtc { get; set; }

	public long IntervalMs { get; set; }
}
