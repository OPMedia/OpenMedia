using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api.Progressive;

[Route("/Audio/{Id}/stream.{Container}", "GET", Summary = "Gets an audio stream")]
[Route("/Audio/{Id}/stream", "GET", Summary = "Gets an audio stream")]
[Route("/Audio/{Id}/stream.{Container}", "HEAD", Summary = "Gets an audio stream")]
[Route("/Audio/{Id}/stream", "HEAD", Summary = "Gets an audio stream")]
public class GetAudioStream : StreamRequest
{
}
