using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Controller.Subtitles;

public interface ISubtitleProvider
{
	string Name { get; }

	IEnumerable<VideoContentType> SupportedMediaTypes { get; }

	Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken);

	Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken);
}
