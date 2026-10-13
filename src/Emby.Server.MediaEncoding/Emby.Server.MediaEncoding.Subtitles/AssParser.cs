using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.MediaInfo;

namespace Emby.Server.MediaEncoding.Subtitles;

public class AssParser : ISubtitleParser
{
	private readonly CultureInfo _usCulture = new CultureInfo("en-US");

	public const string NewLine = "\r\n";

	public SubtitleTrackInfo Parse(Stream stream, CancellationToken cancellationToken)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected Obj, but got Unknown
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Expected Obj, but got Unknown
		SubtitleTrackInfo val = new SubtitleTrackInfo();
		int num = 1;
		List<SubtitleTrackEvent> list = new List<SubtitleTrackEvent>();
		using (StreamReader streamReader = new StreamReader(stream))
		{
			while (streamReader.ReadLine() != "[Events]")
			{
			}
			Dictionary<string, int> dictionary = ParseFieldHeaders(streamReader.ReadLine());
			string text;
			while ((text = streamReader.ReadLine()) != null)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!string.IsNullOrWhiteSpace(text))
				{
					if (text.StartsWith("["))
					{
						break;
					}
					if (!string.IsNullOrEmpty(text))
					{
						SubtitleTrackEvent val2 = new SubtitleTrackEvent
						{
							Id = num.ToString(_usCulture)
						};
						num++;
						string[] array = text.Substring(10).Split(',');
						val2.StartPositionTicks = GetTicks(array[dictionary["Start"]]);
						val2.EndPositionTicks = GetTicks(array[dictionary["End"]]);
						val2.Text = string.Join(",", array.Skip(dictionary["Text"]));
						RemoteNativeFormatting(val2);
						val2.Text = StringHelper.Replace(val2.Text, "\\n", "\r\n", StringComparison.OrdinalIgnoreCase);
						val2.Text = Regex.Replace(val2.Text, "\\{(\\\\[\\w]+\\(?([\\w\\d]+,?)+\\)?)+\\}", string.Empty, RegexOptions.IgnoreCase);
						list.Add(val2);
					}
				}
			}
		}
		val.TrackEvents = LinqExtensions.ToArray<SubtitleTrackEvent>((IEnumerable<SubtitleTrackEvent>)list, list.Count);
		return val;
	}

	private long GetTicks(string time)
	{
		if (!TimeSpan.TryParseExact(time, "h\\:mm\\:ss\\.ff", _usCulture, out var result))
		{
			return 0L;
		}
		return result.Ticks;
	}

	private Dictionary<string, int> ParseFieldHeaders(string line)
	{
		List<string> list = (from x in line.Substring(8).Split(',')
			select x.Trim()).ToList();
		return new Dictionary<string, int>
		{
			{
				"Start",
				list.IndexOf("Start")
			},
			{
				"End",
				list.IndexOf("End")
			},
			{
				"Text",
				list.IndexOf("Text")
			}
		};
	}

	private void RemoteNativeFormatting(SubtitleTrackEvent p)
	{
		int num = p.Text.IndexOf('{');
		string text = string.Empty;
		while (num >= 0 && p.Text.IndexOf('}') > num)
		{
			string text2 = p.Text.Substring(num);
			if (text2.StartsWith("{\\an1}", StringComparison.Ordinal) || text2.StartsWith("{\\an2}", StringComparison.Ordinal) || text2.StartsWith("{\\an3}", StringComparison.Ordinal) || text2.StartsWith("{\\an4}", StringComparison.Ordinal) || text2.StartsWith("{\\an5}", StringComparison.Ordinal) || text2.StartsWith("{\\an6}", StringComparison.Ordinal) || text2.StartsWith("{\\an7}", StringComparison.Ordinal) || text2.StartsWith("{\\an8}", StringComparison.Ordinal) || text2.StartsWith("{\\an9}", StringComparison.Ordinal))
			{
				text = text2.Substring(0, 6);
			}
			else if (text2.StartsWith("{\\an1\\", StringComparison.Ordinal) || text2.StartsWith("{\\an2\\", StringComparison.Ordinal) || text2.StartsWith("{\\an3\\", StringComparison.Ordinal) || text2.StartsWith("{\\an4\\", StringComparison.Ordinal) || text2.StartsWith("{\\an5\\", StringComparison.Ordinal) || text2.StartsWith("{\\an6\\", StringComparison.Ordinal) || text2.StartsWith("{\\an7\\", StringComparison.Ordinal) || text2.StartsWith("{\\an8\\", StringComparison.Ordinal) || text2.StartsWith("{\\an9\\", StringComparison.Ordinal))
			{
				text = text2.Substring(0, 5) + "}";
			}
			int num2 = p.Text.IndexOf('}');
			p.Text = p.Text.Remove(num, num2 - num + 1);
			num = p.Text.IndexOf('{');
		}
		p.Text = text + p.Text;
	}
}
