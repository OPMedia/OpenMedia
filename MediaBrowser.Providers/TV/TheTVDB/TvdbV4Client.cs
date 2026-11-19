using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;

namespace MediaBrowser.Providers.TV
{
    /// <summary>
    /// Minimal client for the official TheTVDB v4 REST API (https://api4.thetvdb.com/v4).
    /// Owns the bearer token; every provider instance gets its own client.
    /// </summary>
    internal sealed class TvdbV4Client
    {
        internal const string BaseUrl = "https://api4.thetvdb.com/v4/";

        /// <summary>Artwork paths in v4 records are relative to this host.</summary>
        internal const string ArtworkBaseUrl = "https://art.thetvdb.com/banners/";

        private const int DefaultTokenValidityDays = 30;
        private const int MaxPages = 100;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // One process-wide warning is enough; a library with a thousand episodes must not
        // produce a thousand "API key missing" lines.
        private static int _missingKeyWarningLogged;

        private readonly IHttpClient _httpClient;
        private readonly IServerConfigurationManager _config;
        private readonly ILogger _logger;

        private string _token;
        private DateTimeOffset _tokenExpiresAt;
        private Dictionary<long, string> _artworkTypes;

        public TvdbV4Client(IHttpClient httpClient, IServerConfigurationManager config, ILogger logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
        }

        internal bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

        private string ApiKey => _config.Configuration?.TheTvdb?.ApiKey?.Trim();

        private string Pin => _config.Configuration?.TheTvdb?.Pin?.Trim();

        private void WarnMissingKey()
        {
            WarnMissingKeyOnce(_logger);
        }

        /// <summary>Emits the "key missing" warning at most once per process.</summary>
        internal static void WarnMissingKeyOnce(ILogger logger)
        {
            if (Interlocked.Exchange(ref _missingKeyWarningLogged, 1) == 0)
            {
                logger?.Warn(
                    "TheTVDB v4 provider is disabled because no API key is configured. " +
                    "Create a free key at https://www.thetvdb.com/dashboard/account/apikey and set " +
                    "'TheTvdb:ApiKey' (and optionally 'TheTvdb:Pin' for subscriber keys) in system.xml, then restart the server.");
            }
        }

        internal static void ResetMissingKeyWarningForTests()
        {
            Interlocked.Exchange(ref _missingKeyWarningLogged, 0);
        }

        /// <summary>Ensures a valid bearer token exists, logging in when it is missing or expired.</summary>
        private async Task<string> EnsureToken(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(_token) && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _token;
            }

            await Login(cancellationToken).ConfigureAwait(false);
            return _token;
        }

        private async Task Login(CancellationToken cancellationToken)
        {
            var apiKey = ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                WarnMissingKey();
                throw new InvalidOperationException("TheTVDB API key is not configured; the provider is disabled.");
            }

            var body = new Dictionary<string, string>
            {
                ["apikey"] = apiKey
            };

            var pin = Pin;
            if (!string.IsNullOrWhiteSpace(pin))
            {
                body["pin"] = pin;
            }

            var options = new HttpRequestOptions
            {
                Url = BaseUrl + "login",
                RequestContentType = "application/json",
                RequestContent = JsonSerializer.Serialize(body),
                CancellationToken = cancellationToken,
                EnableHttpCompression = true
            };

            TvdbLoginResponse result;
            using (var response = await Send(options, "POST").ConfigureAwait(false))
            {
                result = await JsonSerializer.DeserializeAsync<TvdbLoginResponse>(
                    response.Content, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            var token = result?.Data?.Token;
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("TheTVDB login returned no token. Check the configured API key and PIN.");
            }

            _token = token;
            // expiresIn 在官方文档里是秒数，但不同版本/代理可能给字符串（ISO-8601 时长）。
            // 解析不出来就退回默认 30 天 —— 过期时间算错也不会坏事，401 会触发重登。
            long expiresInSeconds = ReadExpiresInSeconds(result.Data.ExpiresIn);
            var validity = expiresInSeconds > 60
                ? TimeSpan.FromSeconds(expiresInSeconds - 60)
                : TimeSpan.FromDays(DefaultTokenValidityDays).Subtract(TimeSpan.FromHours(1));
            _tokenExpiresAt = DateTimeOffset.UtcNow.Add(validity);
        }

        /// <summary>Sends a request; throws <see cref="HttpException"/> for non-success responses.</summary>
        private static long ReadExpiresInSeconds(System.Text.Json.JsonElement value)
        {
            switch (value.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Number:
                    return value.TryGetInt64(out var number) ? number : 0;
                case System.Text.Json.JsonValueKind.String:
                    return long.TryParse(value.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
                default:
                    return 0;
            }
        }

        private async Task<HttpResponseInfo> Send(HttpRequestOptions options, string method)
        {
            var response = await _httpClient.SendAsync(options, method).ConfigureAwait(false);
            if (response.StatusCode < HttpStatusCode.OK || response.StatusCode >= (HttpStatusCode)300)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new HttpException("TheTVDB request failed with status " + status)
                {
                    StatusCode = status
                };
            }

            return response;
        }

        /// <summary>
        /// Performs a GET with an authenticated envelope response. On 401 the token is
        /// discarded, login is retried once, and the original request is repeated once.
        /// </summary>
        private async Task<TvdbResponse<T>> Get<T>(string relativeUrl, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var token = await EnsureToken(cancellationToken).ConfigureAwait(false);

                var options = new HttpRequestOptions
                {
                    Url = BaseUrl + relativeUrl,
                    AcceptHeader = "application/json",
                    CancellationToken = cancellationToken,
                    EnableHttpCompression = true
                };
                options.RequestHeaders["Authorization"] = "Bearer " + token;

                try
                {
                    using (var response = await Send(options, "GET").ConfigureAwait(false))
                    {
                        return await JsonSerializer.DeserializeAsync<TvdbResponse<T>>(
                            response.Content, JsonOptions, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (HttpException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    _token = null;
                    _tokenExpiresAt = default;
                    await Login(cancellationToken).ConfigureAwait(false);
                }
            }

            return null;
        }

        internal async Task<List<TvdbSearchItem>> SearchByName(string name, int? year, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                WarnMissingKey();
                return new List<TvdbSearchItem>();
            }

            var query = "search?query=" + Uri.EscapeDataString(name ?? string.Empty) + "&type=series";
            if (year.HasValue)
            {
                query += "&year=" + year.Value.ToString(CultureInfo.InvariantCulture);
            }

            var response = await Get<List<TvdbSearchItem>>(query, cancellationToken).ConfigureAwait(false);
            return response?.Data ?? new List<TvdbSearchItem>();
        }

        /// <summary>Resolves an external id (IMDB, TVDB, ...) through /search/remoteid.</summary>
        internal async Task<long?> FindSeriesByRemoteId(string remoteId, CancellationToken cancellationToken)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(remoteId))
            {
                return null;
            }

            var url = "search/remoteid/" + Uri.EscapeDataString(remoteId.Trim());
            var response = await Get<List<TvdbRemoteIdResult>>(url, cancellationToken).ConfigureAwait(false);
            var series = response?.Data?
                .Select(i => i.Series)
                .FirstOrDefault(i => i != null && i.Id > 0);

            return series?.Id;
        }

        internal async Task<TvdbSeries> GetSeries(long id, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                WarnMissingKey();
                return null;
            }

            try
            {
                var response = await Get<TvdbSeries>("series/" + id.ToString(CultureInfo.InvariantCulture) + "/extended", cancellationToken).ConfigureAwait(false);
                return response?.Data;
            }
            catch (HttpException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        /// <summary>Reads every page of the default episode order (100 records per page).</summary>
        internal async Task<List<TvdbEpisode>> GetEpisodes(long id, CancellationToken cancellationToken)
        {
            var episodes = new List<TvdbEpisode>();
            if (!IsConfigured)
            {
                WarnMissingKey();
                return episodes;
            }

            for (var page = 0; page < MaxPages; page++)
            {
                var url = "series/" + id.ToString(CultureInfo.InvariantCulture) + "/episodes/default?page=" + page.ToString(CultureInfo.InvariantCulture);
                var response = await Get<TvdbEpisodePage>(url, cancellationToken).ConfigureAwait(false);
                var pageEpisodes = response?.Data?.Episodes ?? new List<TvdbEpisode>();
                episodes.AddRange(pageEpisodes);

                if (pageEpisodes.Count < EpisodePageSize)
                {
                    break;
                }
            }

            return episodes;
        }

        private const int EpisodePageSize = 100;

        /// <summary>Reads all series updates since a unix timestamp, including paged results.</summary>
        internal async Task<TvdbUpdatesResult> GetUpdates(long since, CancellationToken cancellationToken)
        {
            var updates = new List<TvdbEntityUpdate>();
            if (!IsConfigured)
            {
                WarnMissingKey();
                return new TvdbUpdatesResult(updates, since);
            }

            var newTime = since;
            for (var page = 0; page < MaxPages; page++)
            {
                var url = "updates?since=" + since.ToString(CultureInfo.InvariantCulture) +
                          "&type=series&page=" + page.ToString(CultureInfo.InvariantCulture);
                var response = await Get<List<TvdbEntityUpdate>>(url, cancellationToken).ConfigureAwait(false);
                var data = response?.Data ?? new List<TvdbEntityUpdate>();
                updates.AddRange(data);

                foreach (var update in data)
                {
                    if (update.TimeStamp > newTime)
                    {
                        newTime = update.TimeStamp;
                    }
                }

                if (string.IsNullOrEmpty(response?.Links?.Next) || data.Count == 0)
                {
                    break;
                }
            }

            return new TvdbUpdatesResult(updates, newTime);
        }

        /// <summary>
        /// Artwork type ids are looked up from /artwork/types (not hard-coded) and cached
        /// for the lifetime of this client.
        /// </summary>
        internal async Task<Dictionary<long, string>> GetArtworkTypes(CancellationToken cancellationToken)
        {
            if (_artworkTypes != null)
            {
                return _artworkTypes;
            }

            if (!IsConfigured)
            {
                WarnMissingKey();
                return new Dictionary<long, string>();
            }

            var response = await Get<List<TvdbArtworkType>>("artwork/types", cancellationToken).ConfigureAwait(false);
            var map = new Dictionary<long, string>();
            foreach (var type in response?.Data ?? new List<TvdbArtworkType>())
            {
                map[type.Id] = type.Name ?? string.Empty;
            }

            _artworkTypes = map;
            return map;
        }
    }

    internal sealed class TvdbUpdatesResult
    {
        public TvdbUpdatesResult(List<TvdbEntityUpdate> updates, long newTime)
        {
            Updates = updates;
            NewTime = newTime;
        }

        public List<TvdbEntityUpdate> Updates { get; }

        public long NewTime { get; }
    }

    internal sealed class TvdbLoginResponse
    {
        [JsonPropertyName("data")]
        public TvdbLoginData Data { get; set; }
    }

    internal sealed class TvdbLoginData
    {
        [JsonPropertyName("token")]
        public string Token { get; set; }

        [JsonPropertyName("expiresIn")]
        public System.Text.Json.JsonElement ExpiresIn { get; set; }
    }

    internal sealed class TvdbResponse<T>
    {
        [JsonPropertyName("data")]
        public T Data { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("links")]
        public TvdbLinks Links { get; set; }
    }

    internal sealed class TvdbLinks
    {
        [JsonPropertyName("next")]
        public string Next { get; set; }

        [JsonPropertyName("total_items")]
        public int TotalItems { get; set; }

        [JsonPropertyName("page_size")]
        public int PageSize { get; set; }
    }

    internal sealed class TvdbSearchItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("tvdb_id")]
        public string TvdbId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("year")]
        public string Year { get; set; }

        [JsonPropertyName("overview")]
        public string Overview { get; set; }

        [JsonPropertyName("image_url")]
        public string ImageUrl { get; set; }

        [JsonPropertyName("thumbnail")]
        public string Thumbnail { get; set; }

        [JsonPropertyName("aliases")]
        public List<string> Aliases { get; set; }

        [JsonPropertyName("remote_ids")]
        public List<TvdbRemoteId> RemoteIds { get; set; }
    }

    internal sealed class TvdbRemoteIdResult
    {
        [JsonPropertyName("series")]
        public TvdbSeries Series { get; set; }
    }

    internal sealed class TvdbRemoteId
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("sourceName")]
        public string SourceName { get; set; }

        [JsonPropertyName("type")]
        public long Type { get; set; }
    }

    internal sealed class TvdbSeries
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("overview")]
        public string Overview { get; set; }

        [JsonPropertyName("image")]
        public string Image { get; set; }

        [JsonPropertyName("firstAired")]
        public string FirstAired { get; set; }

        [JsonPropertyName("lastAired")]
        public string LastAired { get; set; }

        [JsonPropertyName("year")]
        public string Year { get; set; }

        [JsonPropertyName("lastUpdated")]
        public string LastUpdated { get; set; }

        [JsonPropertyName("averageRuntime")]
        public int? AverageRuntime { get; set; }

        [JsonPropertyName("status")]
        public TvdbStatus Status { get; set; }

        [JsonPropertyName("airsTime")]
        public string AirsTime { get; set; }

        [JsonPropertyName("airsDays")]
        public TvdbAirsDays AirsDays { get; set; }

        [JsonPropertyName("genres")]
        public List<TvdbNamedRecord> Genres { get; set; }

        [JsonPropertyName("contentRatings")]
        public List<TvdbContentRating> ContentRatings { get; set; }

        [JsonPropertyName("companies")]
        public List<TvdbNamedRecord> Companies { get; set; }

        [JsonPropertyName("latestNetwork")]
        public TvdbNamedRecord LatestNetwork { get; set; }

        [JsonPropertyName("originalNetwork")]
        public TvdbNamedRecord OriginalNetwork { get; set; }

        [JsonPropertyName("remoteIds")]
        public List<TvdbRemoteId> RemoteIds { get; set; }

        [JsonPropertyName("aliases")]
        public List<TvdbAlias> Aliases { get; set; }

        [JsonPropertyName("artworks")]
        public List<TvdbArtwork> Artworks { get; set; }

        [JsonPropertyName("characters")]
        public List<TvdbCharacter> Characters { get; set; }

        [JsonPropertyName("seasons")]
        public List<TvdbSeason> Seasons { get; set; }
    }

    internal sealed class TvdbSeason
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("image")]
        public string Image { get; set; }

        [JsonPropertyName("type")]
        public long Type { get; set; }
    }

    internal sealed class TvdbStatus
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    internal sealed class TvdbAirsDays
    {
        [JsonPropertyName("sunday")]
        public bool Sunday { get; set; }

        [JsonPropertyName("monday")]
        public bool Monday { get; set; }

        [JsonPropertyName("tuesday")]
        public bool Tuesday { get; set; }

        [JsonPropertyName("wednesday")]
        public bool Wednesday { get; set; }

        [JsonPropertyName("thursday")]
        public bool Thursday { get; set; }

        [JsonPropertyName("friday")]
        public bool Friday { get; set; }

        [JsonPropertyName("saturday")]
        public bool Saturday { get; set; }
    }

    internal sealed class TvdbNamedRecord
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    internal sealed class TvdbContentRating
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("country")]
        public string Country { get; set; }
    }

    internal sealed class TvdbAlias
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; }
    }

    internal sealed class TvdbArtwork
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("image")]
        public string Image { get; set; }

        [JsonPropertyName("thumbnail")]
        public string Thumbnail { get; set; }

        [JsonPropertyName("type")]
        public long Type { get; set; }

        [JsonPropertyName("seasonId")]
        public long? SeasonId { get; set; }

        [JsonPropertyName("episodeId")]
        public long? EpisodeId { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; }

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("width")]
        public long Width { get; set; }

        [JsonPropertyName("height")]
        public long Height { get; set; }

        [JsonPropertyName("thumbnailWidth")]
        public long ThumbnailWidth { get; set; }

        [JsonPropertyName("thumbnailHeight")]
        public long ThumbnailHeight { get; set; }
    }

    internal sealed class TvdbArtworkType
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("recordType")]
        public string RecordType { get; set; }
    }

    internal sealed class TvdbCharacter
    {
        [JsonPropertyName("personName")]
        public string PersonName { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("image")]
        public string Image { get; set; }

        [JsonPropertyName("personImgURL")]
        public string PersonImageUrl { get; set; }

        [JsonPropertyName("sort")]
        public long Sort { get; set; }
    }

    internal sealed class TvdbEpisode
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("overview")]
        public string Overview { get; set; }

        [JsonPropertyName("aired")]
        public string Aired { get; set; }

        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("seasonNumber")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("absoluteNumber")]
        public int? AbsoluteNumber { get; set; }

        [JsonPropertyName("image")]
        public string Image { get; set; }

        [JsonPropertyName("runtime")]
        public int? Runtime { get; set; }

        [JsonPropertyName("finaleType")]
        public string FinaleType { get; set; }

        [JsonPropertyName("airsBeforeSeason")]
        public int? AirsBeforeSeason { get; set; }

        [JsonPropertyName("airsBeforeEpisode")]
        public int? AirsBeforeEpisode { get; set; }

        [JsonPropertyName("airsAfterSeason")]
        public int? AirsAfterSeason { get; set; }

        [JsonPropertyName("remoteIds")]
        public List<TvdbRemoteId> RemoteIds { get; set; }

        [JsonPropertyName("lastUpdated")]
        public string LastUpdated { get; set; }
    }

    internal sealed class TvdbEpisodePage
    {
        [JsonPropertyName("episodes")]
        public List<TvdbEpisode> Episodes { get; set; }

        [JsonPropertyName("series")]
        public TvdbSeries Series { get; set; }
    }

    internal sealed class TvdbEntityUpdate
    {
        [JsonPropertyName("recordId")]
        public long RecordId { get; set; }

        [JsonPropertyName("timeStamp")]
        public long TimeStamp { get; set; }

        [JsonPropertyName("entityType")]
        public string EntityType { get; set; }

        [JsonPropertyName("methodInt")]
        public int MethodInt { get; set; }

        [JsonPropertyName("mergeToId")]
        public long? MergeToId { get; set; }

        [JsonPropertyName("mergeToType")]
        public string MergeToType { get; set; }

        [JsonPropertyName("seriesId")]
        public long? SeriesId { get; set; }
    }
}
