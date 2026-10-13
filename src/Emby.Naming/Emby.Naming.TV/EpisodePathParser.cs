using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Emby.Naming.Common;

namespace Emby.Naming.TV;

public class EpisodePathParser
{
	private readonly NamingOptions _options;

	public EpisodePathParser(NamingOptions options)
	{
		_options = options;
	}

	public EpisodePathParserResult Parse(string path, bool IsDirectory, bool? isNamed = null, bool? isOptimistic = null, bool? supportsAbsoluteNumbers = null, bool fillExtendedInfo = true)
	{
		if (IsDirectory)
		{
			path += ".mp4";
		}
		EpisodePathParserResult episodePathParserResult = null;
		EpisodeExpression[] episodeExpressions = _options.EpisodeExpressions;
		foreach (EpisodeExpression episodeExpression in episodeExpressions)
		{
			if ((!supportsAbsoluteNumbers.HasValue || episodeExpression.SupportsAbsoluteEpisodeNumbers == supportsAbsoluteNumbers.Value) && (!isNamed.HasValue || episodeExpression.IsNamed == isNamed.Value) && (!isOptimistic.HasValue || episodeExpression.IsOptimistic == isOptimistic.Value))
			{
				EpisodePathParserResult episodePathParserResult2 = Parse(path, episodeExpression);
				if (episodePathParserResult2.Success)
				{
					episodePathParserResult = episodePathParserResult2;
					break;
				}
			}
		}
		if ((episodePathParserResult != null) & fillExtendedInfo)
		{
			FillAdditional(path, episodePathParserResult);
			if (!string.IsNullOrEmpty(episodePathParserResult.SeriesName))
			{
				episodePathParserResult.SeriesName = episodePathParserResult.SeriesName.Trim().Trim('_', '.', '-').Trim();
			}
		}
		return episodePathParserResult ?? new EpisodePathParserResult();
	}

	private EpisodePathParserResult Parse(string name, EpisodeExpression expression)
	{
		EpisodePathParserResult episodePathParserResult = new EpisodePathParserResult();
		if (expression.IsByDate)
		{
			name = name.Replace('_', '-');
		}
		Match match = expression.Regex.Match(name);
		if (match.Success && match.Groups.Count >= 3)
		{
			if (expression.IsByDate)
			{
				DateTime result;
				if (expression.DateTimeFormats.Length != 0)
				{
					if (DateTime.TryParseExact(match.Groups[0].Value, expression.DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
					{
						episodePathParserResult.Year = result.Year;
						episodePathParserResult.Month = result.Month;
						episodePathParserResult.Day = result.Day;
						episodePathParserResult.Success = true;
					}
				}
				else if (DateTime.TryParse(match.Groups[0].Value, out result))
				{
					episodePathParserResult.Year = result.Year;
					episodePathParserResult.Month = result.Month;
					episodePathParserResult.Day = result.Day;
					episodePathParserResult.Success = true;
				}
				episodePathParserResult.Success = true;
			}
			else if (expression.IsNamed)
			{
				if (int.TryParse(match.Groups["seasonnumber"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2))
				{
					episodePathParserResult.SeasonNumber = result2;
				}
				if (int.TryParse(match.Groups["epnumber"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result2))
				{
					episodePathParserResult.EpisodeNumber = result2;
				}
				Group obj = match.Groups["endingepnumber"];
				if (obj.Success)
				{
					int num = obj.Index + obj.Length;
					if ((num >= name.Length || "0123456789iIpP".IndexOf(name[num]) == -1) && int.TryParse(obj.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result2))
					{
						episodePathParserResult.EndingEpsiodeNumber = result2;
					}
				}
				episodePathParserResult.SeriesName = match.Groups["seriesname"].Value;
				episodePathParserResult.Success = episodePathParserResult.EpisodeNumber.HasValue;
			}
			else
			{
				if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result3))
				{
					episodePathParserResult.SeasonNumber = result3;
				}
				if (int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result3))
				{
					episodePathParserResult.EpisodeNumber = result3;
				}
				episodePathParserResult.Success = episodePathParserResult.EpisodeNumber.HasValue;
			}
			int? seasonNumber = episodePathParserResult.SeasonNumber;
			int num2 = 200;
			if ((seasonNumber.GetValueOrDefault() >= num2 && seasonNumber.HasValue && episodePathParserResult.SeasonNumber < 1928) || episodePathParserResult.SeasonNumber > 2500)
			{
				episodePathParserResult.Success = false;
			}
			episodePathParserResult.IsByDate = expression.IsByDate;
		}
		return episodePathParserResult;
	}

	private void FillAdditional(string path, EpisodePathParserResult info)
	{
		List<EpisodeExpression> list = _options.MultipleEpisodeExpressions.ToList();
		if (string.IsNullOrEmpty(info.SeriesName))
		{
			list.InsertRange(0, _options.EpisodeExpressions.Where((EpisodeExpression i) => i.IsNamed));
		}
		FillAdditional(path, info, list);
	}

	private void FillAdditional(string path, EpisodePathParserResult info, IEnumerable<EpisodeExpression> expressions)
	{
		foreach (EpisodePathParserResult item in from i in expressions
			where i.IsNamed
			select Parse(path, i) into i
			where i.Success
			select i)
		{
			if (string.IsNullOrEmpty(info.SeriesName))
			{
				info.SeriesName = item.SeriesName;
			}
			if (!info.EndingEpsiodeNumber.HasValue && info.EpisodeNumber.HasValue)
			{
				info.EndingEpsiodeNumber = item.EndingEpsiodeNumber;
			}
			if (!string.IsNullOrEmpty(info.SeriesName) && (!info.EpisodeNumber.HasValue || info.EndingEpsiodeNumber.HasValue))
			{
				break;
			}
		}
	}
}
