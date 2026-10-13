using System;
using System.Xml.Serialization;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Model.Dlna;

[XmlRoot("Profile")]
public class DeviceProfile
{
	public string Name { get; set; }

	[XmlIgnore]
	public string Id { get; set; }

	public DeviceIdentification Identification { get; set; }

	public string FriendlyName { get; set; }

	public string Manufacturer { get; set; }

	public string ManufacturerUrl { get; set; }

	public string ModelName { get; set; }

	public string ModelDescription { get; set; }

	public string ModelNumber { get; set; }

	public string ModelUrl { get; set; }

	public string SerialNumber { get; set; }

	public bool EnableAlbumArtInDidl { get; set; }

	public bool EnableSingleAlbumArtLimit { get; set; }

	public bool EnableSingleSubtitleLimit { get; set; }

	public string SupportedMediaTypes { get; set; }

	public string UserId { get; set; }

	public string AlbumArtPn { get; set; }

	public int MaxAlbumArtWidth { get; set; }

	public int MaxAlbumArtHeight { get; set; }

	public int? MaxIconWidth { get; set; }

	public int? MaxIconHeight { get; set; }

	public long? MaxStreamingBitrate { get; set; }

	public long? MaxStaticBitrate { get; set; }

	public int? MusicStreamingTranscodingBitrate { get; set; }

	public int? MaxStaticMusicBitrate { get; set; }

	public string SonyAggregationFlags { get; set; }

	public string ProtocolInfo { get; set; }

	public int TimelineOffsetSeconds { get; set; }

	public bool RequiresPlainVideoItems { get; set; }

	public bool RequiresPlainFolders { get; set; }

	public bool EnableMSMediaReceiverRegistrar { get; set; }

	public bool IgnoreTranscodeByteRangeRequests { get; set; }

	public XmlAttribute[] XmlRootAttributes { get; set; }

	public DirectPlayProfile[] DirectPlayProfiles { get; set; }

	public TranscodingProfile[] TranscodingProfiles { get; set; }

	public ContainerProfile[] ContainerProfiles { get; set; }

	public CodecProfile[] CodecProfiles { get; set; }

	public ResponseProfile[] ResponseProfiles { get; set; }

	public SubtitleProfile[] SubtitleProfiles { get; set; }

	public DeviceProfile()
	{
		DirectPlayProfiles = new DirectPlayProfile[0];
		TranscodingProfiles = new TranscodingProfile[0];
		ResponseProfiles = new ResponseProfile[0];
		CodecProfiles = new CodecProfile[0];
		ContainerProfiles = new ContainerProfile[0];
		SubtitleProfiles = Array.Empty<SubtitleProfile>();
		XmlRootAttributes = new XmlAttribute[0];
		SupportedMediaTypes = "Audio,Photo,Video";
		MaxStreamingBitrate = 8000000L;
		MaxStaticBitrate = 8000000L;
		MusicStreamingTranscodingBitrate = 128000;
	}

	public string[] GetSupportedMediaTypes()
	{
		return ContainerProfile.SplitValue(SupportedMediaTypes);
	}

	public TranscodingProfile GetAudioTranscodingProfile(string container, string audioCodec)
	{
		container = (container ?? string.Empty).TrimStart(new char[1] { '.' });
		TranscodingProfile[] transcodingProfiles = TranscodingProfiles;
		foreach (TranscodingProfile transcodingProfile in transcodingProfiles)
		{
			if (transcodingProfile.Type == DlnaProfileType.Audio && StringHelper.EqualsIgnoreCase(container, transcodingProfile.Container) && ListHelper.ContainsIgnoreCase(transcodingProfile.GetAudioCodecs(), audioCodec ?? string.Empty))
			{
				return transcodingProfile;
			}
		}
		return null;
	}

	public TranscodingProfile GetVideoTranscodingProfile(string container, string audioCodec, string videoCodec)
	{
		container = (container ?? string.Empty).TrimStart(new char[1] { '.' });
		TranscodingProfile[] transcodingProfiles = TranscodingProfiles;
		foreach (TranscodingProfile transcodingProfile in transcodingProfiles)
		{
			if (transcodingProfile.Type == DlnaProfileType.Video && StringHelper.EqualsIgnoreCase(container, transcodingProfile.Container) && ListHelper.ContainsIgnoreCase(transcodingProfile.GetAudioCodecs(), audioCodec ?? string.Empty) && StringHelper.EqualsIgnoreCase(videoCodec, transcodingProfile.VideoCodec ?? string.Empty))
			{
				return transcodingProfile;
			}
		}
		return null;
	}

	public ResponseProfile GetAudioMediaProfile(string container, string audioCodec, int? audioChannels, int? audioBitrate, int? audioSampleRate, int? audioBitDepth)
	{
		ResponseProfile[] responseProfiles = ResponseProfiles;
		foreach (ResponseProfile responseProfile in responseProfiles)
		{
			if (responseProfile.Type != DlnaProfileType.Audio || !ContainerProfile.ContainsContainer(responseProfile.GetContainers(), container))
			{
				continue;
			}
			string[] audioCodecs = responseProfile.GetAudioCodecs();
			if (audioCodecs.Length != 0 && !ListHelper.ContainsIgnoreCase(audioCodecs, audioCodec ?? string.Empty))
			{
				continue;
			}
			ConditionProcessor conditionProcessor = new ConditionProcessor();
			bool flag = false;
			ProfileCondition[] conditions = responseProfile.Conditions;
			foreach (ProfileCondition c in conditions)
			{
				if (!conditionProcessor.IsAudioConditionSatisfied(GetModelProfileCondition(c), audioChannels, audioBitrate, audioSampleRate, audioBitDepth))
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				return responseProfile;
			}
		}
		return null;
	}

	private ProfileCondition GetModelProfileCondition(ProfileCondition c)
	{
		return new ProfileCondition
		{
			Condition = c.Condition,
			IsRequired = c.IsRequired,
			Property = c.Property,
			Value = c.Value
		};
	}

	public ResponseProfile GetImageMediaProfile(string container, int? width, int? height)
	{
		ResponseProfile[] responseProfiles = ResponseProfiles;
		foreach (ResponseProfile responseProfile in responseProfiles)
		{
			if (responseProfile.Type != DlnaProfileType.Photo || !ContainerProfile.ContainsContainer(responseProfile.GetContainers(), container))
			{
				continue;
			}
			ConditionProcessor conditionProcessor = new ConditionProcessor();
			bool flag = false;
			ProfileCondition[] conditions = responseProfile.Conditions;
			foreach (ProfileCondition c in conditions)
			{
				if (!conditionProcessor.IsImageConditionSatisfied(GetModelProfileCondition(c), width, height))
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				return responseProfile;
			}
		}
		return null;
	}

	public ResponseProfile GetVideoMediaProfile(string container, string audioCodec, string videoCodec, int? width, int? height, int? bitDepth, int? videoBitrate, string videoProfile, double? videoLevel, float videoFramerate, int? packetLength, TransportStreamTimestamp timestamp, bool? isAnamorphic, bool? isInterlaced, int? refFrames, int? numVideoStreams, int? numAudioStreams, string videoCodecTag, bool? isAvc)
	{
		ResponseProfile[] responseProfiles = ResponseProfiles;
		foreach (ResponseProfile responseProfile in responseProfiles)
		{
			if (responseProfile.Type != DlnaProfileType.Video || !ContainerProfile.ContainsContainer(responseProfile.GetContainers(), container))
			{
				continue;
			}
			string[] audioCodecs = responseProfile.GetAudioCodecs();
			if (audioCodecs.Length != 0 && !ListHelper.ContainsIgnoreCase(audioCodecs, audioCodec ?? string.Empty))
			{
				continue;
			}
			string[] videoCodecs = responseProfile.GetVideoCodecs();
			if (videoCodecs.Length != 0 && !ListHelper.ContainsIgnoreCase(videoCodecs, videoCodec ?? string.Empty))
			{
				continue;
			}
			ConditionProcessor conditionProcessor = new ConditionProcessor();
			bool flag = false;
			ProfileCondition[] conditions = responseProfile.Conditions;
			foreach (ProfileCondition c in conditions)
			{
				if (!conditionProcessor.IsVideoConditionSatisfied(GetModelProfileCondition(c), width, height, bitDepth, videoBitrate, videoProfile, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, numVideoStreams, numAudioStreams, videoCodecTag, isAvc))
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				return responseProfile;
			}
		}
		return null;
	}
}
