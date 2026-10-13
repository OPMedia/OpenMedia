using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api.Progressive;

[Route("/Videos/{Id}/stream.{Container}", "GET", Summary = "Gets a video stream")]
[Route("/Videos/{Id}/stream", "GET", Summary = "Gets a video stream")]
[Route("/Videos/{Id}/stream.{Container}", "HEAD", Summary = "Gets a video stream")]
[Route("/Videos/{Id}/stream", "HEAD", Summary = "Gets a video stream")]
public class GetVideoStream : VideoStreamRequest
{
}
