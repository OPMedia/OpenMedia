using System;
using System.Collections.Generic;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Model.Dto;

public class BaseItemDto : IHasProviderIds, IItemDto, IHasServerId
{
	public string Name { get; set; }

	public string OriginalTitle { get; set; }

	public string ServerId { get; set; }

	public Guid Id { get; set; }

	public string Etag { get; set; }

	public string SourceType { get; set; }

	public string PlaylistItemId { get; set; }

	public DateTime? DateCreated { get; set; }

	public DateTime? DateLastMediaAdded { get; set; }

	public string ExtraType { get; set; }

	public int? AirsBeforeSeasonNumber { get; set; }

	public int? AirsAfterSeasonNumber { get; set; }

	public int? AirsBeforeEpisodeNumber { get; set; }

	public bool? DisplaySpecialsWithSeasons { get; set; }

	public bool? CanDelete { get; set; }

	public bool? CanDownload { get; set; }

	public bool? HasSubtitles { get; set; }

	public string PreferredMetadataLanguage { get; set; }

	public string PreferredMetadataCountryCode { get; set; }

	public bool? SupportsSync { get; set; }

	public string Container { get; set; }

	public string SortName { get; set; }

	public string ForcedSortName { get; set; }

	public Video3DFormat? Video3DFormat { get; set; }

	public DateTime? PremiereDate { get; set; }

	public ExternalUrl[] ExternalUrls { get; set; }

	public MediaSourceInfo[] MediaSources { get; set; }

	public float? CriticRating { get; set; }

	public string GameSystem { get; set; }

	public string[] ProductionLocations { get; set; }

	public string[] MultiPartGameFiles { get; set; }

	public string Path { get; set; }

	public bool? EnableMediaSourceDisplay { get; set; }

	public string OfficialRating { get; set; }

	public string CustomRating { get; set; }

	public Guid ChannelId { get; set; }

	public string ChannelName { get; set; }

	public string Overview { get; set; }

	public string[] Taglines { get; set; }

	public string[] Genres { get; set; }

	public float? CommunityRating { get; set; }

	public long? CumulativeRunTimeTicks { get; set; }

	public long? RunTimeTicks { get; set; }

	public PlayAccess? PlayAccess { get; set; }

	public string AspectRatio { get; set; }

	public int? ProductionYear { get; set; }

	public bool? IsPlaceHolder { get; set; }

	public string Number { get; set; }

	public string ChannelNumber { get; set; }

	public int? IndexNumber { get; set; }

	public int? IndexNumberEnd { get; set; }

	public int? ParentIndexNumber { get; set; }

	public MediaUrl[] RemoteTrailers { get; set; }

	public Dictionary<string, string> ProviderIds { get; set; }

	public bool? IsHD { get; set; }

	public bool? IsFolder { get; set; }

	public Guid ParentId { get; set; }

	public string Type { get; set; }

	public BaseItemPerson[] People { get; set; }

	public NameGuidPair[] Studios { get; set; }

	public NameGuidPair[] GenreItems { get; set; }

	public string ParentLogoItemId { get; set; }

	public string ParentBackdropItemId { get; set; }

	public string[] ParentBackdropImageTags { get; set; }

	public int? LocalTrailerCount { get; set; }

	public UserItemDataDto UserData { get; set; }

	public int? RecursiveItemCount { get; set; }

	public int? ChildCount { get; set; }

	public string SeriesName { get; set; }

	public Guid SeriesId { get; set; }

	public Guid SeasonId { get; set; }

	public int? SpecialFeatureCount { get; set; }

	public string DisplayPreferencesId { get; set; }

	public string Status { get; set; }

	public string AirTime { get; set; }

	public DayOfWeek[] AirDays { get; set; }

	public string[] Tags { get; set; }

	public double? PrimaryImageAspectRatio { get; set; }

	public string[] Artists { get; set; }

	public NameGuidPair[] ArtistItems { get; set; }

	public string Album { get; set; }

	public string CollectionType { get; set; }

	public string DisplayOrder { get; set; }

	public Guid AlbumId { get; set; }

	public string AlbumPrimaryImageTag { get; set; }

	public string SeriesPrimaryImageTag { get; set; }

	public string AlbumArtist { get; set; }

	public NameGuidPair[] AlbumArtists { get; set; }

	public string SeasonName { get; set; }

	public MediaStream[] MediaStreams { get; set; }

	public VideoType? VideoType { get; set; }

	public int? PartCount { get; set; }

	public int? MediaSourceCount { get; set; }

	public Dictionary<ImageType, string> ImageTags { get; set; }

	public string[] BackdropImageTags { get; set; }

	public string[] ScreenshotImageTags { get; set; }

	public string ParentLogoImageTag { get; set; }

	public string ParentArtItemId { get; set; }

	public string ParentArtImageTag { get; set; }

	public string SeriesThumbImageTag { get; set; }

	public string SeriesStudio { get; set; }

	public string ParentThumbItemId { get; set; }

	public string ParentThumbImageTag { get; set; }

	public string ParentPrimaryImageItemId { get; set; }

	public string ParentPrimaryImageTag { get; set; }

	public List<ChapterInfo> Chapters { get; set; }

	public LocationType? LocationType { get; set; }

	public IsoType? IsoType { get; set; }

	public string MediaType { get; set; }

	public DateTime? EndDate { get; set; }

	public MetadataFields[] LockedFields { get; set; }

	public int? TrailerCount { get; set; }

	public int? MovieCount { get; set; }

	public int? SeriesCount { get; set; }

	public int? ProgramCount { get; set; }

	public int? EpisodeCount { get; set; }

	public int? GameCount { get; set; }

	public int? SongCount { get; set; }

	public int? AlbumCount { get; set; }

	public int? ArtistCount { get; set; }

	public int? MusicVideoCount { get; set; }

	public bool? LockData { get; set; }

	public int? Width { get; set; }

	public int? Height { get; set; }

	public string CameraMake { get; set; }

	public string CameraModel { get; set; }

	public string Software { get; set; }

	public double? ExposureTime { get; set; }

	public double? FocalLength { get; set; }

	public ImageOrientation? ImageOrientation { get; set; }

	public double? Aperture { get; set; }

	public double? ShutterSpeed { get; set; }

	public double? Latitude { get; set; }

	public double? Longitude { get; set; }

	public double? Altitude { get; set; }

	public int? IsoSpeedRating { get; set; }

	public int? RecordingCount { get; set; }

	public string SeriesTimerId { get; set; }

	public string ProgramId { get; set; }

	public string ChannelPrimaryImageTag { get; set; }

	public DateTime? StartDate { get; set; }

	public double? CompletionPercentage { get; set; }

	public bool? IsRepeat { get; set; }

	public string EpisodeTitle { get; set; }

	public ChannelType? ChannelType { get; set; }

	public ProgramAudio? Audio { get; set; }

	public bool? IsMovie { get; set; }

	public bool? IsSports { get; set; }

	public bool? IsSeries { get; set; }

	public bool? IsLive { get; set; }

	public bool? IsNews { get; set; }

	public bool? IsKids { get; set; }

	public bool? IsPremiere { get; set; }

	public string TimerId { get; set; }

	public int? GameSystemId { get; set; }

	public bool? IsNew { get; set; }

	public bool? SupportsResume { get; set; }

	public BaseItemDto CurrentProgram { get; set; }

	public bool IsType(Type type)
	{
		return IsType(type.Name);
	}

	public bool IsType(string type)
	{
		return StringHelper.EqualsIgnoreCase(Type, type);
	}
}
