using MediaBrowser.Model.Services;
using MediaBrowser.Model.Session;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Sessions/Playing/Stopped", "POST", Summary = "Reports playback has stopped within a session")]
public class ReportPlaybackStopped : PlaybackStopInfo, IReturnVoid, IReturn
{
}
