using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>
    /// Episode metadata from the paged v4 /series/{id}/episodes/default endpoint.
    /// </summary>
    internal class TvdbEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        private readonly ILogger _logger;
        private readonly TvdbSeriesProvider _seriesProvider;

        public TvdbEpisodeProvider(IFileSystem fileSystem, IServerConfigurationManager config, IHttpClient httpClient, ILogger logger, ILibraryManager libraryManager, ILocalizationManager localizationManager)
        {
            _logger = logger;
            _seriesProvider = new TvdbSeriesProvider(httpClient, fileSystem, config, logger, libraryManager, localizationManager);
        }

        public string Name => "TheTVDB";

        public int Order => 0;

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var list = new List<RemoteSearchResult>();

            if (!searchInfo.IndexNumber.HasValue && !searchInfo.PremiereDate.HasValue)
            {
                return list;
            }

            var metadata = await GetMetadata(searchInfo, cancellationToken).ConfigureAwait(false);
            if (metadata.HasMetadata)
            {
                var item = metadata.Item;
                list.Add(new RemoteSearchResult
                {
                    IndexNumber = item.IndexNumber,
                    Name = item.Name,
                    ParentIndexNumber = item.ParentIndexNumber,
                    PremiereDate = item.PremiereDate,
                    ProductionYear = item.ProductionYear,
                    ProviderIds = item.ProviderIds,
                    SearchProviderName = Name,
                    IndexNumberEnd = item.IndexNumberEnd
                });
            }

            return list;
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>
            {
                QueriedById = true
            };

            if (!_seriesProvider.IsConfigured || !TvdbSeriesProvider.IsValidSeries(searchInfo.SeriesProviderIds) ||
                (!searchInfo.IndexNumber.HasValue && !searchInfo.PremiereDate.HasValue))
            {
                return result;
            }

            var seriesId = await _seriesProvider.ResolveTvdbId(
                searchInfo.SeriesProviderIds, null, null, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(seriesId))
            {
                return result;
            }

            var data = await _seriesProvider.EnsureData(seriesId, false, cancellationToken).ConfigureAwait(false);
            if (data == null)
            {
                return result;
            }

            var matches = MatchEpisodes(data.Episodes, searchInfo);
            if (matches.Count == 0)
            {
                return result;
            }

            result.Item = MapEpisode(matches[0], searchInfo);
            result.HasMetadata = true;

            for (var i = 1; i < matches.Count; i++)
            {
                var additional = matches[i];
                if (!result.Item.LockedFields.Contains(MetadataFields.Name) && !string.IsNullOrWhiteSpace(additional.Name))
                {
                    result.Item.Name += ", " + additional.Name;
                }

                if (!result.Item.LockedFields.Contains(MetadataFields.Overview) && !string.IsNullOrWhiteSpace(additional.Overview))
                {
                    result.Item.Overview += Environment.NewLine + Environment.NewLine + additional.Overview;
                }
            }

            return result;
        }

        internal static List<TvdbEpisode> MatchEpisodes(List<TvdbEpisode> episodes, EpisodeInfo searchInfo)
        {
            if (episodes == null)
            {
                return new List<TvdbEpisode>();
            }

            if (searchInfo.IndexNumber.HasValue)
            {
                var absolute = string.Equals(searchInfo.SeriesDisplayOrder, "absolute", StringComparison.OrdinalIgnoreCase);
                var byNumber = episodes.Where(i =>
                {
                    var number = absolute ? i.AbsoluteNumber ?? i.Number : i.Number;
                    if (number != searchInfo.IndexNumber.Value)
                    {
                        return false;
                    }

                    return absolute || !searchInfo.ParentIndexNumber.HasValue || i.SeasonNumber == searchInfo.ParentIndexNumber.Value;
                }).ToList();

                if (byNumber.Count > 0)
                {
                    return byNumber;
                }
            }

            if (searchInfo.PremiereDate.HasValue)
            {
                var airDate = searchInfo.PremiereDate.Value.Date;
                return episodes.Where(i =>
                {
                    DateTime parsed;
                    return DateTime.TryParse(i.Aired, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed) && parsed.Date == airDate;
                }).ToList();
            }

            return new List<TvdbEpisode>();
        }

        private Episode MapEpisode(TvdbEpisode episode, EpisodeInfo searchInfo)
        {
            var item = new Episode
            {
                IndexNumber = searchInfo.IndexNumber ?? episode.Number,
                ParentIndexNumber = searchInfo.ParentIndexNumber ?? episode.SeasonNumber,
                IndexNumberEnd = searchInfo.IndexNumberEnd
            };

            item.SetProviderId(MetadataProviders.Tvdb, episode.Id.ToString(CultureInfo.InvariantCulture));

            var imdb = episode.RemoteIds?.FirstOrDefault(i =>
                (i.SourceName ?? string.Empty).IndexOf("imdb", StringComparison.OrdinalIgnoreCase) >= 0);
            if (imdb != null && !string.IsNullOrWhiteSpace(imdb.Id))
            {
                item.SetProviderId(MetadataProviders.Imdb, imdb.Id);
            }

            if (!item.LockedFields.Contains(MetadataFields.Name))
            {
                item.Name = episode.Name;
            }

            if (!item.LockedFields.Contains(MetadataFields.Overview))
            {
                item.Overview = episode.Overview;
            }

            DateTime date;
            if (DateTime.TryParse(episode.Aired, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date))
            {
                item.PremiereDate = date;
                item.ProductionYear = date.Year;
            }

            if (episode.Runtime.HasValue && episode.Runtime.Value > 0)
            {
                item.RunTimeTicks = TimeSpan.FromMinutes(episode.Runtime.Value).Ticks;
            }

            if (episode.AirsBeforeSeason.HasValue)
            {
                item.AirsBeforeSeasonNumber = episode.AirsBeforeSeason.Value;
            }

            if (episode.AirsBeforeEpisode.HasValue)
            {
                item.AirsBeforeEpisodeNumber = episode.AirsBeforeEpisode.Value;
            }

            if (episode.AirsAfterSeason.HasValue)
            {
                item.AirsAfterSeasonNumber = episode.AirsAfterSeason.Value;
            }

            if (!string.IsNullOrWhiteSpace(episode.FinaleType))
            {
                _logger.Debug("TheTVDB episode {0} finale type: {1}", episode.Id, episode.FinaleType);
            }

            return item;
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _seriesProvider.GetImageResponse(url, cancellationToken);
        }
    }
}
