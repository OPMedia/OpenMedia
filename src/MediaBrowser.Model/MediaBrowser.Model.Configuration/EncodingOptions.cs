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

	/// <summary>CPU 路径的 HDR→SDR 色调映射开关；默认开启以避免 HDR 直接裁切导致的发灰/泛白。</summary>
	public bool EnableTonemapping { get; set; }

	/// <summary>hable / mobius / reinhard / bt2390 / none，大小写不敏感；未知值按 hable 处理。</summary>
	public string TonemappingAlgorithm { get; set; }

	public bool EnableSubtitleExtraction { get; set; }

	public string[] HardwareDecodingCodecs { get; set; }

	public EncodingOptions()
	{
		DownMixAudioBoost = 2.0;
		EncodingThreadCount = -1;
		VaapiDevice = "/dev/dri/renderD128";
		H264Crf = 23;
		EnableHardwareEncoding = true;
		EnableTonemapping = true;
		TonemappingAlgorithm = "hable";
		EnableSubtitleExtraction = true;
		HardwareDecodingCodecs = new string[2] { "h264", "vc1" };
	}
}
