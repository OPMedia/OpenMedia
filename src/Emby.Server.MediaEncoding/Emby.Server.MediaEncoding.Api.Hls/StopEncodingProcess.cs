using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api.Hls;

[Route("/Videos/ActiveEncodings", "DELETE")]
[Authenticated]
public class StopEncodingProcess
{
	[ApiMember(Name = "DeviceId", Description = "The device id of the client requesting. Used to stop encoding processes when needed.", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "DELETE")]
	public string DeviceId { get; set; }

	[ApiMember(Name = "PlaySessionId", Description = "The play session id", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "DELETE")]
	public string PlaySessionId { get; set; }
}
