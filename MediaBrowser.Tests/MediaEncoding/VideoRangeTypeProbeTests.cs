using System;
using System.Linq;
using System.Text.Json;
using Emby.Server.MediaEncoding.Probing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JsonSerializerImpl = Emby.Common.Implementations.Serialization.JsonSerializer;

namespace MediaBrowser.Tests.MediaEncoding;

/// <summary>用真实形状的 ffprobe JSON 验证 VideoRangeType 映射（含 DoVi side data）。</summary>
[TestClass]
public class VideoRangeTypeProbeTests
{
    private const string ProbeTemplate = """
        {
          "streams": [
            {
              "index": 0,
              "codec_name": "hevc",
              "codec_type": "video",
              "profile": "Main 10",
              "width": 3840,
              "height": 2160,
              "pix_fmt": "yuv420p10le",
              "color_range": "tv",
              "color_space": "bt2020nc",
              "color_transfer": "__TRANSFER__",
              "color_primaries": "__PRIMARIES__",
              "bits_per_raw_sample": "10",
              "r_frame_rate": "24000/1001",
              "avg_frame_rate": "24000/1001",
              "duration": "10.000000",
              "bit_rate": "20000000"
              __SIDEDATA__
            }
          ],
          "format": {
            "format_name": "mov,mp4,m4a,3gp,3g2,mj2",
            "duration": "10.000000",
            "bit_rate": "20000000"
          }
        }
        """;

    [TestMethod]
    public void SdrJson_MapsToSdr()
    {
        Assert.AreEqual(VideoRangeType.SDR, ProbeRange("bt709", "bt709"));
    }

    [TestMethod]
    public void Hdr10Json_MapsToHdr10()
    {
        Assert.AreEqual(VideoRangeType.HDR10, ProbeRange("smpte2084", "bt2020"));
    }

    [TestMethod]
    public void HlgJson_MapsToHlg()
    {
        Assert.AreEqual(VideoRangeType.HLG, ProbeRange("arib-std-b67", "bt2020"));
    }

    [TestMethod]
    public void DolbyVisionWithHdr10Base_MapsToDolbyVisionWithHdr10()
    {
        const string sideData = """
            ,"side_data_list": [
              {
                "side_data_type": "DOVI configuration record",
                "dv_profile": 7,
                "dv_level": 6,
                "rpu_present": 1,
                "el_present": 1,
                "bl_present": 1
              }
            ]
            """;

        Assert.AreEqual(VideoRangeType.DolbyVisionWithHDR10, ProbeRange("smpte2084", "bt2020", sideData));
    }

    [TestMethod]
    public void DolbyVisionSingleLayer_MapsToDolbyVision()
    {
        const string sideData = """
            ,"side_data_list": [
              {
                "side_data_type": "DOVI configuration record",
                "dv_profile": 5,
                "dv_level": 6,
                "rpu_present": 1,
                "el_present": 0,
                "bl_present": 1
              }
            ]
            """;

        Assert.AreEqual(VideoRangeType.DolbyVision, ProbeRange("smpte2084", "bt2020", sideData));
    }

    [TestMethod]
    public void DolbyVisionWithSdrBase_MapsToDolbyVisionWithSdr()
    {
        const string sideData = """
            ,"side_data_list": [
              {
                "side_data_type": "DOVI configuration record",
                "dv_profile": 8,
                "dv_level": 4,
                "rpu_present": 1,
                "el_present": 0,
                "bl_present": 1
              }
            ]
            """;

        Assert.AreEqual(VideoRangeType.DolbyVisionWithSDR, ProbeRange("bt709", "bt709", sideData));
    }

    private static VideoRangeType ProbeRange(string transfer, string primaries, string sideData = null)
    {
        string json = ProbeTemplate
            .Replace("__TRANSFER__", transfer, StringComparison.Ordinal)
            .Replace("__PRIMARIES__", primaries, StringComparison.Ordinal)
            .Replace("__SIDEDATA__", sideData ?? string.Empty, StringComparison.Ordinal);
        var data = JsonSerializer.Deserialize<InternalMediaInfoResult>(json, JsonSerializerImpl.Options);
        var normalizer = new ProbeResultNormalizer(new NullLogger(), null);

        var info = normalizer.GetMediaInfo(data, VideoType.VideoFile, isAudio: false, "/movie.mkv", MediaProtocol.File);

        return info.MediaStreams.Single(stream => stream.Type == MediaStreamType.Video).VideoRangeType;
    }
}
