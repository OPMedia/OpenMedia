using System;
using System.IO;
using System.Linq;
using Emby.Naming.Common;
using Emby.Naming.Video;

namespace Emby.Naming.TV;

public class EpisodeResolver
{
	private readonly NamingOptions _options;

	public EpisodeResolver(NamingOptions options)
	{
		_options = options;
	}

	public EpisodeInfo Resolve(string path, bool IsDirectory, bool? isNamed = null, bool? isOptimistic = null, bool? supportsAbsoluteNumbers = null, bool fillExtendedInfo = true)
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
		EpisodePathParserResult episodePathParserResult = new EpisodePathParser(_options).Parse(path, IsDirectory, isNamed, isOptimistic, supportsAbsoluteNumbers, fillExtendedInfo);
		return new EpisodeInfo
		{
			Path = path,
			Container = container,
			IsStub = flag,
			EndingEpsiodeNumber = episodePathParserResult.EndingEpsiodeNumber,
			EpisodeNumber = episodePathParserResult.EpisodeNumber,
			SeasonNumber = episodePathParserResult.SeasonNumber,
			SeriesName = episodePathParserResult.SeriesName,
			StubType = stubType,
			Is3D = format3DResult.Is3D,
			Format3D = format3DResult.Format3D,
			IsByDate = episodePathParserResult.IsByDate,
			Day = episodePathParserResult.Day,
			Month = episodePathParserResult.Month,
			Year = episodePathParserResult.Year
		};
	}
}
