using System;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Logging;

namespace Emby.Server.MediaEncoding.Api;

public class StreamState : EncodingJobInfo, IDisposable
{
	public TranscodingJob TranscodingJob;

	public string RequestedUrl { get; set; }

	public StreamRequest Request
	{
		get
		{
			return (StreamRequest)(object)((EncodingJobInfo)this).BaseRequest;
		}
		set
		{
			((EncodingJobInfo)this).BaseRequest = (BaseEncodingJobOptions)(object)value;
		}
	}

	public VideoStreamRequest VideoRequest => Request as VideoStreamRequest;

	public IDirectStreamProvider DirectStreamProvider { get; set; }

	public int SegmentLength
	{
		get
		{
			if (Request.SegmentLength.HasValue)
			{
				return Request.SegmentLength.Value;
			}
			if (string.Equals(((EncodingJobInfo)this).OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				string text = UserAgent ?? string.Empty;
				if (text.IndexOf("AppleTV", StringComparison.OrdinalIgnoreCase) != -1 || text.IndexOf("cfnetwork", StringComparison.OrdinalIgnoreCase) != -1 || text.IndexOf("ipad", StringComparison.OrdinalIgnoreCase) != -1 || text.IndexOf("iphone", StringComparison.OrdinalIgnoreCase) != -1 || text.IndexOf("ipod", StringComparison.OrdinalIgnoreCase) != -1)
				{
					_ = ((EncodingJobInfo)this).IsSegmentedLiveStream;
					return 6;
				}
				if (((EncodingJobInfo)this).IsSegmentedLiveStream)
				{
					return 3;
				}
				return 6;
			}
			return 3;
		}
	}

	public int MinSegments
	{
		get
		{
			if (Request.MinSegments.HasValue)
			{
				return Request.MinSegments.Value;
			}
			if (SegmentLength < 10)
			{
				return 3;
			}
			return 2;
		}
	}

	public string UserAgent { get; set; }

	public DeviceProfile DeviceProfile { get; set; }

	public StreamState(IMediaSourceManager mediaSourceManager, ILogger logger, TranscodingJobType transcodingType, bool isVideoRequest)
		: base(logger, mediaSourceManager, transcodingType)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		((EncodingJobInfo)this).IsVideoRequest = isVideoRequest;
	}

	public override void Dispose()
	{
		// 反编译产物修正：原 IL 是 base.Dispose()（非虚调用），反编译器误渲染为
		// ((EncodingJobInfo)this).Dispose()——C# 强转不绕过虚分派，导致自递归栈溢出
		base.Dispose();
		TranscodingJob = null;
	}

	public override void ReportTranscodingProgress(TimeSpan? transcodingPosition, float framerate, double? percentComplete, long bytesTranscoded, int? bitRate)
	{
		ApiEntryPoint.Instance.ReportTranscodingProgress(TranscodingJob, this, transcodingPosition, framerate, percentComplete, bytesTranscoded, bitRate);
	}
}
