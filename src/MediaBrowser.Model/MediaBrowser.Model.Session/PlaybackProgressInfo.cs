using System;
using MediaBrowser.Model.Dto;

namespace MediaBrowser.Model.Session;

public class PlaybackProgressInfo
{
	public bool CanSeek { get; set; }

	public BaseItemDto Item { get; set; }

	public QueueItem[] NowPlayingQueue { get; set; }

	public string PlaylistItemId { get; set; }

	public Guid ItemId { get; set; }

	public string SessionId { get; set; }

	public string MediaSourceId { get; set; }

	public int? AudioStreamIndex { get; set; }

	public int? SubtitleStreamIndex { get; set; }

	public bool IsPaused { get; set; }

	public bool IsMuted { get; set; }

	public long? PositionTicks { get; set; }

	public long? PlaybackStartTimeTicks { get; set; }

	public int? VolumeLevel { get; set; }

	public int? Brightness { get; set; }

	public string AspectRatio { get; set; }

	public PlayMethod PlayMethod { get; set; }

	public string LiveStreamId { get; set; }

	public string PlaySessionId { get; set; }

	public RepeatMode RepeatMode { get; set; }

	public long? RunTimeTicks { get; set; }
}
