using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Emby.Naming.Common;

namespace Emby.Naming.AudioBook;

public class AudioBookFilePathParser
{
	private readonly NamingOptions _options;

	public AudioBookFilePathParser(NamingOptions options)
	{
		_options = options;
	}

	public AudioBookFilePathParserResult Parse(string path, bool IsDirectory)
	{
		AudioBookFilePathParserResult audioBookFilePathParserResult = Parse(path);
		if (audioBookFilePathParserResult.Success)
		{
			return audioBookFilePathParserResult;
		}
		return new AudioBookFilePathParserResult();
	}

	private AudioBookFilePathParserResult Parse(string path)
	{
		AudioBookFilePathParserResult audioBookFilePathParserResult = new AudioBookFilePathParserResult();
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		string[] audioBookPartsExpressions = _options.AudioBookPartsExpressions;
		for (int i = 0; i < audioBookPartsExpressions.Length; i++)
		{
			Match match = new Regex(audioBookPartsExpressions[i], RegexOptions.IgnoreCase).Match(fileNameWithoutExtension);
			if (!match.Success)
			{
				continue;
			}
			if (!audioBookFilePathParserResult.ChapterNumber.HasValue)
			{
				Group obj = match.Groups["chapter"];
				if (obj.Success && int.TryParse(obj.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
				{
					audioBookFilePathParserResult.ChapterNumber = result;
				}
			}
			if (!audioBookFilePathParserResult.PartNumber.HasValue)
			{
				Group obj2 = match.Groups["part"];
				if (obj2.Success && int.TryParse(obj2.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2))
				{
					audioBookFilePathParserResult.ChapterNumber = result2;
				}
			}
		}
		audioBookFilePathParserResult.Success = audioBookFilePathParserResult.PartNumber.HasValue || audioBookFilePathParserResult.ChapterNumber.HasValue;
		return audioBookFilePathParserResult;
	}
}
