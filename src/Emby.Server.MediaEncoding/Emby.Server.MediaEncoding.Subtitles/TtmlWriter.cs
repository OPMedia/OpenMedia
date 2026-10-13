using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MediaBrowser.Model.MediaInfo;

namespace Emby.Server.MediaEncoding.Subtitles;

public class TtmlWriter : ISubtitleWriter
{
	public void Write(SubtitleTrackInfo info, Stream stream, CancellationToken cancellationToken)
	{
		using StreamWriter streamWriter = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
		streamWriter.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
		streamWriter.WriteLine("<tt xmlns=\"http://www.w3.org/ns/ttml\" xmlns:tts=\"http://www.w3.org/2006/04/ttaf1#styling\" lang=\"no\">");
		streamWriter.WriteLine("<head>");
		streamWriter.WriteLine("<styling>");
		streamWriter.WriteLine("<style id=\"italic\" tts:fontStyle=\"italic\" />");
		streamWriter.WriteLine("<style id=\"left\" tts:textAlign=\"left\" />");
		streamWriter.WriteLine("<style id=\"center\" tts:textAlign=\"center\" />");
		streamWriter.WriteLine("<style id=\"right\" tts:textAlign=\"right\" />");
		streamWriter.WriteLine("</styling>");
		streamWriter.WriteLine("</head>");
		streamWriter.WriteLine("<body>");
		streamWriter.WriteLine("<div>");
		SubtitleTrackEvent[] trackEvents = info.TrackEvents;
		foreach (SubtitleTrackEvent val in trackEvents)
		{
			string text = val.Text;
			if (!string.IsNullOrWhiteSpace(text))
			{
				text = Regex.Replace(text, "\\\\n", "<br/>", RegexOptions.IgnoreCase);
				streamWriter.WriteLine("<p begin=\"{0}\" dur=\"{1}\">{2}</p>", val.StartPositionTicks, val.EndPositionTicks - val.StartPositionTicks, text);
			}
		}
		streamWriter.WriteLine("</div>");
		streamWriter.WriteLine("</body>");
		streamWriter.WriteLine("</tt>");
	}

	private string FormatTime(long ticks)
	{
		TimeSpan timeSpan = TimeSpan.FromTicks(ticks);
		return $"{timeSpan:hh\\:mm\\:ss\\,fff}";
	}
}
