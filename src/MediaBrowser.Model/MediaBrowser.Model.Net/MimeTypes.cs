using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Extensions;

namespace MediaBrowser.Model.Net;

public static class MimeTypes
{
	private static readonly string[] VideoFileExtensions = new string[28]
	{
		".mkv", ".m2t", ".m2ts", ".img", ".iso", ".mk3d", ".ts", ".rmvb", ".mov", ".avi",
		".mpg", ".mpeg", ".wmv", ".mp4", ".divx", ".dvr-ms", ".wtv", ".ogm", ".ogv", ".asf",
		".m4v", ".flv", ".f4v", ".3gp", ".webm", ".mts", ".m2v", ".rec"
	};

	private static readonly Dictionary<string, string> VideoFileExtensionsDictionary = GetVideoFileExtensionsDictionary();

	private static readonly Dictionary<string, string> MimeTypeLookup = GetMimeTypeLookup();

	private static readonly Dictionary<string, string> ExtensionLookup = CreateExtensionLookup();

	private static Dictionary<string, string> GetVideoFileExtensionsDictionary()
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string[] videoFileExtensions = VideoFileExtensions;
		foreach (string text in videoFileExtensions)
		{
			dictionary[text] = text;
		}
		return dictionary;
	}

	private static Dictionary<string, string> GetMimeTypeLookup()
	{
		return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ ".jpg", "image/jpeg" },
			{ ".jpeg", "image/jpeg" },
			{ ".tbn", "image/jpeg" },
			{ ".png", "image/png" },
			{ ".gif", "image/gif" },
			{ ".tiff", "image/tiff" },
			{ ".webp", "image/webp" },
			{ ".ico", "image/vnd.microsoft.icon" },
			{ ".mpg", "video/mpeg" },
			{ ".mpeg", "video/mpeg" },
			{ ".ogv", "video/ogg" },
			{ ".mov", "video/quicktime" },
			{ ".webm", "video/webm" },
			{ ".mkv", "video/x-matroska" },
			{ ".wmv", "video/x-ms-wmv" },
			{ ".flv", "video/x-flv" },
			{ ".avi", "video/x-msvideo" },
			{ ".asf", "video/x-ms-asf" },
			{ ".m4v", "video/x-m4v" },
			{ ".m4s", "video/mp4" },
			{ ".cbz", "application/x-cbz" },
			{ ".cbr", "application/epub+zip" },
			{ ".epub", "application/epub+zip" },
			{ ".pdf", "application/pdf" },
			{ ".mobi", "application/x-mobipocket-ebook" },
			{ ".ass", "text/x-ssa" },
			{ ".ssa", "text/x-ssa" },
			{ ".3gp", "video/3gpp" },
			{ ".3g2", "video/3gpp2" },
			{ ".m2ts", "video/mp2t" },
			{ ".mpegts", "video/mp2t" },
			{ ".ts", "video/mp2t" },
			{ ".mpd", "video/vnd.mpeg.dash.mpd" }
		};
	}

	private static Dictionary<string, string> CreateExtensionLookup()
	{
		Dictionary<string, string> dictionary = (from i in MimeTypeLookup
			group i by i.Value).ToDictionary((IGrouping<string, KeyValuePair<string, string>> x) => x.Key, (IGrouping<string, KeyValuePair<string, string>> x) => x.First().Key, StringComparer.OrdinalIgnoreCase);
		dictionary["image/jpg"] = ".jpg";
		dictionary["image/x-png"] = ".png";
		return dictionary;
	}

	public static string GetMimeType(string path)
	{
		return GetMimeType(path, enableStreamDefault: true);
	}

	public static string GetMimeType(string path, bool enableStreamDefault)
	{
		string text = Path.GetExtension(path) ?? string.Empty;
		if (MimeTypeLookup.TryGetValue(text, out var value))
		{
			return value;
		}
		if (VideoFileExtensionsDictionary.ContainsKey(text))
		{
			return "video/" + text.TrimStart(new char[1] { '.' }).ToLower();
		}
		if (StringHelper.EqualsIgnoreCase(text, ".css"))
		{
			return "text/css";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".csv"))
		{
			return "text/csv";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".html"))
		{
			return "text/html; charset=UTF-8";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".htm"))
		{
			return "text/html; charset=UTF-8";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".txt"))
		{
			return "text/plain";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".xml"))
		{
			return "application/xml";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".mp3"))
		{
			return "audio/mpeg";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".m4a"))
		{
			return "audio/mp4";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".aac"))
		{
			return "audio/mp4";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".webma"))
		{
			return "audio/webm";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".wav"))
		{
			return "audio/wav";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".wma"))
		{
			return "audio/x-ms-wma";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".flac"))
		{
			return "audio/flac";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".aac"))
		{
			return "audio/x-aac";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".ogg"))
		{
			return "audio/ogg";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".oga"))
		{
			return "audio/ogg";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".opus"))
		{
			return "audio/ogg";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".ac3"))
		{
			return "audio/ac3";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".dsf"))
		{
			return "audio/dsf";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".m4b"))
		{
			return "audio/m4b";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".xsp"))
		{
			return "audio/xsp";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".dsp"))
		{
			return "audio/dsp";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".m3u8"))
		{
			return "application/x-mpegURL";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".dll"))
		{
			return "application/octet-stream";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".js"))
		{
			return "application/x-javascript";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".json"))
		{
			return "application/json";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".map"))
		{
			return "application/x-javascript";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".woff"))
		{
			return "font/woff";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".ttf"))
		{
			return "font/ttf";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".eot"))
		{
			return "application/vnd.ms-fontobject";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".svg"))
		{
			return "image/svg+xml";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".svgz"))
		{
			return "image/svg+xml";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".srt"))
		{
			return "text/plain";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".vtt"))
		{
			return "text/vtt";
		}
		if (StringHelper.EqualsIgnoreCase(text, ".ttml"))
		{
			return "application/ttml+xml";
		}
		if (enableStreamDefault)
		{
			return "application/octet-stream";
		}
		return null;
	}

	public static string ToExtension(string mimeType)
	{
		mimeType = mimeType.Split(new char[1] { ';' })[0];
		if (ExtensionLookup.TryGetValue(mimeType, out var value))
		{
			return value;
		}
		return null;
	}
}
