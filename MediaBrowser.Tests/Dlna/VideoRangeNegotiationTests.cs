using System;
using System.Linq;
using System.Reflection;
using Emby.Server.MediaEncoding.Api;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.Dlna;

/// <summary>
/// 客户端 profile 的 VideoRangeType 条件如何影响 DirectPlay/转码，以及 reason 是否传到 PlaybackInfo。
/// </summary>
[TestClass]
public class VideoRangeNegotiationTests
{
    [TestMethod]
    public void SdrSource_SdrOnlyClient_DirectPlayWithoutRangeReason()
    {
        StreamInfo stream = Build(VideoRangeType.SDR, "SDR");

        Assert.AreEqual(PlayMethod.DirectStream, stream.PlayMethod, "非强制路径下 video 的直接播放由 DirectStream 表示");
        Assert.IsFalse(HasRangeReason(stream), "SDR→SDR 不应因动态范围转码");
    }

    [TestMethod]
    public void SdrSource_ClientDeclaringOnlyHdr_DirectPlayWithoutRangeReason()
    {
        StreamInfo stream = Build(VideoRangeType.SDR, "HDR10", condition: ProfileConditionType.Equals);

        Assert.AreEqual(PlayMethod.DirectStream, stream.PlayMethod, "非强制路径下 video 的直接播放由 DirectStream 表示");
        Assert.IsFalse(HasRangeReason(stream), "SDR 是基线，客户端只声明 HDR 不应把 SDR 源判转码");
    }

    [TestMethod]
    public void HdrSource_SdrOnlyClient_TranscodesWithRangeReason()
    {
        StreamInfo stream = Build(VideoRangeType.HDR10, "SDR");

        Assert.AreEqual(PlayMethod.Transcode, stream.PlayMethod);
        Assert.IsTrue(HasRangeReason(stream), "HDR→SDR 必须带 VideoRangeTypeNotSupported，实际=" + string.Join(",", stream.TranscodeReasons));
    }

    [TestMethod]
    public void HdrSource_ClientDeclaringHdr_DirectPlayWithoutRangeReason()
    {
        StreamInfo stream = Build(VideoRangeType.HDR10, "HDR", condition: ProfileConditionType.Equals);

        Assert.AreEqual(PlayMethod.DirectStream, stream.PlayMethod, "非强制路径下 video 的直接播放由 DirectStream 表示");
        Assert.IsFalse(HasRangeReason(stream));
    }

    [TestMethod]
    public void HlgSource_ClientDeclaringHlg_DirectPlayWithoutRangeReason()
    {
        StreamInfo stream = Build(VideoRangeType.HLG, "HDR10|HLG");

        Assert.AreEqual(PlayMethod.DirectStream, stream.PlayMethod, "非强制路径下 video 的直接播放由 DirectStream 表示");
        Assert.IsFalse(HasRangeReason(stream));
    }

    [TestMethod]
    public void HdrSource_NoRangeCondition_DirectPlayWithoutRangeReason()
    {
        StreamInfo stream = Build(VideoRangeType.HDR10, declaredValue: null);

        Assert.AreEqual(PlayMethod.DirectStream, stream.PlayMethod, "没声明条件时行为必须与改前一致");
        Assert.IsFalse(HasRangeReason(stream));
    }

    [TestMethod]
    public void HdrSource_SdrOnlyClient_PlaybackInfoMediaSourceExposesRangeReason()
    {
        StreamInfo stream = Build(VideoRangeType.HDR10, "SDR");
        MediaSourceInfo visibleSource = new MediaSourceInfo();

        // MediaInfoService.GetPostedPlaybackInfo -> SetDeviceSpecificData -> SetDeviceSpecificSubtitleInfo
        // 是 PlaybackInfo 把 StreamInfo.TranscodeReasons 写入客户端可见 MediaSourceInfo 的唯一一步。
        MethodInfo handoff = typeof(MediaInfoService).GetMethod(
            "SetDeviceSpecificSubtitleInfo",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(handoff, "PlaybackInfo 的 reason 交接点不存在");
        handoff.Invoke(
            new MediaInfoService(null, null, null, null, null, null, null, null, null, null),
            new object[] { stream, visibleSource, "token" });

        Assert.IsTrue(HasRangeReason(stream));
        Assert.IsTrue(
            visibleSource.TranscodeReasons.Contains(TranscodeReason.VideoRangeTypeNotSupported),
            "PlaybackInfo 返回的 MediaSourceInfo.TranscodeReasons 应包含该 reason，实际=" + string.Join(",", visibleSource.TranscodeReasons));
    }

    private static bool HasRangeReason(StreamInfo stream)
    {
        return stream.TranscodeReasons.Contains(TranscodeReason.VideoRangeTypeNotSupported);
    }

    private static StreamInfo Build(
        VideoRangeType sourceRange,
        string declaredValue,
        ProfileConditionType condition = ProfileConditionType.EqualsAny,
        string transfer = "smpte2084")
    {
        DeviceProfile profile = new DeviceProfile
        {
            Name = "range-test",
            DirectPlayProfiles = new[]
            {
                new DirectPlayProfile
                {
                    Type = DlnaProfileType.Video,
                    Container = "ts",
                    VideoCodec = "h264",
                    AudioCodec = "aac"
                }
            },
            TranscodingProfiles = new[]
            {
                new TranscodingProfile
                {
                    Type = DlnaProfileType.Video,
                    Context = EncodingContext.Streaming,
                    Container = "ts",
                    VideoCodec = "h264",
                    AudioCodec = "aac",
                    Protocol = "http"
                }
            },
            SubtitleProfiles = Array.Empty<SubtitleProfile>()
        };

        if (declaredValue != null)
        {
            profile.CodecProfiles = new[]
            {
                new CodecProfile
                {
                    Type = CodecType.Video,
                    Codec = "h264",
                    Container = "ts",
                    Conditions = new[]
                    {
                        new ProfileCondition(condition, ProfileConditionValue.VideoRangeType, declaredValue, isRequired: true)
                    }
                }
            };
        }

        MediaSourceInfo mediaSource = new MediaSourceInfo
        {
            Id = "source-1",
            Path = "/media/sample.ts",
            Container = "ts",
            Protocol = MediaProtocol.File,
            Bitrate = 4000000,
            SupportsDirectPlay = true,
            SupportsDirectStream = true,
            SupportsTranscoding = true,
            MediaStreams =
            {
                new MediaStream
                {
                    Type = MediaStreamType.Video,
                    Index = 0,
                    Codec = "h264",
                    VideoRangeType = sourceRange,
                    ColorTransfer = transfer
                },
                new MediaStream
                {
                    Type = MediaStreamType.Audio,
                    Index = 1,
                    Codec = "aac",
                    Channels = 2,
                    BitRate = 128000
                }
            }
        };

        VideoOptions options = new VideoOptions
        {
            ItemId = Guid.NewGuid(),
            DeviceId = "range-test-device",
            Profile = profile,
            MediaSources = new[] { mediaSource }
        };

        return new StreamBuilder(new NullLogger()).BuildVideoItem(options);
    }
}
