namespace MediaBrowser.Model.Session;

public enum TranscodeReason
{
	ContainerNotSupported,
	VideoCodecNotSupported,
	AudioCodecNotSupported,
	ContainerBitrateExceedsLimit,
	AudioBitrateNotSupported,
	AudioChannelsNotSupported,
	VideoResolutionNotSupported,
	UnknownVideoStreamInfo,
	UnknownAudioStreamInfo,
	AudioProfileNotSupported,
	AudioSampleRateNotSupported,
	AnamorphicVideoNotSupported,
	InterlacedVideoNotSupported,
	SecondaryAudioNotSupported,
	RefFramesNotSupported,
	VideoBitDepthNotSupported,
	VideoBitrateNotSupported,
	VideoFramerateNotSupported,
	VideoLevelNotSupported,
	VideoProfileNotSupported,
	AudioBitDepthNotSupported,
	SubtitleCodecNotSupported,
	DirectPlayError
}
