using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Globalization;

namespace MediaBrowser.Providers.TV
{
    /// <summary>
    /// Incrementally refreshes library series using TheTVDB v4 /updates.
    /// The last successfully processed unix timestamp is kept in cache/tvdb/time.txt.
    /// </summary>
    public class TvdbPrescanTask : ILibraryPostScanTask
    {
        /// <summary>Human-facing links in TvExternalIds still point at the website.</summary>
        public const string TvdbBaseUrl = "https://thetvdb.com/";

        private const int FullRefreshThresholdDays = 90;
        private const int ClockSkewMarginSeconds = 300;
        private const int CacheHours = 24;

        private readonly IFileSystem _fileSystem;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly TvdbV4Client _client;
        private readonly TvdbCache _cache;
        private readonly TvdbSeriesProvider _seriesProvider;

        public TvdbPrescanTask(ILogger logger, IHttpClient httpClient, IServerConfigurationManager config, IFileSystem fileSystem, ILibraryManager libraryManager, ILocalizationManager localizationManager)
        {
            _logger = logger;
            _fileSystem = fileSystem;
            _libraryManager = libraryManager;
            _client = new TvdbV4Client(httpClient, config, logger);
            _cache = new TvdbCache(fileSystem, config);
            _seriesProvider = new TvdbSeriesProvider(httpClient, fileSystem, config, logger, libraryManager, localizationManager);
        }

        public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            // A missing key must be a no-op, not a stream of empty /updates calls.
            if (!_client.IsConfigured)
            {
                TvdbV4Client.WarnMissingKeyOnce(_logger);
                progress.Report(100);
                return;
            }

            var path = _cache.Root;
            _fileSystem.CreateDirectory(path);

            var timestampFile = _cache.UpdatesPath;
            var fileInfo = _fileSystem.GetFileInfo(timestampFile);
            if (fileInfo.Exists && (DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileInfo)).TotalHours < CacheHours)
            {
                progress.Report(100);
                return;
            }

            var lastTime = fileInfo.Exists ? ReadTimestamp(timestampFile) : 0;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var isFullRefresh = lastTime <= 0 ||
                                lastTime > now + ClockSkewMarginSeconds ||
                                lastTime < now - TimeSpan.FromDays(FullRefreshThresholdDays).TotalSeconds;

            var idsInLibrary = GetSeriesIdsInLibrary();
            List<string> idsToRefresh;
            long newTime;

            if (isFullRefresh)
            {
                // /updates only covers 90 days; start by populating everything we track.
                idsToRefresh = idsInLibrary.ToList();
                newTime = now - ClockSkewMarginSeconds;
            }
            else
            {
                var result = await _client.GetUpdates(lastTime, cancellationToken).ConfigureAwait(false);
                var changedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var update in result.Updates)
                {
                    if (update.EntityType != null &&
                        !string.Equals(update.EntityType, "series", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AddIfInLibrary(changedIds, idsInLibrary, update.RecordId);
                    if (update.MergeToId.HasValue)
                    {
                        AddIfInLibrary(changedIds, idsInLibrary, update.MergeToId.Value);
                    }
                }

                idsToRefresh = changedIds.ToList();
                newTime = result.NewTime;
            }

            var completed = 0;
            var failed = false;
            foreach (var id in idsToRefresh)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var languages = GetLanguagesForSeries(id);
                foreach (var language in languages)
                {
                    _logger.Info("Updating TheTVDB series {0}, language {1}", id, language);
                    if (!await _seriesProvider.RefreshSeries(id, cancellationToken).ConfigureAwait(false))
                    {
                        failed = true;
                    }
                }

                completed++;
                progress.Report(idsToRefresh.Count == 0 ? 100 : 100d * completed / idsToRefresh.Count);
            }

            // Never persist 0 or an older value: a failed/stale run must not rewind the cursor.
            if (!failed && newTime > 0)
            {
                var safeTime = Math.Min(newTime, now - ClockSkewMarginSeconds);
                if (safeTime > lastTime)
                {
                    _fileSystem.WriteAllText(timestampFile, safeTime.ToString(CultureInfo.InvariantCulture));
                }
            }

            progress.Report(100);
        }

        private static void AddIfInLibrary(HashSet<string> target, HashSet<string> idsInLibrary, long id)
        {
            if (id > 0)
            {
                var value = id.ToString(CultureInfo.InvariantCulture);
                if (idsInLibrary.Contains(value))
                {
                    target.Add(value);
                }
            }
        }

        private HashSet<string> GetSeriesIdsInLibrary()
        {
            var series = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(Series).Name },
                Recursive = true,
                GroupByPresentationUniqueKey = false,
                DtoOptions = new DtoOptions(false)
                {
                    EnableImages = false
                }
            }).OfType<Series>();

            return new HashSet<string>(series
                .Select(i => i.GetProviderId(MetadataProviders.Tvdb))
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim()), StringComparer.OrdinalIgnoreCase);
        }

        private List<string> GetLanguagesForSeries(string id)
        {
            var series = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(Series).Name },
                Recursive = true,
                GroupByPresentationUniqueKey = false,
                DtoOptions = new DtoOptions(false)
                {
                    EnableImages = false
                }
            }).OfType<Series>();

            var languages = series
                .Where(i => string.Equals(i.GetProviderId(MetadataProviders.Tvdb), id, StringComparison.OrdinalIgnoreCase))
                .Select(i => i.GetPreferredMetadataLanguage())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return languages.Count == 0 ? new List<string> { "en" } : languages;
        }

        private long ReadTimestamp(string path)
        {
            try
            {
                long value;
                return long.TryParse(_fileSystem.ReadAllText(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                _logger.Warn("Unable to read TheTVDB update timestamp: {0}", ex.Message);
                return 0;
            }
        }
    }
}
