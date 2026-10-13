using System;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Dlna;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;

namespace Emby.Server.MediaEncoding.Api.Hls;

public class VideoHlsService : BaseHlsService
{
	public object Get(GetLiveHlsStream request)
	{
		return ProcessRequest(request);
	}

	protected override string GetOutputFilePath(StreamState state, EncodingOptions encodingOptions, string outputFileExtension)
	{
		string playSessionId = state.Request.PlaySessionId;
		if (!string.IsNullOrEmpty(playSessionId))
		{
			TranscodingJob transcodingJob = ApiEntryPoint.Instance.GetTranscodingJob(playSessionId);
			if (transcodingJob != null)
			{
				return transcodingJob.Path;
			}
		}
		return base.GetOutputFilePath(state, encodingOptions, outputFileExtension);
	}

	protected override string GetAudioArguments(StreamState state, EncodingOptions encodingOptions)
	{
		string audioEncoder = EncodingHelper.GetAudioEncoder((EncodingJobInfo)(object)state);
		if (string.Equals(audioEncoder, "copy", StringComparison.OrdinalIgnoreCase))
		{
			return "-codec:a:0 copy";
		}
		string text = "-codec:a:0 " + audioEncoder;
		int? outputAudioChannels = ((EncodingJobInfo)state).OutputAudioChannels;
		if (outputAudioChannels.HasValue)
		{
			text = text + " -ac " + outputAudioChannels.Value;
		}
		int? outputAudioBitrate = ((EncodingJobInfo)state).OutputAudioBitrate;
		if (outputAudioBitrate.HasValue)
		{
			text = text + " -ab " + outputAudioBitrate.Value.ToString(UsCulture);
		}
		if (((EncodingJobInfo)state).OutputAudioSampleRate.HasValue)
		{
			text = text + " -ar " + ((EncodingJobInfo)state).OutputAudioSampleRate.Value.ToString(UsCulture);
		}
		return text + " " + EncodingHelper.GetAudioFilterParam((EncodingJobInfo)(object)state, encodingOptions, true);
	}

	protected override string GetVideoArguments(StreamState state, EncodingOptions encodingOptions)
	{
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Invalid comparison between Unknown and I4
		if (!((EncodingJobInfo)state).IsVideoRequest)
		{
			return string.Empty;
		}
		string videoEncoder = EncodingHelper.GetVideoEncoder((EncodingJobInfo)(object)state, encodingOptions);
		string text = "-codec:v:0 " + videoEncoder;
		if (((BaseEncodingJobOptions)state.Request).EnableMpegtsM2TsMode)
		{
			text += " -mpegts_m2ts_mode 1";
		}
		if (videoEncoder.Equals("copy", StringComparison.OrdinalIgnoreCase))
		{
			if (((EncodingJobInfo)state).VideoStream != null && EncodingHelper.IsH264(((EncodingJobInfo)state).VideoStream) && !string.Equals(((EncodingJobInfo)state).VideoStream.NalLengthSize, "0", StringComparison.OrdinalIgnoreCase))
			{
				text += " -bsf:v h264_mp4toannexb";
			}
		}
		else
		{
			string text2 = $" -force_key_frames \"expr:gte(t,n_forced*{state.SegmentLength.ToString(UsCulture)})\"";
			bool num = ((EncodingJobInfo)state).SubtitleStream != null && !((EncodingJobInfo)state).SubtitleStream.IsTextSubtitleStream && (int)((EncodingJobInfo)state).SubtitleDeliveryMethod == 0;
			text = text + " " + EncodingHelper.GetVideoQualityParam((EncodingJobInfo)(object)state, videoEncoder, encodingOptions, GetDefaultH264Preset()) + text2;
			if (!num)
			{
				text += EncodingHelper.GetOutputSizeParam((EncodingJobInfo)(object)state, encodingOptions, videoEncoder, true);
			}
			if (num)
			{
				text += EncodingHelper.GetGraphicalSubtitleParam((EncodingJobInfo)(object)state, encodingOptions, videoEncoder);
			}
		}
		text += " -flags -global_header";
		if (!string.IsNullOrEmpty(((EncodingJobInfo)state).OutputVideoSync))
		{
			text = text + " -vsync " + ((EncodingJobInfo)state).OutputVideoSync;
		}
		return text + EncodingHelper.GetOutputFFlags((EncodingJobInfo)(object)state);
	}

	public VideoHlsService(IServerConfigurationManager serverConfig, IUserManager userManager, ILibraryManager libraryManager, IIsoManager isoManager, IMediaEncoder mediaEncoder, IFileSystem fileSystem, IDlnaManager dlnaManager, ISubtitleEncoder subtitleEncoder, IDeviceManager deviceManager, IMediaSourceManager mediaSourceManager, IZipClient zipClient, IJsonSerializer jsonSerializer, IAuthorizationContext authorizationContext, IServerApplicationHost appHost, IAssemblyInfo assemblyInfo)
		: base(serverConfig, userManager, libraryManager, isoManager, mediaEncoder, fileSystem, dlnaManager, subtitleEncoder, deviceManager, mediaSourceManager, zipClient, jsonSerializer, authorizationContext, appHost, assemblyInfo)
	{
	}
}
