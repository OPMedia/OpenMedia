using System.Net;

namespace MediaBrowser.Model.Services;

public interface IHttpResult : IHasHeaders
{
	int Status { get; set; }

	HttpStatusCode StatusCode { get; set; }

	string ContentType { get; set; }

	object Response { get; set; }

	IRequest RequestContext { get; set; }
}
