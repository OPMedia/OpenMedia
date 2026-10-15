using System;
using System.Net;

namespace MediaBrowser.Model.Net;

public class HttpException : Exception
{
	public HttpStatusCode? StatusCode { get; set; }

	public bool IsTimedOut { get; set; }

	/// <summary>
	/// 服务端 Retry-After 头的毫秒值（503/429 场景，T4.8 重试退避用）；无头为 null。
	/// </summary>
	public int? RetryAfterMs { get; set; }

	public HttpException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public HttpException(string message)
		: base(message)
	{
	}
}
