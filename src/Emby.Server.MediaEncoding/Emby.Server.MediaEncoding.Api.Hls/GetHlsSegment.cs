using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api.Hls;

[Route("/Videos/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}", "GET")]
[Route("/Videos/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}", "HEAD")]
[Route("/Audio/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}", "GET")]
[Route("/Audio/{Id}/hls1/{PlaylistId}/{SegmentId}.{SegmentContainer}", "HEAD")]
public class GetHlsSegment : VideoStreamRequest
{
	public string PlaylistId { get; set; }

	public string SegmentId { get; set; }
}
