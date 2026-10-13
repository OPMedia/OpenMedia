using System;

namespace MediaBrowser.Common.Extensions;

public class RemoteServiceUnavailableException : Exception
{
	public RemoteServiceUnavailableException()
	{
	}

	public RemoteServiceUnavailableException(string message)
		: base(message)
	{
	}
}
