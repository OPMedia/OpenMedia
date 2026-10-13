using System;
using System.Collections.Generic;
using System.Linq;
using Emby.Naming.Common;

namespace Emby.Naming.Video;

public class Format3DParser
{
	private readonly NamingOptions _options;

	public Format3DParser(NamingOptions options)
	{
		_options = options;
	}

	public Format3DResult Parse(string path)
	{
		List<char> list = _options.VideoFlagDelimiters.ToList();
		list.Add(' ');
		return Parse(new FlagParser(_options).GetFlags(path, list.ToArray()));
	}

	internal Format3DResult Parse(string[] videoFlags)
	{
		Format3DRule[] format3DRules = _options.Format3DRules;
		foreach (Format3DRule rule in format3DRules)
		{
			Format3DResult format3DResult = Parse(videoFlags, rule);
			if (format3DResult.Is3D)
			{
				return format3DResult;
			}
		}
		return new Format3DResult();
	}

	private Format3DResult Parse(string[] videoFlags, Format3DRule rule)
	{
		Format3DResult format3DResult = new Format3DResult();
		if (string.IsNullOrEmpty(rule.PreceedingToken))
		{
			format3DResult.Format3D = new string[1] { rule.Token }.FirstOrDefault((string i) => videoFlags.Contains(i, StringComparer.OrdinalIgnoreCase));
			format3DResult.Is3D = !string.IsNullOrEmpty(format3DResult.Format3D);
			if (format3DResult.Is3D)
			{
				format3DResult.Tokens.Add(rule.Token);
			}
		}
		else
		{
			bool flag = false;
			string text = null;
			string[] array = videoFlags;
			foreach (string text2 in array)
			{
				if (flag)
				{
					format3DResult.Tokens.Add(rule.PreceedingToken);
					if (string.Equals(rule.Token, text2, StringComparison.OrdinalIgnoreCase))
					{
						text = text2;
						format3DResult.Tokens.Add(rule.Token);
					}
					break;
				}
				flag = string.Equals(text2, rule.PreceedingToken, StringComparison.OrdinalIgnoreCase);
			}
			format3DResult.Is3D = flag && !string.IsNullOrEmpty(text);
			format3DResult.Format3D = text;
		}
		return format3DResult;
	}
}
