using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Controller.Subtitles;

public interface ISubtitleManager
{
	event EventHandler<SubtitleDownloadFailureEventArgs> SubtitleDownloadFailure;

	event EventHandler<SubtitleDownloadEventArgs> SubtitlesDownloaded;

	void AddParts(IEnumerable<ISubtitleProvider> subtitleProviders);

	Task<RemoteSubtitleInfo[]> SearchSubtitles(Video video, string language, bool? isPerfectMatch, CancellationToken cancellationToken);

	Task<RemoteSubtitleInfo[]> SearchSubtitles(SubtitleSearchRequest request, CancellationToken cancellationToken);

	Task DownloadSubtitles(Video video, string subtitleId, CancellationToken cancellationToken);

	Task DownloadSubtitles(Video video, LibraryOptions libraryOptions, string subtitleId, CancellationToken cancellationToken);

	Task<SubtitleResponse> GetRemoteSubtitles(string id, CancellationToken cancellationToken);

	Task DeleteSubtitles(BaseItem item, int index);

	SubtitleProviderInfo[] GetSupportedProviders(BaseItem item);
}
