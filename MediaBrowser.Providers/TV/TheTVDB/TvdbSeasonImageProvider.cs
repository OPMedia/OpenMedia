using System.Collections.Generic;
using System.Linq;
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
    /// <summary>Season artwork from the v4 extended record, keyed by season id.</summary>
    public class TvdbSeasonImageProvider : IRemoteImageProvider
    {
        private readonly TvdbSeriesProvider _seriesProvider;

        public TvdbSeasonImageProvider(IHttpClient httpClient, IFileSystem fileSystem, IServerConfigurationManager config, ILogger logger, ILibraryManager libraryManager, ILocalizationManager localizationManager)
        {
            _seriesProvider = new TvdbSeriesProvider(httpClient, fileSystem, config, logger, libraryManager, localizationManager);
        }

        public string Name => TvdbArtworkMapper.ProviderName;

        public bool Supports(BaseItem item) => item is Season;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary, ImageType.Banner, ImageType.Backdrop };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var season = item as Season;
            var series = season?.Series;
            if (series == null || !season.IndexNumber.HasValue || !_seriesProvider.IsConfigured ||
                !TvdbSeriesProvider.IsValidSeries(series.ProviderIds))
            {
                return new RemoteImageInfo[] { };
            }

            var id = await _seriesProvider.ResolveTvdbId(series.ProviderIds, series.Name, series.ProductionYear, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(id))
            {
                return new RemoteImageInfo[] { };
            }

            var data = await _seriesProvider.EnsureData(id, false, cancellationToken).ConfigureAwait(false);
            if (data?.Item == null)
            {
                return new RemoteImageInfo[] { };
            }

            var tvdbSeason = data.Item.Seasons?.FirstOrDefault(i => i.Number == season.IndexNumber.Value);
            if (tvdbSeason == null)
            {
                return new RemoteImageInfo[] { };
            }

            var types = await _seriesProvider.GetArtworkTypes(cancellationToken).ConfigureAwait(false);
            var images = TvdbArtworkMapper.Map(
                data.Item.Artworks,
                types,
                series.GetPreferredMetadataLanguage(),
                ArtworkScope.Season,
                tvdbSeason.Id).ToList();

            if (images.Count == 0 && !string.IsNullOrWhiteSpace(tvdbSeason.Image))
            {
                images.Add(new RemoteImageInfo
                {
                    ProviderName = TvdbArtworkMapper.ProviderName,
                    Type = ImageType.Primary,
                    Url = TvdbSeriesProvider.BuildImageUrl(tvdbSeason.Image)
                });
            }

            return images;
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _seriesProvider.GetImageResponse(url, cancellationToken);
        }
    }
}
