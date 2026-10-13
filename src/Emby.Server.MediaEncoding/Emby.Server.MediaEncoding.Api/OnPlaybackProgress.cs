using System;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Session;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Users/{UserId}/PlayingItems/{Id}/Progress", "POST", Summary = "Reports a user's playback progress")]
public class OnPlaybackProgress : IReturnVoid, IReturn
{
	[ApiMember(Name = "UserId", Description = "User Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
	public string UserId { get; set; }

	[ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
	public Guid Id { get; set; }

	[ApiMember(Name = "MediaSourceId", Description = "The id of the MediaSource", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
	public string MediaSourceId { get; set; }

	[ApiMember(Name = "PositionTicks", Description = "Optional. The current position, in ticks. 1 tick = 10000 ms", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "POST")]
	public long? PositionTicks { get; set; }

	[ApiMember(Name = "IsPaused", Description = "Indicates if the player is paused.", IsRequired = false, DataType = "boolean", ParameterType = "query", Verb = "POST")]
	public bool IsPaused { get; set; }

	[ApiMember(Name = "IsMuted", Description = "Indicates if the player is muted.", IsRequired = false, DataType = "boolean", ParameterType = "query", Verb = "POST")]
	public bool IsMuted { get; set; }

	[ApiMember(Name = "AudioStreamIndex", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "POST")]
	public int? AudioStreamIndex { get; set; }

	[ApiMember(Name = "SubtitleStreamIndex", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "POST")]
	public int? SubtitleStreamIndex { get; set; }

	[ApiMember(Name = "VolumeLevel", Description = "Scale of 0-100", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "POST")]
	public int? VolumeLevel { get; set; }

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

	[ApiMember(Name = "RepeatMode", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
	public RepeatMode RepeatMode
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
}
