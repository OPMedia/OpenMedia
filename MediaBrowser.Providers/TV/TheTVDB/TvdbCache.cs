using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Providers.TV
{
    /// <summary>
    /// v4 JSON cache. The legacy XML filenames are written as a small compatibility shim
    /// for MissingEpisodeProvider and TvdbPersonImageProvider (which live outside this
    /// directory); no v1 API request or v1 XML parsing remains.
    /// </summary>
    internal sealed class TvdbCache
    {
        internal const string SeriesFile = "series-v4.json";
        internal const string EpisodesFile = "episodes-v4.json";
        internal const string UpdatesFile = "time.txt";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        private readonly IFileSystem _fileSystem;
        private readonly IServerConfigurationManager _config;

        public TvdbCache(IFileSystem fileSystem, IServerConfigurationManager config)
        {
            _fileSystem = fileSystem;
            _config = config;
        }

        internal string Root => Path.Combine(_config.ApplicationPaths.CachePath, "tvdb");

        internal string SeriesDirectory(string tvdbId) => Path.Combine(Root, tvdbId);

        internal string SeriesPath(string tvdbId) => Path.Combine(SeriesDirectory(tvdbId), SeriesFile);

        internal string EpisodesPath(string tvdbId) => Path.Combine(SeriesDirectory(tvdbId), EpisodesFile);

        internal string UpdatesPath => Path.Combine(Root, UpdatesFile);

        internal bool IsFresh(string path, int hours)
        {
            if (!_fileSystem.FileExists(path))
            {
                return false;
            }

            return (DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(path)).TotalHours < hours;
        }

        internal T Read<T>(string path) where T : class
        {
            try
            {
                var json = _fileSystem.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (IOException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        internal void Write<T>(string path, T value)
        {
            _fileSystem.CreateDirectory(Path.GetDirectoryName(path));
            _fileSystem.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8);
        }

        internal void WriteCompatibilityFiles(string tvdbId, TvdbSeries series, List<TvdbEpisode> episodes)
        {
            var directory = SeriesDirectory(tvdbId);
            _fileSystem.CreateDirectory(directory);

            try
            {
                foreach (var file in _fileSystem.GetFilePaths(directory))
                {
                    if (Path.GetFileName(file).StartsWith("episode-", StringComparison.OrdinalIgnoreCase))
                    {
                        _fileSystem.DeleteFile(file);
                    }
                }
            }
            catch (IOException)
            {
                // Stale files are harmless: they are only used to detect missing episodes.
            }

            var actors = series?.Characters?
                .Where(i => !string.IsNullOrWhiteSpace(i.PersonName))
                .ToList() ?? new List<TvdbCharacter>();

            WriteXml(Path.Combine(directory, "actors.xml"), writer =>
            {
                writer.WriteStartElement("Actors");
                foreach (var character in actors)
                {
                    writer.WriteStartElement("Actor");
                    writer.WriteElementString("Name", character.PersonName);
                    writer.WriteElementString("Image", character.Image ?? character.PersonImageUrl ?? string.Empty);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            });

            foreach (var episode in episodes ?? new List<TvdbEpisode>())
            {
                if (episode.SeasonNumber < 0 || episode.Number < 1 || string.IsNullOrWhiteSpace(episode.Name))
                {
                    continue;
                }

                WriteXml(Path.Combine(directory, string.Format(CultureInfo.InvariantCulture, "episode-{0}-{1}.xml", episode.SeasonNumber, episode.Number)), writer => WriteEpisode(writer, episode));

                if (episode.AbsoluteNumber.HasValue && episode.AbsoluteNumber.Value > 0)
                {
                    WriteXml(Path.Combine(directory, string.Format(CultureInfo.InvariantCulture, "episode-abs-{0}.xml", episode.AbsoluteNumber.Value)), writer => WriteEpisode(writer, episode));
                }
            }
        }

        private static void WriteEpisode(XmlWriter writer, TvdbEpisode episode)
        {
            writer.WriteStartElement("Episode");
            writer.WriteElementString("id", episode.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("EpisodeName", episode.Name);
            writer.WriteElementString("FirstAired", episode.Aired ?? string.Empty);
            writer.WriteElementString("SeasonNumber", episode.SeasonNumber.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("EpisodeNumber", episode.Number.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        private void WriteXml(string path, Action<XmlWriter> write)
        {
            using (var text = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true }))
                {
                    write(writer);
                }

                _fileSystem.WriteAllText(path, text.ToString(), Encoding.UTF8);
            }
        }

        internal static string GetSeriesDataPath(IApplicationPaths appPaths, Dictionary<string, string> providerIds)
        {
            if (providerIds == null)
            {
                return null;
            }

            foreach (var key in new[] { MetadataProviders.Tvdb.ToString(), MetadataProviders.Imdb.ToString(), MetadataProviders.Zap2It.ToString() })
            {
                if (providerIds.TryGetValue(key, out var id) && !string.IsNullOrWhiteSpace(id))
                {
                    return Path.Combine(appPaths.CachePath, "tvdb", id);
                }
            }

            return null;
        }
    }
}
