using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.Library;

public interface ILibraryManager
{
	AggregateFolder RootFolder { get; }

	bool IsScanRunning { get; }

	event EventHandler<ItemChangeEventArgs> ItemAdded;

	event EventHandler<ItemChangeEventArgs> ItemUpdated;

	event EventHandler<ItemChangeEventArgs> ItemRemoved;

	BaseItem ResolvePath(FileSystemMetadata fileInfo, Folder parent = null);

	IEnumerable<BaseItem> ResolvePaths(IEnumerable<FileSystemMetadata> files, IDirectoryService directoryService, Folder parent, LibraryOptions libraryOptions, string collectionType = null);

	Person GetPerson(string name);

	BaseItem FindByPath(string path, bool? isFolder);

	MusicArtist GetArtist(string name);

	MusicArtist GetArtist(string name, DtoOptions options);

	Studio GetStudio(string name);

	Genre GetGenre(string name);

	MusicGenre GetMusicGenre(string name);

	GameGenre GetGameGenre(string name);

	Year GetYear(int value);

	Task ValidatePeople(CancellationToken cancellationToken, IProgress<double> progress);

	Task ValidateMediaLibrary(IProgress<double> progress, CancellationToken cancellationToken);

	void QueueLibraryScan();

	void UpdateImages(BaseItem item);

	List<VirtualFolderInfo> GetVirtualFolders();

	List<VirtualFolderInfo> GetVirtualFolders(bool includeRefreshState);

	BaseItem GetItemById(Guid id);

	Task<IEnumerable<Video>> GetIntros(BaseItem item, User user);

	IEnumerable<string> GetAllIntroFiles();

	void AddParts(IEnumerable<IResolverIgnoreRule> rules, IEnumerable<IItemResolver> resolvers, IEnumerable<IIntroProvider> introProviders, IEnumerable<IBaseItemComparer> itemComparers, IEnumerable<ILibraryPostScanTask> postscanTasks);

	IEnumerable<BaseItem> Sort(IEnumerable<BaseItem> items, User user, IEnumerable<string> sortBy, SortOrder sortOrder);

	IEnumerable<BaseItem> Sort(IEnumerable<BaseItem> items, User user, IEnumerable<(string, SortOrder)> orderBy);

	Folder GetUserRootFolder();

	void CreateItem(BaseItem item, BaseItem parent);

	void CreateItems(IEnumerable<BaseItem> items, BaseItem parent, CancellationToken cancellationToken);

	void UpdateItems(List<BaseItem> items, BaseItem parent, ItemUpdateType updateReason, CancellationToken cancellationToken);

	void UpdateItem(BaseItem item, BaseItem parent, ItemUpdateType updateReason, CancellationToken cancellationToken);

	BaseItem RetrieveItem(Guid id);

	string GetContentType(BaseItem item);

	string GetInheritedContentType(BaseItem item);

	string GetConfiguredContentType(BaseItem item);

	string GetConfiguredContentType(string path);

	List<FileSystemMetadata> NormalizeRootPathList(IEnumerable<FileSystemMetadata> paths);

	void RegisterItem(BaseItem item);

	void DeleteItem(BaseItem item, DeleteOptions options);

	void DeleteItem(BaseItem item, DeleteOptions options, bool notifyParentItem);

	void DeleteItem(BaseItem item, DeleteOptions options, BaseItem parent, bool notifyParentItem);

	UserView GetNamedView(User user, string name, Guid parentId, string viewType, string sortNamen);

	UserView GetNamedView(User user, string name, string viewType, string sortName);

	UserView GetNamedView(string name, string viewType, string sortName);

	UserView GetNamedView(string name, Guid parentId, string viewType, string sortName, string uniqueId);

	UserView GetShadowView(BaseItem parent, string viewType, string sortName);

	bool IsVideoFile(string path);

	bool IsAudioFile(string path);

	bool IsAudioFile(string path, LibraryOptions libraryOptions);

	bool IsVideoFile(string path, LibraryOptions libraryOptions);

	int? GetSeasonNumberFromPath(string path);

	bool FillMissingEpisodeNumbersFromPath(Episode episode, bool forceRefresh);

	ItemLookupInfo ParseName(string name);

	Guid GetNewItemId(string key, Type type);

	IEnumerable<Video> FindTrailers(BaseItem owner, List<FileSystemMetadata> fileSystemChildren, IDirectoryService directoryService);

	IEnumerable<Video> FindExtras(BaseItem owner, List<FileSystemMetadata> fileSystemChildren, IDirectoryService directoryService);

	List<Folder> GetCollectionFolders(BaseItem item);

	List<Folder> GetCollectionFolders(BaseItem item, List<Folder> allUserRootChildren);

	LibraryOptions GetLibraryOptions(BaseItem item);

	List<PersonInfo> GetPeople(BaseItem item);

	List<PersonInfo> GetPeople(InternalPeopleQuery query);

	List<Person> GetPeopleItems(InternalPeopleQuery query);

	void UpdatePeople(BaseItem item, List<PersonInfo> people);

	List<Guid> GetItemIds(InternalItemsQuery query);

	List<string> GetPeopleNames(InternalPeopleQuery query);

	QueryResult<BaseItem> QueryItems(InternalItemsQuery query);

	string GetPathAfterNetworkSubstitution(string path, BaseItem ownerItem = null);

	string SubstitutePath(string path, string from, string to);

	Task<ItemImageInfo> ConvertImageToLocal(BaseItem item, ItemImageInfo image, int imageIndex);

	List<BaseItem> GetItemList(InternalItemsQuery query);

	List<BaseItem> GetItemList(InternalItemsQuery query, bool allowExternalContent);

	List<BaseItem> GetItemList(InternalItemsQuery query, List<BaseItem> parents);

	QueryResult<BaseItem> GetItemsResult(InternalItemsQuery query);

	bool IgnoreFile(FileSystemMetadata file, BaseItem parent);

	Guid GetStudioId(string name);

	Guid GetGenreId(string name);

	Guid GetMusicGenreId(string name);

	Guid GetGameGenreId(string name);

	Task AddVirtualFolder(string name, string collectionType, LibraryOptions options, bool refreshLibrary);

	Task RemoveVirtualFolder(string name, bool refreshLibrary);

	void AddMediaPath(string virtualFolderName, MediaPathInfo path);

	void UpdateMediaPath(string virtualFolderName, MediaPathInfo path);

	void RemoveMediaPath(string virtualFolderName, string path);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetGenres(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetMusicGenres(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetGameGenres(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetStudios(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetArtists(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetAlbumArtists(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetAllArtists(InternalItemsQuery query);

	int GetCount(InternalItemsQuery query);

	void AddExternalSubtitleStreams(List<MediaStream> streams, string videoPath, string[] files);
}
