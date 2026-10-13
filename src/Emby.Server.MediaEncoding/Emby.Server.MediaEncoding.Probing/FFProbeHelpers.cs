using System;
using System.Collections.Generic;

namespace Emby.Server.MediaEncoding.Probing;

public static class FFProbeHelpers
{
	public static void NormalizeFFProbeResult(InternalMediaInfoResult result)
	{
		if (result == null)
		{
			throw new ArgumentNullException("result");
		}
		if (result.format != null && result.format.tags != null)
		{
			result.format.tags = ConvertDictionaryToCaseInSensitive(result.format.tags);
		}
		if (result.streams == null)
		{
			return;
		}
		MediaStreamInfo[] streams = result.streams;
		foreach (MediaStreamInfo mediaStreamInfo in streams)
		{
			if (mediaStreamInfo.tags != null)
			{
				mediaStreamInfo.tags = ConvertDictionaryToCaseInSensitive(mediaStreamInfo.tags);
			}
			if (mediaStreamInfo.disposition != null)
			{
				mediaStreamInfo.disposition = ConvertDictionaryToCaseInSensitive(mediaStreamInfo.disposition);
			}
		}
	}

	public static string GetDictionaryValue(Dictionary<string, string> tags, string key)
	{
		if (tags == null)
		{
			return null;
		}
		tags.TryGetValue(key, out var value);
		return value;
	}

	public static int? GetDictionaryNumericValue(Dictionary<string, string> tags, string key)
	{
		string dictionaryValue = GetDictionaryValue(tags, key);
		if (!string.IsNullOrEmpty(dictionaryValue) && int.TryParse(dictionaryValue, out var result))
		{
			return result;
		}
		return null;
	}

	public static DateTime? GetDictionaryDateTime(Dictionary<string, string> tags, string key)
	{
		string dictionaryValue = GetDictionaryValue(tags, key);
		if (!string.IsNullOrEmpty(dictionaryValue) && DateTime.TryParse(dictionaryValue, out var result))
		{
			return result.ToUniversalTime();
		}
		return null;
	}

	private static Dictionary<string, string> ConvertDictionaryToCaseInSensitive(Dictionary<string, string> dict)
	{
		return new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
	}
}
