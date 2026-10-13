using System;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Session;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Users/{UserId}/PlayingItems/{Id}", "POST", Summary = "Reports that a user has begun playing an item")]
public class OnPlaybackStart : IReturnVoid, IReturn
{
	[ApiMember(Name = "UserId", Description = "User Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
	public string UserId { get; set; }

	[ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
	public Guid Id { get; set; }

	[ApiMember(Name = "MediaSourceId", Description = "The id of the MediaSource", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
	public string MediaSourceId { get; set; }

	[ApiMember(Name = "CanSeek", Description = "Indicates if the client can seek", IsRequired = false, DataType = "boolean", ParameterType = "query", Verb = "POST")]
	public bool CanSeek { get; set; }

	[ApiMember(Name = "AudioStreamIndex", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "POST")]
	public int? AudioStreamIndex { get; set; }

	[ApiMember(Name = "SubtitleStreamIndex", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "POST")]
	public int? SubtitleStreamIndex { get; set; }

	[ApiMember(Name = "PlayMethod", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
	public PlayMethod PlayMethod
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	[ApiMember(Name = "LiveStreamId", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
	public string LiveStreamId { get; set; }

	[ApiMember(Name = "PlaySessionId", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
	public string PlaySessionId { get; set; }
}
