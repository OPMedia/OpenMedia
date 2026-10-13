using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emby.Naming.Common;

namespace Emby.Naming.Subtitles;

public class SubtitleParser
{
	private readonly NamingOptions _options;

	public SubtitleParser(NamingOptions options)
	{
		_options = options;
	}

	public SubtitleInfo ParseFile(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			throw new ArgumentNullException("path");
		}
		string extension = Path.GetExtension(path);
		if (!_options.SubtitleFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
		{
			return null;
		}
		string[] flags = GetFlags(path);
		SubtitleInfo subtitleInfo = new SubtitleInfo
		{
			Path = path,
			IsDefault = _options.SubtitleDefaultFlags.Any((string i) => flags.Contains(i, StringComparer.OrdinalIgnoreCase)),
			IsForced = _options.SubtitleForcedFlags.Any((string i) => flags.Contains(i, StringComparer.OrdinalIgnoreCase))
		};
		List<string> list = flags.Where((string i) => !_options.SubtitleDefaultFlags.Contains(i, StringComparer.OrdinalIgnoreCase) && !_options.SubtitleForcedFlags.Contains(i, StringComparer.OrdinalIgnoreCase)).ToList();
		if (list.Count >= 3)
		{
			subtitleInfo.Language = list[list.Count - 2];
		}
		return subtitleInfo;
	}

	private string[] GetFlags(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			throw new ArgumentNullException("path");
		}
		return Path.GetFileName(path).Split(_options.SubtitleFlagDelimiters, StringSplitOptions.RemoveEmptyEntries);
	}
}
