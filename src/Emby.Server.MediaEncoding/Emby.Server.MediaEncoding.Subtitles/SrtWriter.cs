using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MediaBrowser.Model.MediaInfo;

namespace Emby.Server.MediaEncoding.Subtitles;

public class SrtWriter : ISubtitleWriter
{
	public void Write(SubtitleTrackInfo info, Stream stream, CancellationToken cancellationToken)
	{
		using StreamWriter streamWriter = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
		int num = 1;
		SubtitleTrackEvent[] trackEvents = info.TrackEvents;
		foreach (SubtitleTrackEvent val in trackEvents)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string text = val.Text;
			text = Regex.Replace(text, "\\\\n", " ", RegexOptions.IgnoreCase);
			if (!string.IsNullOrWhiteSpace(text))
			{
				streamWriter.WriteLine(num.ToString(CultureInfo.InvariantCulture));
				streamWriter.WriteLine("{0:hh\\:mm\\:ss\\,fff} --> {1:hh\\:mm\\:ss\\,fff}", TimeSpan.FromTicks(val.StartPositionTicks), TimeSpan.FromTicks(val.EndPositionTicks));
				streamWriter.WriteLine(text);
				streamWriter.WriteLine(string.Empty);
				num++;
			}
		}
	}
}
