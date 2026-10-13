using System;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Items/{Id}/PlaybackInfo", "GET", Summary = "Gets live playback media info for an item")]
public class GetPlaybackInfo : IReturn<PlaybackInfoResponse>, IReturn
{
	[ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
	public Guid Id { get; set; }

	[ApiMember(Name = "UserId", Description = "User Id", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "GET")]
	public Guid UserId { get; set; }
}
