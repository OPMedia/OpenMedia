using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Emby.Naming.Common;

namespace Emby.Naming.Audio;

public class AlbumParser
{
	private readonly NamingOptions _options;

	public AlbumParser(NamingOptions options)
	{
		_options = options;
	}

	public MultiPartResult ParseMultiPart(string path)
	{
		MultiPartResult multiPartResult = new MultiPartResult();
		string fileName = Path.GetFileName(path);
		if (string.IsNullOrEmpty(fileName))
		{
			return multiPartResult;
		}
		fileName = fileName.Replace("-", " ");
		fileName = fileName.Replace(".", " ");
		fileName = fileName.Replace("(", " ");
		fileName = fileName.Replace(")", " ");
		fileName = Regex.Replace(fileName, "\\s+", " ");
		fileName = fileName.TrimStart(Array.Empty<char>());
		string[] albumStackingPrefixes = _options.AlbumStackingPrefixes;
		foreach (string text in albumStackingPrefixes)
		{
			if (fileName.IndexOf(text, StringComparison.OrdinalIgnoreCase) == 0 && int.TryParse(fileName.Substring(text.Length).Trim().Split(new char[1] { ' ' })
				.FirstOrDefault() ?? string.Empty, NumberStyles.Integer, CultureInfo.InvariantCulture, out var _))
			{
				multiPartResult.IsMultiPart = true;
				break;
			}
		}
		return multiPartResult;
	}
}
