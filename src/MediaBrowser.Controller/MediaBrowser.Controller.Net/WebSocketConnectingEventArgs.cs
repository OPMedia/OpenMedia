using System;
using MediaBrowser.Model.Services;

namespace MediaBrowser.Controller.Net;

public class WebSocketConnectingEventArgs : EventArgs
{
	public string Url { get; set; }

	public string Endpoint { get; set; }

	public QueryParamCollection QueryString { get; set; }

	public bool AllowConnection { get; set; }

	public WebSocketConnectingEventArgs()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected Obj, but got Unknown
		QueryString = new QueryParamCollection();
		AllowConnection = true;
	}
}
