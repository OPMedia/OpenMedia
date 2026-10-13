using System;
using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

public class BaseUniversalRequest
{
	[ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
	public Guid Id { get; set; }

	[ApiMember(Name = "MediaSourceId", Description = "The media version id, if playing an alternate version", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
	public string MediaSourceId { get; set; }

	[ApiMember(Name = "DeviceId", Description = "The device id of the client requesting. Used to stop encoding processes when needed.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
	public string DeviceId { get; set; }

	public Guid UserId { get; set; }

	public string AudioCodec { get; set; }

	public string Container { get; set; }

	public int? MaxAudioChannels { get; set; }

	public int? TranscodingAudioChannels { get; set; }

	public long? MaxStreamingBitrate { get; set; }

	[ApiMember(Name = "StartTimeTicks", Description = "Optional. Specify a starting offset, in ticks. 1 tick = 10000 ms", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public long? StartTimeTicks { get; set; }

	public string TranscodingContainer { get; set; }

	public string TranscodingProtocol { get; set; }

	public int? MaxAudioSampleRate { get; set; }

	public int? MaxAudioBitDepth { get; set; }

	public bool EnableRedirection { get; set; }

	public bool EnableRemoteMedia { get; set; }

	public bool BreakOnNonKeyFrames { get; set; }

	public BaseUniversalRequest()
	{
		EnableRedirection = true;
	}
}
