namespace MediaBrowser.Model.Configuration;

public class EncodingOptions
{
	public int EncodingThreadCount { get; set; }

	public string TranscodingTempPath { get; set; }

	public double DownMixAudioBoost { get; set; }

	// 已移除 EnableThrottling / ThrottleDelaySeconds：出货 ffmpeg 的 stdin 不识别暂停键 `p`，
	// 且 IProcess 不暴露 PID，无法用 SIGSTOP/SIGCONT 实现节流。保留配置只会误导使用者。
	public string HardwareAccelerationType { get; set; }

	public string EncoderAppPath { get; set; }

	public string VaapiDevice { get; set; }

	public int H264Crf { get; set; }

	public string H264Preset { get; set; }

	public string DeinterlaceMethod { get; set; }

	public bool EnableHardwareEncoding { get; set; }

	public bool EnableSubtitleExtraction { get; set; }

	public string[] HardwareDecodingCodecs { get; set; }

	public EncodingOptions()
	{
		DownMixAudioBoost = 2.0;
		EncodingThreadCount = -1;
		VaapiDevice = "/dev/dri/renderD128";
		H264Crf = 23;
		EnableHardwareEncoding = true;
		EnableSubtitleExtraction = true;
		HardwareDecodingCodecs = new string[2] { "h264", "vc1" };
	}
}
