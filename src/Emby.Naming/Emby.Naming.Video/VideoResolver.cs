using System;
using System.IO;
using System.Linq;
using Emby.Naming.Common;

namespace Emby.Naming.Video;

public class VideoResolver
{
	private readonly NamingOptions _options;

	public VideoResolver(NamingOptions options)
	{
		_options = options;
	}

	public VideoFileInfo ResolveDirectory(string path)
	{
		return Resolve(path, IsDirectory: true);
	}

	public VideoFileInfo ResolveFile(string path)
	{
		return Resolve(path, IsDirectory: false);
	}

	public VideoFileInfo Resolve(string path, bool IsDirectory, bool parseName = true)
	{
		if (string.IsNullOrEmpty(path))
		{
			throw new ArgumentNullException("path");
		}
		bool flag = false;
		string container = null;
		string stubType = null;
		if (!IsDirectory)
		{
			string text = Path.GetExtension(path) ?? string.Empty;
			if (!_options.VideoFileExtensions.Contains(text, StringComparer.OrdinalIgnoreCase))
			{
				StubResult stubResult = new StubResolver(_options).ResolveFile(path);
				flag = stubResult.IsStub;
				if (!flag)
				{
					return null;
				}
				stubType = stubResult.StubType;
			}
			container = text.TrimStart(new char[1] { '.' });
		}
		string[] flags = new FlagParser(_options).GetFlags(path);
		Format3DResult format3DResult = new Format3DParser(_options).Parse(flags);
		ExtraResult extraInfo = new ExtraResolver(_options).GetExtraInfo(path);
		string name = ((!IsDirectory) ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path));
		int? year = null;
		if (parseName)
		{
			CleanDateTimeResult cleanDateTimeResult = CleanDateTime(name);
			if (string.IsNullOrEmpty(extraInfo.ExtraType))
			{
				name = cleanDateTimeResult.Name;
				name = CleanString(name).Name;
			}
			year = cleanDateTimeResult.Year;
		}
		return new VideoFileInfo
		{
			Path = path,
			Container = container,
			IsStub = flag,
			Name = name,
			Year = year,
			StubType = stubType,
			Is3D = format3DResult.Is3D,
			Format3D = format3DResult.Format3D,
			ExtraType = extraInfo.ExtraType,
			IsDirectory = IsDirectory,
			ExtraRule = extraInfo.Rule
		};
	}

	public bool IsVideoFile(string path)
	{
		string value = Path.GetExtension(path) ?? string.Empty;
		return _options.VideoFileExtensions.Contains(value, StringComparer.OrdinalIgnoreCase);
	}

	public bool IsStubFile(string path)
	{
		string value = Path.GetExtension(path) ?? string.Empty;
		return _options.StubFileExtensions.Contains(value, StringComparer.OrdinalIgnoreCase);
	}

	public CleanStringResult CleanString(string name)
	{
		return new CleanStringParser().Clean(name, _options.CleanStringRegexes);
	}

	public CleanDateTimeResult CleanDateTime(string name)
	{
		return new CleanDateTimeParser(_options).Clean(name);
	}
}
