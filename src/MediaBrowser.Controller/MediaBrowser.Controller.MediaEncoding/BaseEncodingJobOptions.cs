using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Services;

namespace MediaBrowser.Controller.MediaEncoding;

public class BaseEncodingJobOptions
{
	[ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
	public Guid Id { get; set; }

	[ApiMember(Name = "MediaSourceId", Description = "The media version id, if playing an alternate version", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
	public string MediaSourceId { get; set; }

	[ApiMember(Name = "DeviceId", Description = "The device id of the client requesting. Used to stop encoding processes when needed.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
	public string DeviceId { get; set; }

	[ApiMember(Name = "Container", Description = "Container", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "GET")]
	public string Container { get; set; }

	[ApiMember(Name = "AudioCodec", Description = "Optional. Specify a audio codec to encode to, e.g. mp3. If omitted the server will auto-select using the url's extension. Options: aac, mp3, vorbis, wma.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
	public string AudioCodec { get; set; }

	[ApiMember(Name = "EnableAutoStreamCopy", Description = "Whether or not to allow automatic stream copy if requested values match the original source. Defaults to true.", IsRequired = false, DataType = "bool", ParameterType = "query", Verb = "GET")]
	public bool EnableAutoStreamCopy { get; set; }

	public bool AllowVideoStreamCopy { get; set; }

	public bool AllowAudioStreamCopy { get; set; }

	public bool BreakOnNonKeyFrames { get; set; }

	[ApiMember(Name = "AudioSampleRate", Description = "Optional. Specify a specific audio sample rate, e.g. 44100", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? AudioSampleRate { get; set; }

	public int? MaxAudioBitDepth { get; set; }

	[ApiMember(Name = "AudioBitRate", Description = "Optional. Specify an audio bitrate to encode to, e.g. 128000. If omitted this will be left to encoder defaults.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? AudioBitRate { get; set; }

	[ApiMember(Name = "AudioChannels", Description = "Optional. Specify a specific number of audio channels to encode to, e.g. 2", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? AudioChannels { get; set; }

	[ApiMember(Name = "MaxAudioChannels", Description = "Optional. Specify a maximum number of audio channels to encode to, e.g. 2", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? MaxAudioChannels { get; set; }

	[ApiMember(Name = "Static", Description = "Optional. If true, the original file will be streamed statically without any encoding. Use either no url extension or the original file extension. true/false", IsRequired = false, DataType = "bool", ParameterType = "query", Verb = "GET")]
	public bool Static { get; set; }

	[ApiMember(Name = "Profile", Description = "Optional. Specify a specific h264 profile, e.g. main, baseline, high.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
	public string Profile { get; set; }

	[ApiMember(Name = "Level", Description = "Optional. Specify a level for the h264 profile, e.g. 3, 3.1.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
	public string Level { get; set; }

	[ApiMember(Name = "Framerate", Description = "Optional. A specific video framerate to encode to, e.g. 23.976. Generally this should be omitted unless the device has specific requirements.", IsRequired = false, DataType = "double", ParameterType = "query", Verb = "GET")]
	public float? Framerate { get; set; }

	[ApiMember(Name = "MaxFramerate", Description = "Optional. A specific maximum video framerate to encode to, e.g. 23.976. Generally this should be omitted unless the device has specific requirements.", IsRequired = false, DataType = "double", ParameterType = "query", Verb = "GET")]
	public float? MaxFramerate { get; set; }

	[ApiMember(Name = "CopyTimestamps", Description = "Whether or not to copy timestamps when transcoding with an offset. Defaults to false.", IsRequired = false, DataType = "bool", ParameterType = "query", Verb = "GET")]
	public bool CopyTimestamps { get; set; }

	[ApiMember(Name = "StartTimeTicks", Description = "Optional. Specify a starting offset, in ticks. 1 tick = 10000 ms", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public long? StartTimeTicks { get; set; }

	[ApiMember(Name = "Width", Description = "Optional. The fixed horizontal resolution of the encoded video.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? Width { get; set; }

	[ApiMember(Name = "Height", Description = "Optional. The fixed vertical resolution of the encoded video.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? Height { get; set; }

	[ApiMember(Name = "MaxWidth", Description = "Optional. The maximum horizontal resolution of the encoded video.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? MaxWidth { get; set; }

	[ApiMember(Name = "MaxHeight", Description = "Optional. The maximum vertical resolution of the encoded video.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? MaxHeight { get; set; }

	[ApiMember(Name = "VideoBitRate", Description = "Optional. Specify a video bitrate to encode to, e.g. 500000. If omitted this will be left to encoder defaults.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? VideoBitRate { get; set; }

	[ApiMember(Name = "SubtitleStreamIndex", Description = "Optional. The index of the subtitle stream to use. If omitted no subtitles will be used.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? SubtitleStreamIndex { get; set; }

	[ApiMember(Name = "SubtitleMethod", Description = "Optional. Specify the subtitle delivery method.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
	public SubtitleDeliveryMethod SubtitleMethod
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

	[ApiMember(Name = "MaxRefFrames", Description = "Optional.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? MaxRefFrames { get; set; }

	[ApiMember(Name = "MaxVideoBitDepth", Description = "Optional.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? MaxVideoBitDepth { get; set; }

	public bool RequireAvc { get; set; }

	public bool DeInterlace { get; set; }

	public bool RequireNonAnamorphic { get; set; }

	public int? TranscodingMaxAudioChannels { get; set; }

	public int? CpuCoreLimit { get; set; }

	public string LiveStreamId { get; set; }

	public bool EnableMpegtsM2TsMode { get; set; }

	[ApiMember(Name = "VideoCodec", Description = "Optional. Specify a video codec to encode to, e.g. h264. If omitted the server will auto-select using the url's extension. Options: h264, mpeg4, theora, vpx, wmv.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
	public string VideoCodec { get; set; }

	public string SubtitleCodec { get; set; }

	public string TranscodeReasons { get; set; }

	[ApiMember(Name = "AudioStreamIndex", Description = "Optional. The index of the audio stream to use. If omitted the first audio stream will be used.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? AudioStreamIndex { get; set; }

	[ApiMember(Name = "VideoStreamIndex", Description = "Optional. The index of the video stream to use. If omitted the first video stream will be used.", IsRequired = false, DataType = "int", ParameterType = "query", Verb = "GET")]
	public int? VideoStreamIndex { get; set; }

	public EncodingContext Context
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

	public Dictionary<string, string> StreamOptions { get; set; }

	public string GetOption(string qualifier, string name)
	{
		string option = GetOption(qualifier + "-" + name);
		if (string.IsNullOrEmpty(option))
		{
			option = GetOption(name);
		}
		return option;
	}

	public string GetOption(string name)
	{
		if (StreamOptions.TryGetValue(name, out var value))
		{
			return value;
		}
		return null;
	}

	public BaseEncodingJobOptions()
	{
		EnableAutoStreamCopy = true;
		AllowVideoStreamCopy = true;
		AllowAudioStreamCopy = true;
		Context = (EncodingContext)0;
		StreamOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}
}
