using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Tests.TestDoubles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.MediaEncoding;

/// <summary>CPU 色调映射的触发条件、滤镜链与能力门禁。</summary>
[TestClass]
public class TonemappingTests
{
    [TestMethod]
    public void HdrToSdr_WithBothFilters_ProducesTonemapChain()
    {
        var chain = CreateHelper().GetTonemapFilter(CreateState(VideoRangeType.HDR10, "smpte2084"), new EncodingOptions());

        StringAssert.Contains(chain, "zscale=t=linear:npl=1000");
        StringAssert.Contains(chain, "format=gbrpf32le");
        StringAssert.Contains(chain, "zscale=p=bt709");
        StringAssert.Contains(chain, "tonemap=tonemap=hable:desat=0.5");
        StringAssert.Contains(chain, "zscale=t=bt709:m=bt709:r=tv,format=yuv420p");
    }

    [DataTestMethod]
    [DataRow("mobius")]
    [DataRow("reinhard")]
    [DataRow("bt2390")]
    [DataRow("none")]
    public void HdrToSdr_UsesConfiguredAlgorithm(string algorithm)
    {
        var options = new EncodingOptions { TonemappingAlgorithm = algorithm };

        StringAssert.Contains(CreateHelper().GetTonemapFilter(CreateState(VideoRangeType.HDR10, "smpte2084"), options), "tonemap=tonemap=" + algorithm + ":");
    }

    [TestMethod]
    public void SdrToSdr_NoFilter()
    {
        Assert.AreEqual(string.Empty, CreateHelper().GetTonemapFilter(CreateState(VideoRangeType.SDR, "bt709"), new EncodingOptions()));
    }

    [TestMethod]
    public void HdrToHdr_NoFilter()
    {
        var state = CreateState(VideoRangeType.HDR10, "smpte2084");
        state.BaseRequest = new BaseEncodingJobOptions { Static = true };

        Assert.AreEqual("HDR", state.TargetVideoRange);
        Assert.AreEqual(string.Empty, CreateHelper().GetTonemapFilter(state, new EncodingOptions()));
    }

    [TestMethod]
    public void DisabledTonemapping_NoFilter()
    {
        var options = new EncodingOptions { EnableTonemapping = false };

        Assert.AreEqual(string.Empty, CreateHelper().GetTonemapFilter(CreateState(VideoRangeType.HDR10, "smpte2084"), options));
    }

    [DataTestMethod]
    [DataRow(false, true, "zscale")]
    [DataRow(true, false, "tonemap")]
    [DataRow(false, false, "zscale 和 tonemap")]
    public void MissingFilterCapability_NoFilter_AndWarns(bool hasZscale, bool hasTonemap, string expectedMissing)
    {
        var logger = new RecordingLogger();
        var helper = CreateHelper(hasZscale, hasTonemap, logger: logger);

        string chain = helper.GetTonemapFilter(CreateState(VideoRangeType.HDR10, "smpte2084"), new EncodingOptions());

        Assert.AreEqual(string.Empty, chain);
        Assert.IsTrue(logger.Warnings.Any(warning => warning.Contains(expectedMissing, StringComparison.Ordinal)),
            "缺少滤镜必须留下 Warn，不能静默降级到发灰画面：" + string.Join(" | ", logger.Warnings));
    }

    [TestMethod]
    public void DoViBaseLayer_UsesHdrChain_AndLogsUnsupportedDynamicMetadata()
    {
        var logger = new RecordingLogger();

        string chain = CreateHelper(logger: logger).GetTonemapFilter(
            CreateState(VideoRangeType.DolbyVisionWithHDR10, "smpte2084"), new EncodingOptions());

        StringAssert.Contains(chain, "tonemap=tonemap=hable");
        Assert.IsTrue(logger.Warnings.Any(warning => warning.Contains("RPU", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TonemapFilter_IsIncludedInVideoFilterChain()
    {
        var state = CreateState(VideoRangeType.HDR10, "smpte2084");
        state.MediaSource = new MediaSourceInfo();

        string args = CreateHelper().GetOutputSizeParam(state, new EncodingOptions(), "libx264");

        StringAssert.Contains(args, "tonemap=tonemap=hable");
    }

    [TestMethod]
    public void VaapiHwaccelMissing_NoInputInjection_AndWarns()
    {
        var logger = new RecordingLogger();
        var helper = CreateHelper(supportsEncoder: "h264_vaapi", supportsVaapi: false, logger: logger);
        var state = CreateState(VideoRangeType.SDR, "bt709");
        state.IsVideoRequest = true;
        state.OutputVideoCodec = "h264";
        var options = new EncodingOptions { HardwareAccelerationType = "vaapi", EnableHardwareEncoding = true };

        string args = helper.GetInputArgument(state, options);

        Assert.IsFalse(args.Contains("-hwaccel vaapi", StringComparison.Ordinal), args);
        Assert.IsTrue(logger.Warnings.Any(warning => warning.Contains("vaapi", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void VaapiHwaccelPresent_StillInjectsInputOptions()
    {
        var helper = CreateHelper(supportsEncoder: "h264_vaapi", supportsVaapi: true);
        var state = CreateState(VideoRangeType.SDR, "bt709");
        state.IsVideoRequest = true;
        state.OutputVideoCodec = "h264";
        var options = new EncodingOptions { HardwareAccelerationType = "vaapi", EnableHardwareEncoding = true };

        StringAssert.Contains(helper.GetInputArgument(state, options), "-hwaccel vaapi -hwaccel_output_format vaapi");
    }

    [TestMethod]
    public void Dxva2HwaccelMissing_NoDecoderInjection_AndWarns()
    {
        var logger = new RecordingLogger();
        var encoder = ProxyStub<IMediaEncoder>.Create(("SupportsHwaccel", args => false));

        Assert.IsNull(EncodingHelper.GetDxva2Decoder(encoder, PlatformID.Win32NT, logger));
        Assert.IsTrue(logger.Warnings.Any(warning => warning.Contains("dxva2", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Dxva2HwaccelPresent_ReturnsDecoderInjection()
    {
        var encoder = ProxyStub<IMediaEncoder>.Create(("SupportsHwaccel", args =>
            string.Equals((string)args[0], "dxva2", StringComparison.OrdinalIgnoreCase)));

        Assert.AreEqual("-hwaccel dxva2", EncodingHelper.GetDxva2Decoder(encoder, PlatformID.Win32NT, new RecordingLogger()));
        Assert.IsNull(EncodingHelper.GetDxva2Decoder(encoder, PlatformID.Unix, new RecordingLogger()));
    }

    private static EncodingHelper CreateHelper(bool hasZscale = true, bool hasTonemap = true, string supportsEncoder = null, bool supportsVaapi = false, RecordingLogger logger = null)
    {
        var mediaEncoder = ProxyStub<IMediaEncoder>.Create(
            ("SupportsFilter", args =>
            {
                string name = (string)args[0];
                if (string.Equals(name, "zscale", StringComparison.OrdinalIgnoreCase))
                {
                    return hasZscale;
                }

                return string.Equals(name, "tonemap", StringComparison.OrdinalIgnoreCase) && hasTonemap;
            }),
            ("SupportsEncoder", args => string.Equals((string)args[0], supportsEncoder, StringComparison.OrdinalIgnoreCase)),
            ("SupportsHwaccel", args => supportsVaapi && string.Equals((string)args[0], "vaapi", StringComparison.OrdinalIgnoreCase)),
            ("GetInputArgument", args => "-i " + string.Join(",", (string[])args[0])));

        return new EncodingHelper(mediaEncoder, null, null, null, null, logger);
    }

    private static EncodingJobInfo CreateState(VideoRangeType range, string transfer)
    {
        return new EncodingJobInfo(null, null, TranscodingJobType.Progressive)
        {
            BaseRequest = new BaseEncodingJobOptions(),
            VideoStream = new MediaStream
            {
                Type = MediaStreamType.Video,
                Codec = "hevc",
                ColorTransfer = transfer,
                VideoRangeType = range
            }
        };
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = new List<string>();

        public void Info(string message, params object[] paramList)
        {
        }

        public void Error(string message, params object[] paramList)
        {
        }

        public void Warn(string message, params object[] paramList)
        {
            Warnings.Add(paramList == null || paramList.Length == 0 ? message : string.Format(message, paramList));
        }

        public void Debug(string message, params object[] paramList)
        {
        }

        public void Fatal(string message, params object[] paramList)
        {
        }

        public void FatalException(string message, Exception exception, params object[] paramList)
        {
        }

        public void ErrorException(string message, Exception exception, params object[] paramList)
        {
        }

        public void Log(LogSeverity severity, string message, params object[] paramList)
        {
        }

        public void LogMultiline(string message, LogSeverity severity, StringBuilder additionalContent)
        {
        }
    }
}
