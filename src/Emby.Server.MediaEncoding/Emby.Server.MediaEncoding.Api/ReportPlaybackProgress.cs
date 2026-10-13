using MediaBrowser.Model.Services;
using MediaBrowser.Model.Session;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Sessions/Playing/Progress", "POST", Summary = "Reports playback progress within a session")]
public class ReportPlaybackProgress : PlaybackProgressInfo, IReturnVoid, IReturn
{
}
