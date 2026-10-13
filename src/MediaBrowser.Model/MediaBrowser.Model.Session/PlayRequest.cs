using System;
using MediaBrowser.Model.Services;

namespace MediaBrowser.Model.Session;

public class PlayRequest
{
	[ApiMember(Name = "ItemIds", Description = "The ids of the items to play, comma delimited", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST", AllowMultiple = true)]
	public Guid[] ItemIds { get; set; }

	[ApiMember(Name = "StartPositionTicks", Description = "The starting position of the first item.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
	public long? StartPositionTicks { get; set; }

	[ApiMember(Name = "PlayCommand", Description = "The type of play command to issue (PlayNow, PlayNext, PlayLast). Clients who have not yet implemented play next and play last may play now.", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
	public PlayCommand PlayCommand { get; set; }

	public Guid ControllingUserId { get; set; }

	public int? SubtitleStreamIndex { get; set; }

	public int? AudioStreamIndex { get; set; }

	public string MediaSourceId { get; set; }

	public int? StartIndex { get; set; }
}
