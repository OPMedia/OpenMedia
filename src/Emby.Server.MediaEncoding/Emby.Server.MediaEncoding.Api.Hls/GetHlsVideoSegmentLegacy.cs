using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api.Hls;

[Route("/Videos/{Id}/hls/{PlaylistId}/{SegmentId}.{SegmentContainer}", "GET")]
public class GetHlsVideoSegmentLegacy : VideoStreamRequest
{
	public string PlaylistId { get; set; }

	[ApiMember(Name = "SegmentId", Description = "SegmentId", DataType = "string", ParameterType = "path")]
	public string SegmentId { get; set; }
}
