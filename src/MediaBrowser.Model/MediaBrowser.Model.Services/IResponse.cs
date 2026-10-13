using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Model.Services;

public interface IResponse
{
	IRequest Request { get; }

	int StatusCode { get; set; }

	string StatusDescription { get; set; }

	string ContentType { get; set; }

	bool SendChunked { get; set; }

	Stream OutputStream { get; }

	bool IsClosed { get; }

	Dictionary<string, object> Items { get; }

	QueryParamCollection Headers { get; }

	void AddHeader(string name, string value);

	string GetHeader(string name);

	void Redirect(string url);

	void Close();

	void SetContentLength(long contentLength);

	Task TransmitFile(string path, long offset, long count, FileShareMode fileShareMode, CancellationToken cancellationToken);
}
