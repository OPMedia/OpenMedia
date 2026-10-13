using System;
using MediaBrowser.Model.Dto;

namespace MediaBrowser.Model.Session;

public class PlaybackStopInfo
{
	public QueueItem[] NowPlayingQueue { get; set; }

	public string PlaylistItemId { get; set; }

	public BaseItemDto Item { get; set; }

	public Guid ItemId { get; set; }

	public string SessionId { get; set; }

	public string MediaSourceId { get; set; }

	public long? PositionTicks { get; set; }

	public string LiveStreamId { get; set; }

	public string PlaySessionId { get; set; }

	public bool Failed { get; set; }

	public string NextMediaType { get; set; }
}
