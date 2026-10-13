using System;

namespace MediaBrowser.Controller.Net;

public class SecurityException : Exception
{
	public SecurityExceptionType SecurityExceptionType { get; set; }

	public SecurityException(string message)
		: base(message)
	{
	}
}
