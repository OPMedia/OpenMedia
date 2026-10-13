using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

[Route("/LiveStreams/MediaInfo", "POST", Summary = "Closes a media source")]
public class GetLiveStreamMediaInfo : IReturn
{
	[ApiMember(Name = "LiveStreamId", Description = "LiveStreamId", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
	public string LiveStreamId { get; set; }
}
