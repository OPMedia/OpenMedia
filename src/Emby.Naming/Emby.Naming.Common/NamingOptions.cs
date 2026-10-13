using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Emby.Naming.Video;

namespace Emby.Naming.Common;

public class NamingOptions
{
	public string[] AudioFileExtensions { get; set; }

	public string[] AlbumStackingPrefixes { get; set; }

	public string[] SubtitleFileExtensions { get; set; }

	public char[] SubtitleFlagDelimiters { get; set; }

	public string[] SubtitleForcedFlags { get; set; }

	public string[] SubtitleDefaultFlags { get; set; }

	public EpisodeExpression[] EpisodeExpressions { get; set; }

	public string[] EpisodeWithoutSeasonExpressions { get; set; }

	public string[] EpisodeMultiPartExpressions { get; set; }

	public string[] VideoFileExtensions { get; set; }

	public string[] StubFileExtensions { get; set; }

	public string[] AudioBookPartsExpressions { get; set; }

	public StubTypeRule[] StubTypes { get; set; }

	public char[] VideoFlagDelimiters { get; set; }

	public Format3DRule[] Format3DRules { get; set; }

	public string[] VideoFileStackingExpressions { get; set; }

	public string[] CleanDateTimes { get; set; }

	public string[] CleanStrings { get; set; }

	public EpisodeExpression[] MultipleEpisodeExpressions { get; set; }

	public ExtraRule[] VideoExtraRules { get; set; }

	public Regex[] VideoFileStackingRegexes { get; private set; }

	public Regex[] CleanDateTimeRegexes { get; private set; }

	public Regex[] CleanStringRegexes { get; private set; }

	public Regex[] EpisodeWithoutSeasonRegexes { get; private set; }

	public Regex[] EpisodeMultiPartRegexes { get; private set; }

	public NamingOptions()
	{
		VideoFileExtensions = new string[41]
		{
			".m4v", ".3gp", ".nsv", ".ts", ".ty", ".strm", ".rm", ".rmvb", ".ifo", ".mov",
			".qt", ".divx", ".xvid", ".bivx", ".vob", ".nrg", ".img", ".iso", ".pva", ".wmv",
			".asf", ".asx", ".ogm", ".m2v", ".avi", ".bin", ".dvr-ms", ".mpg", ".mpeg", ".mp4",
			".mkv", ".avc", ".vp3", ".svq3", ".nuv", ".viv", ".dv", ".fli", ".flv", ".001",
			".tp"
		};
		VideoFlagDelimiters = new char[7] { '(', ')', '-', '.', '_', '[', ']' };
		StubFileExtensions = new string[1] { ".disc" };
		StubTypes = new StubTypeRule[10]
		{
			new StubTypeRule
			{
				StubType = "dvd",
				Token = "dvd"
			},
			new StubTypeRule
			{
				StubType = "hddvd",
				Token = "hddvd"
			},
			new StubTypeRule
			{
				StubType = "bluray",
				Token = "bluray"
			},
			new StubTypeRule
			{
				StubType = "bluray",
				Token = "brrip"
			},
			new StubTypeRule
			{
				StubType = "bluray",
				Token = "bd25"
			},
			new StubTypeRule
			{
				StubType = "bluray",
				Token = "bd50"
			},
			new StubTypeRule
			{
				StubType = "vhs",
				Token = "vhs"
			},
			new StubTypeRule
			{
				StubType = "tv",
				Token = "HDTV"
			},
			new StubTypeRule
			{
				StubType = "tv",
				Token = "PDTV"
			},
			new StubTypeRule
			{
				StubType = "tv",
				Token = "DSR"
			}
		};
		VideoFileStackingExpressions = new string[3] { "(.*?)([ _.-]*(?:cd|dvd|p(?:ar)?t|dis[ck])[ _.-]*[0-9]+)(.*?)(\\.[^.]+)$", "(.*?)([ _.-]*(?:cd|dvd|p(?:ar)?t|dis[ck])[ _.-]*[a-d])(.*?)(\\.[^.]+)$", "(.*?)([ ._-]*[a-d])(.*?)(\\.[^.]+)$" };
		CleanDateTimes = new string[1] { "(.+[^ _\\,\\.\\(\\)\\[\\]\\-])[ _\\.\\(\\)\\[\\]\\-]+(19[0-9][0-9]|20[0-1][0-9])([ _\\,\\.\\(\\)\\[\\]\\-][^0-9]|$)" };
		CleanStrings = new string[3] { "[ _\\,\\.\\(\\)\\[\\]\\-](ac3|dts|custom|dc|divx|divx5|dsr|dsrip|dutch|dvd|dvdrip|dvdscr|dvdscreener|screener|dvdivx|cam|fragment|fs|hdtv|hdrip|hdtvrip|internal|limited|multisubs|ntsc|ogg|ogm|pal|pdtv|proper|repack|rerip|retail|cd[1-9]|r3|r5|bd5|se|svcd|swedish|german|read.nfo|nfofix|unrated|ws|telesync|ts|telecine|tc|brrip|bdrip|480p|480i|576p|576i|720p|720i|1080p|1080i|2160p|hrhd|hrhdtv|hddvd|bluray|x264|h264|xvid|xvidvd|xxx|www.www|\\[.*\\])([ _\\,\\.\\(\\)\\[\\]\\-]|$)", "[ _\\,\\.\\(\\)\\[\\]\\-](3d|sbs|tab|hsbs|htab|mvc|\\[.*\\])([ _\\,\\.\\(\\)\\[\\]\\-]|$)", "(\\[.*\\])" };
		SubtitleFileExtensions = new string[4] { ".srt", ".ssa", ".ass", ".sub" };
		SubtitleFlagDelimiters = new char[1] { '.' };
		SubtitleForcedFlags = new string[2] { "foreign", "forced" };
		SubtitleDefaultFlags = new string[1] { "default" };
		AlbumStackingPrefixes = new string[5] { "disc", "cd", "disk", "vol", "volume" };
		AudioFileExtensions = new string[62]
		{
			".nsv", ".m4a", ".flac", ".aac", ".strm", ".pls", ".rm", ".mpa", ".wav", ".wma",
			".ogg", ".opus", ".mp3", ".mp2", ".mod", ".amf", ".669", ".dmf", ".dsm", ".far",
			".gdm", ".imf", ".it", ".m15", ".med", ".okt", ".s3m", ".stm", ".sfx", ".ult",
			".uni", ".xm", ".sid", ".ac3", ".dts", ".cue", ".aif", ".aiff", ".ape", ".mac",
			".mpc", ".mp+", ".mpp", ".shn", ".wv", ".nsf", ".spc", ".gym", ".adplug", ".adx",
			".dsp", ".adp", ".ymf", ".ast", ".afc", ".hps", ".xsp", ".acc", ".m4b", ".oga",
			".dsf", ".mka"
		};
		EpisodeExpressions = new EpisodeExpression[18]
		{
			new EpisodeExpression(".*(\\\\|\\/)(?<seriesname>((?![Ss]([0-9]+)[][ ._-]*[Ee]([0-9]+))[^\\\\\\/])*)?[Ss](?<seasonnumber>[0-9]+)[][ ._-]*[Ee](?<epnumber>[0-9]+)([^\\\\/]*)$")
			{
				IsNamed = true
			},
			new EpisodeExpression("[\\._ -]()[Ee][Pp]_?([0-9]+)([^\\\\/]*)$"),
			new EpisodeExpression("([0-9]{4})[\\.-]([0-9]{2})[\\.-]([0-9]{2})", byDate: true)
			{
				DateTimeFormats = new string[3] { "yyyy.MM.dd", "yyyy-MM-dd", "yyyy_MM_dd" }
			},
			new EpisodeExpression("([0-9]{2})[\\.-]([0-9]{2})[\\.-]([0-9]{4})", byDate: true)
			{
				DateTimeFormats = new string[3] { "dd.MM.yyyy", "dd-MM-yyyy", "dd_MM_yyyy" }
			},
			new EpisodeExpression("[\\\\/\\._ \\[\\(-]([0-9]+)x([0-9]+(?:(?:[a-i]|\\.[1-9])(?![0-9]))?)([^\\\\/]*)$")
			{
				SupportsAbsoluteEpisodeNumbers = true
			},
			new EpisodeExpression("[\\\\\\\\/\\\\._ -](?<seriesname>(?![0-9]+[0-9][0-9])([^\\\\\\/])*)[\\\\\\\\/\\\\._ -](?<seasonnumber>[0-9]+)(?<epnumber>[0-9][0-9](?:(?:[a-i]|\\\\.[1-9])(?![0-9]))?)([\\\\._ -][^\\\\\\\\/]*)$")
			{
				IsOptimistic = true,
				IsNamed = true,
				SupportsAbsoluteEpisodeNumbers = false
			},
			new EpisodeExpression("[\\/._ -]p(?:ar)?t[_. -]()([ivx]+|[0-9]+)([._ -][^\\/]*)$")
			{
				SupportsAbsoluteEpisodeNumbers = true
			},
			new EpisodeExpression(".*(\\\\|\\/)[sS]?(?<seasonnumber>\\d{1,4})[xX](?<epnumber>\\d{1,3})[^\\\\\\/]*$")
			{
				IsNamed = true
			},
			new EpisodeExpression(".*(\\\\|\\/)[sS](?<seasonnumber>\\d{1,4})[x,X]?[eE](?<epnumber>\\d{1,3})[^\\\\\\/]*$")
			{
				IsNamed = true
			},
			new EpisodeExpression(".*(\\\\|\\/)(?<seriesname>((?![sS]?\\d{1,4}[xX]\\d{1,3})[^\\\\\\/])*)?([sS]?(?<seasonnumber>\\d{1,4})[xX](?<epnumber>\\d{1,3}))[^\\\\\\/]*$")
			{
				IsNamed = true
			},
			new EpisodeExpression(".*(\\\\|\\/)(?<seriesname>[^\\\\\\/]*)[sS](?<seasonnumber>\\d{1,4})[xX\\.]?[eE](?<epnumber>\\d{1,3})[^\\\\\\/]*$")
			{
				IsNamed = true
			},
			new EpisodeExpression(".*[\\\\\\/](?<epnumber>\\d{1,3})(-(?<endingepnumber>\\d{2,3}))*\\.\\w+$")
			{
				IsOptimistic = true,
				IsNamed = true
			},
			new EpisodeExpression("([0-9]+)-([0-9]+)"),
			new EpisodeExpression(".*(\\\\|\\/)(?<epnumber>\\d{1,3})(-(?<endingepnumber>\\d{2,3}))*\\s?-\\s?[^\\\\\\/]*$")
			{
				IsOptimistic = true,
				IsNamed = true
			},
			new EpisodeExpression(".*(\\\\|\\/)(?<epnumber>\\d{1,3})(-(?<endingepnumber>\\d{2,3}))*\\.[^\\\\\\/]+$")
			{
				IsOptimistic = true,
				IsNamed = true
			},
			new EpisodeExpression(".*[\\\\\\/][^\\\\\\/]* - (?<epnumber>\\d{1,3})(-(?<endingepnumber>\\d{2,3}))*[^\\\\\\/]*$")
			{
				IsOptimistic = true,
				IsNamed = true
			},
			new EpisodeExpression("[Ss]eason[\\._ ](?<seasonnumber>[0-9]+)[\\\\\\/](?<epnumber>\\d{1,3})([^\\\\\\/]*)$")
			{
				IsOptimistic = true,
				IsNamed = true
			},
			new EpisodeExpression(".*[\\\\\\/][^\\\\\\/]* (?<epnumber>\\d{1,3})(-(?<endingepnumber>\\d{2,3}))*[^\\\\\\/]*$")
			{
				IsOptimistic = true,
				IsNamed = true
			}
		};
		EpisodeWithoutSeasonExpressions = new string[1] { "[/\\._ \\-]()([0-9]+)(-[0-9]+)?" };
		EpisodeMultiPartExpressions = new string[1] { "^[-_ex]+([0-9]+(?:(?:[a-i]|\\\\.[1-9])(?![0-9]))?)" };
		VideoExtraRules = new ExtraRule[18]
		{
			new ExtraRule
			{
				ExtraType = "trailer",
				RuleType = ExtraRuleType.Filename,
				Token = "trailer",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "trailer",
				RuleType = ExtraRuleType.Suffix,
				Token = "-trailer",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "trailer",
				RuleType = ExtraRuleType.Suffix,
				Token = ".trailer",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "trailer",
				RuleType = ExtraRuleType.Suffix,
				Token = "_trailer",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "trailer",
				RuleType = ExtraRuleType.Suffix,
				Token = " trailer",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "sample",
				RuleType = ExtraRuleType.Filename,
				Token = "sample",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "sample",
				RuleType = ExtraRuleType.Suffix,
				Token = "-sample",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "sample",
				RuleType = ExtraRuleType.Suffix,
				Token = ".sample",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "sample",
				RuleType = ExtraRuleType.Suffix,
				Token = "_sample",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "sample",
				RuleType = ExtraRuleType.Suffix,
				Token = " sample",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "themesong",
				RuleType = ExtraRuleType.Filename,
				Token = "theme",
				MediaType = MediaType.Audio
			},
			new ExtraRule
			{
				ExtraType = "scene",
				RuleType = ExtraRuleType.Suffix,
				Token = "-scene",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "clip",
				RuleType = ExtraRuleType.Suffix,
				Token = "-clip",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "interview",
				RuleType = ExtraRuleType.Suffix,
				Token = "-interview",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "behindthescenes",
				RuleType = ExtraRuleType.Suffix,
				Token = "-behindthescenes",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "deletedscene",
				RuleType = ExtraRuleType.Suffix,
				Token = "-deleted",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "featurette",
				RuleType = ExtraRuleType.Suffix,
				Token = "-featurette",
				MediaType = MediaType.Video
			},
			new ExtraRule
			{
				ExtraType = "short",
				RuleType = ExtraRuleType.Suffix,
				Token = "-short",
				MediaType = MediaType.Video
			}
		};
		Format3DRules = new Format3DRule[12]
		{
			new Format3DRule
			{
				PreceedingToken = "3d",
				Token = "hsbs"
			},
			new Format3DRule
			{
				PreceedingToken = "3d",
				Token = "sbs"
			},
			new Format3DRule
			{
				PreceedingToken = "3d",
				Token = "htab"
			},
			new Format3DRule
			{
				PreceedingToken = "3d",
				Token = "tab"
			},
			new Format3DRule
			{
				Token = "fsbs"
			},
			new Format3DRule
			{
				Token = "hsbs"
			},
			new Format3DRule
			{
				Token = "sbs"
			},
			new Format3DRule
			{
				Token = "ftab"
			},
			new Format3DRule
			{
				Token = "htab"
			},
			new Format3DRule
			{
				Token = "tab"
			},
			new Format3DRule
			{
				Token = "sbs3d"
			},
			new Format3DRule
			{
				Token = "mvc"
			}
		};
		AudioBookPartsExpressions = new string[6] { "ch(?:apter)?[\\s_-]?(?<chapter>\\d+)", "p(?:ar)?t[\\s_-]?(?<part>\\d+)", "^(?<chapter>\\d+)", "(?<part>\\d+)$", "(?<chapter>\\d+)_(?<part>\\d+)", "dis(?:c|k)[\\s_-]?(?<chapter>\\d+)" };
		List<string> list = VideoFileExtensions.ToList();
		list.AddRange(new string[29]
		{
			".mkv", ".m2t", ".m2ts", ".img", ".iso", ".mk3d", ".ts", ".rmvb", ".mov", ".avi",
			".mpg", ".mpeg", ".wmv", ".mp4", ".divx", ".dvr-ms", ".wtv", ".ogm", ".ogv", ".asf",
			".m4v", ".flv", ".f4v", ".3gp", ".webm", ".mts", ".m2v", ".rec", ".mxf"
		});
		MultipleEpisodeExpressions = new string[10] { ".*(\\\\|\\/)[sS]?(?<seasonnumber>\\d{1,4})[xX](?<epnumber>\\d{1,3})((-| - )\\d{1,4}[eExX](?<endingepnumber>\\d{1,3}))+[^\\\\\\/]*$", ".*(\\\\|\\/)[sS]?(?<seasonnumber>\\d{1,4})[xX](?<epnumber>\\d{1,3})((-| - )\\d{1,4}[xX][eE](?<endingepnumber>\\d{1,3}))+[^\\\\\\/]*$", ".*(\\\\|\\/)[sS]?(?<seasonnumber>\\d{1,4})[xX](?<epnumber>\\d{1,3})((-| - )?[xXeE](?<endingepnumber>\\d{1,3}))+[^\\\\\\/]*$", ".*(\\\\|\\/)[sS]?(?<seasonnumber>\\d{1,4})[xX](?<epnumber>\\d{1,3})(-[xE]?[eE]?(?<endingepnumber>\\d{1,3}))+[^\\\\\\/]*$", ".*(\\\\|\\/)(?<seriesname>((?![sS]?\\d{1,4}[xX]\\d{1,3})[^\\\\\\/])*)?([sS]?(?<seasonnumber>\\d{1,4})[xX](?<epnumber>\\d{1,3}))((-| - )\\d{1,4}[xXeE](?<endingepnumber>\\d{1,3}))+[^\\\\\\/]*$", ".*(\\\\|\\/)(?<seriesname>((?![sS]?\\d{1,4}[xX]\\d{1,3})[^\\\\\\/])*)?([sS]?(?<seasonnumber>\\d{1,4})[xX](?<epnumber>\\d{1,3}))((-| - )\\d{1,4}[xX][eE](?<endingepnumber>\\d{1,3}))+[^\\\\\\/]*$", ".*(\\\\|\\/)(?<seriesname>((?![sS]?\\d{1,4}[xX]\\d{1,3})[^\\\\\\/])*)?([sS]?(?<seasonnumber>\\d{1,4})[xX](?<epnumber>\\d{1,3}))((-| - )?[xXeE](?<endingepnumber>\\d{1,3}))+[^\\\\\\/]*$", ".*(\\\\|\\/)(?<seriesname>((?![sS]?\\d{1,4}[xX]\\d{1,3})[^\\\\\\/])*)?([sS]?(?<seasonnumber>\\d{1,4})[xX](?<epnumber>\\d{1,3}))(-[xX]?[eE]?(?<endingepnumber>\\d{1,3}))+[^\\\\\\/]*$", ".*(\\\\|\\/)(?<seriesname>[^\\\\\\/]*)[sS](?<seasonnumber>\\d{1,4})[xX\\.]?[eE](?<epnumber>\\d{1,3})((-| - )?[xXeE](?<endingepnumber>\\d{1,3}))+[^\\\\\\/]*$", ".*(\\\\|\\/)(?<seriesname>[^\\\\\\/]*)[sS](?<seasonnumber>\\d{1,4})[xX\\.]?[eE](?<epnumber>\\d{1,3})(-[xX]?[eE]?(?<endingepnumber>\\d{1,3}))+[^\\\\\\/]*$" }.Select((string i) => new EpisodeExpression(i)
		{
			IsNamed = true
		}).ToArray();
		VideoFileExtensions = list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		Compile();
	}

	public void Compile()
	{
		VideoFileStackingRegexes = VideoFileStackingExpressions.Select(Compile).ToArray();
		CleanDateTimeRegexes = CleanDateTimes.Select(Compile).ToArray();
		CleanStringRegexes = CleanStrings.Select(Compile).ToArray();
		EpisodeWithoutSeasonRegexes = EpisodeWithoutSeasonExpressions.Select(Compile).ToArray();
		EpisodeMultiPartRegexes = EpisodeMultiPartExpressions.Select(Compile).ToArray();
	}

	private Regex Compile(string exp)
	{
		return new Regex(exp, RegexOptions.IgnoreCase | RegexOptions.Compiled);
	}
}
