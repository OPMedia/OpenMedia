using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Sessions/Playing/Ping", "POST", Summary = "Pings a playback session")]
public class PingPlaybackSession : IReturnVoid, IReturn
{
	[ApiMember(Name = "PlaySessionId", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
	public string PlaySessionId { get; set; }
}
