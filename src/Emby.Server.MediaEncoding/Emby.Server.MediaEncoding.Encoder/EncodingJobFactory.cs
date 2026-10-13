using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace Emby.Server.MediaEncoding.Encoder;

public class EncodingJobFactory
{
	private readonly ILogger _logger;

	private readonly ILibraryManager _libraryManager;

	private readonly IMediaSourceManager _mediaSourceManager;

	private readonly IConfigurationManager _config;

	private readonly IMediaEncoder _mediaEncoder;

	protected static readonly CultureInfo UsCulture = new CultureInfo("en-US");

	public EncodingJobFactory(ILogger logger, ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager, IConfigurationManager config, IMediaEncoder mediaEncoder)
	{
		_logger = logger;
		_libraryManager = libraryManager;
		_mediaSourceManager = mediaSourceManager;
		_config = config;
		_mediaEncoder = mediaEncoder;
	}

	public async Task<EncodingJobInfo> CreateJob(EncodingJobOptions options, EncodingHelper encodingHelper, bool isVideoRequest, IProgress<double> progress, CancellationToken cancellationToken)
	{
		EncodingJobOptions request = options;
		if (string.IsNullOrEmpty(((BaseEncodingJobOptions)request).AudioCodec))
		{
			((BaseEncodingJobOptions)request).AudioCodec = encodingHelper.InferAudioCodec(((BaseEncodingJobOptions)request).Container);
		}
		EncodingJobInfo state = new EncodingJobInfo(_logger, _mediaSourceManager, (TranscodingJobType)0)
		{
			BaseRequest = (BaseEncodingJobOptions)(object)options,
			IsVideoRequest = isVideoRequest,
			Progress = progress
		};
		if (!string.IsNullOrWhiteSpace(((BaseEncodingJobOptions)request).VideoCodec))
		{
			state.SupportedVideoCodecs = ((BaseEncodingJobOptions)request).VideoCodec.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			((BaseEncodingJobOptions)request).VideoCodec = state.SupportedVideoCodecs.FirstOrDefault();
		}
		if (!string.IsNullOrWhiteSpace(((BaseEncodingJobOptions)request).SubtitleCodec))
		{
			state.SupportedSubtitleCodecs = ((BaseEncodingJobOptions)request).SubtitleCodec.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			((BaseEncodingJobOptions)request).SubtitleCodec = state.SupportedSubtitleCodecs.FirstOrDefault((string i) => ((ITranscoderSupport)_mediaEncoder).CanEncodeToSubtitleCodec(i)) ?? state.SupportedSubtitleCodecs.FirstOrDefault();
		}
		BaseItem itemById = _libraryManager.GetItemById(((BaseEncodingJobOptions)request).Id);
		state.IsInputVideo = string.Equals(itemById.MediaType, "Video", StringComparison.OrdinalIgnoreCase);
		ItemImageInfo val = itemById.GetImageInfo((ImageType)0, 0) ?? (from i in itemById.GetParents()
			select i.GetImageInfo((ImageType)0, 0)).FirstOrDefault((ItemImageInfo i) => i != null);
		if (val != null)
		{
			state.AlbumCoverPath = val.Path;
		}
		List<MediaSourceInfo> source = await _mediaSourceManager.GetPlayackMediaSources(itemById, (User)null, true, false, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		MediaSourceInfo val2 = (string.IsNullOrEmpty(((BaseEncodingJobOptions)request).MediaSourceId) ? source.First() : source.First((MediaSourceInfo i) => string.Equals(i.Id, ((BaseEncodingJobOptions)request).MediaSourceId)));
		BaseEncodingJobOptions baseRequest = state.BaseRequest;
		encodingHelper.AttachMediaSourceInfo(state, val2, (string)null);
		state.OutputAudioBitrate = encodingHelper.GetAudioBitrateParam(state.BaseRequest, state.AudioStream);
		state.OutputAudioCodec = state.BaseRequest.AudioCodec;
		state.OutputAudioChannels = encodingHelper.GetNumAudioChannelsParam(state, state.AudioStream, state.OutputAudioCodec);
		if (baseRequest != null)
		{
			state.OutputVideoCodec = state.BaseRequest.VideoCodec;
			state.OutputVideoBitrate = encodingHelper.GetVideoBitrateParamValue(state.BaseRequest, state.VideoStream, state.OutputVideoCodec);
			encodingHelper.TryStreamCopy(state);
			if (state.OutputVideoBitrate.HasValue && !string.Equals(state.OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				ResolutionOptions val3 = ResolutionNormalizer.Normalize((state.VideoStream == null) ? ((int?)null) : state.VideoStream.BitRate, (state.VideoStream == null) ? ((int?)null) : state.VideoStream.Width, (state.VideoStream == null) ? ((int?)null) : state.VideoStream.Height, state.OutputVideoBitrate.Value, (state.VideoStream == null) ? null : state.VideoStream.Codec, state.OutputVideoCodec, baseRequest.MaxWidth, baseRequest.MaxHeight);
				baseRequest.MaxWidth = val3.MaxWidth;
				baseRequest.MaxHeight = val3.MaxHeight;
			}
		}
		ApplyDeviceProfileSettings(state);
		return state;
	}

	protected EncodingOptions GetEncodingOptions()
	{
		return ConfigurationManagerExtensions.GetConfiguration<EncodingOptions>(_config, "encoding");
	}

	private void ApplyDeviceProfileSettings(EncodingJobInfo state)
	{
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		BaseEncodingJobOptions baseRequest = state.BaseRequest;
		EncodingJobOptions val = (EncodingJobOptions)(object)((baseRequest is EncodingJobOptions) ? baseRequest : null);
		DeviceProfile val2 = ((val == null) ? null : val.DeviceProfile);
		if (val2 != null)
		{
			string actualOutputAudioCodec = state.ActualOutputAudioCodec;
			string actualOutputVideoCodec = state.ActualOutputVideoCodec;
			string container = state.BaseRequest.Container;
			ResponseProfile val3 = (state.IsVideoRequest ? val2.GetAudioMediaProfile(container, actualOutputAudioCodec, state.OutputAudioChannels, state.OutputAudioBitrate, state.OutputAudioSampleRate, state.OutputAudioBitDepth) : val2.GetVideoMediaProfile(container, actualOutputAudioCodec, actualOutputVideoCodec, state.OutputWidth, state.OutputHeight, state.TargetVideoBitDepth, state.OutputVideoBitrate, state.TargetVideoProfile, state.TargetVideoLevel, state.TargetFramerate ?? 0f, state.TargetPacketLength, state.TargetTimestamp, state.IsTargetAnamorphic, state.IsTargetInterlaced, state.TargetRefFrames, state.TargetVideoStreamCount, state.TargetAudioStreamCount, state.TargetVideoCodecTag, state.IsTargetAVC));
			if (val3 != null)
			{
				state.MimeType = val3.MimeType;
			}
		}
	}
}
