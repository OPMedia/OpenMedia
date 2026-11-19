using System;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Tests.TestDoubles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.MediaEncoding;

/// <summary>
/// HEVC/AV1 输出必须映射到 ffmpeg 真正认识的编码器名，并在能力探测失败时保持明确回退。
/// </summary>
[TestClass]
public class EncodingHelperCodecTests
{
    [TestMethod]
    public void HevcOutput_UsesLibx265()
    {
        var helper = CreateHelper();

        Assert.AreEqual("libx265", helper.GetVideoEncoder(CreateState("hevc"), new EncodingOptions()));
    }

    [TestMethod]
    public void Av1Output_PrefersLibsvtav1()
    {
        var helper = CreateHelper("libsvtav1", "libaom-av1");

        Assert.AreEqual("libsvtav1", helper.GetVideoEncoder(CreateState("av1"), new EncodingOptions()));
    }

    [TestMethod]
    public void Av1Output_FallsBackToLibaomAv1()
    {
        var helper = CreateHelper("libaom-av1");

        // libsvtav1 缺失时退到 libaom-av1；两者都没有时也保留合法编码器名，
        // 让 ffmpeg 明确报错，而不是回落到无效名或被静默改成 copy。
        Assert.AreEqual("libaom-av1", helper.GetVideoEncoder(CreateState("av1"), new EncodingOptions()));
        Assert.AreEqual("libaom-av1", CreateHelper().GetVideoEncoder(CreateState("av1"), new EncodingOptions()));
    }

    private static EncodingHelper CreateHelper(params string[] supportedEncoders)
    {
        var mediaEncoder = ProxyStub<IMediaEncoder>.Create(
            ("SupportsEncoder", args => Array.IndexOf(supportedEncoders, (string)args[0]) >= 0));

        return new EncodingHelper(mediaEncoder, null, null, null, null);
    }

    private static EncodingJobInfo CreateState(string outputVideoCodec)
    {
        return new EncodingJobInfo(null, null, TranscodingJobType.Progressive)
        {
            OutputVideoCodec = outputVideoCodec
        };
    }
}
