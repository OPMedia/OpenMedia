using System;

namespace MediaBrowser.Common.Extensions;

public class RateLimitExceededException : Exception
{
	public RateLimitExceededException()
	{
	}

	public RateLimitExceededException(string message)
		: base(message)
	{
	}
}
