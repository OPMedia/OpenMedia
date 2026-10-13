using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Session;

namespace MediaBrowser.Controller.MediaEncoding;

public class EncodingJobInfo : IDisposable
{
	protected readonly ILogger Logger;

	protected readonly IMediaSourceManager MediaSourceManager;

	private TranscodeReason[] _transcodeReasons;

	public int? OutputAudioBitrate;

	public int? OutputAudioChannels;

	public TaskCompletionSource<bool> TaskCompletionSource;

	public bool HasExited { get; set; }

	public bool IsCancelled { get; set; }

	public MediaStream VideoStream { get; set; }

	public VideoType VideoType
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	public Dictionary<string, string> RemoteHttpHeaders { get; set; }

	public string OutputVideoCodec { get; set; }

	public MediaProtocol InputProtocol
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	public string MediaPath { get; set; }

	public bool IsInputVideo { get; set; }

	public IIsoMount IsoMount { get; set; }

	public string[] PlayableStreamFileNames { get; set; }

	public string OutputAudioCodec { get; set; }

	public int? OutputVideoBitrate { get; set; }

	public MediaStream SubtitleStream { get; set; }

	public SubtitleDeliveryMethod SubtitleDeliveryMethod
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	public string[] SupportedSubtitleCodecs { get; set; }

	public int InternalSubtitleStreamOffset { get; set; }

	public MediaSourceInfo MediaSource { get; set; }

	public User User { get; set; }

	public long? RunTimeTicks { get; set; }

	public bool ReadInputAtNativeFramerate { get; set; }

	public Stream LogFileStream { get; set; }

	public string OutputFilePath { get; set; }

	public string MimeType { get; set; }

	public long? EncodingDurationTicks { get; set; }

	public TranscodeReason[] TranscodeReasons
	{
		get
		{
			if (_transcodeReasons == null)
			{
				_transcodeReasons = (from i in (BaseRequest.TranscodeReasons ?? string.Empty).Split(new char[1] { ',' })
					where !string.IsNullOrEmpty(i)
					select i).Select((Func<string, TranscodeReason>)((string v) =>
				{
					//IL_0011: Unknown result type (might be due to invalid IL or missing references)
					return (TranscodeReason)Enum.Parse(typeof(TranscodeReason), v, ignoreCase: true);
				})).ToArray();
			}
			return _transcodeReasons;
		}
	}

	public bool IgnoreInputDts => MediaSource.IgnoreDts;

	public bool IgnoreInputIndex => MediaSource.IgnoreIndex;

	public bool GenPtsInput => MediaSource.GenPtsInput;

	public bool DiscardCorruptFramesInput => false;

	public bool EnableFastSeekInput => false;

	public bool GenPtsOutput => false;

	public string OutputContainer { get; set; }

	public string OutputVideoSync
	{
		get
		{
			if ((string.Equals(InputContainer, "mpegts", StringComparison.OrdinalIgnoreCase) || string.Equals(InputContainer, "ts", StringComparison.OrdinalIgnoreCase)) && !MediaSource.RunTimeTicks.HasValue)
			{
				return "cfr";
			}
			return "-1";
		}
	}

	public string AlbumCoverPath { get; set; }

	public string InputAudioSync { get; set; }

	public string InputVideoSync { get; set; }

	public TransportStreamTimestamp InputTimestamp
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	public MediaStream AudioStream { get; set; }

	public string[] SupportedAudioCodecs { get; set; }

	public string[] SupportedVideoCodecs { get; set; }

	public string InputContainer { get; set; }

	public IsoType? IsoType { get; set; }

	public BaseEncodingJobOptions BaseRequest { get; set; }

	public long? StartTimeTicks => BaseRequest.StartTimeTicks;

	public bool CopyTimestamps => BaseRequest.CopyTimestamps;

	public bool IsVideoRequest { get; set; }

	public TranscodingJobType TranscodingType { get; set; }

	public Guid Id { get; set; }

	public IProgress<double> Progress { get; set; }

	public bool IsSegmentedLiveStream
	{
		get
		{
			if (TranscodingType != TranscodingJobType.Progressive)
			{
				return !RunTimeTicks.HasValue;
			}
			return false;
		}
	}

	public int? TotalOutputBitrate => (OutputAudioBitrate ?? 0) + (OutputVideoBitrate ?? 0);

	public int? OutputWidth
	{
		get
		{
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0079: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
			if (VideoStream != null && VideoStream.Width.HasValue && VideoStream.Height.HasValue)
			{
				ImageSize val = default;
				val.Width = VideoStream.Width.Value;
				val.Height = VideoStream.Height.Value;
				ImageSize val2 = DrawingUtils.Resize(val, (double)(BaseRequest.Width ?? 0), (double)(BaseRequest.Height ?? 0), (double)(BaseRequest.MaxWidth ?? 0), (double)(BaseRequest.MaxHeight ?? 0));
				return Convert.ToInt32(val2.Width);
			}
			if (!IsVideoRequest)
			{
				return null;
			}
			return BaseRequest.MaxWidth ?? BaseRequest.Width;
		}
	}

	public int? OutputHeight
	{
		get
		{
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0079: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
			if (VideoStream != null && VideoStream.Width.HasValue && VideoStream.Height.HasValue)
			{
				ImageSize val = default;
				val.Width = VideoStream.Width.Value;
				val.Height = VideoStream.Height.Value;
				ImageSize val2 = DrawingUtils.Resize(val, (double)(BaseRequest.Width ?? 0), (double)(BaseRequest.Height ?? 0), (double)(BaseRequest.MaxWidth ?? 0), (double)(BaseRequest.MaxHeight ?? 0));
				return Convert.ToInt32(val2.Height);
			}
			if (!IsVideoRequest)
			{
				return null;
			}
			return BaseRequest.MaxHeight ?? BaseRequest.Height;
		}
	}

	public int? OutputAudioSampleRate
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputAudioCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (AudioStream != null)
				{
					return AudioStream.SampleRate;
				}
			}
			else if (BaseRequest.AudioSampleRate.HasValue)
			{
				return Math.Min(44100, BaseRequest.AudioSampleRate.Value);
			}
			return null;
		}
	}

	public int? OutputAudioBitDepth
	{
		get
		{
			if ((BaseRequest.Static || string.Equals(OutputAudioCodec, "copy", StringComparison.OrdinalIgnoreCase)) && AudioStream != null)
			{
				return AudioStream.BitDepth;
			}
			return null;
		}
	}

	public double? TargetVideoLevel
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (VideoStream != null)
				{
					return VideoStream.Level;
				}
				return null;
			}
			string requestedLevel = GetRequestedLevel(ActualOutputVideoCodec);
			if (!string.IsNullOrEmpty(requestedLevel) && double.TryParse(requestedLevel, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
			{
				return result;
			}
			return null;
		}
	}

	public int? TargetVideoBitDepth
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (VideoStream != null)
				{
					return VideoStream.BitDepth;
				}
				return null;
			}
			return null;
		}
	}

	public int? TargetRefFrames
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (VideoStream != null)
				{
					return VideoStream.RefFrames;
				}
				return null;
			}
			return null;
		}
	}

	public float? TargetFramerate
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (VideoStream != null)
				{
					return VideoStream.AverageFrameRate ?? VideoStream.RealFrameRate;
				}
				return null;
			}
			return BaseRequest.MaxFramerate ?? BaseRequest.Framerate;
		}
	}

	public TransportStreamTimestamp TargetTimestamp
	{
		get
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			TransportStreamTimestamp result = (TransportStreamTimestamp)(string.Equals(OutputContainer, "m2ts", StringComparison.OrdinalIgnoreCase) ? 2 : 0);
			if (BaseRequest.Static)
			{
				return InputTimestamp;
			}
			return result;
		}
	}

	public int? TargetPacketLength
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (VideoStream != null)
				{
					return VideoStream.PacketLength;
				}
				return null;
			}
			return null;
		}
	}

	public string TargetVideoProfile
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (VideoStream != null)
				{
					return VideoStream.Profile;
				}
				return null;
			}
			string text = GetRequestedProfiles(ActualOutputVideoCodec).FirstOrDefault();
			if (!string.IsNullOrEmpty(text))
			{
				return text;
			}
			return null;
		}
	}

	public string TargetVideoRange
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (VideoStream != null)
				{
					return VideoStream.VideoRange;
				}
				return null;
			}
			return "SDR";
		}
	}

	public string TargetAudioProfile
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputAudioCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (AudioStream != null)
				{
					return AudioStream.Profile;
				}
				return null;
			}
			return null;
		}
	}

	public string TargetVideoCodecTag
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (VideoStream != null)
				{
					return VideoStream.CodecTag;
				}
				return null;
			}
			return null;
		}
	}

	public bool? IsTargetAnamorphic
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (VideoStream != null)
				{
					return VideoStream.IsAnamorphic;
				}
				return null;
			}
			return false;
		}
	}

	public string ActualOutputVideoCodec
	{
		get
		{
			string outputVideoCodec = OutputVideoCodec;
			if (string.Equals(outputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				MediaStream videoStream = VideoStream;
				if (videoStream != null)
				{
					return videoStream.Codec;
				}
				return null;
			}
			return outputVideoCodec;
		}
	}

	public string ActualOutputAudioCodec
	{
		get
		{
			string outputAudioCodec = OutputAudioCodec;
			if (string.Equals(outputAudioCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				MediaStream audioStream = AudioStream;
				if (audioStream != null)
				{
					return audioStream.Codec;
				}
				return null;
			}
			return outputAudioCodec;
		}
	}

	public bool? IsTargetInterlaced
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (VideoStream != null)
				{
					return VideoStream.IsInterlaced;
				}
				return null;
			}
			if (DeInterlace(ActualOutputVideoCodec, forceDeinterlaceIfSourceIsInterlaced: true))
			{
				return false;
			}
			if (VideoStream != null)
			{
				return VideoStream.IsInterlaced;
			}
			return null;
		}
	}

	public bool? IsTargetAVC
	{
		get
		{
			if (BaseRequest.Static || string.Equals(OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				if (VideoStream != null)
				{
					return VideoStream.IsAVC;
				}
				return null;
			}
			return false;
		}
	}

	public int? TargetVideoStreamCount
	{
		get
		{
			if (BaseRequest.Static)
			{
				return GetMediaStreamCount((MediaStreamType)1, int.MaxValue);
			}
			return GetMediaStreamCount((MediaStreamType)1, 1);
		}
	}

	public int? TargetAudioStreamCount
	{
		get
		{
			if (BaseRequest.Static)
			{
				return GetMediaStreamCount((MediaStreamType)0, int.MaxValue);
			}
			return GetMediaStreamCount((MediaStreamType)0, 1);
		}
	}

	public int HlsListSize => 0;

	public string GetMimeType(string outputPath, bool enableStreamDefault = true)
	{
		if (!string.IsNullOrEmpty(MimeType))
		{
			return MimeType;
		}
		return MimeTypes.GetMimeType(outputPath, enableStreamDefault);
	}

	public bool DeInterlace(string videoCodec, bool forceDeinterlaceIfSourceIsInterlaced)
	{
		MediaStream videoStream = VideoStream;
		bool flag = videoStream != null && videoStream.IsInterlaced;
		if (!flag)
		{
			return false;
		}
		if (BaseRequest.DeInterlace)
		{
			return true;
		}
		if (!string.IsNullOrEmpty(videoCodec) && string.Equals(BaseRequest.GetOption(videoCodec, "deinterlace"), "true", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (forceDeinterlaceIfSourceIsInterlaced && flag)
		{
			return true;
		}
		return false;
	}

	public string[] GetRequestedProfiles(string codec)
	{
		if (!string.IsNullOrEmpty(BaseRequest.Profile))
		{
			return BaseRequest.Profile.Split(new char[2] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
		}
		if (!string.IsNullOrEmpty(codec))
		{
			string option = BaseRequest.GetOption(codec, "profile");
			if (!string.IsNullOrEmpty(option))
			{
				return option.Split(new char[2] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
			}
		}
		return Array.Empty<string>();
	}

	public string GetRequestedLevel(string codec)
	{
		if (!string.IsNullOrEmpty(BaseRequest.Level))
		{
			return BaseRequest.Level;
		}
		if (!string.IsNullOrEmpty(codec))
		{
			return BaseRequest.GetOption(codec, "level");
		}
		return null;
	}

	public int? GetRequestedMaxRefFrames(string codec)
	{
		if (BaseRequest.MaxRefFrames.HasValue)
		{
			return BaseRequest.MaxRefFrames;
		}
		if (!string.IsNullOrEmpty(codec))
		{
			string option = BaseRequest.GetOption(codec, "maxrefframes");
			if (!string.IsNullOrEmpty(option) && int.TryParse(option, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				return result;
			}
		}
		return null;
	}

	public int? GetRequestedVideoBitDepth(string codec)
	{
		if (BaseRequest.MaxVideoBitDepth.HasValue)
		{
			return BaseRequest.MaxVideoBitDepth;
		}
		if (!string.IsNullOrEmpty(codec))
		{
			string option = BaseRequest.GetOption(codec, "videobitdepth");
			if (!string.IsNullOrEmpty(option) && int.TryParse(option, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				return result;
			}
		}
		return null;
	}

	public int? GetRequestedAudioBitDepth(string codec)
	{
		if (BaseRequest.MaxAudioBitDepth.HasValue)
		{
			return BaseRequest.MaxAudioBitDepth;
		}
		if (!string.IsNullOrEmpty(codec))
		{
			string option = BaseRequest.GetOption(codec, "audiobitdepth");
			if (!string.IsNullOrEmpty(option) && int.TryParse(option, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				return result;
			}
		}
		return null;
	}

	public int? GetRequestedAudioChannels(string codec)
	{
		if (BaseRequest.MaxAudioChannels.HasValue)
		{
			return BaseRequest.MaxAudioChannels;
		}
		if (BaseRequest.AudioChannels.HasValue)
		{
			return BaseRequest.AudioChannels;
		}
		if (!string.IsNullOrEmpty(codec))
		{
			string option = BaseRequest.GetOption(codec, "audiochannels");
			if (!string.IsNullOrEmpty(option) && int.TryParse(option, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				return result;
			}
		}
		return null;
	}

	public EncodingJobInfo(ILogger logger, IMediaSourceManager mediaSourceManager, TranscodingJobType jobType)
	{
		Id = Guid.NewGuid();
		TaskCompletionSource = new TaskCompletionSource<bool>();
		Logger = logger;
		MediaSourceManager = mediaSourceManager;
		TranscodingType = jobType;
		RemoteHttpHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		PlayableStreamFileNames = Array.Empty<string>();
		SupportedAudioCodecs = Array.Empty<string>();
		SupportedVideoCodecs = Array.Empty<string>();
		SupportedSubtitleCodecs = Array.Empty<string>();
	}

	public bool EnableBreakOnNonKeyFrames(string videoCodec)
	{
		if (TranscodingType != TranscodingJobType.Progressive)
		{
			if (IsSegmentedLiveStream)
			{
				return false;
			}
			if (BaseRequest.BreakOnNonKeyFrames)
			{
				return string.Equals(videoCodec, "copy", StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}
		return false;
	}

	private int? GetMediaStreamCount(MediaStreamType type, int limit)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		int? result = MediaSource.GetStreamCount(type);
		if (result.HasValue)
		{
			result = Math.Min(result.Value, limit);
		}
		return result;
	}

	public virtual void Dispose()
	{
		DisposeLiveStream();
		DisposeLogStream();
		DisposeIsoMount();
	}

	private void DisposeLogStream()
	{
		if (LogFileStream != null)
		{
			try
			{
				LogFileStream.Dispose();
			}
			catch (Exception ex)
			{
				Logger.ErrorException("Error disposing log stream", ex, Array.Empty<object>());
			}
			LogFileStream = null;
		}
	}

	private async void DisposeLiveStream()
	{
		if (MediaSource.RequiresClosing && string.IsNullOrWhiteSpace(BaseRequest.LiveStreamId) && !string.IsNullOrWhiteSpace(MediaSource.LiveStreamId))
		{
			try
			{
				await MediaSourceManager.CloseLiveStream(MediaSource.LiveStreamId).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex)
			{
				Logger.ErrorException("Error closing media source", ex, Array.Empty<object>());
			}
		}
	}

	private void DisposeIsoMount()
	{
		if (IsoMount != null)
		{
			try
			{
				((IDisposable)IsoMount).Dispose();
			}
			catch (Exception ex)
			{
				Logger.ErrorException("Error disposing iso mount", ex, Array.Empty<object>());
			}
			IsoMount = null;
		}
	}

	public virtual void ReportTranscodingProgress(TimeSpan? transcodingPosition, float framerate, double? percentComplete, long bytesTranscoded, int? bitRate)
	{
		long num = (transcodingPosition.HasValue ? transcodingPosition.Value.Ticks : 0);
		if (!percentComplete.HasValue && num > 0 && RunTimeTicks.HasValue)
		{
			long num2 = num / RunTimeTicks.Value;
			percentComplete = num2 * 100;
		}
		if (percentComplete.HasValue)
		{
			Progress.Report(percentComplete.Value);
		}
		if (!string.IsNullOrWhiteSpace(BaseRequest.DeviceId))
		{
			_ = ActualOutputVideoCodec;
			_ = ActualOutputVideoCodec;
		}
	}
}
