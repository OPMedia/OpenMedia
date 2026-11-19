using System;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Tests.TestDoubles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.MediaEncoding;

/// <summary>
/// AMF 没有 MPEG-2 解码器：必须软件解码，且不能把 mpeg2video 留在硬件解码列表里误导上层。
/// </summary>
[TestClass]
public class AmfDecoderTests
{
    [TestMethod]
    public void Mpeg2Video_WithAmf_FallsBackToSoftwareDecoding()
    {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            Assert.Inconclusive("Windows 下 AMF 分支走 dxva2，非本用例覆盖的 Linux 路径");
        }

        // 即使 ffmpeg 谎称支持 mpeg2_amf/hevc_amf，也不能选中不存在的 AMF MPEG-2 解码器。
        var mediaEncoder = ProxyStub<IMediaEncoder>.Create(("SupportsDecoder", _ => true));
        var helper = new TestableEncodingHelper(mediaEncoder);
        var state = new EncodingJobInfo(null, null, TranscodingJobType.Progressive)
        {
            OutputVideoCodec = "h264",
            MediaSource = new MediaBrowser.Model.Dto.MediaSourceInfo(),
            VideoStream = new MediaStream { Codec = "mpeg2video" }
        };
        var options = new EncodingOptions
        {
            HardwareAccelerationType = "amf",
            HardwareDecodingCodecs = new[] { "h264", "mpeg2video" }
        };

        Assert.IsNull(helper.GetHardwareDecoder(state, options));
        CollectionAssert.AreEqual(new[] { "h264" }, options.HardwareDecodingCodecs);
    }

    private sealed class TestableEncodingHelper : EncodingHelper
    {
        public TestableEncodingHelper(IMediaEncoder mediaEncoder)
            : base(mediaEncoder, null, null, null, null)
        {
        }

        public string GetHardwareDecoder(EncodingJobInfo state, EncodingOptions options)
        {
            return GetHardwareAcceleratedVideoDecoder(state, options);
        }
    }
}
