using System.IO;
using System.Threading.Tasks;

namespace MediaBrowser.Common.Net;

public interface IHttpClient
{
	Task<HttpResponseInfo> GetResponse(HttpRequestOptions options);

	Task<Stream> Get(HttpRequestOptions options);

	Task<HttpResponseInfo> SendAsync(HttpRequestOptions options, string httpMethod);

	Task<HttpResponseInfo> Post(HttpRequestOptions options);

	Task<string> GetTempFile(HttpRequestOptions options);

	Task<HttpResponseInfo> GetTempFileResponse(HttpRequestOptions options);
}
