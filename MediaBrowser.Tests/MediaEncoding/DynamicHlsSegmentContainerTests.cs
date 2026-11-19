using System.Collections.Generic;
using Emby.Server.MediaEncoding.Api;
using Emby.Server.MediaEncoding.Api.Hls;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Dlna;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Tests.TestDoubles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.MediaEncoding;

/// <summary>
/// fMP4 HLS 的两种分片容器（m4s / mp4）都必须生成 -hls_segment_type fmp4。
/// </summary>
[TestClass]
public class DynamicHlsSegmentContainerTests
{
    [DataTestMethod]
    [DataRow("m4s")]
    [DataRow("mp4")]
    public void Fmp4SegmentContainer_UsesFmp4SegmentType(string segmentContainer)
    {
        string arguments = BuildArguments(segmentContainer);

        StringAssert.Contains(arguments, "-hls_segment_type fmp4");
    }

    private static string BuildArguments(string segmentContainer)
    {
        var fileSystem = ProxyStub<IFileSystem>.Create(
            ("GetDirectoryName", _ => "/tmp/transcode"),
            ("GetFileNameWithoutExtension", _ => "stream"));
        var appPaths = ProxyStub<IServerApplicationPaths>.Create();
        var serverConfig = ProxyStub<IServerConfigurationManager>.Create(
            ("get_ApplicationPaths", _ => appPaths));
        var mediaEncoder = ProxyStub<IMediaEncoder>.Create(
            ("GetInputArgument", _ => "-i /tmp/in.mp4"));

        var service = new TestableDynamicHlsService(
            serverConfig, null, null, null, mediaEncoder, fileSystem,
            null, null, null, null, null, null, null, null, null, null);

        var state = new StreamState(null, null, TranscodingJobType.Hls, true)
        {
            Request = new StreamRequest
            {
                SegmentContainer = segmentContainer,
                SegmentLength = 6
            },
            MediaSource = new MediaSourceInfo(),
            MediaPath = "/tmp/in.mp4",
            OutputVideoCodec = "copy",
            OutputAudioCodec = "copy",
            RemoteHttpHeaders = new Dictionary<string, string>()
        };

        return service.BuildCommandLineArguments("/tmp/transcode/stream.m3u8", new EncodingOptions(), state);
    }

    private sealed class TestableDynamicHlsService : DynamicHlsService
    {
        public TestableDynamicHlsService(
            IServerConfigurationManager serverConfig,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IIsoManager isoManager,
            IMediaEncoder mediaEncoder,
            IFileSystem fileSystem,
            IDlnaManager dlnaManager,
            ISubtitleEncoder subtitleEncoder,
            IDeviceManager deviceManager,
            IMediaSourceManager mediaSourceManager,
            IZipClient zipClient,
            IJsonSerializer jsonSerializer,
            IAuthorizationContext authorizationContext,
            INetworkManager networkManager,
            IServerApplicationHost appHost,
            IAssemblyInfo assemblyInfo)
            : base(
                serverConfig, userManager, libraryManager, isoManager, mediaEncoder, fileSystem,
                dlnaManager, subtitleEncoder, deviceManager, mediaSourceManager, zipClient,
                jsonSerializer, authorizationContext, networkManager, appHost, assemblyInfo)
        {
        }

        public string BuildCommandLineArguments(string outputPath, EncodingOptions options, StreamState state)
        {
            return GetCommandLineArguments(outputPath, options, state, true);
        }
    }
}
