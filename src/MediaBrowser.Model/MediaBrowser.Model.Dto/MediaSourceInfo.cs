using System;
using System.Collections.Generic;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Session;

namespace MediaBrowser.Model.Dto;

public class MediaSourceInfo
{
	public MediaProtocol Protocol { get; set; }

	public string Id { get; set; }

	public string Path { get; set; }

	public string EncoderPath { get; set; }

	public MediaProtocol? EncoderProtocol { get; set; }

	public MediaSourceType Type { get; set; }

	public string Container { get; set; }

	public long? Size { get; set; }

	public string Name { get; set; }

	public bool IsRemote { get; set; }

	public string ETag { get; set; }

	public long? RunTimeTicks { get; set; }

	public bool ReadAtNativeFramerate { get; set; }

	public bool IgnoreDts { get; set; }

	public bool IgnoreIndex { get; set; }

	public bool GenPtsInput { get; set; }

	public bool SupportsTranscoding { get; set; }

	public bool SupportsDirectStream { get; set; }

	public bool SupportsDirectPlay { get; set; }

	public bool IsInfiniteStream { get; set; }

	public bool RequiresOpening { get; set; }

	public string OpenToken { get; set; }

	public bool RequiresClosing { get; set; }

	public string LiveStreamId { get; set; }

	public int? BufferMs { get; set; }

	public bool RequiresLooping { get; set; }

	public bool SupportsProbing { get; set; }

	public VideoType? VideoType { get; set; }

	public IsoType? IsoType { get; set; }

	public Video3DFormat? Video3DFormat { get; set; }

	public List<MediaStream> MediaStreams { get; set; }

	public string[] Formats { get; set; }

	public int? Bitrate { get; set; }

	public TransportStreamTimestamp? Timestamp { get; set; }

	public Dictionary<string, string> RequiredHttpHeaders { get; set; }

	public string TranscodingUrl { get; set; }

	public string TranscodingSubProtocol { get; set; }

	public string TranscodingContainer { get; set; }

	public int? AnalyzeDurationMs { get; set; }

	[IgnoreDataMember]
	public TranscodeReason[] TranscodeReasons { get; set; }

	public int? DefaultAudioStreamIndex { get; set; }

	public int? DefaultSubtitleStreamIndex { get; set; }

	[IgnoreDataMember]
	public MediaStream DefaultAudioStream => GetDefaultAudioStream(DefaultAudioStreamIndex);

	[IgnoreDataMember]
	public MediaStream VideoStream
	{
		get
		{
			foreach (MediaStream mediaStream in MediaStreams)
			{
				if (mediaStream.Type == MediaStreamType.Video)
				{
					return mediaStream;
				}
			}
			return null;
		}
	}

	public MediaSourceInfo()
	{
		Formats = Array.Empty<string>();
		MediaStreams = new List<MediaStream>();
		RequiredHttpHeaders = new Dictionary<string, string>();
		SupportsTranscoding = true;
		SupportsDirectStream = true;
		SupportsDirectPlay = true;
		SupportsProbing = true;
	}

	public void InferTotalBitrate(bool force = false)
	{
		if (MediaStreams == null || (!force && Bitrate.HasValue))
		{
			return;
		}
		int num = 0;
		foreach (MediaStream mediaStream in MediaStreams)
		{
			if (!mediaStream.IsExternal)
			{
				num += mediaStream.BitRate ?? 0;
			}
		}
		if (num > 0)
		{
			Bitrate = num;
		}
	}

	public MediaStream GetDefaultAudioStream(int? defaultIndex)
	{
		if (defaultIndex.HasValue)
		{
			int value = defaultIndex.Value;
			foreach (MediaStream mediaStream in MediaStreams)
			{
				if (mediaStream.Type == MediaStreamType.Audio && mediaStream.Index == value)
				{
					return mediaStream;
				}
			}
		}
		foreach (MediaStream mediaStream2 in MediaStreams)
		{
			if (mediaStream2.Type == MediaStreamType.Audio && mediaStream2.IsDefault)
			{
				return mediaStream2;
			}
		}
		foreach (MediaStream mediaStream3 in MediaStreams)
		{
			if (mediaStream3.Type == MediaStreamType.Audio)
			{
				return mediaStream3;
			}
		}
		return null;
	}

	public MediaStream GetMediaStream(MediaStreamType type, int index)
	{
		foreach (MediaStream mediaStream in MediaStreams)
		{
			if (mediaStream.Type == type && mediaStream.Index == index)
			{
				return mediaStream;
			}
		}
		return null;
	}

	public int? GetStreamCount(MediaStreamType type)
	{
		int num = 0;
		int num2 = 0;
		foreach (MediaStream mediaStream in MediaStreams)
		{
			num2++;
			if (mediaStream.Type == type)
			{
				num++;
			}
		}
		if (num2 == 0)
		{
			return null;
		}
		return num;
	}

	public bool? IsSecondaryAudio(MediaStream stream)
	{
		List<MediaStream> list = new List<MediaStream>();
		foreach (MediaStream mediaStream in MediaStreams)
		{
			if (mediaStream.Type == MediaStreamType.Audio)
			{
				list.Add(mediaStream);
			}
		}
		if (list.Count < 2)
		{
			return false;
		}
		foreach (MediaStream item in list)
		{
			if (item.IsDefault)
			{
				if (item.Index != stream.Index)
				{
					return true;
				}
				break;
			}
		}
		using (List<MediaStream>.Enumerator enumerator = list.GetEnumerator())
		{
			if (enumerator.MoveNext())
			{
				return enumerator.Current.Index != stream.Index;
			}
		}
		return null;
	}
}
