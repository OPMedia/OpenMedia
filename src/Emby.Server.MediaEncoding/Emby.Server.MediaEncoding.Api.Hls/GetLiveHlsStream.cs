using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api.Hls;

[Route("/Videos/{Id}/live.m3u8", "GET")]
public class GetLiveHlsStream : VideoStreamRequest
{
}
