using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Globalization;

namespace MediaBrowser.Providers.TV
{
    /// <summary>Episode stills from the v4 episode list's "image" field.</summary>
    public class TvdbEpisodeImageProvider : IRemoteImageProvider
    {
        private readonly TvdbSeriesProvider _seriesProvider;

        public TvdbEpisodeImageProvider(IHttpClient httpClient, IFileSystem fileSystem, IServerConfigurationManager config, ILogger logger, ILibraryManager libraryManager, ILocalizationManager localizationManager)
        {
            _seriesProvider = new TvdbSeriesProvider(httpClient, fileSystem, config, logger, libraryManager, localizationManager);
        }

        public string Name => TvdbArtworkMapper.ProviderName;

        public bool Supports(BaseItem item) => item is Episode;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var episode = item as Episode;
            var series = episode?.Series;
            if (series == null || !_seriesProvider.IsConfigured || !TvdbSeriesProvider.IsValidSeries(series.ProviderIds))
            {
                return new RemoteImageInfo[] { };
            }

            var id = await _seriesProvider.ResolveTvdbId(series.ProviderIds, series.Name, series.ProductionYear, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(id))
            {
                return new RemoteImageInfo[] { };
            }

            var data = await _seriesProvider.EnsureData(id, false, cancellationToken).ConfigureAwait(false);
            if (data == null)
            {
                return new RemoteImageInfo[] { };
            }

            var matches = TvdbEpisodeProvider.MatchEpisodes(data.Episodes, episode.GetLookupInfo());
            return new List<RemoteImageInfo>(TvdbArtworkMapper.ForEpisode(matches));
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _seriesProvider.GetImageResponse(url, cancellationToken);
        }
    }
}
