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
    /// <summary>Series artwork from the v4 extended record's "artworks" array.</summary>
    public class TvdbSeriesImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly TvdbSeriesProvider _seriesProvider;

        public TvdbSeriesImageProvider(IHttpClient httpClient, IFileSystem fileSystem, IServerConfigurationManager config, ILogger logger, ILibraryManager libraryManager, ILocalizationManager localizationManager)
        {
            _seriesProvider = new TvdbSeriesProvider(httpClient, fileSystem, config, logger, libraryManager, localizationManager);
        }

        public string Name => TvdbArtworkMapper.ProviderName;

        public static string ProviderName => TvdbArtworkMapper.ProviderName;

        public int Order => 0;

        public bool Supports(BaseItem item) => item is Series;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary, ImageType.Banner, ImageType.Backdrop };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var series = item as Series;
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
            if (data?.Item?.Artworks == null)
            {
                return new RemoteImageInfo[] { };
            }

            var types = await _seriesProvider.GetArtworkTypes(cancellationToken).ConfigureAwait(false);
            return TvdbArtworkMapper.Map(
                data.Item.Artworks,
                types,
                series.GetPreferredMetadataLanguage(),
                ArtworkScope.Series).ToList();
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _seriesProvider.GetImageResponse(url, cancellationToken);
        }
    }
}
