using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;

namespace MediaBrowser.Model.MediaInfo;

public class PlaybackInfoResponse
{
	public MediaSourceInfo[] MediaSources { get; set; }

	public string PlaySessionId { get; set; }

	public PlaybackErrorCode? ErrorCode { get; set; }

	public PlaybackInfoResponse()
	{
		MediaSources = new MediaSourceInfo[0];
	}
}
