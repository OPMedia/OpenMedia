using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace MediaBrowser.Common.Net;

public class HttpResponseInfo : IDisposable
{
	private readonly IDisposable _disposable;

	public string ContentType { get; set; }

	public string ResponseUrl { get; set; }

	public Stream Content { get; set; }

	public HttpStatusCode StatusCode { get; set; }

	public string TempFilePath { get; set; }

	public long? ContentLength { get; set; }

	public Dictionary<string, string> Headers { get; set; }

	public HttpResponseInfo(IDisposable disposable)
	{
		_disposable = disposable;
		Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public HttpResponseInfo()
	{
		Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public void Dispose()
	{
		if (_disposable != null)
		{
			_disposable.Dispose();
		}
	}
}
