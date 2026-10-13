using System;
using System.IO;
using System.Linq;
using Emby.Naming.Common;

namespace Emby.Naming.AudioBook;

public class AudioBookResolver
{
	private readonly NamingOptions _options;

	public AudioBookResolver(NamingOptions options)
	{
		_options = options;
	}

	public AudioBookFileInfo ParseFile(string path)
	{
		return Resolve(path);
	}

	public AudioBookFileInfo ParseDirectory(string path)
	{
		return Resolve(path, IsDirectory: true);
	}

	public AudioBookFileInfo Resolve(string path, bool IsDirectory = false)
	{
		if (string.IsNullOrEmpty(path))
		{
			throw new ArgumentNullException("path");
		}
		if (IsDirectory)
		{
			return null;
		}
		string text = Path.GetExtension(path) ?? string.Empty;
		if (!_options.AudioFileExtensions.Contains(text, StringComparer.OrdinalIgnoreCase))
		{
			return null;
		}
		string container = text.TrimStart(new char[1] { '.' });
		AudioBookFilePathParserResult audioBookFilePathParserResult = new AudioBookFilePathParser(_options).Parse(path, IsDirectory);
		return new AudioBookFileInfo
		{
			Path = path,
			Container = container,
			PartNumber = audioBookFilePathParserResult.PartNumber,
			ChapterNumber = audioBookFilePathParserResult.ChapterNumber,
			IsDirectory = IsDirectory
		};
	}
}
