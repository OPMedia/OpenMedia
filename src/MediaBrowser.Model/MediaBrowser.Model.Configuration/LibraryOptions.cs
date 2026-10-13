using System;

namespace MediaBrowser.Model.Configuration;

public class LibraryOptions
{
	public bool EnableArchiveMediaFiles { get; set; }

	public bool EnablePhotos { get; set; }

	public bool EnableRealtimeMonitor { get; set; }

	public bool EnableChapterImageExtraction { get; set; }

	public bool ExtractChapterImagesDuringLibraryScan { get; set; }

	public bool DownloadImagesInAdvance { get; set; }

	public MediaPathInfo[] PathInfos { get; set; }

	public bool SaveLocalMetadata { get; set; }

	public bool EnableInternetProviders { get; set; }

	public bool ImportMissingEpisodes { get; set; }

	public bool EnableAutomaticSeriesGrouping { get; set; }

	public bool EnableEmbeddedTitles { get; set; }

	public int AutomaticRefreshIntervalDays { get; set; }

	public string PreferredMetadataLanguage { get; set; }

	public string MetadataCountryCode { get; set; }

	public string SeasonZeroDisplayName { get; set; }

	public string[] MetadataSavers { get; set; }

	public string[] DisabledLocalMetadataReaders { get; set; }

	public string[] LocalMetadataReaderOrder { get; set; }

	public string[] DisabledSubtitleFetchers { get; set; }

	public string[] SubtitleFetcherOrder { get; set; }

	public bool SkipSubtitlesIfEmbeddedSubtitlesPresent { get; set; }

	public bool SkipSubtitlesIfAudioTrackMatches { get; set; }

	public string[] SubtitleDownloadLanguages { get; set; }

	public bool RequirePerfectSubtitleMatch { get; set; }

	public bool SaveSubtitlesWithMedia { get; set; }

	public TypeOptions[] TypeOptions { get; set; }

	public TypeOptions GetTypeOptions(string type)
	{
		TypeOptions[] typeOptions = TypeOptions;
		foreach (TypeOptions typeOptions2 in typeOptions)
		{
			if (string.Equals(typeOptions2.Type, type, StringComparison.OrdinalIgnoreCase))
			{
				return typeOptions2;
			}
		}
		return null;
	}

	public LibraryOptions()
	{
		TypeOptions = Array.Empty<TypeOptions>();
		DisabledSubtitleFetchers = Array.Empty<string>();
		SubtitleFetcherOrder = Array.Empty<string>();
		DisabledLocalMetadataReaders = Array.Empty<string>();
		RequirePerfectSubtitleMatch = true;
		EnablePhotos = true;
		SaveSubtitlesWithMedia = true;
		EnableRealtimeMonitor = true;
		PathInfos = Array.Empty<MediaPathInfo>();
		EnableInternetProviders = true;
		EnableAutomaticSeriesGrouping = true;
		SeasonZeroDisplayName = "Specials";
	}
}
