using MediaBrowser.Controller.MediaEncoding;

namespace Emby.Server.MediaEncoding.Api;

public class VideoStreamRequest : StreamRequest
{
	public bool HasFixedResolution
	{
		get
		{
			if (!((BaseEncodingJobOptions)this).Width.HasValue)
			{
				return ((BaseEncodingJobOptions)this).Height.HasValue;
			}
			return true;
		}
	}

	public bool EnableSubtitlesInManifest { get; set; }
}
