using System;

namespace MediaBrowser.Common.MediaEncoding;

/// <summary>
/// 与 ffmpeg 发行版选型的共享契约。Dockerfile 中的 FFMPEG_VERSION 决定实际出货的二进制，
/// 这里决定我们能够保证的最低能力边界；低于该版本必须显式报错，不允许静默降级。
/// </summary>
public static class FFmpegConstants
{
	public static Version MinimumEncoderVersion { get; } = new Version(7, 0);
}
