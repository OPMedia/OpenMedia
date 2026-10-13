using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using System;
using System.Collections.Generic;

namespace MediaBrowser.Api
{
    /// <summary>
    /// Emby 4.1 encoding information endpoints.
    /// </summary>
    public class VideoCodecInfo
    {
        public string Name { get; set; }

        public string CodecKind { get; set; }

        public string Direction { get; set; }

        public string MediaTypeName { get; set; }

        public string Description { get; set; }

        public bool? IsHardwareCodec { get; set; }

        public bool? IsEnabledByDefault { get; set; }

        public int? DefaultPriority { get; set; }
    }

    public class CodecConfigurationInfo
    {
        public string CodecId { get; set; }

        public bool? IsEnabled { get; set; }

        public int? Priority { get; set; }
    }

    [Route("/Encoding/CodecInformation/Video", "GET", Summary = "Gets video codec information")]
    [Authenticated]
    public class GetVideoCodecInformation : IReturn<List<VideoCodecInfo>>
    {
    }

    [Route("/Encoding/CodecConfiguration/Defaults", "GET", Summary = "Gets default codec configuration")]
    [Authenticated]
    public class GetCodecConfigurationDefaults : IReturn<List<CodecConfigurationInfo>>
    {
    }

    [Authenticated]
    public class EncodingService : BaseApiService
    {
        private readonly IMediaEncoder _mediaEncoder;

        public EncodingService(IMediaEncoder mediaEncoder)
        {
            _mediaEncoder = mediaEncoder;
        }

        public object Get(GetVideoCodecInformation request)
        {
            // The 4.1 codec registry (hardware encoders, profiles, level info) has no 3.5.3 counterpart.
            // Expose the endpoint contract; the dashboard transcode page tolerates an empty list.
            var codecs = new List<VideoCodecInfo>
            {
                new VideoCodecInfo
                {
                    Name = "h264",
                    CodecKind = "Video",
                    Direction = "Encoder",
                    MediaTypeName = "h264",
                    Description = "H.264 (software via ffmpeg)",
                    IsHardwareCodec = false,
                    IsEnabledByDefault = true,
                    DefaultPriority = 0
                },
                new VideoCodecInfo
                {
                    Name = "hevc",
                    CodecKind = "Video",
                    Direction = "Encoder",
                    MediaTypeName = "hevc",
                    Description = "HEVC (software via ffmpeg)",
                    IsHardwareCodec = false,
                    IsEnabledByDefault = true,
                    DefaultPriority = 0
                }
            };

            return ToOptimizedResult(codecs);
        }

        public object Get(GetCodecConfigurationDefaults request)
        {
            // No persisted codec configuration exists in 3.5.3; return defaults.
            return ToOptimizedResult(new List<CodecConfigurationInfo>());
        }
    }
}
