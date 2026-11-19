namespace MediaBrowser.Model.Entities;

/// <summary>
/// 由 ffprobe 的颜色元数据归一出的视频动态范围类型。
/// 只列出当前能被 ffprobe 明确产出的取值；其余 HDR10+ 等留给后续补充。
/// </summary>
public enum VideoRangeType
{
	Unknown = 0,
	SDR = 1,
	HDR10 = 2,
	HLG = 3,
	DolbyVision = 4,
	DolbyVisionWithHDR10 = 5,
	DolbyVisionWithSDR = 6
}
