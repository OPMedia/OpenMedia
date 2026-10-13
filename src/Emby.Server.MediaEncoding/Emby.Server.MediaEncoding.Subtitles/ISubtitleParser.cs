using System.IO;
using System.Threading;
using MediaBrowser.Model.MediaInfo;

namespace Emby.Server.MediaEncoding.Subtitles;

public interface ISubtitleParser
{
	SubtitleTrackInfo Parse(Stream stream, CancellationToken cancellationToken);
}
