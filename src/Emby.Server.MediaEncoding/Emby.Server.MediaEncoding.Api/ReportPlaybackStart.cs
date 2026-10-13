using MediaBrowser.Model.Services;
using MediaBrowser.Model.Session;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Sessions/Playing", "POST", Summary = "Reports playback has started within a session")]
public class ReportPlaybackStart : PlaybackStartInfo, IReturnVoid, IReturn
{
}
