using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;

namespace Emby.Server.MediaEncoding.Subtitles;

public class SrtParser : ISubtitleParser
{
	private readonly ILogger _logger;

	private readonly CultureInfo _usCulture = new CultureInfo("en-US");

	public const string NewLine = "\r\n";

	public SrtParser(ILogger logger)
	{
		_logger = logger;
	}

	public SubtitleTrackInfo Parse(Stream stream, CancellationToken cancellationToken)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected Obj, but got Unknown
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Expected Obj, but got Unknown
		SubtitleTrackInfo val = new SubtitleTrackInfo();
		List<SubtitleTrackEvent> list = new List<SubtitleTrackEvent>();
		using (StreamReader streamReader = new StreamReader(stream))
		{
			string value;
			while ((value = streamReader.ReadLine()) != null)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (string.IsNullOrWhiteSpace(value))
				{
					continue;
				}
				SubtitleTrackEvent val2 = new SubtitleTrackEvent();
				value = streamReader.ReadLine();
				if (string.IsNullOrWhiteSpace(value))
				{
					continue;
				}
				string[] array = Regex.Split(value, "[\\t ]*-->[\\t ]*");
				if (array.Length < 2)
				{
					_logger.Warn("Unrecognized line in srt: {0}", new object[1] { value });
					continue;
				}
				val2.StartPositionTicks = GetTicks(array[0]);
				string text = array[1];
				int num = text.IndexOf(" ", StringComparison.Ordinal);
				if (num > 0)
				{
					text = text.Substring(0, num);
				}
				val2.EndPositionTicks = GetTicks(text);
				List<string> list2 = new List<string>();
				while ((value = streamReader.ReadLine()) != null && !string.IsNullOrEmpty(value))
				{
					list2.Add(value);
				}
				val2.Text = string.Join("\r\n", list2);
				val2.Text = StringHelper.Replace(val2.Text, "\\N", "\r\n", StringComparison.OrdinalIgnoreCase);
				val2.Text = Regex.Replace(val2.Text, "\\{(\\\\[\\w]+\\(?([\\w\\d]+,?)+\\)?)+\\}", string.Empty, RegexOptions.IgnoreCase);
				val2.Text = Regex.Replace(val2.Text, "<", "&lt;", RegexOptions.IgnoreCase);
				val2.Text = Regex.Replace(val2.Text, ">", "&gt;", RegexOptions.IgnoreCase);
				val2.Text = Regex.Replace(val2.Text, "&lt;(\\/?(font|b|u|i|s))((\\s+(\\w|\\w[\\w\\-]*\\w)(\\s*=\\s*(?:\\\".*?\\\"|'.*?'|[^'\\\">\\s]+))?)+\\s*|\\s*)(\\/?)&gt;", "<$1$3$7>", RegexOptions.IgnoreCase);
				list.Add(val2);
			}
		}
		val.TrackEvents = LinqExtensions.ToArray<SubtitleTrackEvent>((IEnumerable<SubtitleTrackEvent>)list, list.Count);
		return val;
	}

	private long GetTicks(string time)
	{
		if (!TimeSpan.TryParseExact(time, "hh\\:mm\\:ss\\.fff", _usCulture, out var result))
		{
			if (!TimeSpan.TryParseExact(time, "hh\\:mm\\:ss\\,fff", _usCulture, out result))
			{
				return 0L;
			}
			return result.Ticks;
		}
		return result.Ticks;
	}
}
