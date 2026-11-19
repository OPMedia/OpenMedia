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
	DirectPlayError,
	// 追加在末尾，避免改动既有 reason 的数值（客户端按数值识别）。
	VideoRangeTypeNotSupported
}
