using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace MediaBrowser.Model.Net;

public class HttpResponse : IDisposable
{
	private readonly IDisposable _disposable;

	public string ContentType { get; set; }

	public string ResponseUrl { get; set; }

	public Stream Content { get; set; }

	public HttpStatusCode StatusCode { get; set; }

	public long? ContentLength { get; set; }

	public Dictionary<string, string> Headers { get; set; }

	public HttpResponse(IDisposable disposable)
	{
		_disposable = disposable;
	}

	public HttpResponse()
	{
	}

	public void Dispose()
	{
		if (_disposable != null)
		{
			_disposable.Dispose();
		}
	}
}
