using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Audio/{Id}/universal.{Container}", "GET", Summary = "Gets an audio stream")]
[Route("/Audio/{Id}/universal", "GET", Summary = "Gets an audio stream")]
[Route("/Audio/{Id}/universal.{Container}", "HEAD", Summary = "Gets an audio stream")]
[Route("/Audio/{Id}/universal", "HEAD", Summary = "Gets an audio stream")]
public class GetUniversalAudioStream : BaseUniversalRequest
{
}
