using System.IO;
using System.Threading;
using MediaBrowser.Model.MediaInfo;

namespace Emby.Server.MediaEncoding.Subtitles;

public interface ISubtitleWriter
{
	void Write(SubtitleTrackInfo info, Stream stream, CancellationToken cancellationToken);
}
