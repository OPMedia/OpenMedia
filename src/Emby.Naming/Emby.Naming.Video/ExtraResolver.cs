using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Emby.Naming.Audio;
using Emby.Naming.Common;

namespace Emby.Naming.Video;

public class ExtraResolver
{
	private readonly NamingOptions _options;

	public ExtraResolver(NamingOptions options)
	{
		_options = options;
	}

	public ExtraResult GetExtraInfo(string path)
	{
		return _options.VideoExtraRules.Select((ExtraRule i) => GetExtraInfo(path, i)).FirstOrDefault((ExtraResult i) => !string.IsNullOrEmpty(i.ExtraType)) ?? new ExtraResult();
	}

	private ExtraResult GetExtraInfo(string path, ExtraRule rule)
	{
		ExtraResult extraResult = new ExtraResult();
		if (rule.MediaType == MediaType.Audio)
		{
			if (!new AudioFileParser(_options).IsAudioFile(path))
			{
				return extraResult;
			}
		}
		else
		{
			if (rule.MediaType != MediaType.Video)
			{
				return extraResult;
			}
			if (!new VideoResolver(_options).IsVideoFile(path))
			{
				return extraResult;
			}
		}
		if (rule.RuleType == ExtraRuleType.Filename)
		{
			if (string.Equals(Path.GetFileNameWithoutExtension(path), rule.Token, StringComparison.OrdinalIgnoreCase))
			{
				extraResult.ExtraType = rule.ExtraType;
				extraResult.Rule = rule;
			}
		}
		else if (rule.RuleType == ExtraRuleType.Suffix)
		{
			if (Path.GetFileNameWithoutExtension(path).IndexOf(rule.Token, StringComparison.OrdinalIgnoreCase) > 0)
			{
				extraResult.ExtraType = rule.ExtraType;
				extraResult.Rule = rule;
			}
		}
		else if (rule.RuleType == ExtraRuleType.Regex)
		{
			string fileName = Path.GetFileName(path);
			if (new Regex(rule.Token, RegexOptions.IgnoreCase).IsMatch(fileName))
			{
				extraResult.ExtraType = rule.ExtraType;
				extraResult.Rule = rule;
			}
		}
		return extraResult;
	}
}
