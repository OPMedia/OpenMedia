using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Session;

namespace MediaBrowser.Model.Dlna;

public class StreamInfo
{
	public Guid ItemId { get; set; }

	public PlayMethod PlayMethod { get; set; }

	public EncodingContext Context { get; set; }

	public DlnaProfileType MediaType { get; set; }

	public string Container { get; set; }

	public string SubProtocol { get; set; }

	public long StartPositionTicks { get; set; }

	public int? SegmentLength { get; set; }

	public int? MinSegments { get; set; }

	public bool BreakOnNonKeyFrames { get; set; }

	public bool RequireAvc { get; set; }

	public bool RequireNonAnamorphic { get; set; }

	public bool CopyTimestamps { get; set; }

	public bool EnableMpegtsM2TsMode { get; set; }

	public bool EnableSubtitlesInManifest { get; set; }

	public string[] AudioCodecs { get; set; }

	public string[] VideoCodecs { get; set; }

	public int? AudioStreamIndex { get; set; }

	public int? SubtitleStreamIndex { get; set; }

	public int? TranscodingMaxAudioChannels { get; set; }

	public int? GlobalMaxAudioChannels { get; set; }

	public int? AudioBitrate { get; set; }

	public int? VideoBitrate { get; set; }

	public int? MaxWidth { get; set; }

	public int? MaxHeight { get; set; }

	public float? MaxFramerate { get; set; }

	public DeviceProfile DeviceProfile { get; set; }

	public string DeviceProfileId { get; set; }

	public string DeviceId { get; set; }

	public long? RunTimeTicks { get; set; }

	public TranscodeSeekInfo TranscodeSeekInfo { get; set; }

	public bool EstimateContentLength { get; set; }

	public MediaSourceInfo MediaSource { get; set; }

	public string[] SubtitleCodecs { get; set; }

	public SubtitleDeliveryMethod SubtitleDeliveryMethod { get; set; }

	public string SubtitleFormat { get; set; }

	public string PlaySessionId { get; set; }

	public TranscodeReason[] TranscodeReasons { get; set; }

	public Dictionary<string, string> StreamOptions { get; private set; }

	public string MediaSourceId
	{
		get
		{
			if (MediaSource != null)
			{
				return MediaSource.Id;
			}
			return null;
		}
	}

	public bool IsDirectStream
	{
		get
		{
			if (PlayMethod != PlayMethod.DirectStream)
			{
				return PlayMethod == PlayMethod.DirectPlay;
			}
			return true;
		}
	}

	public MediaStream TargetAudioStream
	{
		get
		{
			if (MediaSource != null)
			{
				return MediaSource.GetDefaultAudioStream(AudioStreamIndex);
			}
			return null;
		}
	}

	public MediaStream TargetVideoStream
	{
		get
		{
			if (MediaSource != null)
			{
				return MediaSource.VideoStream;
			}
			return null;
		}
	}

	public int? TargetAudioSampleRate => TargetAudioStream?.SampleRate;

	public int? TargetAudioBitDepth
	{
		get
		{
			if (IsDirectStream)
			{
				if (TargetAudioStream != null)
				{
					return TargetAudioStream.BitDepth;
				}
				return null;
			}
			string[] targetAudioCodec = TargetAudioCodec;
			string text = ((targetAudioCodec.Length == 0) ? null : targetAudioCodec[0]);
			if (!string.IsNullOrEmpty(text))
			{
				return GetTargetAudioBitDepth(text);
			}
			if (TargetAudioStream != null)
			{
				return TargetAudioStream.BitDepth;
			}
			return null;
		}
	}

	public int? TargetVideoBitDepth
	{
		get
		{
			if (IsDirectStream)
			{
				if (TargetVideoStream != null)
				{
					return TargetVideoStream.BitDepth;
				}
				return null;
			}
			string[] targetVideoCodec = TargetVideoCodec;
			string text = ((targetVideoCodec.Length == 0) ? null : targetVideoCodec[0]);
			if (!string.IsNullOrEmpty(text))
			{
				return GetTargetVideoBitDepth(text);
			}
			if (TargetVideoStream != null)
			{
				return TargetVideoStream.BitDepth;
			}
			return null;
		}
	}

	public int? TargetRefFrames
	{
		get
		{
			if (IsDirectStream)
			{
				if (TargetVideoStream != null)
				{
					return TargetVideoStream.RefFrames;
				}
				return null;
			}
			string[] targetVideoCodec = TargetVideoCodec;
			string text = ((targetVideoCodec.Length == 0) ? null : targetVideoCodec[0]);
			if (!string.IsNullOrEmpty(text))
			{
				return GetTargetRefFrames(text);
			}
			if (TargetVideoStream != null)
			{
				return TargetVideoStream.RefFrames;
			}
			return null;
		}
	}

	public float? TargetFramerate
	{
		get
		{
			MediaStream targetVideoStream = TargetVideoStream;
			if (!MaxFramerate.HasValue || IsDirectStream)
			{
				if (targetVideoStream != null)
				{
					return targetVideoStream.AverageFrameRate ?? targetVideoStream.RealFrameRate;
				}
				return null;
			}
			return MaxFramerate;
		}
	}

	public double? TargetVideoLevel
	{
		get
		{
			if (IsDirectStream)
			{
				if (TargetVideoStream != null)
				{
					return TargetVideoStream.Level;
				}
				return null;
			}
			string[] targetVideoCodec = TargetVideoCodec;
			string text = ((targetVideoCodec.Length == 0) ? null : targetVideoCodec[0]);
			if (!string.IsNullOrEmpty(text))
			{
				return GetTargetVideoLevel(text);
			}
			if (TargetVideoStream != null)
			{
				return TargetVideoStream.Level;
			}
			return null;
		}
	}

	public int? TargetPacketLength
	{
		get
		{
			MediaStream targetVideoStream = TargetVideoStream;
			if (IsDirectStream)
			{
				return targetVideoStream?.PacketLength;
			}
			return null;
		}
	}

	public string TargetVideoProfile
	{
		get
		{
			if (IsDirectStream)
			{
				if (TargetVideoStream != null)
				{
					return TargetVideoStream.Profile;
				}
				return null;
			}
			string[] targetVideoCodec = TargetVideoCodec;
			string text = ((targetVideoCodec.Length == 0) ? null : targetVideoCodec[0]);
			if (!string.IsNullOrEmpty(text))
			{
				return GetOption(text, "profile");
			}
			if (TargetVideoStream != null)
			{
				return TargetVideoStream.Profile;
			}
			return null;
		}
	}

	public string TargetVideoCodecTag
	{
		get
		{
			MediaStream targetVideoStream = TargetVideoStream;
			if (IsDirectStream)
			{
				return targetVideoStream?.CodecTag;
			}
			return null;
		}
	}

	public int? TargetAudioBitrate
	{
		get
		{
			MediaStream targetAudioStream = TargetAudioStream;
			if (!AudioBitrate.HasValue || IsDirectStream)
			{
				return targetAudioStream?.BitRate;
			}
			return AudioBitrate;
		}
	}

	public int? TargetAudioChannels
	{
		get
		{
			if (IsDirectStream)
			{
				if (TargetAudioStream != null)
				{
					return TargetAudioStream.Channels;
				}
				return null;
			}
			string[] targetAudioCodec = TargetAudioCodec;
			string text = ((targetAudioCodec.Length == 0) ? null : targetAudioCodec[0]);
			if (!string.IsNullOrEmpty(text))
			{
				return GetTargetRefFrames(text);
			}
			if (TargetAudioStream != null)
			{
				return TargetAudioStream.Channels;
			}
			return null;
		}
	}

	public string[] TargetAudioCodec
	{
		get
		{
			string text = TargetAudioStream?.Codec;
			if (IsDirectStream)
			{
				if (!string.IsNullOrEmpty(text))
				{
					return new string[1] { text };
				}
				return Array.Empty<string>();
			}
			string[] audioCodecs = AudioCodecs;
			foreach (string text2 in audioCodecs)
			{
				if (StringHelper.EqualsIgnoreCase(text2, text))
				{
					if (!string.IsNullOrEmpty(text2))
					{
						return new string[1] { text2 };
					}
					return Array.Empty<string>();
				}
			}
			return AudioCodecs;
		}
	}

	public string[] TargetVideoCodec
	{
		get
		{
			string text = TargetVideoStream?.Codec;
			if (IsDirectStream)
			{
				if (!string.IsNullOrEmpty(text))
				{
					return new string[1] { text };
				}
				return Array.Empty<string>();
			}
			string[] videoCodecs = VideoCodecs;
			foreach (string text2 in videoCodecs)
			{
				if (StringHelper.EqualsIgnoreCase(text2, text))
				{
					if (!string.IsNullOrEmpty(text2))
					{
						return new string[1] { text2 };
					}
					return Array.Empty<string>();
				}
			}
			return VideoCodecs;
		}
	}

	public long? TargetSize
	{
		get
		{
			if (IsDirectStream)
			{
				return MediaSource.Size;
			}
			if (RunTimeTicks.HasValue)
			{
				int? targetTotalBitrate = TargetTotalBitrate;
				double num = RunTimeTicks.Value;
				num /= 10000.0;
				num /= 1000.0;
				if (!targetTotalBitrate.HasValue)
				{
					return null;
				}
				return Convert.ToInt64((double)targetTotalBitrate.Value * num);
			}
			return null;
		}
	}

	public int? TargetVideoBitrate
	{
		get
		{
			MediaStream targetVideoStream = TargetVideoStream;
			if (!VideoBitrate.HasValue || IsDirectStream)
			{
				return targetVideoStream?.BitRate;
			}
			return VideoBitrate;
		}
	}

	public TransportStreamTimestamp TargetTimestamp
	{
		get
		{
			TransportStreamTimestamp result = (StringHelper.EqualsIgnoreCase(Container, "m2ts") ? TransportStreamTimestamp.Valid : TransportStreamTimestamp.None);
			if (IsDirectStream)
			{
				if (MediaSource != null)
				{
					return MediaSource.Timestamp ?? TransportStreamTimestamp.None;
				}
				return result;
			}
			return result;
		}
	}

	public int? TargetTotalBitrate => (TargetAudioBitrate ?? 0) + (TargetVideoBitrate ?? 0);

	public bool? IsTargetAnamorphic
	{
		get
		{
			if (IsDirectStream)
			{
				if (TargetVideoStream != null)
				{
					return TargetVideoStream.IsAnamorphic;
				}
				return null;
			}
			return false;
		}
	}

	public bool? IsTargetInterlaced
	{
		get
		{
			if (IsDirectStream)
			{
				if (TargetVideoStream != null)
				{
					return TargetVideoStream.IsInterlaced;
				}
				return null;
			}
			string[] targetVideoCodec = TargetVideoCodec;
			string text = ((targetVideoCodec.Length == 0) ? null : targetVideoCodec[0]);
			if (!string.IsNullOrEmpty(text) && string.Equals(GetOption(text, "deinterlace"), "true", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (TargetVideoStream != null)
			{
				return TargetVideoStream.IsInterlaced;
			}
			return null;
		}
	}

	public bool? IsTargetAVC
	{
		get
		{
			if (IsDirectStream)
			{
				if (TargetVideoStream != null)
				{
					return TargetVideoStream.IsAVC;
				}
				return null;
			}
			return true;
		}
	}

	public int? TargetWidth
	{
		get
		{
			MediaStream targetVideoStream = TargetVideoStream;
			if (targetVideoStream != null && targetVideoStream.Width.HasValue && targetVideoStream.Height.HasValue)
			{
				ImageSize size = new ImageSize
				{
					Width = targetVideoStream.Width.Value,
					Height = targetVideoStream.Height.Value
				};
				double? num = (MaxWidth.HasValue ? new double?(MaxWidth.Value) : ((double?)null));
				return Convert.ToInt32(DrawingUtils.Resize(maxHeight: (MaxHeight.HasValue ? new double?(MaxHeight.Value) : ((double?)null)) ?? 0.0, size: size, width: 0.0, height: 0.0, maxWidth: num ?? 0.0).Width);
			}
			return MaxWidth;
		}
	}

	public int? TargetHeight
	{
		get
		{
			MediaStream targetVideoStream = TargetVideoStream;
			if (targetVideoStream != null && targetVideoStream.Width.HasValue && targetVideoStream.Height.HasValue)
			{
				ImageSize size = new ImageSize
				{
					Width = targetVideoStream.Width.Value,
					Height = targetVideoStream.Height.Value
				};
				double? num = (MaxWidth.HasValue ? new double?(MaxWidth.Value) : ((double?)null));
				return Convert.ToInt32(DrawingUtils.Resize(maxHeight: (MaxHeight.HasValue ? new double?(MaxHeight.Value) : ((double?)null)) ?? 0.0, size: size, width: 0.0, height: 0.0, maxWidth: num ?? 0.0).Height);
			}
			return MaxHeight;
		}
	}

	public int? TargetVideoStreamCount
	{
		get
		{
			if (IsDirectStream)
			{
				return GetMediaStreamCount(MediaStreamType.Video, int.MaxValue);
			}
			return GetMediaStreamCount(MediaStreamType.Video, 1);
		}
	}

	public int? TargetAudioStreamCount
	{
		get
		{
			if (IsDirectStream)
			{
				return GetMediaStreamCount(MediaStreamType.Audio, int.MaxValue);
			}
			return GetMediaStreamCount(MediaStreamType.Audio, 1);
		}
	}

	public StreamInfo()
	{
		AudioCodecs = Array.Empty<string>();
		VideoCodecs = Array.Empty<string>();
		SubtitleCodecs = Array.Empty<string>();
		TranscodeReasons = Array.Empty<TranscodeReason>();
		StreamOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public void SetOption(string qualifier, string name, string value)
	{
		if (string.IsNullOrEmpty(qualifier))
		{
			SetOption(name, value);
		}
		else
		{
			SetOption(qualifier + "-" + name, value);
		}
	}

	public void SetOption(string name, string value)
	{
		StreamOptions[name] = value;
	}

	public string GetOption(string qualifier, string name)
	{
		string option = GetOption(qualifier + "-" + name);
		if (string.IsNullOrEmpty(option))
		{
			option = GetOption(name);
		}
		return option;
	}

	public string GetOption(string name)
	{
		if (StreamOptions.TryGetValue(name, out var value))
		{
			return value;
		}
		return null;
	}

	public string ToUrl(string baseUrl, string accessToken)
	{
		if (PlayMethod == PlayMethod.DirectPlay)
		{
			return MediaSource.Path;
		}
		List<string> list = new List<string>();
		foreach (NameValuePair item in BuildParams(this, accessToken))
		{
			if (!string.IsNullOrEmpty(item.Value) && (!StringHelper.EqualsIgnoreCase(item.Name, "StartTimeTicks") || !StringHelper.EqualsIgnoreCase(item.Value, "0")) && (!StringHelper.EqualsIgnoreCase(item.Name, "SubtitleStreamIndex") || !StringHelper.EqualsIgnoreCase(item.Value, "-1")) && (!StringHelper.EqualsIgnoreCase(item.Name, "Static") || !StringHelper.EqualsIgnoreCase(item.Value, "false")))
			{
				string arg = item.Value.Replace(" ", "%20");
				list.Add($"{item.Name}={arg}");
			}
		}
		string queryString = string.Join("&", list.ToArray(list.Count));
		return GetUrl(baseUrl, queryString);
	}

	private string GetUrl(string baseUrl, string queryString)
	{
		string text = (string.IsNullOrEmpty(Container) ? string.Empty : ("." + Container));
		baseUrl = baseUrl.TrimEnd(new char[1] { '/' });
		string text2 = ItemId.ToString("N");
		if (MediaType == DlnaProfileType.Audio)
		{
			if (StringHelper.EqualsIgnoreCase(SubProtocol, "hls"))
			{
				return $"{baseUrl}/audio/{text2}/master.m3u8?{queryString}";
			}
			return $"{baseUrl}/audio/{text2}/stream{text}?{queryString}";
		}
		if (StringHelper.EqualsIgnoreCase(SubProtocol, "hls"))
		{
			return $"{baseUrl}/videos/{text2}/master.m3u8?{queryString}";
		}
		return $"{baseUrl}/videos/{text2}/stream{text}?{queryString}";
	}

	private static List<NameValuePair> BuildParams(StreamInfo item, string accessToken)
	{
		List<NameValuePair> list = new List<NameValuePair>();
		string value = ((item.AudioCodecs.Length == 0) ? string.Empty : string.Join(",", item.AudioCodecs));
		string value2 = ((item.VideoCodecs.Length == 0) ? string.Empty : string.Join(",", item.VideoCodecs));
		list.Add(new NameValuePair("DeviceProfileId", item.DeviceProfileId ?? string.Empty));
		list.Add(new NameValuePair("DeviceId", item.DeviceId ?? string.Empty));
		list.Add(new NameValuePair("MediaSourceId", item.MediaSourceId ?? string.Empty));
		list.Add(new NameValuePair("Static", item.IsDirectStream.ToString().ToLower()));
		list.Add(new NameValuePair("VideoCodec", value2));
		list.Add(new NameValuePair("AudioCodec", value));
		list.Add(new NameValuePair("AudioStreamIndex", item.AudioStreamIndex.HasValue ? item.AudioStreamIndex.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
		list.Add(new NameValuePair("SubtitleStreamIndex", (item.SubtitleStreamIndex.HasValue && item.SubtitleDeliveryMethod != SubtitleDeliveryMethod.External) ? item.SubtitleStreamIndex.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
		list.Add(new NameValuePair("VideoBitrate", item.VideoBitrate.HasValue ? item.VideoBitrate.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
		list.Add(new NameValuePair("AudioBitrate", item.AudioBitrate.HasValue ? item.AudioBitrate.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
		list.Add(new NameValuePair("MaxFramerate", item.MaxFramerate.HasValue ? item.MaxFramerate.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
		list.Add(new NameValuePair("MaxWidth", item.MaxWidth.HasValue ? item.MaxWidth.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
		list.Add(new NameValuePair("MaxHeight", item.MaxHeight.HasValue ? item.MaxHeight.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
		long startPositionTicks = item.StartPositionTicks;
		bool flag = StringHelper.EqualsIgnoreCase(item.SubProtocol, "hls");
		if (flag)
		{
			list.Add(new NameValuePair("StartTimeTicks", string.Empty));
		}
		else
		{
			list.Add(new NameValuePair("StartTimeTicks", startPositionTicks.ToString(CultureInfo.InvariantCulture)));
		}
		list.Add(new NameValuePair("PlaySessionId", item.PlaySessionId ?? string.Empty));
		list.Add(new NameValuePair("api_key", accessToken ?? string.Empty));
		string text = ((item.MediaSource == null) ? null : item.MediaSource.LiveStreamId);
		list.Add(new NameValuePair("LiveStreamId", text ?? string.Empty));
		list.Add(new NameValuePair("SubtitleMethod", (item.SubtitleStreamIndex.HasValue && item.SubtitleDeliveryMethod != SubtitleDeliveryMethod.External) ? item.SubtitleDeliveryMethod.ToString() : string.Empty));
		if (!item.IsDirectStream)
		{
			if (item.RequireNonAnamorphic)
			{
				list.Add(new NameValuePair("RequireNonAnamorphic", item.RequireNonAnamorphic.ToString().ToLower()));
			}
			list.Add(new NameValuePair("TranscodingMaxAudioChannels", item.TranscodingMaxAudioChannels.HasValue ? item.TranscodingMaxAudioChannels.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
			if (item.EnableSubtitlesInManifest)
			{
				list.Add(new NameValuePair("EnableSubtitlesInManifest", item.EnableSubtitlesInManifest.ToString().ToLower()));
			}
			if (item.EnableMpegtsM2TsMode)
			{
				list.Add(new NameValuePair("EnableMpegtsM2TsMode", item.EnableMpegtsM2TsMode.ToString().ToLower()));
			}
			if (item.EstimateContentLength)
			{
				list.Add(new NameValuePair("EstimateContentLength", item.EstimateContentLength.ToString().ToLower()));
			}
			if (item.TranscodeSeekInfo != TranscodeSeekInfo.Auto)
			{
				list.Add(new NameValuePair("TranscodeSeekInfo", item.TranscodeSeekInfo.ToString().ToLower()));
			}
			if (item.CopyTimestamps)
			{
				list.Add(new NameValuePair("CopyTimestamps", item.CopyTimestamps.ToString().ToLower()));
			}
			list.Add(new NameValuePair("RequireAvc", item.RequireAvc.ToString().ToLower()));
		}
		list.Add(new NameValuePair("Tag", item.MediaSource.ETag ?? string.Empty));
		string text2 = ((item.SubtitleCodecs.Length == 0) ? string.Empty : string.Join(",", item.SubtitleCodecs));
		list.Add(new NameValuePair("SubtitleCodec", (item.SubtitleStreamIndex.HasValue && item.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Embed) ? text2 : string.Empty));
		if (flag)
		{
			list.Add(new NameValuePair("SegmentContainer", item.Container ?? string.Empty));
			if (item.SegmentLength.HasValue)
			{
				list.Add(new NameValuePair("SegmentLength", item.SegmentLength.Value.ToString(CultureInfo.InvariantCulture)));
			}
			if (item.MinSegments.HasValue)
			{
				list.Add(new NameValuePair("MinSegments", item.MinSegments.Value.ToString(CultureInfo.InvariantCulture)));
			}
			list.Add(new NameValuePair("BreakOnNonKeyFrames", item.BreakOnNonKeyFrames.ToString()));
		}
		foreach (KeyValuePair<string, string> streamOption in item.StreamOptions)
		{
			if (!string.IsNullOrEmpty(streamOption.Value))
			{
				list.Add(new NameValuePair(streamOption.Key, streamOption.Value.Replace(" ", "")));
			}
		}
		if (!item.IsDirectStream)
		{
			list.Add(new NameValuePair("TranscodeReasons", string.Join(",", (from i in item.TranscodeReasons.Distinct()
				select i.ToString()).ToArray())));
		}
		return list;
	}

	public List<SubtitleStreamInfo> GetExternalSubtitles(ITranscoderSupport transcoderSupport, bool includeSelectedTrackOnly, string baseUrl, string accessToken)
	{
		return GetExternalSubtitles(transcoderSupport, includeSelectedTrackOnly, enableAllProfiles: false, baseUrl, accessToken);
	}

	public List<SubtitleStreamInfo> GetExternalSubtitles(ITranscoderSupport transcoderSupport, bool includeSelectedTrackOnly, bool enableAllProfiles, string baseUrl, string accessToken)
	{
		List<SubtitleStreamInfo> subtitleProfiles = GetSubtitleProfiles(transcoderSupport, includeSelectedTrackOnly, enableAllProfiles, baseUrl, accessToken);
		List<SubtitleStreamInfo> list = new List<SubtitleStreamInfo>();
		foreach (SubtitleStreamInfo item in subtitleProfiles)
		{
			if (item.DeliveryMethod == SubtitleDeliveryMethod.External)
			{
				list.Add(item);
			}
		}
		return list;
	}

	public List<SubtitleStreamInfo> GetSubtitleProfiles(ITranscoderSupport transcoderSupport, bool includeSelectedTrackOnly, string baseUrl, string accessToken)
	{
		return GetSubtitleProfiles(transcoderSupport, includeSelectedTrackOnly, enableAllProfiles: false, baseUrl, accessToken);
	}

	public List<SubtitleStreamInfo> GetSubtitleProfiles(ITranscoderSupport transcoderSupport, bool includeSelectedTrackOnly, bool enableAllProfiles, string baseUrl, string accessToken)
	{
		List<SubtitleStreamInfo> list = new List<SubtitleStreamInfo>();
		long startPositionTicks;
		if (StringHelper.EqualsIgnoreCase(SubProtocol, "hls"))
		{
			startPositionTicks = 0L;
		}
		else
		{
			startPositionTicks = ((PlayMethod == PlayMethod.Transcode && !CopyTimestamps) ? StartPositionTicks : 0);
		}
		if (SubtitleStreamIndex.HasValue)
		{
			foreach (MediaStream mediaStream in MediaSource.MediaStreams)
			{
				if (mediaStream.Type == MediaStreamType.Subtitle && mediaStream.Index == SubtitleStreamIndex.Value)
				{
					AddSubtitleProfiles(list, mediaStream, transcoderSupport, enableAllProfiles, baseUrl, accessToken, startPositionTicks);
				}
			}
		}
		if (!includeSelectedTrackOnly)
		{
			foreach (MediaStream mediaStream2 in MediaSource.MediaStreams)
			{
				if (mediaStream2.Type == MediaStreamType.Subtitle && (!SubtitleStreamIndex.HasValue || mediaStream2.Index != SubtitleStreamIndex.Value))
				{
					AddSubtitleProfiles(list, mediaStream2, transcoderSupport, enableAllProfiles, baseUrl, accessToken, startPositionTicks);
				}
			}
		}
		return list;
	}

	private void AddSubtitleProfiles(List<SubtitleStreamInfo> list, MediaStream stream, ITranscoderSupport transcoderSupport, bool enableAllProfiles, string baseUrl, string accessToken, long startPositionTicks)
	{
		if (enableAllProfiles)
		{
			SubtitleProfile[] subtitleProfiles = DeviceProfile.SubtitleProfiles;
			foreach (SubtitleProfile subtitleProfile in subtitleProfiles)
			{
				SubtitleStreamInfo subtitleStreamInfo = GetSubtitleStreamInfo(stream, baseUrl, accessToken, startPositionTicks, new SubtitleProfile[1] { subtitleProfile }, transcoderSupport);
				list.Add(subtitleStreamInfo);
			}
		}
		else
		{
			SubtitleStreamInfo subtitleStreamInfo2 = GetSubtitleStreamInfo(stream, baseUrl, accessToken, startPositionTicks, DeviceProfile.SubtitleProfiles, transcoderSupport);
			list.Add(subtitleStreamInfo2);
		}
	}

	private SubtitleStreamInfo GetSubtitleStreamInfo(MediaStream stream, string baseUrl, string accessToken, long startPositionTicks, SubtitleProfile[] subtitleProfiles, ITranscoderSupport transcoderSupport)
	{
		SubtitleProfile subtitleProfile = StreamBuilder.GetSubtitleProfile(MediaSource, stream, subtitleProfiles, PlayMethod, transcoderSupport, Container, SubProtocol);
		SubtitleStreamInfo subtitleStreamInfo = new SubtitleStreamInfo
		{
			IsForced = stream.IsForced,
			Language = stream.Language,
			Name = (stream.Language ?? "Unknown"),
			Format = subtitleProfile.Format,
			Index = stream.Index,
			DeliveryMethod = subtitleProfile.Method,
			DisplayTitle = stream.DisplayTitle
		};
		if (subtitleStreamInfo.DeliveryMethod == SubtitleDeliveryMethod.External)
		{
			if (MediaSource.Protocol == MediaProtocol.File || !StringHelper.EqualsIgnoreCase(stream.Codec, subtitleProfile.Format) || !stream.IsExternal)
			{
				subtitleStreamInfo.Url = string.Format("{0}/Videos/{1}/{2}/Subtitles/{3}/{4}/Stream.{5}", baseUrl, ItemId.ToString("N"), MediaSourceId, stream.Index.ToString(CultureInfo.InvariantCulture), startPositionTicks.ToString(CultureInfo.InvariantCulture), subtitleProfile.Format);
				if (!string.IsNullOrEmpty(accessToken))
				{
					subtitleStreamInfo.Url = subtitleStreamInfo.Url + "?api_key=" + accessToken;
				}
				subtitleStreamInfo.IsExternalUrl = false;
			}
			else
			{
				subtitleStreamInfo.Url = stream.Path;
				subtitleStreamInfo.IsExternalUrl = true;
			}
		}
		return subtitleStreamInfo;
	}

	public int? GetTargetVideoBitDepth(string codec)
	{
		string option = GetOption(codec, "videobitdepth");
		if (string.IsNullOrEmpty(option))
		{
			return null;
		}
		if (int.TryParse(option, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		return null;
	}

	public int? GetTargetAudioBitDepth(string codec)
	{
		string option = GetOption(codec, "audiobitdepth");
		if (string.IsNullOrEmpty(option))
		{
			return null;
		}
		if (int.TryParse(option, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		return null;
	}

	public double? GetTargetVideoLevel(string codec)
	{
		string option = GetOption(codec, "level");
		if (string.IsNullOrEmpty(option))
		{
			return null;
		}
		if (double.TryParse(option, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		return null;
	}

	public int? GetTargetRefFrames(string codec)
	{
		string option = GetOption(codec, "maxrefframes");
		if (string.IsNullOrEmpty(option))
		{
			return null;
		}
		if (int.TryParse(option, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		return null;
	}

	public int? GetTargetAudioChannels(string codec)
	{
		int? globalMaxAudioChannels = GlobalMaxAudioChannels;
		string option = GetOption(codec, "audiochannels");
		if (string.IsNullOrEmpty(option))
		{
			return globalMaxAudioChannels;
		}
		if (int.TryParse(option, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return Math.Min(result, globalMaxAudioChannels ?? result);
		}
		return globalMaxAudioChannels;
	}

	private int? GetMediaStreamCount(MediaStreamType type, int limit)
	{
		int? result = MediaSource.GetStreamCount(type);
		if (result.HasValue)
		{
			result = Math.Min(result.Value, limit);
		}
		return result;
	}

	public List<MediaStream> GetSelectableAudioStreams()
	{
		return GetSelectableStreams(MediaStreamType.Audio);
	}

	public List<MediaStream> GetSelectableSubtitleStreams()
	{
		return GetSelectableStreams(MediaStreamType.Subtitle);
	}

	public List<MediaStream> GetSelectableStreams(MediaStreamType type)
	{
		List<MediaStream> list = new List<MediaStream>();
		foreach (MediaStream mediaStream in MediaSource.MediaStreams)
		{
			if (type == mediaStream.Type)
			{
				list.Add(mediaStream);
			}
		}
		return list;
	}
}
