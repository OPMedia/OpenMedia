using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Session;

namespace MediaBrowser.Model.Dlna;

public class StreamBuilder
{
	private readonly ILogger _logger;

	private readonly ITranscoderSupport _transcoderSupport;

	public StreamBuilder(ITranscoderSupport transcoderSupport, ILogger logger)
	{
		_transcoderSupport = transcoderSupport;
		_logger = logger;
	}

	public StreamBuilder(ILogger logger)
		: this(new FullTranscoderSupport(), logger)
	{
	}

	public StreamInfo BuildAudioItem(AudioOptions options)
	{
		ValidateAudioInput(options);
		List<MediaSourceInfo> list = new List<MediaSourceInfo>();
		MediaSourceInfo[] mediaSources = options.MediaSources;
		foreach (MediaSourceInfo mediaSourceInfo in mediaSources)
		{
			if (string.IsNullOrEmpty(options.MediaSourceId) || StringHelper.EqualsIgnoreCase(mediaSourceInfo.Id, options.MediaSourceId))
			{
				list.Add(mediaSourceInfo);
			}
		}
		List<StreamInfo> list2 = new List<StreamInfo>();
		foreach (MediaSourceInfo item in list)
		{
			StreamInfo streamInfo = BuildAudioItem(item, options);
			if (streamInfo != null)
			{
				list2.Add(streamInfo);
			}
		}
		foreach (StreamInfo item2 in list2)
		{
			item2.DeviceId = options.DeviceId;
			item2.DeviceProfileId = options.Profile.Id;
		}
		return GetOptimalStream(list2, options.GetMaxBitrate(isAudio: true) ?? 0);
	}

	public StreamInfo BuildVideoItem(VideoOptions options)
	{
		ValidateInput(options);
		List<MediaSourceInfo> list = new List<MediaSourceInfo>();
		MediaSourceInfo[] mediaSources = options.MediaSources;
		foreach (MediaSourceInfo mediaSourceInfo in mediaSources)
		{
			if (string.IsNullOrEmpty(options.MediaSourceId) || StringHelper.EqualsIgnoreCase(mediaSourceInfo.Id, options.MediaSourceId))
			{
				list.Add(mediaSourceInfo);
			}
		}
		List<StreamInfo> list2 = new List<StreamInfo>();
		foreach (MediaSourceInfo item in list)
		{
			StreamInfo streamInfo = BuildVideoItem(item, options);
			if (streamInfo != null)
			{
				list2.Add(streamInfo);
			}
		}
		foreach (StreamInfo item2 in list2)
		{
			item2.DeviceId = options.DeviceId;
			item2.DeviceProfileId = options.Profile.Id;
		}
		return GetOptimalStream(list2, options.GetMaxBitrate(isAudio: false) ?? 0);
	}

	private StreamInfo GetOptimalStream(List<StreamInfo> streams, long maxBitrate)
	{
		StreamInfo[] array = SortMediaSources(streams, maxBitrate);
		int num = 0;
		if (num < array.Length)
		{
			return array[num];
		}
		return null;
	}

	private StreamInfo[] SortMediaSources(List<StreamInfo> streams, long maxBitrate)
	{
		return streams.OrderBy((StreamInfo i) => (i.PlayMethod != PlayMethod.DirectPlay || i.MediaSource.Protocol != MediaProtocol.File) ? 1 : 0).ThenBy((StreamInfo i) =>
		{
			PlayMethod playMethod = i.PlayMethod;
			return ((uint)(playMethod - 1) > 1u) ? 1 : 0;
		}).ThenBy((StreamInfo i) => (i.MediaSource.Protocol != MediaProtocol.File) ? 1 : 0)
			.ThenBy((StreamInfo i) => (maxBitrate > 0 && i.MediaSource.Bitrate.HasValue) ? Math.Abs(i.MediaSource.Bitrate.Value - maxBitrate) : 0)
			.ThenBy(streams.IndexOf)
			.ToArray();
	}

	private TranscodeReason? GetTranscodeReasonForFailedCondition(ProfileCondition condition)
	{
		switch (condition.Property)
		{
		case ProfileConditionValue.AudioBitrate:
			_ = condition.Condition;
			_ = 2;
			return TranscodeReason.AudioBitrateNotSupported;
		case ProfileConditionValue.AudioChannels:
			_ = condition.Condition;
			_ = 2;
			return TranscodeReason.AudioChannelsNotSupported;
		case ProfileConditionValue.AudioProfile:
			return TranscodeReason.AudioProfileNotSupported;
		case ProfileConditionValue.AudioSampleRate:
			return TranscodeReason.AudioSampleRateNotSupported;
		case ProfileConditionValue.Has64BitOffsets:
			return null;
		case ProfileConditionValue.Height:
			return TranscodeReason.VideoResolutionNotSupported;
		case ProfileConditionValue.IsAnamorphic:
			return TranscodeReason.AnamorphicVideoNotSupported;
		case ProfileConditionValue.IsAvc:
			return null;
		case ProfileConditionValue.IsInterlaced:
			return TranscodeReason.InterlacedVideoNotSupported;
		case ProfileConditionValue.IsSecondaryAudio:
			return TranscodeReason.SecondaryAudioNotSupported;
		case ProfileConditionValue.NumAudioStreams:
			return null;
		case ProfileConditionValue.NumVideoStreams:
			return null;
		case ProfileConditionValue.PacketLength:
			return null;
		case ProfileConditionValue.RefFrames:
			return TranscodeReason.RefFramesNotSupported;
		case ProfileConditionValue.VideoBitDepth:
			return TranscodeReason.VideoBitDepthNotSupported;
		case ProfileConditionValue.AudioBitDepth:
			return TranscodeReason.AudioBitDepthNotSupported;
		case ProfileConditionValue.VideoBitrate:
			return TranscodeReason.VideoBitrateNotSupported;
		case ProfileConditionValue.VideoCodecTag:
			return TranscodeReason.VideoCodecNotSupported;
		case ProfileConditionValue.VideoFramerate:
			return TranscodeReason.VideoFramerateNotSupported;
		case ProfileConditionValue.VideoLevel:
			return TranscodeReason.VideoLevelNotSupported;
		case ProfileConditionValue.VideoProfile:
			return TranscodeReason.VideoProfileNotSupported;
		case ProfileConditionValue.VideoTimestamp:
			return null;
		case ProfileConditionValue.Width:
			return TranscodeReason.VideoResolutionNotSupported;
		default:
			return null;
		}
	}

	public static string NormalizeMediaSourceFormatIntoSingleContainer(string inputContainer, string inputPath, DeviceProfile profile, DlnaProfileType type)
	{
		if (string.IsNullOrEmpty(inputContainer))
		{
			return null;
		}
		string[] array = ContainerProfile.SplitValue(inputContainer);
		if (array.Length == 1)
		{
			return array[0];
		}
		if (profile != null)
		{
			string extension = Path.GetExtension(inputPath);
			if (!string.IsNullOrEmpty(extension))
			{
				extension = extension.TrimStart(new char[1] { '.' });
				if (!string.IsNullOrEmpty(extension) && array.Contains(extension, StringComparer.OrdinalIgnoreCase))
				{
					List<string> list = array.ToList();
					list.Insert(0, extension);
					array = list.ToArray();
				}
			}
			string[] array2 = array;
			foreach (string text in array2)
			{
				DirectPlayProfile[] directPlayProfiles = profile.DirectPlayProfiles;
				foreach (DirectPlayProfile directPlayProfile in directPlayProfiles)
				{
					if (directPlayProfile.Type == type && directPlayProfile.SupportsContainer(text))
					{
						return text;
					}
				}
			}
		}
		return array[0];
	}

	private StreamInfo BuildAudioItem(MediaSourceInfo item, AudioOptions options)
	{
		List<TranscodeReason> list = new List<TranscodeReason>();
		StreamInfo streamInfo = new StreamInfo
		{
			ItemId = options.ItemId,
			MediaType = DlnaProfileType.Audio,
			MediaSource = item,
			RunTimeTicks = item.RunTimeTicks,
			Context = options.Context,
			DeviceProfile = options.Profile
		};
		if (options.ForceDirectPlay)
		{
			streamInfo.PlayMethod = PlayMethod.DirectPlay;
			streamInfo.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, item.Path, options.Profile, DlnaProfileType.Audio);
			return streamInfo;
		}
		if (options.ForceDirectStream)
		{
			streamInfo.PlayMethod = PlayMethod.DirectStream;
			streamInfo.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, item.Path, options.Profile, DlnaProfileType.Audio);
			return streamInfo;
		}
		MediaStream defaultAudioStream = item.GetDefaultAudioStream(null);
		Tuple<List<PlayMethod>, List<TranscodeReason>> audioDirectPlayMethods = GetAudioDirectPlayMethods(item, defaultAudioStream, options);
		List<PlayMethod> item2 = audioDirectPlayMethods.Item1;
		list.AddRange(audioDirectPlayMethods.Item2);
		ConditionProcessor conditionProcessor = new ConditionProcessor();
		int? audioChannels = defaultAudioStream?.Channels;
		int? audioBitrate = defaultAudioStream?.BitDepth;
		int? audioSampleRate = defaultAudioStream?.SampleRate;
		int? audioBitDepth = defaultAudioStream?.BitDepth;
		if (item2.Count > 0)
		{
			string codec = defaultAudioStream?.Codec;
			List<ProfileCondition> list2 = new List<ProfileCondition>();
			CodecProfile[] codecProfiles = options.Profile.CodecProfiles;
			foreach (CodecProfile codecProfile in codecProfiles)
			{
				if (codecProfile.Type != CodecType.Audio || !codecProfile.ContainsAnyCodec(codec, item.Container))
				{
					continue;
				}
				bool flag = true;
				ProfileCondition[] applyConditions = codecProfile.ApplyConditions;
				foreach (ProfileCondition condition in applyConditions)
				{
					if (!conditionProcessor.IsAudioConditionSatisfied(condition, audioChannels, audioBitrate, audioSampleRate, audioBitDepth))
					{
						LogConditionFailure(options.Profile, "AudioCodecProfile", condition, item);
						flag = false;
						break;
					}
				}
				if (flag)
				{
					applyConditions = codecProfile.Conditions;
					foreach (ProfileCondition item3 in applyConditions)
					{
						list2.Add(item3);
					}
				}
			}
			bool flag2 = true;
			foreach (ProfileCondition item5 in list2)
			{
				if (!conditionProcessor.IsAudioConditionSatisfied(item5, audioChannels, audioBitrate, audioSampleRate, audioBitDepth))
				{
					LogConditionFailure(options.Profile, "AudioCodecProfile", item5, item);
					TranscodeReason? transcodeReasonForFailedCondition = GetTranscodeReasonForFailedCondition(item5);
					if (transcodeReasonForFailedCondition.HasValue)
					{
						list.Add(transcodeReasonForFailedCondition.Value);
					}
					flag2 = false;
					break;
				}
			}
			if (flag2)
			{
				if (item2.Contains(PlayMethod.DirectStream))
				{
					streamInfo.PlayMethod = PlayMethod.DirectStream;
				}
				streamInfo.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, item.Path, options.Profile, DlnaProfileType.Audio);
				return streamInfo;
			}
		}
		TranscodingProfile transcodingProfile = null;
		TranscodingProfile[] transcodingProfiles = options.Profile.TranscodingProfiles;
		foreach (TranscodingProfile transcodingProfile2 in transcodingProfiles)
		{
			if (transcodingProfile2.Type == streamInfo.MediaType && transcodingProfile2.Context == options.Context && _transcoderSupport.CanEncodeToAudioCodec(transcodingProfile2.AudioCodec ?? transcodingProfile2.Container))
			{
				transcodingProfile = transcodingProfile2;
				break;
			}
		}
		if (transcodingProfile != null)
		{
			if (!item.SupportsTranscoding)
			{
				return null;
			}
			SetStreamInfoOptionsFromTranscodingProfile(streamInfo, transcodingProfile);
			List<CodecProfile> list3 = new List<CodecProfile>();
			CodecProfile[] codecProfiles = options.Profile.CodecProfiles;
			foreach (CodecProfile codecProfile2 in codecProfiles)
			{
				if (codecProfile2.Type == CodecType.Audio && codecProfile2.ContainsAnyCodec(transcodingProfile.AudioCodec, transcodingProfile.Container))
				{
					list3.Add(codecProfile2);
				}
				if (list3.Count >= 1)
				{
					break;
				}
			}
			List<ProfileCondition> list4 = new List<ProfileCondition>();
			foreach (CodecProfile item6 in list3)
			{
				bool flag3 = true;
				ProfileCondition[] applyConditions = item6.ApplyConditions;
				foreach (ProfileCondition condition2 in applyConditions)
				{
					if (!conditionProcessor.IsAudioConditionSatisfied(condition2, audioChannels, audioBitrate, audioSampleRate, audioBitDepth))
					{
						LogConditionFailure(options.Profile, "AudioCodecProfile", condition2, item);
						flag3 = false;
						break;
					}
				}
				if (flag3)
				{
					applyConditions = item6.Conditions;
					foreach (ProfileCondition item4 in applyConditions)
					{
						list4.Add(item4);
					}
				}
			}
			ApplyTranscodingConditions(streamInfo, list4, null, enableQualifiedConditions: true, enableNonQualifiedConditions: true);
			streamInfo.GlobalMaxAudioChannels = options.MaxAudioChannels;
			long? maxBitrate = options.GetMaxBitrate(isAudio: true);
			long num = ((long?)options.AudioTranscodingBitrate) ?? ((long?)((options.Context == EncodingContext.Streaming) ? options.Profile.MusicStreamingTranscodingBitrate : ((int?)null))) ?? maxBitrate ?? 128000;
			if (maxBitrate.HasValue)
			{
				num = Math.Min(maxBitrate.Value, num);
			}
			long num2 = Math.Min(num, ((long?)streamInfo.AudioBitrate) ?? num);
			streamInfo.AudioBitrate = ((num2 > int.MaxValue) ? int.MaxValue : Convert.ToInt32(num2));
		}
		streamInfo.TranscodeReasons = list.ToArray();
		return streamInfo;
	}

	private long? GetBitrateForDirectPlayCheck(MediaSourceInfo item, AudioOptions options, bool isAudio)
	{
		if (item.Protocol == MediaProtocol.File)
		{
			if (isAudio && options.Profile.MaxStaticMusicBitrate.HasValue)
			{
				return options.Profile.MaxStaticMusicBitrate;
			}
			return options.Profile.MaxStaticBitrate;
		}
		return options.GetMaxBitrate(isAudio);
	}

	private Tuple<List<PlayMethod>, List<TranscodeReason>> GetAudioDirectPlayMethods(MediaSourceInfo item, MediaStream audioStream, AudioOptions options)
	{
		List<TranscodeReason> list = new List<TranscodeReason>();
		DirectPlayProfile directPlayProfile = null;
		DirectPlayProfile[] directPlayProfiles = options.Profile.DirectPlayProfiles;
		foreach (DirectPlayProfile directPlayProfile2 in directPlayProfiles)
		{
			if (directPlayProfile2.Type == DlnaProfileType.Audio && IsAudioDirectPlaySupported(directPlayProfile2, item, audioStream))
			{
				directPlayProfile = directPlayProfile2;
				break;
			}
		}
		List<PlayMethod> list2 = new List<PlayMethod>();
		if (directPlayProfile != null)
		{
			if (item.SupportsDirectStream)
			{
				if (IsAudioEligibleForDirectPlay(item, options.GetMaxBitrate(isAudio: true) ?? 0, PlayMethod.DirectStream))
				{
					if (options.EnableDirectStream)
					{
						list2.Add(PlayMethod.DirectStream);
					}
				}
				else
				{
					list.Add(TranscodeReason.ContainerBitrateExceedsLimit);
				}
			}
			if (item.SupportsDirectPlay)
			{
				if (IsAudioEligibleForDirectPlay(item, GetBitrateForDirectPlayCheck(item, options, isAudio: true) ?? 0, PlayMethod.DirectPlay))
				{
					if (options.EnableDirectPlay)
					{
						list2.Add(PlayMethod.DirectPlay);
					}
				}
				else
				{
					list.Add(TranscodeReason.ContainerBitrateExceedsLimit);
				}
			}
		}
		else
		{
			list.InsertRange(0, GetTranscodeReasonsFromDirectPlayProfile(item, null, audioStream, options.Profile.DirectPlayProfiles));
			_logger.Info("Profile: {0}, No direct play profiles found for Path: {1}", options.Profile.Name ?? "Unknown Profile", item.Path ?? "Unknown path");
		}
		if (list2.Count > 0)
		{
			list.Clear();
		}
		else
		{
			list = list.Distinct().ToList();
		}
		return new Tuple<List<PlayMethod>, List<TranscodeReason>>(list2, list);
	}

	private List<TranscodeReason> GetTranscodeReasonsFromDirectPlayProfile(MediaSourceInfo item, MediaStream videoStream, MediaStream audioStream, IEnumerable<DirectPlayProfile> directPlayProfiles)
	{
		List<TranscodeReason> list = new List<TranscodeReason>();
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		foreach (DirectPlayProfile directPlayProfile in directPlayProfiles)
		{
			flag2 = false;
			flag3 = false;
			if (directPlayProfile.SupportsContainer(item.Container))
			{
				flag = true;
				if (videoStream != null && directPlayProfile.SupportsVideoCodec(videoStream.Codec))
				{
					flag3 = true;
				}
				if (audioStream != null && directPlayProfile.SupportsAudioCodec(audioStream.Codec))
				{
					flag2 = true;
				}
				if (flag3 & flag2)
				{
					break;
				}
			}
		}
		if (!flag)
		{
			list.Add(TranscodeReason.ContainerNotSupported);
		}
		if (videoStream != null && !flag3)
		{
			list.Add(TranscodeReason.VideoCodecNotSupported);
		}
		if (audioStream != null && !flag2)
		{
			list.Add(TranscodeReason.AudioCodecNotSupported);
		}
		return list;
	}

	private int? GetDefaultSubtitleStreamIndex(MediaSourceInfo item, SubtitleProfile[] subtitleProfiles)
	{
		int num = -1;
		foreach (MediaStream mediaStream in item.MediaStreams)
		{
			if (mediaStream.Type == MediaStreamType.Subtitle && mediaStream.Score.HasValue && mediaStream.Score.Value > num)
			{
				num = mediaStream.Score.Value;
			}
		}
		List<MediaStream> list = new List<MediaStream>();
		foreach (MediaStream mediaStream2 in item.MediaStreams)
		{
			if (mediaStream2.Type == MediaStreamType.Subtitle && mediaStream2.Score.HasValue && mediaStream2.Score.Value == num)
			{
				list.Add(mediaStream2);
			}
		}
		if (list.Count > 1)
		{
			foreach (MediaStream item2 in list)
			{
				foreach (SubtitleProfile subtitleProfile in subtitleProfiles)
				{
					if (subtitleProfile.Method == SubtitleDeliveryMethod.External && StringHelper.EqualsIgnoreCase(subtitleProfile.Format, item2.Codec))
					{
						return item2.Index;
					}
				}
			}
		}
		return item.DefaultSubtitleStreamIndex;
	}

	private void SetStreamInfoOptionsFromTranscodingProfile(StreamInfo playlistItem, TranscodingProfile transcodingProfile)
	{
		if (string.IsNullOrEmpty(transcodingProfile.AudioCodec))
		{
			playlistItem.AudioCodecs = Array.Empty<string>();
		}
		else
		{
			playlistItem.AudioCodecs = transcodingProfile.AudioCodec.Split(new char[1] { ',' });
		}
		playlistItem.Container = transcodingProfile.Container;
		playlistItem.EstimateContentLength = transcodingProfile.EstimateContentLength;
		playlistItem.TranscodeSeekInfo = transcodingProfile.TranscodeSeekInfo;
		if (string.IsNullOrEmpty(transcodingProfile.VideoCodec))
		{
			playlistItem.VideoCodecs = Array.Empty<string>();
		}
		else
		{
			playlistItem.VideoCodecs = transcodingProfile.VideoCodec.Split(new char[1] { ',' });
		}
		playlistItem.CopyTimestamps = transcodingProfile.CopyTimestamps;
		playlistItem.EnableSubtitlesInManifest = transcodingProfile.EnableSubtitlesInManifest;
		playlistItem.EnableMpegtsM2TsMode = transcodingProfile.EnableMpegtsM2TsMode;
		playlistItem.BreakOnNonKeyFrames = transcodingProfile.BreakOnNonKeyFrames;
		if (transcodingProfile.MinSegments > 0)
		{
			playlistItem.MinSegments = transcodingProfile.MinSegments;
		}
		if (transcodingProfile.SegmentLength > 0)
		{
			playlistItem.SegmentLength = transcodingProfile.SegmentLength;
		}
		playlistItem.SubProtocol = transcodingProfile.Protocol;
		if (!string.IsNullOrEmpty(transcodingProfile.MaxAudioChannels) && int.TryParse(transcodingProfile.MaxAudioChannels, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			playlistItem.TranscodingMaxAudioChannels = result;
		}
	}

	private StreamInfo BuildVideoItem(MediaSourceInfo item, VideoOptions options)
	{
		if (item == null)
		{
			throw new ArgumentNullException("item");
		}
		List<TranscodeReason> list = new List<TranscodeReason>();
		StreamInfo streamInfo = new StreamInfo
		{
			ItemId = options.ItemId,
			MediaType = DlnaProfileType.Video,
			MediaSource = item,
			RunTimeTicks = item.RunTimeTicks,
			Context = options.Context,
			DeviceProfile = options.Profile
		};
		streamInfo.SubtitleStreamIndex = options.SubtitleStreamIndex ?? GetDefaultSubtitleStreamIndex(item, options.Profile.SubtitleProfiles);
		MediaStream mediaStream = (streamInfo.SubtitleStreamIndex.HasValue ? item.GetMediaStream(MediaStreamType.Subtitle, streamInfo.SubtitleStreamIndex.Value) : null);
		MediaStream defaultAudioStream = item.GetDefaultAudioStream(options.AudioStreamIndex ?? item.DefaultAudioStreamIndex);
		if (defaultAudioStream != null)
		{
			streamInfo.AudioStreamIndex = defaultAudioStream.Index;
		}
		MediaStream videoStream = item.VideoStream;
		(bool, TranscodeReason?) tuple = IsEligibleForDirectPlay(item, GetBitrateForDirectPlayCheck(item, options, isAudio: true) ?? 0, mediaStream, options, PlayMethod.DirectPlay);
		(bool, TranscodeReason?) tuple2 = IsEligibleForDirectPlay(item, options.GetMaxBitrate(isAudio: false) ?? 0, mediaStream, options, PlayMethod.DirectStream);
		bool flag = options.EnableDirectPlay && (options.ForceDirectPlay || tuple.Item1);
		bool flag2 = options.EnableDirectStream && (options.ForceDirectStream || tuple2.Item1);
		_logger.Info("Profile: {0}, Path: {1}, isEligibleForDirectPlay: {2}, isEligibleForDirectStream: {3}", options.Profile.Name ?? "Unknown Profile", item.Path ?? "Unknown path", flag, flag2);
		if (flag | flag2)
		{
			Tuple<PlayMethod?, List<TranscodeReason>> videoDirectPlayProfile = GetVideoDirectPlayProfile(options, item, videoStream, defaultAudioStream, flag, flag2);
			PlayMethod? item2 = videoDirectPlayProfile.Item1;
			if (item2.HasValue)
			{
				streamInfo.PlayMethod = item2.Value;
				streamInfo.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, item.Path, options.Profile, DlnaProfileType.Video);
				if (mediaStream != null)
				{
					SubtitleProfile subtitleProfile = GetSubtitleProfile(item, mediaStream, options.Profile.SubtitleProfiles, item2.Value, _transcoderSupport, item.Container, null);
					streamInfo.SubtitleDeliveryMethod = subtitleProfile.Method;
					streamInfo.SubtitleFormat = subtitleProfile.Format;
				}
				return streamInfo;
			}
			list.AddRange(videoDirectPlayProfile.Item2);
		}
		if (tuple.Item2.HasValue)
		{
			list.Add(tuple.Item2.Value);
		}
		if (tuple2.Item2.HasValue)
		{
			list.Add(tuple2.Item2.Value);
		}
		TranscodingProfile transcodingProfile = null;
		TranscodingProfile[] transcodingProfiles = options.Profile.TranscodingProfiles;
		foreach (TranscodingProfile transcodingProfile2 in transcodingProfiles)
		{
			if (transcodingProfile2.Type == streamInfo.MediaType && transcodingProfile2.Context == options.Context)
			{
				transcodingProfile = transcodingProfile2;
				break;
			}
		}
		if (transcodingProfile != null)
		{
			if (!item.SupportsTranscoding)
			{
				return null;
			}
			if (mediaStream != null)
			{
				SubtitleProfile subtitleProfile2 = GetSubtitleProfile(item, mediaStream, options.Profile.SubtitleProfiles, PlayMethod.Transcode, _transcoderSupport, transcodingProfile.Container, transcodingProfile.Protocol);
				streamInfo.SubtitleDeliveryMethod = subtitleProfile2.Method;
				streamInfo.SubtitleFormat = subtitleProfile2.Format;
				streamInfo.SubtitleCodecs = new string[1] { subtitleProfile2.Format };
			}
			streamInfo.PlayMethod = PlayMethod.Transcode;
			SetStreamInfoOptionsFromTranscodingProfile(streamInfo, transcodingProfile);
			ConditionProcessor conditionProcessor = new ConditionProcessor();
			bool enableNonQualifiedConditions = true;
			CodecProfile[] codecProfiles = options.Profile.CodecProfiles;
			foreach (CodecProfile codecProfile in codecProfiles)
			{
				if (codecProfile.Type != CodecType.Video || !codecProfile.ContainsAnyCodec(transcodingProfile.VideoCodec, transcodingProfile.Container))
				{
					continue;
				}
				bool flag3 = true;
				ProfileCondition[] applyConditions = codecProfile.ApplyConditions;
				foreach (ProfileCondition condition in applyConditions)
				{
					int? width = videoStream?.Width;
					int? height = videoStream?.Height;
					int? videoBitDepth = videoStream?.BitDepth;
					int? videoBitrate = videoStream?.BitRate;
					double? videoLevel = videoStream?.Level;
					string videoProfile = videoStream?.Profile;
					float videoFramerate = ((videoStream == null) ? 0f : (videoStream.AverageFrameRate ?? videoStream.AverageFrameRate ?? 0f));
					bool? isAnamorphic = videoStream?.IsAnamorphic;
					bool? isInterlaced = videoStream?.IsInterlaced;
					string videoCodecTag = videoStream?.CodecTag;
					bool? isAvc = videoStream?.IsAVC;
					TransportStreamTimestamp? timestamp = ((videoStream == null) ? new TransportStreamTimestamp?(TransportStreamTimestamp.None) : item.Timestamp);
					int? packetLength = videoStream?.PacketLength;
					int? refFrames = videoStream?.RefFrames;
					int? streamCount = item.GetStreamCount(MediaStreamType.Audio);
					int? streamCount2 = item.GetStreamCount(MediaStreamType.Video);
					if (!conditionProcessor.IsVideoConditionSatisfied(condition, width, height, videoBitDepth, videoBitrate, videoProfile, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, streamCount2, streamCount, videoCodecTag, isAvc))
					{
						flag3 = false;
						break;
					}
				}
				if (!flag3)
				{
					continue;
				}
				string[] array = ContainerProfile.SplitValue(transcodingProfile.VideoCodec);
				foreach (string text in array)
				{
					if (codecProfile.ContainsAnyCodec(text, transcodingProfile.Container))
					{
						ApplyTranscodingConditions(streamInfo, codecProfile.Conditions, text, enableQualifiedConditions: true, enableNonQualifiedConditions);
						enableNonQualifiedConditions = false;
					}
				}
			}
			streamInfo.GlobalMaxAudioChannels = options.MaxAudioChannels;
			int audioBitrate = GetAudioBitrate(streamInfo.SubProtocol, options.GetMaxBitrate(isAudio: false) ?? 0, streamInfo.TargetAudioCodec, defaultAudioStream, streamInfo);
			streamInfo.AudioBitrate = Math.Min(streamInfo.AudioBitrate ?? audioBitrate, audioBitrate);
			enableNonQualifiedConditions = true;
			codecProfiles = options.Profile.CodecProfiles;
			foreach (CodecProfile codecProfile2 in codecProfiles)
			{
				if (codecProfile2.Type != CodecType.VideoAudio || !codecProfile2.ContainsAnyCodec(transcodingProfile.AudioCodec, transcodingProfile.Container))
				{
					continue;
				}
				bool flag4 = true;
				ProfileCondition[] applyConditions = codecProfile2.ApplyConditions;
				foreach (ProfileCondition condition2 in applyConditions)
				{
					bool? isSecondaryTrack = ((defaultAudioStream == null) ? ((bool?)null) : item.IsSecondaryAudio(defaultAudioStream));
					int? audioBitrate2 = defaultAudioStream?.BitRate;
					int? audioChannels = defaultAudioStream?.Channels;
					string audioProfile = defaultAudioStream?.Profile;
					int? audioSampleRate = defaultAudioStream?.SampleRate;
					int? audioBitDepth = defaultAudioStream?.BitDepth;
					if (!conditionProcessor.IsVideoAudioConditionSatisfied(condition2, audioChannels, audioBitrate2, audioSampleRate, audioBitDepth, audioProfile, isSecondaryTrack))
					{
						flag4 = false;
						break;
					}
				}
				if (!flag4)
				{
					continue;
				}
				string[] array = ContainerProfile.SplitValue(transcodingProfile.AudioCodec);
				foreach (string text2 in array)
				{
					if (codecProfile2.ContainsAnyCodec(text2, transcodingProfile.Container))
					{
						ApplyTranscodingConditions(streamInfo, codecProfile2.Conditions, text2, enableQualifiedConditions: true, enableNonQualifiedConditions);
						enableNonQualifiedConditions = false;
					}
				}
			}
			long? maxBitrate = options.GetMaxBitrate(isAudio: false);
			if (maxBitrate.HasValue)
			{
				long num = maxBitrate.Value;
				if (streamInfo.AudioBitrate.HasValue)
				{
					num -= streamInfo.AudioBitrate.Value;
				}
				long val = ((long?)streamInfo.VideoBitrate) ?? num;
				long num2 = Math.Max(Math.Min(num, val), 64000L);
				streamInfo.VideoBitrate = ((num2 >= int.MaxValue) ? int.MaxValue : Convert.ToInt32(num2));
			}
		}
		streamInfo.TranscodeReasons = list.ToArray();
		return streamInfo;
	}

	private int GetDefaultAudioBitrateIfUnknown(MediaStream audioStream)
	{
		if ((audioStream.Channels ?? 0) >= 6)
		{
			return 384000;
		}
		return 192000;
	}

	private int GetAudioBitrate(string subProtocol, long maxTotalBitrate, string[] targetAudioCodecs, MediaStream audioStream, StreamInfo item)
	{
		string codec = ((targetAudioCodecs.Length == 0) ? null : targetAudioCodecs[0]);
		int? targetAudioChannels = item.GetTargetAudioChannels(codec);
		int num = ((audioStream == null) ? 192000 : (audioStream.BitRate ?? GetDefaultAudioBitrateIfUnknown(audioStream)));
		if (targetAudioChannels.HasValue && audioStream != null && audioStream.Channels.HasValue && targetAudioChannels.Value < audioStream.Channels.Value)
		{
			num = ((targetAudioChannels.Value <= 2) ? 128000 : 192000);
		}
		int val = int.MaxValue;
		if (audioStream != null && audioStream.Channels.HasValue && audioStream.Channels.Value == 1 && (audioStream.BitRate ?? 0) < 64000)
		{
			val = 64000;
		}
		if (maxTotalBitrate > 0)
		{
			num = Math.Min(GetMaxAudioBitrateForTotalBitrate(maxTotalBitrate), num);
		}
		return Math.Min(num, val);
	}

	private int GetMaxAudioBitrateForTotalBitrate(long totalBitrate)
	{
		if (totalBitrate <= 640000)
		{
			return 128000;
		}
		if (totalBitrate <= 2000000)
		{
			return 384000;
		}
		if (totalBitrate <= 3000000)
		{
			return 448000;
		}
		return 640000;
	}

	private Tuple<PlayMethod?, List<TranscodeReason>> GetVideoDirectPlayProfile(VideoOptions options, MediaSourceInfo mediaSource, MediaStream videoStream, MediaStream audioStream, bool isEligibleForDirectPlay, bool isEligibleForDirectStream)
	{
		DeviceProfile profile = options.Profile;
		if (options.ForceDirectPlay)
		{
			return new Tuple<PlayMethod?, List<TranscodeReason>>(PlayMethod.DirectPlay, new List<TranscodeReason>());
		}
		if (options.ForceDirectStream)
		{
			return new Tuple<PlayMethod?, List<TranscodeReason>>(PlayMethod.DirectStream, new List<TranscodeReason>());
		}
		DirectPlayProfile directPlayProfile = null;
		DirectPlayProfile[] directPlayProfiles = profile.DirectPlayProfiles;
		foreach (DirectPlayProfile directPlayProfile2 in directPlayProfiles)
		{
			if (directPlayProfile2.Type == DlnaProfileType.Video && IsVideoDirectPlaySupported(directPlayProfile2, mediaSource, videoStream, audioStream))
			{
				directPlayProfile = directPlayProfile2;
				break;
			}
		}
		if (directPlayProfile == null)
		{
			_logger.Info("Profile: {0}, No direct play profiles found for Path: {1}", profile.Name ?? "Unknown Profile", mediaSource.Path ?? "Unknown path");
			return new Tuple<PlayMethod?, List<TranscodeReason>>(null, GetTranscodeReasonsFromDirectPlayProfile(mediaSource, videoStream, audioStream, profile.DirectPlayProfiles));
		}
		string container = mediaSource.Container;
		List<ProfileCondition> list = new List<ProfileCondition>();
		ContainerProfile[] containerProfiles = profile.ContainerProfiles;
		foreach (ContainerProfile containerProfile in containerProfiles)
		{
			if (containerProfile.Type == DlnaProfileType.Video && containerProfile.ContainsContainer(container))
			{
				ProfileCondition[] conditions = containerProfile.Conditions;
				foreach (ProfileCondition item in conditions)
				{
					list.Add(item);
				}
			}
		}
		ConditionProcessor conditionProcessor = new ConditionProcessor();
		int? width = videoStream?.Width;
		int? height = videoStream?.Height;
		int? videoBitDepth = videoStream?.BitDepth;
		int? videoBitrate = videoStream?.BitRate;
		double? videoLevel = videoStream?.Level;
		string videoProfile = videoStream?.Profile;
		float videoFramerate = ((videoStream == null) ? 0f : (videoStream.AverageFrameRate ?? videoStream.AverageFrameRate ?? 0f));
		bool? isAnamorphic = videoStream?.IsAnamorphic;
		bool? isInterlaced = videoStream?.IsInterlaced;
		string videoCodecTag = videoStream?.CodecTag;
		bool? isAvc = videoStream?.IsAVC;
		int? audioBitrate = audioStream?.BitRate;
		int? audioChannels = audioStream?.Channels;
		string audioProfile = audioStream?.Profile;
		int? audioSampleRate = audioStream?.SampleRate;
		int? audioBitDepth = audioStream?.BitDepth;
		TransportStreamTimestamp? timestamp = ((videoStream == null) ? new TransportStreamTimestamp?(TransportStreamTimestamp.None) : mediaSource.Timestamp);
		int? packetLength = videoStream?.PacketLength;
		int? refFrames = videoStream?.RefFrames;
		int? streamCount = mediaSource.GetStreamCount(MediaStreamType.Audio);
		int? streamCount2 = mediaSource.GetStreamCount(MediaStreamType.Video);
		foreach (ProfileCondition item7 in list)
		{
			if (!conditionProcessor.IsVideoConditionSatisfied(item7, width, height, videoBitDepth, videoBitrate, videoProfile, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, streamCount2, streamCount, videoCodecTag, isAvc))
			{
				LogConditionFailure(profile, "VideoContainerProfile", item7, mediaSource);
				TranscodeReason? transcodeReasonForFailedCondition = GetTranscodeReasonForFailedCondition(item7);
				List<TranscodeReason> item2 = (transcodeReasonForFailedCondition.HasValue ? new List<TranscodeReason> { transcodeReasonForFailedCondition.Value } : new List<TranscodeReason>());
				return new Tuple<PlayMethod?, List<TranscodeReason>>(null, item2);
			}
		}
		string codec = videoStream?.Codec;
		list = new List<ProfileCondition>();
		CodecProfile[] codecProfiles = profile.CodecProfiles;
		foreach (CodecProfile codecProfile in codecProfiles)
		{
			if (codecProfile.Type != CodecType.Video || !codecProfile.ContainsAnyCodec(codec, container))
			{
				continue;
			}
			bool flag = true;
			ProfileCondition[] conditions = codecProfile.ApplyConditions;
			foreach (ProfileCondition condition in conditions)
			{
				if (!conditionProcessor.IsVideoConditionSatisfied(condition, width, height, videoBitDepth, videoBitrate, videoProfile, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, streamCount2, streamCount, videoCodecTag, isAvc))
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				conditions = codecProfile.Conditions;
				foreach (ProfileCondition item3 in conditions)
				{
					list.Add(item3);
				}
			}
		}
		foreach (ProfileCondition item8 in list)
		{
			if (!conditionProcessor.IsVideoConditionSatisfied(item8, width, height, videoBitDepth, videoBitrate, videoProfile, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, streamCount2, streamCount, videoCodecTag, isAvc))
			{
				LogConditionFailure(profile, "VideoCodecProfile", item8, mediaSource);
				TranscodeReason? transcodeReasonForFailedCondition2 = GetTranscodeReasonForFailedCondition(item8);
				List<TranscodeReason> item4 = (transcodeReasonForFailedCondition2.HasValue ? new List<TranscodeReason> { transcodeReasonForFailedCondition2.Value } : new List<TranscodeReason>());
				return new Tuple<PlayMethod?, List<TranscodeReason>>(null, item4);
			}
		}
		if (audioStream != null)
		{
			string codec2 = audioStream.Codec;
			list = new List<ProfileCondition>();
			bool? isSecondaryTrack = ((audioStream == null) ? ((bool?)null) : mediaSource.IsSecondaryAudio(audioStream));
			codecProfiles = profile.CodecProfiles;
			foreach (CodecProfile codecProfile2 in codecProfiles)
			{
				if (codecProfile2.Type != CodecType.VideoAudio || !codecProfile2.ContainsAnyCodec(codec2, container))
				{
					continue;
				}
				bool flag2 = true;
				ProfileCondition[] conditions = codecProfile2.ApplyConditions;
				foreach (ProfileCondition condition2 in conditions)
				{
					if (!conditionProcessor.IsVideoAudioConditionSatisfied(condition2, audioChannels, audioBitrate, audioSampleRate, audioBitDepth, audioProfile, isSecondaryTrack))
					{
						flag2 = false;
						break;
					}
				}
				if (flag2)
				{
					conditions = codecProfile2.Conditions;
					foreach (ProfileCondition item5 in conditions)
					{
						list.Add(item5);
					}
				}
			}
			foreach (ProfileCondition item9 in list)
			{
				if (!conditionProcessor.IsVideoAudioConditionSatisfied(item9, audioChannels, audioBitrate, audioSampleRate, audioBitDepth, audioProfile, isSecondaryTrack))
				{
					LogConditionFailure(profile, "VideoAudioCodecProfile", item9, mediaSource);
					TranscodeReason? transcodeReasonForFailedCondition3 = GetTranscodeReasonForFailedCondition(item9);
					List<TranscodeReason> item6 = (transcodeReasonForFailedCondition3.HasValue ? new List<TranscodeReason> { transcodeReasonForFailedCondition3.Value } : new List<TranscodeReason>());
					return new Tuple<PlayMethod?, List<TranscodeReason>>(null, item6);
				}
			}
		}
		if (isEligibleForDirectStream && mediaSource.SupportsDirectStream)
		{
			return new Tuple<PlayMethod?, List<TranscodeReason>>(PlayMethod.DirectStream, new List<TranscodeReason>());
		}
		return new Tuple<PlayMethod?, List<TranscodeReason>>(null, new List<TranscodeReason> { TranscodeReason.ContainerBitrateExceedsLimit });
	}

	private void LogConditionFailure(DeviceProfile profile, string type, ProfileCondition condition, MediaSourceInfo mediaSource)
	{
		_logger.Info("Profile: {0}, DirectPlay=false. Reason={1}.{2} Condition: {3}. ConditionValue: {4}. IsRequired: {5}. Path: {6}", type, profile.Name ?? "Unknown Profile", condition.Property, condition.Condition, condition.Value ?? string.Empty, condition.IsRequired, mediaSource.Path ?? "Unknown path");
	}

	private (bool, TranscodeReason?) IsEligibleForDirectPlay(MediaSourceInfo item, long maxBitrate, MediaStream subtitleStream, VideoOptions options, PlayMethod playMethod)
	{
		if (subtitleStream != null)
		{
			SubtitleProfile subtitleProfile = GetSubtitleProfile(item, subtitleStream, options.Profile.SubtitleProfiles, playMethod, _transcoderSupport, item.Container, null);
			if (subtitleProfile.Method != SubtitleDeliveryMethod.External && subtitleProfile.Method != SubtitleDeliveryMethod.Embed)
			{
				_logger.Info("Not eligible for {0} due to unsupported subtitles", playMethod);
				return (false, TranscodeReason.SubtitleCodecNotSupported);
			}
		}
		bool flag = IsAudioEligibleForDirectPlay(item, maxBitrate, playMethod);
		if (flag)
		{
			return (flag, null);
		}
		return (flag, TranscodeReason.ContainerBitrateExceedsLimit);
	}

	public static SubtitleProfile GetSubtitleProfile(MediaSourceInfo mediaSource, MediaStream subtitleStream, SubtitleProfile[] subtitleProfiles, PlayMethod playMethod, ITranscoderSupport transcoderSupport, string outputContainer, string transcodingSubProtocol)
	{
		if (!subtitleStream.IsExternal && (playMethod != PlayMethod.Transcode || !string.Equals(transcodingSubProtocol, "hls", StringComparison.OrdinalIgnoreCase)))
		{
			SubtitleProfile[] array = subtitleProfiles;
			foreach (SubtitleProfile subtitleProfile in array)
			{
				if (subtitleProfile.SupportsLanguage(subtitleStream.Language) && subtitleProfile.Method == SubtitleDeliveryMethod.Embed && ContainerProfile.ContainsContainer(subtitleProfile.Container, outputContainer) && (playMethod != PlayMethod.Transcode || IsSubtitleEmbedSupported(subtitleStream, subtitleProfile, transcodingSubProtocol, outputContainer)) && subtitleStream.IsTextSubtitleStream == MediaStream.IsTextFormat(subtitleProfile.Format) && StringHelper.EqualsIgnoreCase(subtitleProfile.Format, subtitleStream.Codec))
				{
					return subtitleProfile;
				}
			}
			array = subtitleProfiles;
			foreach (SubtitleProfile subtitleProfile2 in array)
			{
				if (subtitleProfile2.SupportsLanguage(subtitleStream.Language) && subtitleProfile2.Method == SubtitleDeliveryMethod.Embed && ContainerProfile.ContainsContainer(subtitleProfile2.Container, outputContainer) && (playMethod != PlayMethod.Transcode || IsSubtitleEmbedSupported(subtitleStream, subtitleProfile2, transcodingSubProtocol, outputContainer)) && subtitleStream.IsTextSubtitleStream && subtitleStream.SupportsSubtitleConversionTo(subtitleProfile2.Format))
				{
					return subtitleProfile2;
				}
			}
		}
		return GetExternalSubtitleProfile(mediaSource, subtitleStream, subtitleProfiles, playMethod, transcoderSupport, allowConversion: false) ?? GetExternalSubtitleProfile(mediaSource, subtitleStream, subtitleProfiles, playMethod, transcoderSupport, allowConversion: true) ?? new SubtitleProfile
		{
			Method = SubtitleDeliveryMethod.Encode,
			Format = subtitleStream.Codec
		};
	}

	private static bool IsSubtitleEmbedSupported(MediaStream subtitleStream, SubtitleProfile subtitleProfile, string transcodingSubProtocol, string transcodingContainer)
	{
		if (!string.IsNullOrEmpty(transcodingContainer))
		{
			string[] profileContainers = ContainerProfile.SplitValue(transcodingContainer);
			if (ContainerProfile.ContainsContainer(profileContainers, "ts"))
			{
				return false;
			}
			if (ContainerProfile.ContainsContainer(profileContainers, "mpegts"))
			{
				return false;
			}
			if (ContainerProfile.ContainsContainer(profileContainers, "mp4"))
			{
				return false;
			}
			if (ContainerProfile.ContainsContainer(profileContainers, "mkv") || ContainerProfile.ContainsContainer(profileContainers, "matroska"))
			{
				return true;
			}
		}
		return false;
	}

	private static SubtitleProfile GetExternalSubtitleProfile(MediaSourceInfo mediaSource, MediaStream subtitleStream, SubtitleProfile[] subtitleProfiles, PlayMethod playMethod, ITranscoderSupport transcoderSupport, bool allowConversion)
	{
		foreach (SubtitleProfile subtitleProfile in subtitleProfiles)
		{
			if ((subtitleProfile.Method == SubtitleDeliveryMethod.External || subtitleProfile.Method == SubtitleDeliveryMethod.Hls) && (subtitleProfile.Method != SubtitleDeliveryMethod.Hls || playMethod == PlayMethod.Transcode) && subtitleProfile.SupportsLanguage(subtitleStream.Language) && (subtitleStream.IsExternal || transcoderSupport.CanExtractSubtitles(subtitleStream.Codec)) && ((subtitleProfile.Method == SubtitleDeliveryMethod.External && subtitleStream.IsTextSubtitleStream == MediaStream.IsTextFormat(subtitleProfile.Format)) || (subtitleProfile.Method == SubtitleDeliveryMethod.Hls && subtitleStream.IsTextSubtitleStream)))
			{
				if (StringHelper.EqualsIgnoreCase(subtitleStream.Codec, subtitleProfile.Format))
				{
					return subtitleProfile;
				}
				if (allowConversion && !mediaSource.IsInfiniteStream && subtitleStream.IsTextSubtitleStream && subtitleStream.SupportsExternalStream && subtitleStream.SupportsSubtitleConversionTo(subtitleProfile.Format))
				{
					return subtitleProfile;
				}
			}
		}
		return null;
	}

	private bool IsAudioEligibleForDirectPlay(MediaSourceInfo item, long maxBitrate, PlayMethod playMethod)
	{
		if (item.IsRemote)
		{
			return true;
		}
		long num = ((maxBitrate > 0) ? maxBitrate : 1000000);
		int num2 = item.Bitrate ?? 40000000;
		if (num2 > num)
		{
			_logger.Info(string.Concat("Bitrate exceeds ", playMethod, " limit: media bitrate: {0}, max bitrate: {1}"), num2.ToString(CultureInfo.InvariantCulture), num.ToString(CultureInfo.InvariantCulture));
			return false;
		}
		return true;
	}

	private void ValidateInput(VideoOptions options)
	{
		ValidateAudioInput(options);
		if (options.AudioStreamIndex.HasValue && string.IsNullOrEmpty(options.MediaSourceId))
		{
			throw new ArgumentException("MediaSourceId is required when a specific audio stream is requested");
		}
		if (options.SubtitleStreamIndex.HasValue && string.IsNullOrEmpty(options.MediaSourceId))
		{
			throw new ArgumentException("MediaSourceId is required when a specific subtitle stream is requested");
		}
	}

	private void ValidateAudioInput(AudioOptions options)
	{
		if (options.ItemId.Equals(Guid.Empty))
		{
			throw new ArgumentException("ItemId is required");
		}
		if (string.IsNullOrEmpty(options.DeviceId))
		{
			throw new ArgumentException("DeviceId is required");
		}
		if (options.Profile == null)
		{
			throw new ArgumentException("Profile is required");
		}
		if (options.MediaSources == null)
		{
			throw new ArgumentException("MediaSources is required");
		}
	}

	private void ApplyTranscodingConditions(StreamInfo item, List<CodecProfile> codecProfiles)
	{
		foreach (CodecProfile codecProfile in codecProfiles)
		{
			ApplyTranscodingConditions(item, codecProfile);
		}
	}

	private void ApplyTranscodingConditions(StreamInfo item, CodecProfile codecProfile)
	{
		string[] array = ContainerProfile.SplitValue(codecProfile.Codec);
		if (array.Length == 0)
		{
			ApplyTranscodingConditions(item, codecProfile.Conditions, null, enableQualifiedConditions: true, enableNonQualifiedConditions: true);
			return;
		}
		bool enableNonQualifiedConditions = true;
		string[] array2 = array;
		foreach (string qualifier in array2)
		{
			ApplyTranscodingConditions(item, codecProfile.Conditions, qualifier, enableQualifiedConditions: true, enableNonQualifiedConditions);
			enableNonQualifiedConditions = false;
		}
	}

	private void ApplyTranscodingConditions(StreamInfo item, IEnumerable<ProfileCondition> conditions, string qualifier, bool enableQualifiedConditions, bool enableNonQualifiedConditions)
	{
		foreach (ProfileCondition condition in conditions)
		{
			string value = condition.Value;
			if (string.IsNullOrEmpty(value) || condition.Condition == ProfileConditionType.GreaterThanEqual)
			{
				continue;
			}
			switch (condition.Property)
			{
			case ProfileConditionValue.AudioBitrate:
			{
				if (enableNonQualifiedConditions && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result12))
				{
					if (condition.Condition == ProfileConditionType.Equals)
					{
						item.AudioBitrate = result12;
					}
					else if (condition.Condition == ProfileConditionType.LessThanEqual)
					{
						item.AudioBitrate = Math.Min(result12, item.AudioBitrate ?? result12);
					}
					else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
					{
						item.AudioBitrate = Math.Max(result12, item.AudioBitrate ?? result12);
					}
				}
				break;
			}
			case ProfileConditionValue.AudioChannels:
			{
				if (string.IsNullOrEmpty(qualifier))
				{
					if (!enableNonQualifiedConditions)
					{
						break;
					}
				}
				else if (!enableQualifiedConditions)
				{
					break;
				}
				if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result9))
				{
					if (condition.Condition == ProfileConditionType.Equals)
					{
						item.SetOption(qualifier, "audiochannels", result9.ToString(CultureInfo.InvariantCulture));
					}
					else if (condition.Condition == ProfileConditionType.LessThanEqual)
					{
						item.SetOption(qualifier, "audiochannels", Math.Min(result9, item.GetTargetAudioChannels(qualifier) ?? result9).ToString(CultureInfo.InvariantCulture));
					}
					else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
					{
						item.SetOption(qualifier, "audiochannels", Math.Max(result9, item.GetTargetAudioChannels(qualifier) ?? result9).ToString(CultureInfo.InvariantCulture));
					}
				}
				break;
			}
			case ProfileConditionValue.IsAvc:
			{
				if (enableNonQualifiedConditions && bool.TryParse(value, out var result2))
				{
					if (result2 && condition.Condition == ProfileConditionType.Equals)
					{
						item.RequireAvc = true;
					}
					else if (!result2 && condition.Condition == ProfileConditionType.NotEquals)
					{
						item.RequireAvc = true;
					}
				}
				break;
			}
			case ProfileConditionValue.IsAnamorphic:
			{
				if (enableNonQualifiedConditions && bool.TryParse(value, out var result8))
				{
					if (result8 && condition.Condition == ProfileConditionType.Equals)
					{
						item.RequireNonAnamorphic = true;
					}
					else if (!result8 && condition.Condition == ProfileConditionType.NotEquals)
					{
						item.RequireNonAnamorphic = true;
					}
				}
				break;
			}
			case ProfileConditionValue.IsInterlaced:
			{
				if (string.IsNullOrEmpty(qualifier))
				{
					if (!enableNonQualifiedConditions)
					{
						break;
					}
				}
				else if (!enableQualifiedConditions)
				{
					break;
				}
				if (bool.TryParse(value, out var result5))
				{
					if (!result5 && condition.Condition == ProfileConditionType.Equals)
					{
						item.SetOption(qualifier, "deinterlace", "true");
					}
					else if (result5 && condition.Condition == ProfileConditionType.NotEquals)
					{
						item.SetOption(qualifier, "deinterlace", "true");
					}
				}
				break;
			}
			case ProfileConditionValue.RefFrames:
			{
				if (string.IsNullOrEmpty(qualifier))
				{
					if (!enableNonQualifiedConditions)
					{
						break;
					}
				}
				else if (!enableQualifiedConditions)
				{
					break;
				}
				if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result4))
				{
					if (condition.Condition == ProfileConditionType.Equals)
					{
						item.SetOption(qualifier, "maxrefframes", result4.ToString(CultureInfo.InvariantCulture));
					}
					else if (condition.Condition == ProfileConditionType.LessThanEqual)
					{
						item.SetOption(qualifier, "maxrefframes", Math.Min(result4, item.GetTargetRefFrames(qualifier) ?? result4).ToString(CultureInfo.InvariantCulture));
					}
					else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
					{
						item.SetOption(qualifier, "maxrefframes", Math.Max(result4, item.GetTargetRefFrames(qualifier) ?? result4).ToString(CultureInfo.InvariantCulture));
					}
				}
				break;
			}
			case ProfileConditionValue.VideoBitDepth:
			{
				if (string.IsNullOrEmpty(qualifier))
				{
					if (!enableNonQualifiedConditions)
					{
						break;
					}
				}
				else if (!enableQualifiedConditions)
				{
					break;
				}
				if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result7))
				{
					if (condition.Condition == ProfileConditionType.Equals)
					{
						item.SetOption(qualifier, "videobitdepth", result7.ToString(CultureInfo.InvariantCulture));
					}
					else if (condition.Condition == ProfileConditionType.LessThanEqual)
					{
						item.SetOption(qualifier, "videobitdepth", Math.Min(result7, item.GetTargetVideoBitDepth(qualifier) ?? result7).ToString(CultureInfo.InvariantCulture));
					}
					else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
					{
						item.SetOption(qualifier, "videobitdepth", Math.Max(result7, item.GetTargetVideoBitDepth(qualifier) ?? result7).ToString(CultureInfo.InvariantCulture));
					}
				}
				break;
			}
			case ProfileConditionValue.VideoProfile:
				if (!string.IsNullOrEmpty(qualifier) && !string.IsNullOrEmpty(value))
				{
					string[] value2 = value.Split(new char[1] { '|' }, StringSplitOptions.RemoveEmptyEntries);
					if (condition.Condition == ProfileConditionType.Equals || condition.Condition == ProfileConditionType.EqualsAny)
					{
						item.SetOption(qualifier, "profile", string.Join(",", value2));
					}
				}
				break;
			case ProfileConditionValue.Height:
			{
				if (enableNonQualifiedConditions && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result11))
				{
					if (condition.Condition == ProfileConditionType.Equals)
					{
						item.MaxHeight = result11;
					}
					else if (condition.Condition == ProfileConditionType.LessThanEqual)
					{
						item.MaxHeight = Math.Min(result11, item.MaxHeight ?? result11);
					}
					else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
					{
						item.MaxHeight = Math.Max(result11, item.MaxHeight ?? result11);
					}
				}
				break;
			}
			case ProfileConditionValue.VideoBitrate:
			{
				if (enableNonQualifiedConditions && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result10))
				{
					if (condition.Condition == ProfileConditionType.Equals)
					{
						item.VideoBitrate = result10;
					}
					else if (condition.Condition == ProfileConditionType.LessThanEqual)
					{
						item.VideoBitrate = Math.Min(result10, item.VideoBitrate ?? result10);
					}
					else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
					{
						item.VideoBitrate = Math.Max(result10, item.VideoBitrate ?? result10);
					}
				}
				break;
			}
			case ProfileConditionValue.VideoFramerate:
			{
				if (enableNonQualifiedConditions && float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result6))
				{
					if (condition.Condition == ProfileConditionType.Equals)
					{
						item.MaxFramerate = result6;
					}
					else if (condition.Condition == ProfileConditionType.LessThanEqual)
					{
						item.MaxFramerate = Math.Min(result6, item.MaxFramerate ?? result6);
					}
					else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
					{
						item.MaxFramerate = Math.Max(result6, item.MaxFramerate ?? result6);
					}
				}
				break;
			}
			case ProfileConditionValue.VideoLevel:
			{
				if (!string.IsNullOrEmpty(qualifier) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result3))
				{
					if (condition.Condition == ProfileConditionType.Equals)
					{
						item.SetOption(qualifier, "level", result3.ToString(CultureInfo.InvariantCulture));
					}
					else if (condition.Condition == ProfileConditionType.LessThanEqual)
					{
						item.SetOption(qualifier, "level", Math.Min(result3, item.GetTargetVideoLevel(qualifier) ?? ((double)result3)).ToString(CultureInfo.InvariantCulture));
					}
					else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
					{
						item.SetOption(qualifier, "level", Math.Max(result3, item.GetTargetVideoLevel(qualifier) ?? ((double)result3)).ToString(CultureInfo.InvariantCulture));
					}
				}
				break;
			}
			case ProfileConditionValue.Width:
			{
				if (enableNonQualifiedConditions && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
				{
					if (condition.Condition == ProfileConditionType.Equals)
					{
						item.MaxWidth = result;
					}
					else if (condition.Condition == ProfileConditionType.LessThanEqual)
					{
						item.MaxWidth = Math.Min(result, item.MaxWidth ?? result);
					}
					else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
					{
						item.MaxWidth = Math.Max(result, item.MaxWidth ?? result);
					}
				}
				break;
			}
			}
		}
	}

	private bool IsAudioDirectPlaySupported(DirectPlayProfile profile, MediaSourceInfo item, MediaStream audioStream)
	{
		if (!profile.SupportsContainer(item.Container))
		{
			return false;
		}
		string codec = audioStream?.Codec;
		if (!profile.SupportsAudioCodec(codec))
		{
			return false;
		}
		return true;
	}

	private bool IsVideoDirectPlaySupported(DirectPlayProfile profile, MediaSourceInfo item, MediaStream videoStream, MediaStream audioStream)
	{
		if (!profile.SupportsContainer(item.Container))
		{
			return false;
		}
		string codec = videoStream?.Codec;
		if (!profile.SupportsVideoCodec(codec))
		{
			return false;
		}
		if (audioStream != null)
		{
			string codec2 = audioStream?.Codec;
			if (!profile.SupportsAudioCodec(codec2))
			{
				return false;
			}
		}
		return true;
	}
}
