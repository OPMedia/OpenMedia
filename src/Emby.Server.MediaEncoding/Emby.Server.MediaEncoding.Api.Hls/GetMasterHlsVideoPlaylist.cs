using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api.Hls;

[Route("/Videos/{Id}/master.m3u8", "GET", Summary = "Gets a video stream using HTTP live streaming.")]
[Route("/Videos/{Id}/master.m3u8", "HEAD", Summary = "Gets a video stream using HTTP live streaming.")]
public class GetMasterHlsVideoPlaylist : VideoStreamRequest, IMasterHlsRequest
{
	public bool EnableAdaptiveBitrateStreaming { get; set; }

	public GetMasterHlsVideoPlaylist()
	{
		EnableAdaptiveBitrateStreaming = true;
	}
}
