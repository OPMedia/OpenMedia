using System;

namespace MediaBrowser.Controller.LiveTv;

public class LiveTvConflictException : Exception
{
	public LiveTvConflictException()
	{
	}

	public LiveTvConflictException(string message)
		: base(message)
	{
	}
}
