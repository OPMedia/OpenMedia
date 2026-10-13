using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

[Route("/LiveStreams/Open", "POST", Summary = "Opens a media source")]
public class OpenMediaSource : LiveStreamRequest, IReturn<LiveStreamResponse>, IReturn
{
}
