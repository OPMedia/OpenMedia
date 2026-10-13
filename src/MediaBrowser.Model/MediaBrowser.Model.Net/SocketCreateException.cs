using System;

namespace MediaBrowser.Model.Net;

public class SocketCreateException : Exception
{
	public string ErrorCode { get; private set; }

	public SocketCreateException(string errorCode, Exception originalException)
		: base(errorCode, originalException)
	{
		ErrorCode = errorCode;
	}
}
