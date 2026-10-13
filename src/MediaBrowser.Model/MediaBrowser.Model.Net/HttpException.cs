using System;
using System.Net;

namespace MediaBrowser.Model.Net;

public class HttpException : Exception
{
	public HttpStatusCode? StatusCode { get; set; }

	public bool IsTimedOut { get; set; }

	public HttpException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public HttpException(string message)
		: base(message)
	{
	}
}
