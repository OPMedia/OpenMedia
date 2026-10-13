using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Users;

namespace MediaBrowser.Controller.Entities;

public abstract class BaseItem : IHasProviderIds, IHasLookupInfo<ItemLookupInfo>
{
	public static readonly char[] SlugReplaceChars = new char[3] { '?', '/', '&' };

	public static char SlugChar = '-';

	public static readonly string[] SupportedImageExtensions = new string[5] { ".png", ".jpg", ".jpeg", ".tbn", ".gif" };

	public static readonly List<string> SupportedImageExtensionsList = SupportedImageExtensions.ToList();

	public static string TrailerFolderName = "trailers";

	public static string ThemeSongsFolderName = "theme-music";

	public static string ThemeSongFilename = "theme";

	public static string ThemeVideosFolderName = "backdrops";

	public static ExtraType[] DisplayExtraTypes;

	private string _name;

	private string _forcedSortName;

	private string _sortName;

	private static Dictionary<string, string> EmptyDictionary;

	public MediaUrl[] RemoteTrailers { get; set; }

	[IgnoreDataMember]
	public Guid[] ExtraIds { get; set; }

	[IgnoreDataMember]
	public Guid[] ThemeSongIds => (from i in GetThemeSongs()
		select i.Id).ToArray();

	[IgnoreDataMember]
	public Guid[] ThemeVideoIds => (from i in GetThemeVideos()
		select i.Id).ToArray();

	[IgnoreDataMember]
	public Guid[] LocalTrailerIds => (from i in GetExtras(new ExtraType[1] { (ExtraType)2 }).OfType<Trailer>()
		where Enumerable.Contains(i.TrailerTypes, (TrailerType)5)
		select i.Id).ToArray();

	[IgnoreDataMember]
	public string PreferredMetadataCountryCode { get; set; }

	[IgnoreDataMember]
	public string PreferredMetadataLanguage { get; set; }

	public long? Size { get; set; }

	public string Container { get; set; }

	[IgnoreDataMember]
	public string Tagline { get; set; }

	[IgnoreDataMember]
	public virtual ItemImageInfo[] ImageInfos { get; set; }

	[IgnoreDataMember]
	public bool IsVirtualItem { get; set; }

	[IgnoreDataMember]
	public string Album { get; set; }

	[IgnoreDataMember]
	public Guid ChannelId { get; set; }

	[IgnoreDataMember]
	public virtual bool SupportsAddingToPlaylist => false;

	[IgnoreDataMember]
	public virtual bool AlwaysScanInternalMetadataPath => false;

	[IgnoreDataMember]
	public bool IsInMixedFolder { get; set; }

	[IgnoreDataMember]
	public virtual bool SupportsPlayedStatus => false;

	[IgnoreDataMember]
	public virtual bool SupportsPositionTicksResume => false;

	[IgnoreDataMember]
	public virtual bool SupportsRemoteImageDownloading => true;

	[IgnoreDataMember]
	public virtual string Name
	{
		get
		{
			return _name;
		}
		set
		{
			_name = value;
			_sortName = null;
		}
	}

	public bool IsShortcut { get; set; }

	public string ShortcutPath { get; set; }

	[IgnoreDataMember]
	public bool IsUnaired
	{
		get
		{
			if (PremiereDate.HasValue)
			{
				return PremiereDate.Value.ToLocalTime().Date >= DateTime.Now.Date;
			}
			return false;
		}
	}

	[IgnoreDataMember]
	public int? TotalBitrate { get; set; }

	[IgnoreDataMember]
	public ExtraType? ExtraType { get; set; }

	[IgnoreDataMember]
	public bool IsThemeMedia
	{
		get
		{
			//IL_0019: Unknown result type (might be due to invalid IL or missing references)
			//IL_001f: Invalid comparison between Unknown and I4
			//IL_002a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Invalid comparison between Unknown and I4
			if (ExtraType.HasValue)
			{
				if ((int)ExtraType.Value != 8)
				{
					return (int)ExtraType.Value == 9;
				}
				return true;
			}
			return false;
		}
	}

	[IgnoreDataMember]
	public string OriginalTitle { get; set; }

	[IgnoreDataMember]
	public Guid Id { get; set; }

	[IgnoreDataMember]
	public Guid OwnerId { get; set; }

	[IgnoreDataMember]
	public bool IsHD
	{
		get
		{
			if (Width < 1200)
			{
				return Height >= 700;
			}
			return true;
		}
	}

	[IgnoreDataMember]
	public int Width { get; set; }

	[IgnoreDataMember]
	public int Height { get; set; }

	[IgnoreDataMember]
	public ProgramAudio? Audio { get; set; }

	[IgnoreDataMember]
	public virtual Guid DisplayPreferencesId
	{
		get
		{
			Type type = GetType();
			if (!(type == typeof(Folder)))
			{
				return BaseExtensions.GetMD5(type.FullName);
			}
			return Id;
		}
	}

	[IgnoreDataMember]
	public virtual string Path { get; set; }

	[IgnoreDataMember]
	public virtual SourceType SourceType
	{
		get
		{
			if (!ChannelId.Equals(Guid.Empty))
			{
				return SourceType.Channel;
			}
			return SourceType.Library;
		}
	}

	[IgnoreDataMember]
	public virtual string ContainingFolderPath
	{
		get
		{
			if (IsFolder)
			{
				return Path;
			}
			return FileSystem.GetDirectoryName(Path);
		}
	}

	[IgnoreDataMember]
	public string ExternalId { get; set; }

	[IgnoreDataMember]
	public string ExternalSeriesId { get; set; }

	[IgnoreDataMember]
	public virtual bool IsHidden => false;

	[IgnoreDataMember]
	public virtual LocationType LocationType
	{
		get
		{
			string path = Path;
			if (string.IsNullOrEmpty(path))
			{
				if (SourceType != SourceType.Channel)
				{
					return (LocationType)2;
				}
				return (LocationType)1;
			}
			if (FileSystem.IsPathFile(path))
			{
				return (LocationType)0;
			}
			return (LocationType)1;
		}
	}

	[IgnoreDataMember]
	public MediaProtocol? PathProtocol
	{
		get
		{
			//IL_001f: Unknown result type (might be due to invalid IL or missing references)
			string path = Path;
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			return MediaSourceManager.GetPathProtocol(path);
		}
	}

	[IgnoreDataMember]
	public bool IsFileProtocol => IsPathProtocol((MediaProtocol)0);

	[IgnoreDataMember]
	public bool HasPathProtocol => PathProtocol.HasValue;

	[IgnoreDataMember]
	public virtual bool SupportsLocalMetadata
	{
		get
		{
			if (SourceType == SourceType.Channel)
			{
				return false;
			}
			return IsFileProtocol;
		}
	}

	[IgnoreDataMember]
	public virtual string FileNameWithoutExtension
	{
		get
		{
			if (IsFileProtocol)
			{
				return System.IO.Path.GetFileNameWithoutExtension(Path);
			}
			return null;
		}
	}

	[IgnoreDataMember]
	public virtual bool EnableAlphaNumericSorting => true;

	[IgnoreDataMember]
	public string PrimaryImagePath => this.GetImagePath((ImageType)0);

	[IgnoreDataMember]
	public virtual bool SupportsExternalTransfer => false;

	[IgnoreDataMember]
	public DateTime DateCreated { get; set; }

	[IgnoreDataMember]
	public DateTime DateModified { get; set; }

	[IgnoreDataMember]
	public DateTime DateLastSaved { get; set; }

	[IgnoreDataMember]
	public DateTime DateLastRefreshed { get; set; }

	public static ILogger Logger { get; set; }

	public static ILibraryManager LibraryManager { get; set; }

	public static IServerConfigurationManager ConfigurationManager { get; set; }

	public static IProviderManager ProviderManager { get; set; }

	public static ILocalizationManager LocalizationManager { get; set; }

	public static IItemRepository ItemRepository { get; set; }

	public static IFileSystem FileSystem { get; set; }

	public static IUserDataManager UserDataManager { get; set; }

	public static IChannelManager ChannelManager { get; set; }

	public static IMediaSourceManager MediaSourceManager { get; set; }

	[IgnoreDataMember]
	public bool IsLocked { get; set; }

	[IgnoreDataMember]
	public MetadataFields[] LockedFields { get; set; }

	[IgnoreDataMember]
	public virtual string MediaType => null;

	[IgnoreDataMember]
	public virtual string[] PhysicalLocations
	{
		get
		{
			if (!IsFileProtocol)
			{
				return Array.Empty<string>();
			}
			return new string[1] { Path };
		}
	}

	[IgnoreDataMember]
	public string ForcedSortName
	{
		get
		{
			return _forcedSortName;
		}
		set
		{
			_forcedSortName = value;
			_sortName = null;
		}
	}

	[IgnoreDataMember]
	public string SortName
	{
		get
		{
			if (_sortName == null)
			{
				if (!string.IsNullOrEmpty(ForcedSortName))
				{
					_sortName = ModifySortChunks(ForcedSortName).ToLower();
				}
				else
				{
					_sortName = CreateSortName();
				}
			}
			return _sortName;
		}
		set
		{
			_sortName = value;
		}
	}

	[IgnoreDataMember]
	public bool EnableMediaSourceDisplay
	{
		get
		{
			if (SourceType == SourceType.Channel)
			{
				return ChannelManager.EnableMediaSourceDisplay(this);
			}
			return true;
		}
	}

	[IgnoreDataMember]
	public Guid ParentId { get; set; }

	[IgnoreDataMember]
	public Folder Parent
	{
		get
		{
			return GetParent() as Folder;
		}
		set
		{
		}
	}

	[IgnoreDataMember]
	public virtual Guid DisplayParentId => ParentId;

	[IgnoreDataMember]
	public BaseItem DisplayParent
	{
		get
		{
			Guid displayParentId = DisplayParentId;
			if (displayParentId.Equals(Guid.Empty))
			{
				return null;
			}
			return LibraryManager.GetItemById(displayParentId);
		}
	}

	[IgnoreDataMember]
	public DateTime? PremiereDate { get; set; }

	[IgnoreDataMember]
	public DateTime? EndDate { get; set; }

	[IgnoreDataMember]
	public string OfficialRating { get; set; }

	[IgnoreDataMember]
	public int InheritedParentalRatingValue { get; set; }

	[IgnoreDataMember]
	public float? CriticRating { get; set; }

	[IgnoreDataMember]
	public string CustomRating { get; set; }

	[IgnoreDataMember]
	public string Overview { get; set; }

	[IgnoreDataMember]
	public string[] Studios { get; set; }

	[IgnoreDataMember]
	public string[] Genres { get; set; }

	[IgnoreDataMember]
	public string[] Tags { get; set; }

	[IgnoreDataMember]
	public string[] ProductionLocations { get; set; }

	[IgnoreDataMember]
	public float? CommunityRating { get; set; }

	[IgnoreDataMember]
	public long? RunTimeTicks { get; set; }

	[IgnoreDataMember]
	public int? ProductionYear { get; set; }

	[IgnoreDataMember]
	public int? IndexNumber { get; set; }

	[IgnoreDataMember]
	public int? ParentIndexNumber { get; set; }

	[IgnoreDataMember]
	public virtual bool HasLocalAlternateVersions => false;

	[IgnoreDataMember]
	public string OfficialRatingForComparison
	{
		get
		{
			string officialRating = OfficialRating;
			if (!string.IsNullOrEmpty(officialRating))
			{
				return officialRating;
			}
			return DisplayParent?.OfficialRatingForComparison;
		}
	}

	[IgnoreDataMember]
	public string CustomRatingForComparison
	{
		get
		{
			string customRating = CustomRating;
			if (!string.IsNullOrEmpty(customRating))
			{
				return customRating;
			}
			return DisplayParent?.CustomRatingForComparison;
		}
	}

	[IgnoreDataMember]
	protected virtual bool SupportsOwnedItems
	{
		get
		{
			if (!ParentId.Equals(Guid.Empty))
			{
				return IsFileProtocol;
			}
			return false;
		}
	}

	[IgnoreDataMember]
	public virtual bool SupportsPeople => false;

	[IgnoreDataMember]
	public virtual bool SupportsThemeMedia => false;

	[IgnoreDataMember]
	public virtual bool SupportsLocalTrailers => false;

	[IgnoreDataMember]
	public Dictionary<string, string> ProviderIds { get; set; }

	[IgnoreDataMember]
	public virtual Folder LatestItemsIndexContainer => null;

	[IgnoreDataMember]
	public string PresentationUniqueKey { get; set; }

	[IgnoreDataMember]
	public virtual bool SupportsInheritedParentImages => false;

	[IgnoreDataMember]
	public virtual bool IsFolder => false;

	[IgnoreDataMember]
	public virtual bool IsDisplayedAsFolder => false;

	[IgnoreDataMember]
	public virtual bool EnableRememberingTrackSelections => true;

	[IgnoreDataMember]
	public virtual bool IsTopParent
	{
		get
		{
			if (this is BasePluginFolder || this is Channel)
			{
				return true;
			}
			if (this is IHasCollectionType hasCollectionType && string.Equals(hasCollectionType.CollectionType, "livetv", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (GetParent() is AggregateFolder)
			{
				return true;
			}
			return false;
		}
	}

	[IgnoreDataMember]
	public virtual bool SupportsAncestors => true;

	[IgnoreDataMember]
	public virtual bool StopRefreshIfLocalMetadataFound => true;

	protected BaseItem()
	{
		ExtraIds = Array.Empty<Guid>();
		Tags = Array.Empty<string>();
		Genres = Array.Empty<string>();
		Studios = Array.Empty<string>();
		ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		LockedFields = Array.Empty<MetadataFields>();
		ImageInfos = Array.Empty<ItemImageInfo>();
		ProductionLocations = Array.Empty<string>();
		RemoteTrailers = Array.Empty<MediaUrl>();
	}

	public IEnumerable<BaseItem> GetDisplayExtras()
	{
		return GetExtras(DisplayExtraTypes);
	}

	public IEnumerable<BaseItem> GetExtras()
	{
		return from i in ExtraIds.Select(LibraryManager.GetItemById)
			where i != null
			orderby i.SortName
			select i;
	}

	public IEnumerable<BaseItem> GetExtras(ExtraType[] extraTypes)
	{
		return from i in ExtraIds.Select(LibraryManager.GetItemById).Where((BaseItem i) =>
			{
				//IL_0022: Unknown result type (might be due to invalid IL or missing references)
				return i != null && i.ExtraType.HasValue && Enumerable.Contains(extraTypes, i.ExtraType.Value);
			})
			orderby i.SortName
			select i;
	}

	public IEnumerable<BaseItem> GetThemeSongs()
	{
		return GetExtras(new ExtraType[1] { (ExtraType)8 });
	}

	public IEnumerable<BaseItem> GetThemeVideos()
	{
		return GetExtras(new ExtraType[1] { (ExtraType)9 });
	}

	public virtual long GetRunTimeTicksForPlayState()
	{
		return RunTimeTicks ?? 0;
	}

	public BaseItem GetOwner()
	{
		Guid ownerId = OwnerId;
		if (!ownerId.Equals(Guid.Empty))
		{
			return LibraryManager.GetItemById(ownerId);
		}
		return null;
	}

	public bool IsPathProtocol(MediaProtocol protocol)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		MediaProtocol? pathProtocol = PathProtocol;
		if (pathProtocol.HasValue)
		{
			return pathProtocol.Value == protocol;
		}
		return false;
	}

	private List<Tuple<StringBuilder, bool>> GetSortChunks(string s1)
	{
		List<Tuple<StringBuilder, bool>> list = new List<Tuple<StringBuilder, bool>>();
		int num = 0;
		while (num < s1.Length && num < s1.Length)
		{
			char c = s1[num];
			StringBuilder stringBuilder = new StringBuilder();
			while (num < s1.Length && (stringBuilder.Length == 0 || SortHelper.InChunk(c, stringBuilder[0])))
			{
				stringBuilder.Append(c);
				num++;
				if (num < s1.Length)
				{
					c = s1[num];
				}
			}
			bool item = stringBuilder.Length > 0 && char.IsDigit(stringBuilder[0]);
			list.Add(new Tuple<StringBuilder, bool>(stringBuilder, item));
		}
		return list;
	}

	public bool IsMetadataFetcherEnabled(LibraryOptions libraryOptions, string name)
	{
		if (SourceType == SourceType.Channel)
		{
			return !EnableMediaSourceDisplay;
		}
		if (this is Person)
		{
			return true;
		}
		TypeOptions typeOptions = libraryOptions.GetTypeOptions(GetType().Name);
		if (typeOptions != null)
		{
			return typeOptions.MetadataFetchers.Contains(name, StringComparer.OrdinalIgnoreCase);
		}
		if (!libraryOptions.EnableInternetProviders)
		{
			return false;
		}
		MetadataOptions val = ConfigurationManager.Configuration.MetadataOptions.FirstOrDefault((MetadataOptions i) => string.Equals(i.ItemType, GetType().Name, StringComparison.OrdinalIgnoreCase));
		if (val != null)
		{
			return !val.DisabledMetadataFetchers.Contains(name, StringComparer.OrdinalIgnoreCase);
		}
		return true;
	}

	public bool IsImageFetcherEnabled(LibraryOptions libraryOptions, string name)
	{
		if (this is Channel)
		{
			return true;
		}
		if (SourceType == SourceType.Channel)
		{
			return !EnableMediaSourceDisplay;
		}
		TypeOptions typeOptions = libraryOptions.GetTypeOptions(GetType().Name);
		if (typeOptions != null)
		{
			return typeOptions.ImageFetchers.Contains(name, StringComparer.OrdinalIgnoreCase);
		}
		if (!libraryOptions.EnableInternetProviders)
		{
			return false;
		}
		MetadataOptions val = ConfigurationManager.Configuration.MetadataOptions.FirstOrDefault((MetadataOptions i) => string.Equals(i.ItemType, GetType().Name, StringComparison.OrdinalIgnoreCase));
		if (val != null)
		{
			return !val.DisabledImageFetchers.Contains(name, StringComparer.OrdinalIgnoreCase);
		}
		return true;
	}

	public virtual bool CanDelete()
	{
		if (SourceType == SourceType.Channel)
		{
			return ChannelManager.CanDelete(this);
		}
		return IsFileProtocol;
	}

	public virtual bool IsAuthorizedToDelete(User user, List<Folder> allCollectionFolders)
	{
		if (user.Policy.EnableContentDeletion)
		{
			return true;
		}
		string[] enableContentDeletionFromFolders = user.Policy.EnableContentDeletionFromFolders;
		if (SourceType == SourceType.Channel)
		{
			return enableContentDeletionFromFolders.Contains(ChannelId.ToString("N"), StringComparer.OrdinalIgnoreCase);
		}
		foreach (Folder collectionFolder in LibraryManager.GetCollectionFolders(this, allCollectionFolders))
		{
			if (enableContentDeletionFromFolders.Contains(collectionFolder.Id.ToString("N"), StringComparer.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	public bool CanDelete(User user, List<Folder> allCollectionFolders)
	{
		if (CanDelete())
		{
			return IsAuthorizedToDelete(user, allCollectionFolders);
		}
		return false;
	}

	public bool CanDelete(User user)
	{
		List<Folder> allCollectionFolders = LibraryManager.GetUserRootFolder().Children.OfType<Folder>().ToList();
		return CanDelete(user, allCollectionFolders);
	}

	public virtual bool CanDownload()
	{
		return false;
	}

	public virtual bool IsAuthorizedToDownload(User user)
	{
		return user.Policy.EnableContentDownloading;
	}

	public bool CanDownload(User user)
	{
		if (CanDownload())
		{
			return IsAuthorizedToDownload(user);
		}
		return false;
	}

	protected virtual bool CanDownloadAsSingleMedia()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Invalid comparison between Unknown and I4
		if ((int)LocationType == 2)
		{
			return false;
		}
		if (SourceType == SourceType.Channel)
		{
			return ChannelManager.SupportsExternalTransfer(ChannelId);
		}
		return true;
	}

	protected virtual bool CanDownloadAsFolder()
	{
		if (SourceType == SourceType.Channel)
		{
			return ChannelManager.SupportsExternalTransfer(ChannelId);
		}
		if (this is IHasCollectionType hasCollectionType && string.Equals(hasCollectionType.CollectionType, "livetv", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return true;
	}

	public override string ToString()
	{
		return Name;
	}

	public string GetInternalMetadataPath()
	{
		string internalMetadataPath = ConfigurationManager.ApplicationPaths.InternalMetadataPath;
		return GetInternalMetadataPath(internalMetadataPath);
	}

	protected virtual string GetInternalMetadataPath(string basePath)
	{
		if (SourceType == SourceType.Channel)
		{
			return System.IO.Path.Combine(basePath, "channels", ChannelId.ToString("N"), Id.ToString("N"));
		}
		string text = Id.ToString("N");
		basePath = System.IO.Path.Combine(basePath, "library");
		return System.IO.Path.Combine(basePath, text.Substring(0, 2), text);
	}

	protected virtual string CreateSortName()
	{
		if (Name == null)
		{
			return null;
		}
		if (!EnableAlphaNumericSorting)
		{
			return Name.TrimStart(Array.Empty<char>());
		}
		string text = Name.Trim().ToLower();
		string[] sortRemoveCharacters = ConfigurationManager.Configuration.SortRemoveCharacters;
		foreach (string oldValue in sortRemoveCharacters)
		{
			text = text.Replace(oldValue, string.Empty);
		}
		sortRemoveCharacters = ConfigurationManager.Configuration.SortReplaceCharacters;
		foreach (string oldValue2 in sortRemoveCharacters)
		{
			text = text.Replace(oldValue2, " ");
		}
		sortRemoveCharacters = ConfigurationManager.Configuration.SortRemoveWords;
		foreach (string text2 in sortRemoveCharacters)
		{
			if (text.StartsWith(text2 + " "))
			{
				text = text.Remove(0, text2.Length + 1);
			}
			text = text.Replace(" " + text2 + " ", " ");
			if (text.EndsWith(" " + text2))
			{
				text = text.Remove(text.Length - (text2.Length + 1));
			}
		}
		return ModifySortChunks(text);
	}

	private string ModifySortChunks(string name)
	{
		List<Tuple<StringBuilder, bool>> sortChunks = GetSortChunks(name);
		StringBuilder stringBuilder = new StringBuilder();
		foreach (Tuple<StringBuilder, bool> item2 in sortChunks)
		{
			StringBuilder item = item2.Item1;
			if (item2.Item2)
			{
				while (item.Length < 10)
				{
					item.Insert(0, '0');
				}
			}
			stringBuilder.Append((object?)item);
		}
		return stringBuilder.ToString().RemoveDiacritics();
	}

	public void SetParent(Folder parent)
	{
		ParentId = parent?.Id ?? Guid.Empty;
	}

	public BaseItem GetParent()
	{
		Guid parentId = ParentId;
		if (!parentId.Equals(Guid.Empty))
		{
			return LibraryManager.GetItemById(parentId);
		}
		return null;
	}

	public IEnumerable<BaseItem> GetParents()
	{
		for (BaseItem parent = GetParent(); parent != null; parent = parent.GetParent())
		{
			yield return parent;
		}
	}

	public T FindParent<T>() where T : Folder
	{
		foreach (BaseItem parent in GetParents())
		{
			if (parent is T result)
			{
				return result;
			}
		}
		return null;
	}

	public PlayAccess GetPlayAccess(User user)
	{
		if (user.Policy.EnableMediaPlayback)
		{
			return (PlayAccess)0;
		}
		return (PlayAccess)1;
	}

	public virtual List<MediaStream> GetMediaStreams()
	{
		return MediaSourceManager.GetMediaStreams(new MediaStreamQuery
		{
			ItemId = Id
		});
	}

	protected virtual bool IsActiveRecording()
	{
		return false;
	}

	public virtual List<MediaSourceInfo> GetMediaSources(bool enablePathSubstitution)
	{
		if (SourceType == SourceType.Channel)
		{
			List<MediaSourceInfo> list = ChannelManager.GetStaticMediaSources(this, CancellationToken.None).ToList();
			if (list.Count > 0)
			{
				return list;
			}
		}
		List<MediaSourceInfo> list2 = GetAllItemsForMediaSources().Select((Tuple<BaseItem, MediaSourceType> i) =>
		{
			//IL_0013: Unknown result type (might be due to invalid IL or missing references)
			return GetVersionInfo(enablePathSubstitution, i.Item1, i.Item2);
		}).ToList();
		if (IsActiveRecording())
		{
			foreach (MediaSourceInfo item in list2)
			{
				item.Type = (MediaSourceType)2;
			}
		}
		return list2.OrderBy((MediaSourceInfo i) =>
		{
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			return (i.VideoType != (VideoType?)0) ? 1 : 0;
		}).ThenBy((MediaSourceInfo i) => i.Video3DFormat.HasValue ? 1 : 0).ThenByDescending((MediaSourceInfo i) =>
		{
			MediaStream videoStream = i.VideoStream;
			return (videoStream != null && videoStream.Width.HasValue) ? videoStream.Width.Value : 0;
		})
			.ToList();
	}

	protected virtual List<Tuple<BaseItem, MediaSourceType>> GetAllItemsForMediaSources()
	{
		return new List<Tuple<BaseItem, MediaSourceType>>();
	}

	private MediaSourceInfo GetVersionInfo(bool enablePathSubstitution, BaseItem item, MediaSourceType type)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Expected Obj, but got Unknown
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0134: Unknown result type (might be due to invalid IL or missing references)
		//IL_0159: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0196: Unknown result type (might be due to invalid IL or missing references)
		//IL_019c: Invalid comparison between Unknown and I4
		//IL_0246: Unknown result type (might be due to invalid IL or missing references)
		//IL_0233: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a9: Unknown result type (might be due to invalid IL or missing references)
		if (item == null)
		{
			throw new ArgumentNullException("media");
		}
		MediaProtocol? pathProtocol = item.PathProtocol;
		MediaSourceInfo val = new MediaSourceInfo
		{
			Id = item.Id.ToString("N"),
			Protocol = (MediaProtocol)(((int?)pathProtocol) ?? 0),
			MediaStreams = MediaSourceManager.GetMediaStreams(item.Id),
			Name = GetMediaSourceName(item),
			Path = (enablePathSubstitution ? GetMappedPath(item, item.Path, pathProtocol) : item.Path),
			RunTimeTicks = item.RunTimeTicks,
			Container = item.Container,
			Size = item.Size,
			Type = type
		};
		if (string.IsNullOrEmpty(val.Path))
		{
			val.Type = (MediaSourceType)2;
		}
		if ((int)val.Protocol == 0)
		{
			val.ETag = BaseExtensions.GetMD5(item.DateModified.Ticks.ToString(CultureInfo.InvariantCulture)).ToString("N");
		}
		if (item.IsShortcut)
		{
			val.IsRemote = true;
			val.Path = item.ShortcutPath;
			val.Protocol = MediaSourceManager.GetPathProtocol(val.Path);
		}
		Video video = item as Video;
		if (video != null)
		{
			val.IsoType = video.IsoType;
			val.VideoType = video.VideoType;
			val.Video3DFormat = video.Video3DFormat;
			val.Timestamp = video.Timestamp;
			if (string.IsNullOrEmpty(val.Container) && ((int)video.VideoType == 0 || (int)video.VideoType == 1) && pathProtocol.HasValue && (int)pathProtocol.Value == 0)
			{
				val.Container = System.IO.Path.GetExtension(item.Path).TrimStart(new char[1] { '.' });
			}
		}
		if (string.IsNullOrEmpty(val.Container) && pathProtocol.HasValue && (int)pathProtocol.Value == 0)
		{
			val.Container = System.IO.Path.GetExtension(item.Path).TrimStart(new char[1] { '.' });
		}
		if (val.SupportsDirectStream && !string.IsNullOrEmpty(val.Path))
		{
			val.SupportsDirectStream = MediaSourceManager.SupportsDirectStream(val.Path, val.Protocol);
		}
		if (video != null && (int)video.VideoType != 0)
		{
			val.SupportsDirectStream = false;
		}
		val.Bitrate = item.TotalBitrate;
		val.InferTotalBitrate(false);
		return val;
	}

	private string GetMediaSourceName(BaseItem item)
	{
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Invalid comparison between Unknown and I4
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Invalid comparison between Unknown and I4
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Invalid comparison between Unknown and I4
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Invalid comparison between Unknown and I4
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		List<string> list = new List<string>();
		string path = item.Path;
		if (item.IsFileProtocol && !string.IsNullOrEmpty(path))
		{
			if (HasLocalAlternateVersions)
			{
				string text = StringHelper.Replace(System.IO.Path.GetFileNameWithoutExtension(path), System.IO.Path.GetFileName(ContainingFolderPath), string.Empty, StringComparison.OrdinalIgnoreCase).TrimStart(' ', '-');
				if (!string.IsNullOrEmpty(text))
				{
					list.Add(text);
				}
			}
			if (list.Count == 0)
			{
				string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(path);
				list.Add(fileNameWithoutExtension);
			}
		}
		if (list.Count == 0)
		{
			list.Add(item.Name);
		}
		if (item is Video { Video3DFormat: var video3DFormat } video)
		{
			if (video3DFormat.HasValue)
			{
				list.Add("3D");
			}
			if ((int)video.VideoType == 3)
			{
				list.Add("Bluray");
			}
			else if ((int)video.VideoType == 2)
			{
				list.Add("DVD");
			}
			else if ((int)video.VideoType == 1)
			{
				if (video.IsoType.HasValue)
				{
					if ((int)video.IsoType.Value == 1)
					{
						list.Add("Bluray");
					}
					else if ((int)video.IsoType.Value == 0)
					{
						list.Add("DVD");
					}
				}
				else
				{
					list.Add("ISO");
				}
			}
		}
		return string.Join("/", LinqExtensions.ToArray<string>((IEnumerable<string>)list, list.Count));
	}

	private static BaseItem[] LoadThemeSongs(List<FileSystemMetadata> fileSystemChildren, IDirectoryService directoryService, Guid ownerId)
	{
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Expected Obj, but got Unknown
		List<FileSystemMetadata> list = (from i in fileSystemChildren
			where i.IsDirectory
			where string.Equals(i.Name, ThemeSongsFolderName, StringComparison.OrdinalIgnoreCase)
			select i).SelectMany((FileSystemMetadata i) => FileSystem.GetFiles(i.FullName, false)).ToList();
		list.AddRange(fileSystemChildren.Where((FileSystemMetadata i) => !i.IsDirectory && string.Equals(FileSystem.GetFileNameWithoutExtension(i), ThemeSongFilename, StringComparison.OrdinalIgnoreCase)));
		return (from i in LibraryManager.ResolvePaths(list, directoryService, null, new LibraryOptions()).OfType<MediaBrowser.Controller.Entities.Audio.Audio>().Select((MediaBrowser.Controller.Entities.Audio.Audio audio) =>
			{
				if (LibraryManager.GetItemById(audio.Id) is MediaBrowser.Controller.Entities.Audio.Audio audio2)
				{
					audio = audio2;
				}
				audio.ExtraType = (ExtraType)8;
				audio.ParentId = Guid.Empty;
				audio.OwnerId = ownerId;
				return audio;
			})
			orderby i.Path
			select i).ToArray();
	}

	private static BaseItem[] LoadThemeVideos(IEnumerable<FileSystemMetadata> fileSystemChildren, IDirectoryService directoryService, Guid ownerId)
	{
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Expected Obj, but got Unknown
		IEnumerable<FileSystemMetadata> files = (from i in fileSystemChildren
			where i.IsDirectory
			where string.Equals(i.Name, ThemeVideosFolderName, StringComparison.OrdinalIgnoreCase)
			select i).SelectMany((FileSystemMetadata i) => FileSystem.GetFiles(i.FullName, false));
		return (from i in LibraryManager.ResolvePaths(files, directoryService, null, new LibraryOptions()).OfType<Video>().Select((Video item) =>
			{
				if (LibraryManager.GetItemById(item.Id) is Video video)
				{
					item = video;
				}
				item.ExtraType = (ExtraType)9;
				item.ParentId = Guid.Empty;
				item.OwnerId = ownerId;
				return item;
			})
			orderby i.Path
			select i).ToArray();
	}

	public Task RefreshMetadata(CancellationToken cancellationToken)
	{
		return RefreshMetadata(new MetadataRefreshOptions((IDirectoryService)new DirectoryService(Logger, FileSystem)), cancellationToken);
	}

	protected virtual void TriggerOnRefreshStart()
	{
	}

	protected virtual void TriggerOnRefreshComplete()
	{
	}

	public async Task<ItemUpdateType> RefreshMetadata(MetadataRefreshOptions options, CancellationToken cancellationToken)
	{
		TriggerOnRefreshStart();
		bool requiresSave = false;
		if (SupportsOwnedItems)
		{
			try
			{
				List<FileSystemMetadata> fileSystemChildren = (IsFileProtocol ? GetFileSystemChildren(options.DirectoryService).ToList() : new List<FileSystemMetadata>());
				if (await RefreshedOwnedItems(options, fileSystemChildren, cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
				{
					requiresSave = true;
				}
			}
			catch (Exception ex)
			{
				Logger.ErrorException("Error refreshing owned items for {0}", ex, new object[1] { Path ?? Name });
			}
		}
		try
		{
			MetadataRefreshOptions options2 = (requiresSave ? new MetadataRefreshOptions(options)
			{
				ForceSave = true
			} : options);
			return await ProviderManager.RefreshSingleItem(this, options2, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			TriggerOnRefreshComplete();
		}
	}

	protected virtual Task<bool> RefreshedOwnedItems(MetadataRefreshOptions options, List<FileSystemMetadata> fileSystemChildren, CancellationToken cancellationToken)
	{
		if (IsFileProtocol && SupportsOwnedItems && SupportsThemeMedia && !IsInMixedFolder)
		{
			return RefreshExtras(this, options, fileSystemChildren, cancellationToken);
		}
		return Task.FromResult(result: false);
	}

	protected virtual FileSystemMetadata[] GetFileSystemChildren(IDirectoryService directoryService)
	{
		string containingFolderPath = ContainingFolderPath;
		return directoryService.GetFileSystemEntries(containingFolderPath);
	}

	private async Task<bool> RefreshExtras(BaseItem item, MetadataRefreshOptions options, List<FileSystemMetadata> fileSystemChildren, CancellationToken cancellationToken)
	{
		Guid id = item.Id;
		BaseItem[] source = LoadExtras(fileSystemChildren, options.DirectoryService).Concat(LoadLocalTrailers(fileSystemChildren, options.DirectoryService)).Concat(LoadRemoteTrailers(fileSystemChildren, options.DirectoryService)).Concat(LoadThemeSongs(fileSystemChildren, options.DirectoryService, id))
			.Concat(LoadThemeVideos(fileSystemChildren, options.DirectoryService, id))
			.ToArray();
		Guid[] newExtraIds = source.Select((BaseItem i) => i.Id).ToArray();
		bool extraIdsChanged = !Enumerable.SequenceEqual(item.ExtraIds, newExtraIds);
		await Task.WhenAll(source.Select((BaseItem i) =>
		{
			MetadataRefreshOptions metadataRefreshOptions = new MetadataRefreshOptions(options);
			if (extraIdsChanged)
			{
				metadataRefreshOptions.ForceSave = true;
			}
			return RefreshMetadataForOwnedItem(i, copyTitleMetadata: true, metadataRefreshOptions, cancellationToken);
		})).ConfigureAwait(continueOnCapturedContext: false);
		item.ExtraIds = newExtraIds;
		return extraIdsChanged;
	}

	private IEnumerable<BaseItem> LoadLocalTrailers(List<FileSystemMetadata> fileSystemChildren, IDirectoryService directoryService)
	{
		if (SupportsLocalTrailers)
		{
			return LibraryManager.FindTrailers(this, fileSystemChildren, directoryService);
		}
		return Array.Empty<BaseItem>();
	}

	protected virtual Dictionary<string, string> GetProviderIdsForLinkedTrailerSearch()
	{
		return EmptyDictionary;
	}

	private IEnumerable<BaseItem> LoadRemoteTrailers(List<FileSystemMetadata> fileSystemChildren, IDirectoryService directoryService)
	{
		Dictionary<string, string> providerIdsForLinkedTrailerSearch = GetProviderIdsForLinkedTrailerSearch();
		if (providerIdsForLinkedTrailerSearch.Count > 0)
		{
			List<BaseItem> itemList = LibraryManager.GetItemList(new InternalItemsQuery
			{
				IncludeItemTypes = new string[1] { typeof(Trailer).Name },
				Recursive = true,
				DtoOptions = new DtoOptions(allFields: false),
				HasOwnerId = false,
				HasAnyProviderId = providerIdsForLinkedTrailerSearch
			});
			if (itemList.Count > 0)
			{
				return itemList;
			}
		}
		return Array.Empty<BaseItem>();
	}

	protected virtual BaseItem[] LoadExtras(List<FileSystemMetadata> fileSystemChildren, IDirectoryService directoryService)
	{
		return Array.Empty<BaseItem>();
	}

	public virtual double GetDefaultPrimaryImageAspectRatio()
	{
		return 0.0;
	}

	public virtual string CreatePresentationUniqueKey()
	{
		return Id.ToString("N");
	}

	public string GetPresentationUniqueKey()
	{
		return PresentationUniqueKey ?? CreatePresentationUniqueKey();
	}

	public virtual bool RequiresRefresh()
	{
		return false;
	}

	public virtual List<string> GetUserDataKeys()
	{
		List<string> list = new List<string>();
		if (SourceType == SourceType.Channel && !string.IsNullOrEmpty(ExternalId))
		{
			list.Add(ExternalId);
		}
		list.Add(Id.ToString());
		return list;
	}

	internal virtual ItemUpdateType UpdateFromResolvedItem(BaseItem newItem)
	{
		ItemUpdateType itemUpdateType = ItemUpdateType.None;
		if (IsInMixedFolder != newItem.IsInMixedFolder)
		{
			IsInMixedFolder = newItem.IsInMixedFolder;
			itemUpdateType |= ItemUpdateType.MetadataImport;
		}
		return itemUpdateType;
	}

	public void AfterMetadataRefresh()
	{
		_sortName = null;
	}

	public string GetPreferredMetadataLanguage()
	{
		string text = PreferredMetadataLanguage;
		if (string.IsNullOrEmpty(text))
		{
			text = (from i in GetParents()
				select i.PreferredMetadataLanguage).FirstOrDefault((string i) => !string.IsNullOrEmpty(i));
		}
		if (string.IsNullOrEmpty(text))
		{
			text = (from i in LibraryManager.GetCollectionFolders(this)
				select i.PreferredMetadataLanguage).FirstOrDefault((string i) => !string.IsNullOrEmpty(i));
		}
		if (string.IsNullOrEmpty(text))
		{
			text = LibraryManager.GetLibraryOptions(this).PreferredMetadataLanguage;
		}
		if (string.IsNullOrEmpty(text))
		{
			text = ConfigurationManager.Configuration.PreferredMetadataLanguage;
		}
		return text;
	}

	public string GetPreferredMetadataCountryCode()
	{
		string text = PreferredMetadataCountryCode;
		if (string.IsNullOrEmpty(text))
		{
			text = (from i in GetParents()
				select i.PreferredMetadataCountryCode).FirstOrDefault((string i) => !string.IsNullOrEmpty(i));
		}
		if (string.IsNullOrEmpty(text))
		{
			text = (from i in LibraryManager.GetCollectionFolders(this)
				select i.PreferredMetadataCountryCode).FirstOrDefault((string i) => !string.IsNullOrEmpty(i));
		}
		if (string.IsNullOrEmpty(text))
		{
			text = LibraryManager.GetLibraryOptions(this).MetadataCountryCode;
		}
		if (string.IsNullOrEmpty(text))
		{
			text = ConfigurationManager.Configuration.MetadataCountryCode;
		}
		return text;
	}

	public virtual bool IsSaveLocalMetadataEnabled()
	{
		if (SourceType == SourceType.Channel)
		{
			return false;
		}
		return LibraryManager.GetLibraryOptions(this).SaveLocalMetadata;
	}

	public bool IsParentalAllowed(User user)
	{
		if (user == null)
		{
			throw new ArgumentNullException("user");
		}
		if (!IsVisibleViaTags(user))
		{
			return false;
		}
		int? maxParentalRating = user.Policy.MaxParentalRating;
		if (!maxParentalRating.HasValue)
		{
			return true;
		}
		string text = CustomRatingForComparison;
		if (string.IsNullOrEmpty(text))
		{
			text = OfficialRatingForComparison;
		}
		if (string.IsNullOrEmpty(text))
		{
			return !GetBlockUnratedValue(user.Policy);
		}
		int? ratingLevel = LocalizationManager.GetRatingLevel(text);
		if (!ratingLevel.HasValue)
		{
			bool flag = !GetBlockUnratedValue(user.Policy);
			if (!flag)
			{
				Logger.Debug("{0} has an unrecognized parental rating of {1}.", new object[2] { Name, text });
			}
			return flag;
		}
		return ratingLevel.Value <= maxParentalRating.Value;
	}

	public int? GetParentalRatingValue()
	{
		string text = CustomRating;
		if (string.IsNullOrEmpty(text))
		{
			text = OfficialRating;
		}
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		return LocalizationManager.GetRatingLevel(text);
	}

	public int? GetInheritedParentalRatingValue()
	{
		string text = CustomRatingForComparison;
		if (string.IsNullOrEmpty(text))
		{
			text = OfficialRatingForComparison;
		}
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		return LocalizationManager.GetRatingLevel(text);
	}

	public List<string> GetInheritedTags()
	{
		List<string> list = new List<string>();
		list.AddRange(Tags);
		foreach (BaseItem parent in GetParents())
		{
			list.AddRange(parent.Tags);
		}
		return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private bool IsVisibleViaTags(User user)
	{
		if (user.Policy.BlockedTags.Any((string i) => Tags.Contains(i, StringComparer.OrdinalIgnoreCase)))
		{
			return false;
		}
		return true;
	}

	protected virtual bool IsAllowTagFilterEnforced()
	{
		return true;
	}

	public virtual UnratedItem GetBlockUnratedType()
	{
		if (SourceType != SourceType.Channel)
		{
			return (UnratedItem)9;
		}
		return (UnratedItem)8;
	}

	protected virtual bool GetBlockUnratedValue(UserPolicy config)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		if (IsFolder)
		{
			return false;
		}
		if (this is IItemByName)
		{
			return false;
		}
		return Enumerable.Contains(config.BlockUnratedItems, GetBlockUnratedType());
	}

	public virtual bool IsVisible(User user)
	{
		if (user == null)
		{
			throw new ArgumentNullException("user");
		}
		return IsParentalAllowed(user);
	}

	public virtual bool IsVisibleStandalone(User user)
	{
		if (SourceType == SourceType.Channel)
		{
			if (IsVisibleStandaloneInternal(user, checkFolders: false))
			{
				return Channel.IsChannelVisible(this, user);
			}
			return false;
		}
		return IsVisibleStandaloneInternal(user, checkFolders: true);
	}

	protected bool IsVisibleStandaloneInternal(User user, bool checkFolders)
	{
		if (!IsVisible(user))
		{
			return false;
		}
		if (GetParents().Any((BaseItem i) => !i.IsVisible(user)))
		{
			return false;
		}
		if (checkFolders)
		{
			if (string.IsNullOrEmpty((GetParents().LastOrDefault() ?? this).Path))
			{
				return true;
			}
			List<Guid> list = (from i in LibraryManager.GetCollectionFolders(this)
				select i.Id).ToList();
			if (list.Count > 0)
			{
				List<Guid> list2 = (from i in LibraryManager.GetUserRootFolder().GetChildren(user, includeLinkedChildren: true)
					select i.Id).ToList();
				if (!list.Any(list2.Contains))
				{
					return false;
				}
			}
		}
		return true;
	}

	public virtual string GetClientTypeName()
	{
		if (IsFolder && SourceType == SourceType.Channel && !(this is Channel))
		{
			return "ChannelFolderItem";
		}
		return GetType().Name;
	}

	protected BaseItem GetLinkedChild(LinkedChild info)
	{
		if (info.ItemId.HasValue)
		{
			if (info.ItemId.Value.Equals(Guid.Empty))
			{
				return null;
			}
			BaseItem itemById = LibraryManager.GetItemById(info.ItemId.Value);
			if (itemById != null)
			{
				return itemById;
			}
		}
		BaseItem baseItem = FindLinkedChild(info);
		if (baseItem == null)
		{
			info.ItemId = Guid.Empty;
		}
		else
		{
			info.ItemId = baseItem.Id;
		}
		return baseItem;
	}

	private BaseItem FindLinkedChild(LinkedChild info)
	{
		string path = info.Path;
		if (!string.IsNullOrEmpty(path))
		{
			path = FileSystem.MakeAbsolutePath(ContainingFolderPath, path);
			return LibraryManager.FindByPath(path, null);
		}
		if (!string.IsNullOrEmpty(info.LibraryItemId))
		{
			return LibraryManager.GetItemById(info.LibraryItemId);
		}
		return null;
	}

	public void AddStudio(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			throw new ArgumentNullException("name");
		}
		string[] studios = Studios;
		if (!studios.Contains(name, StringComparer.OrdinalIgnoreCase))
		{
			if (studios.Length == 0)
			{
				Studios = new string[1] { name };
			}
			else
			{
				string[] array = LinqExtensions.ToArray<string>((IEnumerable<string>)studios, studios.Length + 1);
				array[^1] = name;
				Studios = array;
			}
		}
	}

	public void SetStudios(IEnumerable<string> names)
	{
		Studios = names.Distinct().ToArray();
	}

	public void AddGenre(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			throw new ArgumentNullException("name");
		}
		string[] genres = Genres;
		if (!genres.Contains(name, StringComparer.OrdinalIgnoreCase))
		{
			List<string> list = genres.ToList();
			list.Add(name);
			Genres = list.ToArray();
		}
	}

	public virtual void MarkPlayed(User user, DateTime? datePlayed, bool resetPosition)
	{
		if (user == null)
		{
			throw new ArgumentNullException();
		}
		UserItemData userData = UserDataManager.GetUserData(user, this);
		if (datePlayed.HasValue)
		{
			userData.PlayCount++;
		}
		userData.PlayCount = Math.Max(userData.PlayCount, 1);
		if (resetPosition)
		{
			userData.PlaybackPositionTicks = 0L;
		}
		userData.LastPlayedDate = datePlayed ?? userData.LastPlayedDate ?? DateTime.UtcNow;
		userData.Played = true;
		UserDataManager.SaveUserData(user, this, userData, (UserDataSaveReason)4, CancellationToken.None);
	}

	public virtual void MarkUnplayed(User user)
	{
		if (user == null)
		{
			throw new ArgumentNullException();
		}
		UserItemData userData = UserDataManager.GetUserData(user, this);
		userData.PlayCount = 0;
		userData.PlaybackPositionTicks = 0L;
		userData.LastPlayedDate = null;
		userData.Played = false;
		UserDataManager.SaveUserData(user, this, userData, (UserDataSaveReason)4, CancellationToken.None);
	}

	public virtual void ChangedExternally()
	{
		ProviderManager.QueueRefresh(Id, new MetadataRefreshOptions(FileSystem), RefreshPriority.High);
	}

	public bool HasImage(ImageType type, int imageIndex)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		return GetImageInfo(type, imageIndex) != null;
	}

	public void SetImage(ItemImageInfo image, int index)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Invalid comparison between Unknown and I4
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		if ((int)image.Type == 10)
		{
			throw new ArgumentException("Cannot set chapter images using SetImagePath");
		}
		ItemImageInfo imageInfo = GetImageInfo(image.Type, index);
		if (imageInfo != null)
		{
			imageInfo.Path = image.Path;
			imageInfo.DateModified = image.DateModified;
			imageInfo.Width = image.Width;
			imageInfo.Height = image.Height;
		}
		else
		{
			int num = ImageInfos.Length;
			ItemImageInfo[] array = LinqExtensions.ToArray<ItemImageInfo>((IEnumerable<ItemImageInfo>)ImageInfos, num + 1);
			array[num] = image;
			ImageInfos = array;
		}
	}

	public void SetImagePath(ImageType type, int index, FileSystemMetadata file)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0003: Invalid comparison between Unknown and I4
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		if ((int)type == 10)
		{
			throw new ArgumentException("Cannot set chapter images using SetImagePath");
		}
		ItemImageInfo imageInfo = GetImageInfo(type, index);
		if (imageInfo == null)
		{
			int num = ImageInfos.Length;
			ItemImageInfo[] array = LinqExtensions.ToArray<ItemImageInfo>((IEnumerable<ItemImageInfo>)ImageInfos, num + 1);
			array[num] = GetImageInfo(file, type);
			ImageInfos = array;
		}
		else
		{
			ItemImageInfo imageInfo2 = GetImageInfo(file, type);
			imageInfo.Path = file.FullName;
			imageInfo.DateModified = imageInfo2.DateModified;
			imageInfo.Width = 0;
			imageInfo.Height = 0;
		}
	}

	public void DeleteImage(ImageType type, int index)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		ItemImageInfo imageInfo = GetImageInfo(type, index);
		if (imageInfo != null)
		{
			RemoveImage(imageInfo);
			if (imageInfo.IsLocalFile)
			{
				FileSystem.DeleteFile(imageInfo.Path);
			}
			UpdateToRepository(ItemUpdateType.ImageUpdate, CancellationToken.None);
		}
	}

	public void RemoveImage(ItemImageInfo image)
	{
		RemoveImages(new List<ItemImageInfo> { image });
	}

	public void RemoveImages(List<ItemImageInfo> deletedImages)
	{
		ImageInfos = ImageInfos.Except(deletedImages).ToArray();
	}

	public virtual void UpdateToRepository(ItemUpdateType updateReason, CancellationToken cancellationToken)
	{
		LibraryManager.UpdateItem(this, GetParent(), updateReason, cancellationToken);
	}

	public bool ValidateImages(IDirectoryService directoryService)
	{
		List<string> allFiles = (from i in ImageInfos
			where i.IsLocalFile
			select FileSystem.GetDirectoryName(i.Path)).Distinct(StringComparer.OrdinalIgnoreCase).SelectMany((string i) => directoryService.GetFilePaths(i)).ToList();
		List<ItemImageInfo> list = ImageInfos.Where((ItemImageInfo image) => image.IsLocalFile && !allFiles.Contains(image.Path, StringComparer.OrdinalIgnoreCase)).ToList();
		if (list.Count > 0)
		{
			ImageInfos = ImageInfos.Except(list).ToArray();
		}
		return list.Count > 0;
	}

	public string GetImagePath(ImageType imageType, int imageIndex)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		return GetImageInfo(imageType, imageIndex)?.Path;
	}

	public ItemImageInfo GetImageInfo(ImageType imageType, int imageIndex)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0003: Invalid comparison between Unknown and I4
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		if ((int)imageType == 10)
		{
			ChapterInfo chapter = ItemRepository.GetChapter(this, imageIndex);
			if (chapter == null)
			{
				return null;
			}
			string imagePath = chapter.ImagePath;
			if (string.IsNullOrEmpty(imagePath))
			{
				return null;
			}
			return new ItemImageInfo
			{
				Path = imagePath,
				DateModified = chapter.ImageDateModified,
				Type = imageType
			};
		}
		return GetImages(imageType).ElementAtOrDefault(imageIndex);
	}

	public IEnumerable<ItemImageInfo> GetImages(ImageType imageType)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Invalid comparison between Unknown and I4
		if ((int)imageType == 10)
		{
			throw new ArgumentException("No image info for chapter images");
		}
		return ImageInfos.Where((ItemImageInfo i) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			return i.Type == imageType;
		});
	}

	public bool AddImages(ImageType imageType, List<FileSystemMetadata> images)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0003: Invalid comparison between Unknown and I4
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c6: Unknown result type (might be due to invalid IL or missing references)
		if ((int)imageType == 10)
		{
			throw new ArgumentException("Cannot call AddImages with chapter images");
		}
		List<ItemImageInfo> list = GetImages(imageType).ToList();
		List<FileSystemMetadata> list2 = new List<FileSystemMetadata>();
		bool flag = false;
		bool flag2 = false;
		foreach (FileSystemMetadata newImage in images)
		{
			if (newImage == null)
			{
				throw new ArgumentException("null image found in list");
			}
			ItemImageInfo itemImageInfo = list.FirstOrDefault((ItemImageInfo i) => string.Equals(i.Path, newImage.FullName, StringComparison.OrdinalIgnoreCase));
			if (itemImageInfo == null)
			{
				list2.Add(newImage);
				flag = true;
			}
			else if (itemImageInfo.IsLocalFile)
			{
				DateTime lastWriteTimeUtc = FileSystem.GetLastWriteTimeUtc(newImage);
				if (itemImageInfo.DateModified != lastWriteTimeUtc && (itemImageInfo.Width > 0 || itemImageInfo.Height > 0))
				{
					itemImageInfo.Width = 0;
					itemImageInfo.Height = 0;
					flag2 = true;
				}
				itemImageInfo.DateModified = lastWriteTimeUtc;
			}
		}
		if (flag || images.Count != list.Count)
		{
			List<string> newImagePaths = images.Select((FileSystemMetadata i) => i.FullName).ToList();
			List<ItemImageInfo> list3 = list.Where((ItemImageInfo i) => i.IsLocalFile && !newImagePaths.Contains(i.Path, StringComparer.OrdinalIgnoreCase) && !FileSystem.FileExists(i.Path)).ToList();
			if (list3.Count > 0)
			{
				ImageInfos = ImageInfos.Except(list3).ToArray();
			}
		}
		if (list2.Count > 0)
		{
			int num = ImageInfos.Length;
			ItemImageInfo[] array = LinqExtensions.ToArray<ItemImageInfo>((IEnumerable<ItemImageInfo>)ImageInfos, num + list2.Count);
			foreach (FileSystemMetadata item in list2)
			{
				array[num] = GetImageInfo(item, imageType);
				num++;
			}
			ImageInfos = array;
		}
		if (!flag2)
		{
			return list2.Count > 0;
		}
		return true;
	}

	private ItemImageInfo GetImageInfo(FileSystemMetadata file, ImageType type)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		return new ItemImageInfo
		{
			Path = file.FullName,
			Type = type,
			DateModified = FileSystem.GetLastWriteTimeUtc(file)
		};
	}

	public virtual IEnumerable<FileSystemMetadata> GetDeletePaths()
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Expected Obj, but got Unknown
		return ((IEnumerable<FileSystemMetadata>)new FileSystemMetadata[1]
		{
			new FileSystemMetadata
			{
				FullName = Path,
				IsDirectory = IsFolder
			}
		}).Concat((IEnumerable<FileSystemMetadata>)GetLocalMetadataFilesToDelete());
	}

	protected List<FileSystemMetadata> GetLocalMetadataFilesToDelete()
	{
		if (IsFolder || !IsInMixedFolder)
		{
			return new List<FileSystemMetadata>();
		}
		string filename = System.IO.Path.GetFileNameWithoutExtension(Path);
		List<string> list = new List<string>
		{
			".nfo", ".xml", ".srt", ".vtt", ".sub", ".idx", ".txt", ".edl", ".bif", ".smi",
			".ttml"
		};
		list.AddRange(SupportedImageExtensions);
		return (from i in FileSystem.GetFiles(FileSystem.GetDirectoryName(Path), LinqExtensions.ToArray<string>((IEnumerable<string>)list, list.Count), false, false)
			where System.IO.Path.GetFileNameWithoutExtension(i.FullName).StartsWith(filename, StringComparison.OrdinalIgnoreCase)
			select i).ToList();
	}

	public bool AllowsMultipleImages(ImageType type)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Invalid comparison between Unknown and I4
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Invalid comparison between Unknown and I4
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Invalid comparison between Unknown and I4
		if ((int)type != 2 && (int)type != 8)
		{
			return (int)type == 10;
		}
		return true;
	}

	public void SwapImages(ImageType type, int index1, int index2)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		if (!AllowsMultipleImages(type))
		{
			throw new ArgumentException("The change index operation is only applicable to backdrops and screenshots");
		}
		ItemImageInfo imageInfo = GetImageInfo(type, index1);
		ItemImageInfo imageInfo2 = GetImageInfo(type, index2);
		if (imageInfo != null && imageInfo2 != null && imageInfo.IsLocalFile && imageInfo2.IsLocalFile)
		{
			string path = imageInfo.Path;
			string path2 = imageInfo2.Path;
			FileSystem.SwapFiles(path, path2);
			imageInfo.DateModified = FileSystem.GetLastWriteTimeUtc(imageInfo.Path);
			imageInfo2.DateModified = FileSystem.GetLastWriteTimeUtc(imageInfo2.Path);
			imageInfo.Width = 0;
			imageInfo.Height = 0;
			imageInfo2.Width = 0;
			imageInfo2.Height = 0;
			UpdateToRepository(ItemUpdateType.ImageUpdate, CancellationToken.None);
		}
	}

	public virtual bool IsPlayed(User user)
	{
		return UserDataManager.GetUserData(user, this)?.Played ?? false;
	}

	public bool IsFavoriteOrLiked(User user)
	{
		UserItemData userData = UserDataManager.GetUserData(user, this);
		if (userData != null)
		{
			if (!userData.IsFavorite)
			{
				return userData.Likes ?? false;
			}
			return true;
		}
		return false;
	}

	public virtual bool IsUnplayed(User user)
	{
		if (user == null)
		{
			throw new ArgumentNullException("user");
		}
		UserItemData userData = UserDataManager.GetUserData(user, this);
		if (userData != null)
		{
			return !userData.Played;
		}
		return true;
	}

	ItemLookupInfo IHasLookupInfo<ItemLookupInfo>.GetLookupInfo()
	{
		return GetItemLookupInfo<ItemLookupInfo>();
	}

	protected T GetItemLookupInfo<T>() where T : ItemLookupInfo, new()
	{
		return new T
		{
			MetadataCountryCode = GetPreferredMetadataCountryCode(),
			MetadataLanguage = GetPreferredMetadataLanguage(),
			Name = GetNameForMetadataLookup(),
			ProviderIds = ProviderIds,
			IndexNumber = IndexNumber,
			ParentIndexNumber = ParentIndexNumber,
			Year = ProductionYear,
			PremiereDate = PremiereDate
		};
	}

	protected virtual string GetNameForMetadataLookup()
	{
		return Name;
	}

	public virtual bool BeforeMetadataRefresh(bool replaceAllMetdata)
	{
		_sortName = null;
		bool result = false;
		if (string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(Path))
		{
			Name = FileSystem.GetFileNameWithoutExtension(Path);
			result = true;
		}
		return result;
	}

	protected static string GetMappedPath(BaseItem item, string path, MediaProtocol? protocol)
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		if (protocol.HasValue && (int)protocol.Value == 0)
		{
			return LibraryManager.GetPathAfterNetworkSubstitution(path, item);
		}
		return path;
	}

	public virtual void FillUserDataDtoValues(UserItemDataDto dto, UserItemData userData, BaseItemDto itemDto, User user, DtoOptions options)
	{
		if (!RunTimeTicks.HasValue)
		{
			return;
		}
		double num = RunTimeTicks.Value;
		if (num > 0.0)
		{
			num = (double)userData.PlaybackPositionTicks / num;
			if (num > 0.0)
			{
				dto.PlayedPercentage = 100.0 * num;
			}
		}
	}

	protected Task RefreshMetadataForOwnedItem(BaseItem ownedItem, bool copyTitleMetadata, MetadataRefreshOptions options, CancellationToken cancellationToken)
	{
		MetadataRefreshOptions metadataRefreshOptions = new MetadataRefreshOptions(options);
		metadataRefreshOptions.SearchResult = null;
		if (copyTitleMetadata)
		{
			if (!Enumerable.SequenceEqual(Genres, ownedItem.Genres, StringComparer.Ordinal))
			{
				metadataRefreshOptions.ForceSave = true;
				ownedItem.Genres = Genres;
			}
			if (!Enumerable.SequenceEqual(Studios, ownedItem.Studios, StringComparer.Ordinal))
			{
				metadataRefreshOptions.ForceSave = true;
				ownedItem.Studios = Studios;
			}
			if (!Enumerable.SequenceEqual(ProductionLocations, ownedItem.ProductionLocations, StringComparer.Ordinal))
			{
				metadataRefreshOptions.ForceSave = true;
				ownedItem.ProductionLocations = ProductionLocations;
			}
			if (CommunityRating != ownedItem.CommunityRating)
			{
				ownedItem.CommunityRating = CommunityRating;
				metadataRefreshOptions.ForceSave = true;
			}
			if (CriticRating != ownedItem.CriticRating)
			{
				ownedItem.CriticRating = CriticRating;
				metadataRefreshOptions.ForceSave = true;
			}
			if (!string.Equals(Overview, ownedItem.Overview, StringComparison.Ordinal))
			{
				ownedItem.Overview = Overview;
				metadataRefreshOptions.ForceSave = true;
			}
			if (!string.Equals(OfficialRating, ownedItem.OfficialRating, StringComparison.Ordinal))
			{
				ownedItem.OfficialRating = OfficialRating;
				metadataRefreshOptions.ForceSave = true;
			}
			if (!string.Equals(CustomRating, ownedItem.CustomRating, StringComparison.Ordinal))
			{
				ownedItem.CustomRating = CustomRating;
				metadataRefreshOptions.ForceSave = true;
			}
		}
		return ownedItem.RefreshMetadata(metadataRefreshOptions, cancellationToken);
	}

	protected Task RefreshMetadataForOwnedVideo(MetadataRefreshOptions options, bool copyTitleMetadata, string path, CancellationToken cancellationToken)
	{
		MetadataRefreshOptions metadataRefreshOptions = new MetadataRefreshOptions(options);
		metadataRefreshOptions.SearchResult = null;
		Guid newItemId = LibraryManager.GetNewItemId(path, typeof(Video));
		Video video = LibraryManager.GetItemById(newItemId) as Video;
		if (video == null)
		{
			video = LibraryManager.ResolvePath(FileSystem.GetFileSystemInfo(path)) as Video;
			metadataRefreshOptions.ForceSave = true;
		}
		if (video == null)
		{
			return Task.CompletedTask;
		}
		return RefreshMetadataForOwnedItem(video, copyTitleMetadata, metadataRefreshOptions, cancellationToken);
	}

	public string GetEtag(User user)
	{
		List<string> etagValues = GetEtagValues(user);
		return BaseExtensions.GetMD5(string.Join("|", LinqExtensions.ToArray<string>((IEnumerable<string>)etagValues, etagValues.Count))).ToString("N");
	}

	protected virtual List<string> GetEtagValues(User user)
	{
		return new List<string> { DateLastSaved.Ticks.ToString(CultureInfo.InvariantCulture) };
	}

	public virtual IEnumerable<Guid> GetAncestorIds()
	{
		return (from i in GetParents()
			select i.Id).Concat(from i in LibraryManager.GetCollectionFolders(this)
			select i.Id);
	}

	public BaseItem GetTopParent()
	{
		if (IsTopParent)
		{
			return this;
		}
		foreach (BaseItem parent in GetParents())
		{
			if (parent.IsTopParent)
			{
				return parent;
			}
		}
		return null;
	}

	public virtual IEnumerable<Guid> GetIdsForAncestorQuery()
	{
		return new Guid[1] { Id };
	}

	public virtual List<ExternalUrl> GetRelatedUrls()
	{
		return new List<ExternalUrl>();
	}

	public virtual double? GetRefreshProgress()
	{
		return null;
	}

	public virtual ItemUpdateType OnMetadataChanged()
	{
		ItemUpdateType itemUpdateType = ItemUpdateType.None;
		int num = GetInheritedParentalRatingValue() ?? 0;
		if (num != InheritedParentalRatingValue)
		{
			InheritedParentalRatingValue = num;
			itemUpdateType |= ItemUpdateType.MetadataImport;
		}
		return itemUpdateType;
	}

	public bool UpdateRatingToItems(IList<BaseItem> children)
	{
		string officialRating = OfficialRating;
		IEnumerable<string> source = from i in (from i in children
				select i.OfficialRating into i
				where !string.IsNullOrEmpty(i)
				select i).Distinct(StringComparer.OrdinalIgnoreCase)
			select (i, LocalizationManager.GetRatingLevel(i)) into i
			orderby i.Item2 ?? 1000
			select i.Item1;
		OfficialRating = source.FirstOrDefault() ?? officialRating;
		return !string.Equals(officialRating ?? string.Empty, OfficialRating ?? string.Empty, StringComparison.OrdinalIgnoreCase);
	}

	static BaseItem()
	{
		DisplayExtraTypes = new MediaBrowser.Model.Entities.ExtraType[]
		{
			MediaBrowser.Model.Entities.ExtraType.BehindTheScenes,
			MediaBrowser.Model.Entities.ExtraType.Clip,
			MediaBrowser.Model.Entities.ExtraType.DeletedScene,
			MediaBrowser.Model.Entities.ExtraType.Interview,
			MediaBrowser.Model.Entities.ExtraType.Sample,
			MediaBrowser.Model.Entities.ExtraType.Scene
		};
		EmptyDictionary = new Dictionary<string, string>();
	}
}
