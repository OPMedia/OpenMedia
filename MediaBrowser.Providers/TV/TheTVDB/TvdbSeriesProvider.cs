using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Globalization;

namespace MediaBrowser.Providers.TV
{
    /// <summary>
    /// Series metadata provider backed by the official TheTVDB v4 REST API.
    /// </summary>
    public class TvdbSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        internal static TvdbSeriesProvider Current { get; private set; }

        private const int CacheHours = 12;

        private readonly IServerConfigurationManager _config;
        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly ILocalizationManager _localizationManager;
        private readonly TvdbV4Client _client;
        private readonly TvdbCache _cache;
        private readonly SemaphoreSlim _ensureSemaphore = new SemaphoreSlim(1, 1);

        public TvdbSeriesProvider(IHttpClient httpClient, IFileSystem fileSystem, IServerConfigurationManager config, ILogger logger, ILibraryManager libraryManager, ILocalizationManager localizationManager)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
            _libraryManager = libraryManager;
            _localizationManager = localizationManager;
            _client = new TvdbV4Client(httpClient, config, logger);
            _cache = new TvdbCache(fileSystem, config);
            Current = this;
        }

        public string Name => "TheTVDB";

        public int Order => 0;

        internal bool IsConfigured => _client.IsConfigured;

        internal const string remove = "\"'!`?";
        internal const string spacers = "/,.:;\\(){}[]+-_=–*";

        /// <summary>Kept because other TV providers use it to locate the tvdb cache directory.</summary>
        internal static string GetSeriesDataPath(IApplicationPaths appPaths, Dictionary<string, string> seriesProviderIds)
        {
            return TvdbCache.GetSeriesDataPath(appPaths, seriesProviderIds);
        }

        internal static string GetSeriesDataPath(IApplicationPaths appPaths)
        {
            return Path.Combine(appPaths.CachePath, "tvdb");
        }

        internal static bool IsValidSeries(Dictionary<string, string> seriesProviderIds)
        {
            if (seriesProviderIds == null)
            {
                return false;
            }

            return new[] { MetadataProviders.Tvdb.ToString(), MetadataProviders.Imdb.ToString(), MetadataProviders.Zap2It.ToString() }
                .Any(key => seriesProviderIds.TryGetValue(key, out var id) && !string.IsNullOrWhiteSpace(id));
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                return Array.Empty<RemoteSearchResult>();
            }

            if (IsValidSeries(searchInfo.ProviderIds))
            {
                var metadata = await GetMetadata(searchInfo, cancellationToken).ConfigureAwait(false);
                if (metadata.HasMetadata)
                {
                    return new List<RemoteSearchResult>
                    {
                        new RemoteSearchResult
                        {
                            Name = metadata.Item.Name,
                            PremiereDate = metadata.Item.PremiereDate,
                            ProductionYear = metadata.Item.ProductionYear,
                            ProviderIds = metadata.Item.ProviderIds,
                            SearchProviderName = Name
                        }
                    };
                }
            }

            return await FindSeries(searchInfo.Name, searchInfo.Year, cancellationToken).ConfigureAwait(false);
        }

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo itemId, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>
            {
                QueriedById = true
            };

            if (!IsConfigured)
            {
                return result;
            }

            var tvdbId = await ResolveTvdbId(itemId.ProviderIds, itemId.Name, itemId.Year, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(tvdbId))
            {
                result.QueriedById = false;
                return result;
            }

            var data = await EnsureData(tvdbId, false, cancellationToken).ConfigureAwait(false);
            if (data == null)
            {
                return result;
            }

            result.Item = new Series();
            result.HasMetadata = true;
            MapSeries(result, data.Item, tvdbId);
            return result;
        }

        public async Task Identify(SeriesInfo info)
        {
            if (!IsConfigured || !string.IsNullOrWhiteSpace(info.GetProviderId(MetadataProviders.Tvdb)))
            {
                return;
            }

            var id = await ResolveTvdbId(info.ProviderIds, info.Name, info.Year, CancellationToken.None).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(id))
            {
                info.SetProviderId(MetadataProviders.Tvdb, id);
            }
        }

        /// <summary>Resolves a TVDB id from provider ids or, as a last resort, a name search.</summary>
        internal async Task<string> ResolveTvdbId(Dictionary<string, string> providerIds, string name, int? year, CancellationToken cancellationToken)
        {
            if (providerIds != null)
            {
                if (providerIds.TryGetValue(MetadataProviders.Tvdb.ToString(), out var tvdbId) && !string.IsNullOrWhiteSpace(tvdbId))
                {
                    return tvdbId.Trim();
                }

                foreach (var key in new[] { MetadataProviders.Imdb.ToString(), MetadataProviders.Zap2It.ToString() })
                {
                    if (providerIds.TryGetValue(key, out var remoteId) && !string.IsNullOrWhiteSpace(remoteId))
                    {
                        var found = await _client.FindSeriesByRemoteId(remoteId.Trim(), cancellationToken).ConfigureAwait(false);
                        if (found.HasValue)
                        {
                            return found.Value.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var results = await FindSeries(name, year, cancellationToken).ConfigureAwait(false);
            return results.Select(i => i.GetProviderId(MetadataProviders.Tvdb)).FirstOrDefault(i => !string.IsNullOrWhiteSpace(i));
        }

        internal async Task<List<RemoteSearchResult>> FindSeries(string name, int? year, CancellationToken cancellationToken)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(name))
            {
                return new List<RemoteSearchResult>();
            }

            var results = await SearchByName(name, cancellationToken).ConfigureAwait(false);
            if (results.Count == 0)
            {
                var parsedName = _libraryManager.ParseName(name).Name;
                if (!string.IsNullOrWhiteSpace(parsedName) && !string.Equals(parsedName, name, StringComparison.OrdinalIgnoreCase))
                {
                    results = await SearchByName(parsedName, cancellationToken).ConfigureAwait(false);
                }
            }

            var comparableName = GetComparableName(name);
            return results
                .Where(i => !year.HasValue || !i.ProductionYear.HasValue || Math.Abs(year.Value - i.ProductionYear.Value) <= 1)
                .OrderBy(i => GetComparableName(i.Name ?? string.Empty).Contains(comparableName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();
        }

        private async Task<List<RemoteSearchResult>> SearchByName(string name, CancellationToken cancellationToken)
        {
            var items = await _client.SearchByName(name, null, cancellationToken).ConfigureAwait(false);
            var list = new List<RemoteSearchResult>();

            foreach (var item in items)
            {
                var id = !string.IsNullOrWhiteSpace(item.TvdbId) ? item.TvdbId : item.Id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var searchResult = new RemoteSearchResult
                {
                    Name = item.Title ?? item.Name,
                    Overview = item.Overview,
                    ImageUrl = BuildImageUrl(item.ImageUrl ?? item.Thumbnail),
                    SearchProviderName = Name
                };

                searchResult.SetProviderId(MetadataProviders.Tvdb, id);

                var imdb = item.RemoteIds?.FirstOrDefault(i => IsImdb(i));
                if (imdb != null && !string.IsNullOrWhiteSpace(imdb.Id))
                {
                    searchResult.SetProviderId(MetadataProviders.Imdb, imdb.Id);
                }

                int parsedYear;
                if (!string.IsNullOrWhiteSpace(item.Year) &&
                    int.TryParse(item.Year, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedYear))
                {
                    searchResult.ProductionYear = parsedYear;
                }

                list.Add(searchResult);
            }

            return list;
        }

        private static bool IsImdb(TvdbRemoteId id)
        {
            return id != null && (id.SourceName ?? string.Empty).IndexOf("imdb", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string BuildImageUrl(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? path
                : TvdbV4Client.ArtworkBaseUrl + path.TrimStart('/');
        }

        private string GetComparableName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            name = name.ToLower(CultureInfo.InvariantCulture);
            name = _localizationManager.NormalizeFormKD(name);
            var sb = new StringBuilder();
            foreach (var c in name)
            {
                if ((int)c >= 0x2B0 && (int)c <= 0x0333)
                {
                    continue;
                }

                if (remove.IndexOf(c) > -1)
                {
                    continue;
                }

                if (spacers.IndexOf(c) > -1)
                {
                    sb.Append(' ');
                }
                else if (c == '&')
                {
                    sb.Append(" and ");
                }
                else
                {
                    sb.Append(c);
                }
            }

            name = sb.ToString().Replace(", the", string.Empty).Replace("the ", " ").Replace(" the ", " ");
            string previous;
            do
            {
                previous = name;
                name = name.Replace("  ", " ");
            }
            while (name.Length != previous.Length);

            return name.Trim();
        }

        /// <summary>Gets cached v4 data, refreshing it when stale or when forced by the scan task.</summary>
        internal async Task<TvdbCacheEntry> EnsureData(string tvdbId, bool force, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(tvdbId) || !IsConfigured)
            {
                return null;
            }

            await _ensureSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!force &&
                    _cache.IsFresh(_cache.SeriesPath(tvdbId), CacheHours) &&
                    _cache.IsFresh(_cache.EpisodesPath(tvdbId), CacheHours))
                {
                    var cachedSeries = _cache.Read<TvdbSeries>(_cache.SeriesPath(tvdbId));
                    var cachedEpisodes = _cache.Read<List<TvdbEpisode>>(_cache.EpisodesPath(tvdbId));
                    if (cachedSeries != null && cachedEpisodes != null)
                    {
                        return new TvdbCacheEntry(cachedSeries, cachedEpisodes);
                    }
                }

                var series = await _client.GetSeries(long.Parse(tvdbId, CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
                if (series == null)
                {
                    return null;
                }

                var episodes = await _client.GetEpisodes(series.Id, cancellationToken).ConfigureAwait(false);
                _cache.Write(_cache.SeriesPath(tvdbId), series);
                _cache.Write(_cache.EpisodesPath(tvdbId), episodes);
                _cache.WriteCompatibilityFiles(tvdbId, series, episodes);

                return new TvdbCacheEntry(series, episodes);
            }
            finally
            {
                _ensureSemaphore.Release();
            }
        }

        internal Task<Dictionary<long, string>> GetArtworkTypes(CancellationToken cancellationToken)
        {
            return _client.GetArtworkTypes(cancellationToken);
        }

        /// <summary>Called by the post-scan task for an id reported by /v4/updates.</summary>
        internal async Task<bool> RefreshSeries(string tvdbId, CancellationToken cancellationToken)
        {
            try
            {
                var entry = await EnsureData(tvdbId, true, cancellationToken).ConfigureAwait(false);
                return entry != null;
            }
            catch (HttpException ex)
            {
                _logger.ErrorException("Error refreshing TheTVDB series {0}", ex, tvdbId);
                return false;
            }
            catch (FormatException ex)
            {
                _logger.ErrorException("Invalid TheTVDB series id {0}", ex, tvdbId);
                return false;
            }
        }

        private void MapSeries(MetadataResult<Series> result, TvdbSeries series, string tvdbId)
        {
            var item = result.Item;
            item.SetProviderId(MetadataProviders.Tvdb, tvdbId);

            item.Name = series.Name;
            item.Overview = series.Overview;
            item.AirTime = series.AirsTime;

            var imdb = series.RemoteIds?.FirstOrDefault(i => IsImdb(i));
            if (imdb != null && !string.IsNullOrWhiteSpace(imdb.Id))
            {
                item.SetProviderId(MetadataProviders.Imdb, imdb.Id);
            }

            var zap2it = series.RemoteIds?.FirstOrDefault(i =>
                (i.SourceName ?? string.Empty).IndexOf("zap2it", StringComparison.OrdinalIgnoreCase) >= 0);
            if (zap2it != null && !string.IsNullOrWhiteSpace(zap2it.Id))
            {
                item.SetProviderId(MetadataProviders.Zap2It, zap2it.Id);
            }

            DateTime date;
            if (DateTime.TryParse(series.FirstAired, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date))
            {
                item.PremiereDate = date;
                item.ProductionYear = date.Year;
            }

            if (!string.IsNullOrWhiteSpace(series.LastAired) &&
                DateTime.TryParse(series.LastAired, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date))
            {
                item.EndDate = date;
            }

            var statusName = series.Status?.Name;
            if (!string.IsNullOrWhiteSpace(statusName))
            {
                SeriesStatus status;
                if (Enum.TryParse(statusName, true, out status))
                {
                    item.Status = status;
                }
                else if (statusName.IndexOf("continu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         statusName.IndexOf("returning", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    item.Status = SeriesStatus.Continuing;
                }
                else if (statusName.IndexOf("end", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    item.Status = SeriesStatus.Ended;
                }
            }

            if (series.AverageRuntime.HasValue && series.AverageRuntime.Value > 0)
            {
                item.RunTimeTicks = TimeSpan.FromMinutes(series.AverageRuntime.Value).Ticks;
            }

            if (series.Genres != null)
            {
                item.Genres = Array.Empty<string>();
                foreach (var genre in series.Genres.Where(i => !string.IsNullOrWhiteSpace(i.Name)).Select(i => i.Name))
                {
                    item.AddGenre(genre);
                }
            }

            var contentRating = series.ContentRatings?
                .FirstOrDefault(i => string.Equals(i.Country, "USA", StringComparison.OrdinalIgnoreCase)) ?? series.ContentRatings?.FirstOrDefault();
            if (contentRating != null && !string.IsNullOrWhiteSpace(contentRating.Name))
            {
                item.OfficialRating = contentRating.Name;
            }

            var studios = new List<string>();
            if (!string.IsNullOrWhiteSpace(series.LatestNetwork?.Name))
            {
                studios.Add(series.LatestNetwork.Name);
            }

            if (!string.IsNullOrWhiteSpace(series.OriginalNetwork?.Name) && !studios.Contains(series.OriginalNetwork.Name, StringComparer.OrdinalIgnoreCase))
            {
                studios.Add(series.OriginalNetwork.Name);
            }

            if (studios.Count > 0)
            {
                item.SetStudios(studios);
            }

            if (series.AirsDays != null)
            {
                var days = new List<DayOfWeek>();
                if (series.AirsDays.Sunday) days.Add(DayOfWeek.Sunday);
                if (series.AirsDays.Monday) days.Add(DayOfWeek.Monday);
                if (series.AirsDays.Tuesday) days.Add(DayOfWeek.Tuesday);
                if (series.AirsDays.Wednesday) days.Add(DayOfWeek.Wednesday);
                if (series.AirsDays.Thursday) days.Add(DayOfWeek.Thursday);
                if (series.AirsDays.Friday) days.Add(DayOfWeek.Friday);
                if (series.AirsDays.Saturday) days.Add(DayOfWeek.Saturday);
                item.AirDays = days.ToArray();
            }

            foreach (var character in series.Characters ?? new List<TvdbCharacter>())
            {
                if (string.IsNullOrWhiteSpace(character.PersonName))
                {
                    continue;
                }

                result.AddPerson(new PersonInfo
                {
                    Type = PersonType.Actor,
                    Name = character.PersonName,
                    Role = character.Name,
                    ImageUrl = BuildImageUrl(character.Image ?? character.PersonImageUrl)
                });
            }
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClient.GetResponse(new HttpRequestOptions
            {
                CancellationToken = cancellationToken,
                Url = url,
                BufferContent = false
            });
        }
    }

    internal sealed class TvdbCacheEntry
    {
        public TvdbCacheEntry(TvdbSeries item, List<TvdbEpisode> episodes)
        {
            Item = item;
            Episodes = episodes;
        }

        public TvdbSeries Item { get; }

        public List<TvdbEpisode> Episodes { get; }
    }
}
