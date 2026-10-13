using System;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

public class BaseApiService : IService, IRequiresRequest
{
	public ILogger Logger => ApiEntryPoint.Instance.Logger;

	public IHttpResultFactory ResultFactory => ApiEntryPoint.Instance.ResultFactory;

	public IRequest Request { get; set; }

	public string GetHeader(string name)
	{
		return Request.Headers[name];
	}

	protected SessionInfo GetSession(ISessionContext sessionContext)
	{
		return sessionContext.GetSession(Request) ?? throw new ArgumentException("Session not found.");
	}
}
