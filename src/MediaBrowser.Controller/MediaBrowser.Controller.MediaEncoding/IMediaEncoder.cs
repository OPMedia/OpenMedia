using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Controller.MediaEncoding;

public interface IMediaEncoder : ITranscoderSupport
{
	string EncoderLocationType { get; }

	string EncoderPath { get; }

	/// <summary>探测到的 ffmpeg 版本；尚未探测或探测失败时为 null。</summary>
	Version? EncoderVersion { get; }

	/// <summary>支持的最低 ffmpeg 版本；低于该版本在 Init() 时直接报错。</summary>
	Version MinimumEncoderVersion { get; }

	bool SupportsDecoder(string decoder);

	/// <summary>ffmpeg -filters 中是否提供该滤镜。</summary>
	bool SupportsFilter(string filter);

	/// <summary>该滤镜是否提供 option；value 非空时还要求帮助文本列出该取值。</summary>
	bool SupportsFilterWithOption(string filter, string option, string value = null);

	/// <summary>ffmpeg -hwaccels 中是否提供该硬解方式。</summary>
	bool SupportsHwaccel(string hwaccel);

	/// <summary>该 bitstream filter 是否提供 option；value 非空时还要求帮助文本列出该取值。</summary>
	bool SupportsBitStreamFilterWithOption(string name, string option, string value = null);

	Task<string> ExtractAudioImage(string path, int? imageStreamIndex, CancellationToken cancellationToken);

	Task<string> ExtractVideoImage(string[] inputFiles, string container, MediaProtocol protocol, MediaStream videoStream, Video3DFormat? threedFormat, TimeSpan? offset, CancellationToken cancellationToken);

	Task<string> ExtractVideoImage(string[] inputFiles, string container, MediaProtocol protocol, MediaStream imageStream, int? imageStreamIndex, CancellationToken cancellationToken);

	Task ExtractVideoImagesOnInterval(string[] inputFiles, string container, MediaStream videoStream, MediaProtocol protocol, Video3DFormat? threedFormat, TimeSpan interval, string targetDirectory, string filenamePrefix, int? maxWidth, CancellationToken cancellationToken);

	Task<MediaInfo> GetMediaInfo(MediaInfoRequest request, CancellationToken cancellationToken);

	string GetInputArgument(string[] inputFiles, MediaProtocol protocol);

	string GetTimeParameter(long ticks);

	Task<string> EncodeAudio(EncodingJobOptions options, IProgress<double> progress, CancellationToken cancellationToken);

	Task<string> EncodeVideo(EncodingJobOptions options, IProgress<double> progress, CancellationToken cancellationToken);

	Task ConvertImage(string inputPath, string outputPath);

	string EscapeSubtitleFilterPath(string path);

	void Init();

	void UpdateEncoderPath(string path, string pathType);

	bool SupportsEncoder(string encoder);

	string[] GetPlayableStreamFileNames(string path, VideoType videoType);

	IEnumerable<string> GetPrimaryPlaylistVobFiles(string path, IIsoMount isoMount, uint? titleNumber);
}
