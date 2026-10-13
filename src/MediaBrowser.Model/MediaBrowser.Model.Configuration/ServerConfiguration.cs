using System;
using MediaBrowser.Model.Dto;

namespace MediaBrowser.Model.Configuration;

public class ServerConfiguration : BaseApplicationConfiguration
{
	public const int DefaultHttpPort = 8096;

	public const int DefaultHttpsPort = 8920;

	public bool EnableUPnP { get; set; }

	public int PublicPort { get; set; }

	public int PublicHttpsPort { get; set; }

	public int HttpServerPortNumber { get; set; }

	public int HttpsPortNumber { get; set; }

	public bool EnableHttps { get; set; }

	public bool EnableNormalizedItemByNameIds { get; set; }

	public string CertificatePath { get; set; }

	public string CertificatePassword { get; set; }

	public bool IsPortAuthorized { get; set; }

	public bool AutoRunWebApp { get; set; }

	public bool EnableRemoteAccess { get; set; }

	public bool CameraUploadUpgraded { get; set; }

	public bool CollectionsUpgraded { get; set; }

	public bool SubtitlePermissionsUpgraded { get; set; }

	public bool CollapseVideoFolders { get; set; }

	public bool EnableOriginalTrackTitles { get; set; }

	public bool LogAllQueryTimes { get; set; }

	public bool EnableCaseSensitiveItemIds { get; set; }

	public bool DisableLiveTvChannelUserDataName { get; set; }

	public string MetadataPath { get; set; }

	public string MetadataNetworkPath { get; set; }

	public string PreferredMetadataLanguage { get; set; }

	public string MetadataCountryCode { get; set; }

	public string[] SortReplaceCharacters { get; set; }

	public string[] SortRemoveCharacters { get; set; }

	public string[] SortRemoveWords { get; set; }

	public int MinResumePct { get; set; }

	public int MaxResumePct { get; set; }

	public int MinResumeDurationSeconds { get; set; }

	public int LibraryMonitorDelay { get; set; }

	public bool EnableDashboardResponseCaching { get; set; }

	public string DashboardSourcePath { get; set; }

	public ImageSavingConvention ImageSavingConvention { get; set; }

	public MetadataOptions[] MetadataOptions { get; set; }

	public bool EnableAutomaticRestart { get; set; }

	public bool SkipDeserializationForBasicTypes { get; set; }

	public string ServerName { get; set; }

	public string WanDdns { get; set; }

	public string UICulture { get; set; }

	public bool SaveMetadataHidden { get; set; }

	public NameValuePair[] ContentTypes { get; set; }

	public int RemoteClientBitrateLimit { get; set; }

	public int SchemaVersion { get; set; }

	public bool EnableAnonymousUsageReporting { get; set; }

	public bool EnableFolderView { get; set; }

	public bool EnableGroupingIntoCollections { get; set; }

	public bool DisplaySpecialsWithinSeasons { get; set; }

	public string[] LocalNetworkSubnets { get; set; }

	public string[] LocalNetworkAddresses { get; set; }

	public string[] CodecsUsed { get; set; }

	public bool EnableExternalContentInSuggestions { get; set; }

	public bool RequireHttps { get; set; }

	public bool IsBehindProxy { get; set; }

	public bool EnableNewOmdbSupport { get; set; }

	public string[] RemoteIPFilter { get; set; }

	public bool IsRemoteIPFilterBlacklist { get; set; }

	public int ImageExtractionTimeoutMs { get; set; }

	public PathSubstitution[] PathSubstitutions { get; set; }

	public bool EnableSimpleArtistDetection { get; set; }

	public string[] UninstalledPlugins { get; set; }

	public ServerConfiguration()
	{
		UninstalledPlugins = Array.Empty<string>();
		RemoteIPFilter = Array.Empty<string>();
		LocalNetworkSubnets = Array.Empty<string>();
		LocalNetworkAddresses = Array.Empty<string>();
		CodecsUsed = Array.Empty<string>();
		ImageExtractionTimeoutMs = 0;
		PathSubstitutions = new PathSubstitution[0];
		EnableSimpleArtistDetection = true;
		DisplaySpecialsWithinSeasons = true;
		EnableExternalContentInSuggestions = true;
		ImageSavingConvention = ImageSavingConvention.Compatible;
		PublicPort = 8096;
		PublicHttpsPort = 8920;
		HttpServerPortNumber = 8096;
		HttpsPortNumber = 8920;
		EnableHttps = false;
		EnableDashboardResponseCaching = true;
		EnableAnonymousUsageReporting = true;
		EnableCaseSensitiveItemIds = true;
		EnableAutomaticRestart = true;
		AutoRunWebApp = true;
		EnableRemoteAccess = true;
		EnableUPnP = true;
		MinResumePct = 5;
		MaxResumePct = 90;
		MinResumeDurationSeconds = 300;
		LibraryMonitorDelay = 60;
		ContentTypes = new NameValuePair[0];
		PreferredMetadataLanguage = "en";
		MetadataCountryCode = "US";
		SortReplaceCharacters = new string[3] { ".", "+", "%" };
		SortRemoveCharacters = new string[6] { ",", "&", "-", "{", "}", "'" };
		SortRemoveWords = new string[3] { "the", "a", "an" };
		UICulture = "en-us";
		MetadataOptions = new MetadataOptions[9]
		{
			new MetadataOptions
			{
				ItemType = "Book"
			},
			new MetadataOptions
			{
				ItemType = "Movie"
			},
			new MetadataOptions
			{
				ItemType = "MusicVideo",
				DisabledMetadataFetchers = new string[1] { "The Open Movie Database" },
				DisabledImageFetchers = new string[2] { "The Open Movie Database", "FanArt" }
			},
			new MetadataOptions
			{
				ItemType = "Series",
				DisabledMetadataFetchers = new string[1] { "TheMovieDb" },
				DisabledImageFetchers = new string[1] { "TheMovieDb" }
			},
			new MetadataOptions
			{
				ItemType = "MusicAlbum",
				DisabledMetadataFetchers = new string[1] { "TheAudioDB" }
			},
			new MetadataOptions
			{
				ItemType = "MusicArtist",
				DisabledMetadataFetchers = new string[1] { "TheAudioDB" }
			},
			new MetadataOptions
			{
				ItemType = "BoxSet"
			},
			new MetadataOptions
			{
				ItemType = "Season",
				DisabledMetadataFetchers = new string[1] { "TheMovieDb" },
				DisabledImageFetchers = new string[1] { "FanArt" }
			},
			new MetadataOptions
			{
				ItemType = "Episode",
				DisabledMetadataFetchers = new string[2] { "The Open Movie Database", "TheMovieDb" },
				DisabledImageFetchers = new string[2] { "The Open Movie Database", "TheMovieDb" }
			}
		};
	}
}
