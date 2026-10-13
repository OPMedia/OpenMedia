using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Emby.Naming.Common;

namespace Emby.Naming.TV;

public class SeasonPathParser
{
	private readonly NamingOptions _options;

	private static readonly string[] SeasonFolderNames = new string[8] { "season", "sæson", "temporada", "saison", "staffel", "series", "сезон", "stagione" };

	public SeasonPathParser(NamingOptions options)
	{
		_options = options;
	}

	public SeasonPathParserResult Parse(string path, bool supportSpecialAliases, bool supportNumericSeasonFolders)
	{
		SeasonPathParserResult seasonPathParserResult = new SeasonPathParserResult();
		Tuple<int?, bool> seasonNumberFromPath = GetSeasonNumberFromPath(path, supportSpecialAliases, supportNumericSeasonFolders);
		seasonPathParserResult.SeasonNumber = seasonNumberFromPath.Item1;
		if (seasonPathParserResult.SeasonNumber.HasValue)
		{
			seasonPathParserResult.Success = true;
			seasonPathParserResult.IsSeasonFolder = seasonNumberFromPath.Item2;
		}
		return seasonPathParserResult;
	}

	private Tuple<int?, bool> GetSeasonNumberFromPath(string path, bool supportSpecialAliases, bool supportNumericSeasonFolders)
	{
		string fileName = Path.GetFileName(path);
		if (supportSpecialAliases)
		{
			if (string.Equals(fileName, "specials", StringComparison.OrdinalIgnoreCase))
			{
				return new Tuple<int?, bool>(0, item2: true);
			}
			if (string.Equals(fileName, "extras", StringComparison.OrdinalIgnoreCase))
			{
				return new Tuple<int?, bool>(0, item2: true);
			}
		}
		if (supportNumericSeasonFolders && int.TryParse(fileName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return new Tuple<int?, bool>(result, item2: true);
		}
		if (fileName.StartsWith("s", StringComparison.OrdinalIgnoreCase) && int.TryParse(fileName.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2))
		{
			return new Tuple<int?, bool>(result2, item2: true);
		}
		string[] seasonFolderNames = SeasonFolderNames;
		foreach (string text in seasonFolderNames)
		{
			if (fileName.IndexOf(text, StringComparison.OrdinalIgnoreCase) != -1)
			{
				Tuple<int?, bool> seasonNumberFromPathSubstring = GetSeasonNumberFromPathSubstring(StringExtensions.Replace(fileName, text, " ", StringComparison.OrdinalIgnoreCase));
				if (!seasonNumberFromPathSubstring.Item1.HasValue)
				{
					break;
				}
				return seasonNumberFromPathSubstring;
			}
		}
		return new Tuple<int?, bool>(fileName.Split(new char[4] { '.', '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(GetSeasonNumberFromPart).FirstOrDefault((int? num) => num.HasValue), item2: true);
	}

	private int? GetSeasonNumberFromPart(string part)
	{
		if (part.Length < 2 || !part.StartsWith("s", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		part = part.Substring(1);
		if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		return null;
	}

	private Tuple<int?, bool> GetSeasonNumberFromPathSubstring(string path)
	{
		int num = -1;
		int num2 = 0;
		bool flag = false;
		bool item = true;
		for (int i = 0; i < path.Length; i++)
		{
			if (char.IsNumber(path, i))
			{
				if (!flag)
				{
					if (num == -1)
					{
						num = i;
					}
					num2++;
				}
			}
			else if (num != -1)
			{
				item = false;
				break;
			}
			char c = path[i];
			if (c.Equals('('))
			{
				flag = true;
			}
			else if (c.Equals(')'))
			{
				flag = false;
			}
		}
		if (num == -1)
		{
			return new Tuple<int?, bool>(null, item);
		}
		return new Tuple<int?, bool>(int.Parse(path.Substring(num, num2), CultureInfo.InvariantCulture), item);
	}
}
