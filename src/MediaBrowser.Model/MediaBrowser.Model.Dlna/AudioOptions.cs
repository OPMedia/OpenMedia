using System;
using MediaBrowser.Model.Dto;

namespace MediaBrowser.Model.Dlna;

public class AudioOptions
{
	public bool EnableDirectPlay { get; set; }

	public bool EnableDirectStream { get; set; }

	public bool ForceDirectPlay { get; set; }

	public bool ForceDirectStream { get; set; }

	public Guid ItemId { get; set; }

	public MediaSourceInfo[] MediaSources { get; set; }

	public DeviceProfile Profile { get; set; }

	public string MediaSourceId { get; set; }

	public string DeviceId { get; set; }

	public int? MaxAudioChannels { get; set; }

	public long? MaxBitrate { get; set; }

	public EncodingContext Context { get; set; }

	public int? AudioTranscodingBitrate { get; set; }

	public AudioOptions()
	{
		Context = EncodingContext.Streaming;
		EnableDirectPlay = true;
		EnableDirectStream = true;
	}

	public long? GetMaxBitrate(bool isAudio)
	{
		if (MaxBitrate.HasValue)
		{
			return MaxBitrate;
		}
		if (Profile != null)
		{
			if (Context == EncodingContext.Static)
			{
				if (isAudio && Profile.MaxStaticMusicBitrate.HasValue)
				{
					return Profile.MaxStaticMusicBitrate;
				}
				return Profile.MaxStaticBitrate;
			}
			return Profile.MaxStreamingBitrate;
		}
		return null;
	}
}
