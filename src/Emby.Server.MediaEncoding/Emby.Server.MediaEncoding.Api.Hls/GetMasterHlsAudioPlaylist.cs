using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api.Hls;

[Route("/Audio/{Id}/master.m3u8", "GET", Summary = "Gets an audio stream using HTTP live streaming.")]
[Route("/Audio/{Id}/master.m3u8", "HEAD", Summary = "Gets an audio stream using HTTP live streaming.")]
public class GetMasterHlsAudioPlaylist : StreamRequest, IMasterHlsRequest
{
	public bool EnableAdaptiveBitrateStreaming { get; set; }

	public GetMasterHlsAudioPlaylist()
	{
		EnableAdaptiveBitrateStreaming = true;
	}
}
