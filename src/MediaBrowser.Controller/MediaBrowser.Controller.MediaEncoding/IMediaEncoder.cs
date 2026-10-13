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

	bool SupportsDecoder(string decoder);

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
