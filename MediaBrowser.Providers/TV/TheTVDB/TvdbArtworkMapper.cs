using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Providers.TV
{
    /// <summary>
    /// Maps v4 artwork records to Emby image types. Artwork type ids are resolved from
    /// /v4/artwork/types by the client, so no numeric ids are hard-coded here.
    /// </summary>
    internal static class TvdbArtworkMapper
    {
        public const string ProviderName = "TheTVDB";

        public static IEnumerable<RemoteImageInfo> Map(
            IEnumerable<TvdbArtwork> artworks,
            Dictionary<long, string> typeNames,
            string preferredLanguage,
            ArtworkScope scope,
            long? seasonId = null)
        {
            var result = new List<RemoteImageInfo>();

            foreach (var artwork in artworks ?? Enumerable.Empty<TvdbArtwork>())
            {
                if (artwork == null || string.IsNullOrWhiteSpace(artwork.Image))
                {
                    continue;
                }

                if (scope == ArtworkScope.Series && (artwork.SeasonId.HasValue || artwork.EpisodeId.HasValue))
                {
                    continue;
                }

                if (scope == ArtworkScope.Season &&
                    (!seasonId.HasValue || artwork.SeasonId != seasonId.Value || artwork.EpisodeId.HasValue))
                {
                    continue;
                }

                typeNames.TryGetValue(artwork.Type, out var typeName);
                var imageType = GetImageType(typeName ?? string.Empty, scope);
                if (!imageType.HasValue)
                {
                    continue;
                }

                result.Add(new RemoteImageInfo
                {
                    ProviderName = ProviderName,
                    Type = imageType.Value,
                    Url = TvdbSeriesProvider.BuildImageUrl(artwork.Image),
                    ThumbnailUrl = TvdbSeriesProvider.BuildImageUrl(artwork.Thumbnail),
                    Language = artwork.Language,
                    Width = artwork.Width > 0 ? (int)artwork.Width : 0,
                    Height = artwork.Height > 0 ? (int)artwork.Height : 0,
                    CommunityRating = artwork.Score > 0 ? artwork.Score : (double?)null,
                    VoteCount = null
                });
            }

            var isLanguageEn = string.Equals(preferredLanguage, "en", System.StringComparison.OrdinalIgnoreCase);
            return result
                .OrderByDescending(i => IsPreferredLanguage(i.Language, preferredLanguage) ? 3 :
                    !isLanguageEn && string.Equals(i.Language, "en", System.StringComparison.OrdinalIgnoreCase) ? 2 :
                    string.IsNullOrEmpty(i.Language) ? (isLanguageEn ? 3 : 2) : 0)
                .ThenByDescending(i => i.CommunityRating ?? 0)
                .ToList();
        }

        public static IEnumerable<RemoteImageInfo> ForEpisode(IEnumerable<TvdbEpisode> episodes)
        {
            foreach (var episode in episodes ?? Enumerable.Empty<TvdbEpisode>())
            {
                if (string.IsNullOrWhiteSpace(episode?.Image))
                {
                    continue;
                }

                yield return new RemoteImageInfo
                {
                    ProviderName = ProviderName,
                    Type = ImageType.Primary,
                    Url = TvdbSeriesProvider.BuildImageUrl(episode.Image),
                    Width = 0,
                    Height = 0
                };
            }
        }

        private static bool IsPreferredLanguage(string language, string preferredLanguage)
        {
            if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(preferredLanguage))
            {
                return false;
            }

            var shortLanguage = language.Split('-')[0];
            return string.Equals(shortLanguage, preferredLanguage.Split('-')[0], System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(shortLanguage, ThreeLetterToTwoLetter(preferredLanguage), System.StringComparison.OrdinalIgnoreCase);
        }

        private static string ThreeLetterToTwoLetter(string language)
        {
            switch (language.ToLowerInvariant())
            {
                case "eng": return "en";
                case "fra": return "fr";
                case "deu": return "de";
                case "spa": return "es";
                case "ita": return "it";
                case "jpn": return "ja";
                case "por": return "pt";
                case "zho": return "zh";
                case "kor": return "ko";
                case "rus": return "ru";
                default: return language;
            }
        }

        private static ImageType? GetImageType(string artworkTypeName, ArtworkScope scope)
        {
            var name = artworkTypeName.ToLowerInvariant();

            if (name.Contains("banner"))
            {
                return ImageType.Banner;
            }

            if (name.Contains("fanart") || name.Contains("backdrop"))
            {
                return ImageType.Backdrop;
            }

            if (name.Contains("poster") || (scope == ArtworkScope.Episode && name.Contains("episode")))
            {
                return ImageType.Primary;
            }

            return null;
        }
    }

    internal enum ArtworkScope
    {
        Series,
        Season,
        Episode
    }
}
