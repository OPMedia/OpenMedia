using System.Runtime.CompilerServices;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

public class StreamRequest : BaseEncodingJobOptions
{
	[ApiMember(Name = "DeviceProfileId", Description = "Optional. The dlna device profile id to utilize.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
	public string DeviceProfileId { get; set; }

	public string PlaySessionId { get; set; }

	public string Tag { get; set; }

	[ApiMember(Name = "SegmentContainer", Description = "SegmentContainer", DataType = "string", ParameterType = "path")]
	public string SegmentContainer { get; set; }

	public int? SegmentLength { get; set; }

	public int? MinSegments { get; set; }

	public bool DlnaHeaders { get; set; }

	public bool EstimateContentLength { get; set; }

	public TranscodeSeekInfo TranscodeSeekInfo
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
}
