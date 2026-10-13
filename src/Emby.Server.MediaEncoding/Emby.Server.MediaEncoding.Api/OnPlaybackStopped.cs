using System;
using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Users/{UserId}/PlayingItems/{Id}", "DELETE", Summary = "Reports that a user has stopped playing an item")]
public class OnPlaybackStopped : IReturnVoid, IReturn
{
	[ApiMember(Name = "UserId", Description = "User Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "DELETE")]
	public string UserId { get; set; }

	[ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "DELETE")]
	public Guid Id { get; set; }

	[ApiMember(Name = "MediaSourceId", Description = "The id of the MediaSource", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "DELETE")]
	public string MediaSourceId { get; set; }

	[ApiMember(Name = "NextMediaType", Description = "The next media type that will play", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "DELETE")]
	public string NextMediaType { get; set; }

	[ApiMember(Name = "PositionTicks", Description = "Optional. The position, in ticks, where playback stopped. 1 tick = 10000 ms", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "DELETE")]
	public long? PositionTicks { get; set; }

	[ApiMember(Name = "LiveStreamId", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "DELETE")]
	public string LiveStreamId { get; set; }

	[ApiMember(Name = "PlaySessionId", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "DELETE")]
	public string PlaySessionId { get; set; }
}
