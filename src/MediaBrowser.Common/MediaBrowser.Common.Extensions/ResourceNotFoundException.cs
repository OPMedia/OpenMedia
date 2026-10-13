using System;

namespace MediaBrowser.Common.Extensions;

public class ResourceNotFoundException : Exception
{
	public ResourceNotFoundException()
	{
	}

	public ResourceNotFoundException(string message)
		: base(message)
	{
	}
}
