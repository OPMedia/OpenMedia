using System.Linq;
using MediaBrowser.Model.Dlna;

namespace MediaBrowser.Controller.MediaEncoding;

public class EncodingJobOptions : BaseEncodingJobOptions
{
	public string TempDirectory { get; set; }

	public bool ReadInputAtNativeFramerate { get; set; }

	public DeviceProfile DeviceProfile { get; set; }

	public EncodingJobOptions(StreamInfo info, DeviceProfile deviceProfile)
	{
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Invalid comparison between Unknown and I4
		Container = info.Container;
		StartTimeTicks = info.StartPositionTicks;
		MaxWidth = info.MaxWidth;
		MaxHeight = info.MaxHeight;
		MaxFramerate = info.MaxFramerate;
		Id = info.ItemId;
		MediaSourceId = info.MediaSourceId;
		AudioCodec = info.TargetAudioCodec.FirstOrDefault();
		MaxAudioChannels = info.GlobalMaxAudioChannels;
		AudioBitRate = info.AudioBitrate;
		AudioSampleRate = info.TargetAudioSampleRate;
		DeviceProfile = deviceProfile;
		VideoCodec = info.TargetVideoCodec.FirstOrDefault();
		VideoBitRate = info.VideoBitrate;
		AudioStreamIndex = info.AudioStreamIndex;
		SubtitleMethod = info.SubtitleDeliveryMethod;
		Context = info.Context;
		TranscodingMaxAudioChannels = info.TranscodingMaxAudioChannels;
		if ((int)info.SubtitleDeliveryMethod != 2)
		{
			SubtitleStreamIndex = info.SubtitleStreamIndex;
		}
		StreamOptions = info.StreamOptions;
	}
}
