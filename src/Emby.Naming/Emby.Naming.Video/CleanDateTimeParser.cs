using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Emby.Naming.Common;

namespace Emby.Naming.Video;

public class CleanDateTimeParser
{
	private readonly NamingOptions _options;

	public CleanDateTimeParser(NamingOptions options)
	{
		_options = options;
	}

	public CleanDateTimeResult Clean(string name)
	{
		string name2 = name;
		try
		{
			string value = Path.GetExtension(name) ?? string.Empty;
			if (!_options.VideoFileExtensions.Contains(value, StringComparer.OrdinalIgnoreCase) && !_options.AudioFileExtensions.Contains(value, StringComparer.OrdinalIgnoreCase))
			{
				name += ".mkv";
			}
		}
		catch (ArgumentException)
		{
		}
		CleanDateTimeResult cleanDateTimeResult = _options.CleanDateTimeRegexes.Select((Regex i) => Clean(name, i)).FirstOrDefault((CleanDateTimeResult i) => i.HasChanged) ?? new CleanDateTimeResult
		{
			Name = name2
		};
		if (cleanDateTimeResult.HasChanged)
		{
			return cleanDateTimeResult;
		}
		CleanStringResult cleanStringResult = new CleanStringParser().Clean(name, _options.CleanStringRegexes);
		if (!cleanStringResult.HasChanged)
		{
			return cleanDateTimeResult;
		}
		return _options.CleanDateTimeRegexes.Select((Regex i) => Clean(cleanStringResult.Name, i)).FirstOrDefault((CleanDateTimeResult i) => i.HasChanged) ?? cleanDateTimeResult;
	}

	private CleanDateTimeResult Clean(string name, Regex expression)
	{
		CleanDateTimeResult cleanDateTimeResult = new CleanDateTimeResult();
		Match match = expression.Match(name);
		if (match.Success && match.Groups.Count == 4 && match.Groups[1].Success && match.Groups[2].Success && int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			name = match.Groups[1].Value;
			cleanDateTimeResult.Year = result;
			cleanDateTimeResult.HasChanged = true;
		}
		cleanDateTimeResult.Name = name;
		return cleanDateTimeResult;
	}
}
