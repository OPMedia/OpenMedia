using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Items/{Id}/PlaybackInfo", "POST", Summary = "Gets live playback media info for an item")]
public class GetPostedPlaybackInfo : PlaybackInfoRequest, IReturn<PlaybackInfoResponse>, IReturn
{
}
