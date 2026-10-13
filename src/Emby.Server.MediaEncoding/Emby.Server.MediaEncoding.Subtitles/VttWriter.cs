using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MediaBrowser.Model.MediaInfo;

namespace Emby.Server.MediaEncoding.Subtitles;

public class VttWriter : ISubtitleWriter
{
	private string GetTimeString(TimeSpan time)
	{
		return time.ToString("hh\\:mm\\:ss\\.fff");
	}

	public void Write(SubtitleTrackInfo info, Stream stream, CancellationToken cancellationToken)
	{
		using StreamWriter streamWriter = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
		streamWriter.WriteLine("WEBVTT");
		streamWriter.WriteLine(string.Empty);
		SubtitleTrackEvent[] trackEvents = info.TrackEvents;
		foreach (SubtitleTrackEvent val in trackEvents)
		{
			string text = val.Text;
			if (!string.IsNullOrWhiteSpace(text))
			{
				cancellationToken.ThrowIfCancellationRequested();
				TimeSpan time = TimeSpan.FromTicks(val.StartPositionTicks);
				TimeSpan time2 = TimeSpan.FromTicks(val.EndPositionTicks);
				if (time2.TotalMilliseconds <= time.TotalMilliseconds)
				{
					time2 = time.Add(TimeSpan.FromMilliseconds(1.0));
				}
				streamWriter.WriteLine("{0} --> {1}", GetTimeString(time), GetTimeString(time2));
				text = Regex.Replace(text, "\\\\n", " ", RegexOptions.IgnoreCase);
				streamWriter.WriteLine(text);
				streamWriter.WriteLine(string.Empty);
			}
		}
	}
}
