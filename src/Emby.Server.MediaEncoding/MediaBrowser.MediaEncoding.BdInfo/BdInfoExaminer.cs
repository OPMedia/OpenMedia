using System;
using System.Collections.Generic;
using System.Linq;
using BDInfo;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Text;

namespace MediaBrowser.MediaEncoding.BdInfo;

public class BdInfoExaminer : IBlurayExaminer
{
	private readonly IFileSystem _fileSystem;

	private readonly ITextEncoding _textEncoding;

	public BdInfoExaminer(IFileSystem fileSystem, ITextEncoding textEncoding)
	{
		_fileSystem = fileSystem;
		_textEncoding = textEncoding;
	}

	public BlurayDiscInfo GetDiscInfo(string path)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Expected Obj, but got Unknown
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentNullException("path");
		}
		BDROM val = new BDROM(path, _fileSystem, _textEncoding);
		val.Scan();
		TSPlaylistFile val2 = val.PlaylistFiles.Values.OrderByDescending((TSPlaylistFile p) => p.TotalLength).FirstOrDefault((TSPlaylistFile p) => p.IsValid);
		BlurayDiscInfo val3 = new BlurayDiscInfo();
		if (val2 == null)
		{
			return val3;
		}
		val3.Chapters = val2.Chapters.ToArray();
		val3.RunTimeTicks = TimeSpan.FromSeconds(val2.TotalLength).Ticks;
		List<MediaStream> list = new List<MediaStream>();
		foreach (TSStream sortedStream in val2.SortedStreams)
		{
			TSVideoStream val4 = (TSVideoStream)(object)((sortedStream is TSVideoStream) ? sortedStream : null);
			if (val4 != null)
			{
				AddVideoStream(list, val4);
				continue;
			}
			TSAudioStream val5 = (TSAudioStream)(object)((sortedStream is TSAudioStream) ? sortedStream : null);
			if (val5 != null)
			{
				AddAudioStream(list, val5);
				continue;
			}
			TSTextStream val6 = (TSTextStream)(object)((sortedStream is TSTextStream) ? sortedStream : null);
			if (val6 != null)
			{
				AddSubtitleStream(list, val6);
				continue;
			}
			TSGraphicsStream val7 = (TSGraphicsStream)(object)((sortedStream is TSGraphicsStream) ? sortedStream : null);
			if (val7 != null)
			{
				AddSubtitleStream(list, val7);
			}
		}
		val3.MediaStreams = LinqExtensions.ToArray<MediaStream>((IEnumerable<MediaStream>)list, list.Count);
		val3.PlaylistName = val2.Name;
		if (val2.StreamClips != null && val2.StreamClips.Any())
		{
			val3.Files = val2.StreamClips.Select((TSStreamClip i) => i.StreamFile.Name).ToArray();
		}
		return val3;
	}

	private void AddVideoStream(List<MediaStream> streams, TSVideoStream videoStream)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Expected Obj, but got Unknown
		MediaStream val = new MediaStream
		{
			BitRate = Convert.ToInt32(((TSStream)videoStream).BitRate),
			Width = videoStream.Width,
			Height = videoStream.Height,
			Codec = ((TSStream)videoStream).CodecShortName,
			IsInterlaced = videoStream.IsInterlaced,
			Type = (MediaStreamType)1,
			Index = streams.Count
		};
		if (videoStream.FrameRateDenominator > 0)
		{
			float num = videoStream.FrameRateEnumerator;
			float num2 = videoStream.FrameRateDenominator;
			float? averageFrameRate = (val.RealFrameRate = num / num2);
			val.AverageFrameRate = averageFrameRate;
		}
		streams.Add(val);
	}

	private void AddAudioStream(List<MediaStream> streams, TSAudioStream audioStream)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Expected Obj, but got Unknown
		MediaStream val = new MediaStream
		{
			Codec = ((TSStream)audioStream).CodecShortName,
			Language = ((TSStream)audioStream).LanguageCode,
			Channels = audioStream.ChannelCount,
			SampleRate = audioStream.SampleRate,
			Type = (MediaStreamType)0,
			Index = streams.Count
		};
		int num = Convert.ToInt32(((TSStream)audioStream).BitRate);
		if (num > 0)
		{
			val.BitRate = num;
		}
		if (audioStream.LFE > 0)
		{
			val.Channels = audioStream.ChannelCount + 1;
		}
		streams.Add(val);
	}

	private void AddSubtitleStream(List<MediaStream> streams, TSTextStream textStream)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Expected Obj, but got Unknown
		streams.Add(new MediaStream
		{
			Language = ((TSStream)textStream).LanguageCode,
			Codec = ((TSStream)textStream).CodecShortName,
			Type = (MediaStreamType)2,
			Index = streams.Count
		});
	}

	private void AddSubtitleStream(List<MediaStream> streams, TSGraphicsStream textStream)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Expected Obj, but got Unknown
		streams.Add(new MediaStream
		{
			Language = ((TSStream)textStream).LanguageCode,
			Codec = ((TSStream)textStream).CodecShortName,
			Type = (MediaStreamType)2,
			Index = streams.Count
		});
	}
}
