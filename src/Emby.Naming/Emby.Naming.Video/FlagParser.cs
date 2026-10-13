using System;
using System.IO;
using Emby.Naming.Common;

namespace Emby.Naming.Video;

public class FlagParser
{
	private readonly NamingOptions _options;

	public FlagParser(NamingOptions options)
	{
		_options = options;
	}

	public string[] GetFlags(string path)
	{
		return GetFlags(path, _options.VideoFlagDelimiters);
	}

	public string[] GetFlags(string path, char[] delimeters)
	{
		if (string.IsNullOrEmpty(path))
		{
			throw new ArgumentNullException("path");
		}
		return Path.GetFileName(path).Split(delimeters, StringSplitOptions.RemoveEmptyEntries);
	}
}
