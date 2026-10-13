namespace MediaBrowser.Model.Services;

public interface IHttpRequest : IRequest
{
	IHttpResponse HttpResponse { get; }

	string HttpMethod { get; }

	string XForwardedFor { get; }

	int? XForwardedPort { get; }

	string XForwardedProtocol { get; }

	string XRealIp { get; }

	string Accept { get; }
}
