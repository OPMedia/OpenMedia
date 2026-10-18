using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.Persistence;

/// <summary>
/// 媒体项仓储。
///
/// 契约约定：
/// - <c>*Async</c> 方法为唯一真实实现，内部全链路 await（OpenAsync/ExecuteReaderAsync/...），
///   不占用线程池等待 I/O。
/// - 同名的同步方法仅为遗留门面兼容（默认接口实现 → 异步版本），
///   新代码一律调用 Async 版本。
/// </summary>
public interface IItemRepository : IRepository, IDisposable
{
	// ===================== 写入 =====================

	Task SaveItemAsync(BaseItem item, CancellationToken cancellationToken);

	Task SaveItemsAsync(List<BaseItem> items, CancellationToken cancellationToken);

	Task SaveImagesAsync(BaseItem item, CancellationToken cancellationToken = default);

	Task DeleteItemAsync(Guid id, CancellationToken cancellationToken);

	Task SaveChaptersAsync(Guid id, List<ChapterInfo> chapters, CancellationToken cancellationToken = default);

	Task SaveMediaStreamsAsync(Guid id, List<MediaStream> streams, CancellationToken cancellationToken);

	Task UpdatePeopleAsync(Guid itemId, List<PersonInfo> people, CancellationToken cancellationToken = default);

	Task UpdateInheritedValuesAsync(CancellationToken cancellationToken);

	// ===================== 读取 =====================

	Task<BaseItem> RetrieveItemAsync(Guid id, CancellationToken cancellationToken = default);

	Task<List<ChapterInfo>> GetChaptersAsync(BaseItem item, CancellationToken cancellationToken = default);

	Task<ChapterInfo> GetChapterAsync(BaseItem item, int index, CancellationToken cancellationToken = default);

	Task<List<MediaStream>> GetMediaStreamsAsync(MediaStreamQuery query, CancellationToken cancellationToken = default);

	Task<QueryResult<Guid>> GetItemIdsAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<QueryResult<BaseItem>> GetItemsAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<List<Guid>> GetItemIdsListAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<List<PersonInfo>> GetPeopleAsync(InternalPeopleQuery query, CancellationToken cancellationToken = default);

	Task<List<string>> GetPeopleNamesAsync(InternalPeopleQuery query, CancellationToken cancellationToken = default);

	Task<List<Tuple<Guid, string>>> GetItemIdsWithPathAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<List<BaseItem>> GetItemListAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<int> GetCountAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetGenresAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetMusicGenresAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetGameGenresAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetStudiosAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetArtistsAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetAlbumArtistsAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<QueryResult<Tuple<BaseItem, ItemCounts>>> GetAllArtistsAsync(InternalItemsQuery query, CancellationToken cancellationToken = default);

	Task<List<string>> GetGameGenreNamesAsync(CancellationToken cancellationToken = default);

	Task<List<string>> GetMusicGenreNamesAsync(CancellationToken cancellationToken = default);

	Task<List<string>> GetStudioNamesAsync(CancellationToken cancellationToken = default);

	Task<List<string>> GetGenreNamesAsync(CancellationToken cancellationToken = default);

	Task<List<string>> GetAllArtistNamesAsync(CancellationToken cancellationToken = default);

	// ===================== 遗留同步桥接（默认实现） =====================

	void SaveItem(BaseItem item, CancellationToken cancellationToken) => SaveItemAsync(item, cancellationToken).GetAwaiter().GetResult();

	void SaveItems(List<BaseItem> items, CancellationToken cancellationToken) => SaveItemsAsync(items, cancellationToken).GetAwaiter().GetResult();

	void SaveImages(BaseItem item) => SaveImagesAsync(item).GetAwaiter().GetResult();

	void DeleteItem(Guid id, CancellationToken cancellationToken) => DeleteItemAsync(id, cancellationToken).GetAwaiter().GetResult();

	void SaveChapters(Guid id, List<ChapterInfo> chapters) => SaveChaptersAsync(id, chapters).GetAwaiter().GetResult();

	void SaveMediaStreams(Guid id, List<MediaStream> streams, CancellationToken cancellationToken) => SaveMediaStreamsAsync(id, streams, cancellationToken).GetAwaiter().GetResult();

	void UpdatePeople(Guid itemId, List<PersonInfo> people) => UpdatePeopleAsync(itemId, people).GetAwaiter().GetResult();

	void UpdateInheritedValues(CancellationToken cancellationToken) => UpdateInheritedValuesAsync(cancellationToken).GetAwaiter().GetResult();

	BaseItem RetrieveItem(Guid id) => RetrieveItemAsync(id).GetAwaiter().GetResult();

	List<ChapterInfo> GetChapters(BaseItem item) => GetChaptersAsync(item).GetAwaiter().GetResult();

	ChapterInfo GetChapter(BaseItem item, int index) => GetChapterAsync(item, index).GetAwaiter().GetResult();

	List<MediaStream> GetMediaStreams(MediaStreamQuery query) => GetMediaStreamsAsync(query).GetAwaiter().GetResult();

	QueryResult<Guid> GetItemIds(InternalItemsQuery query) => GetItemIdsAsync(query).GetAwaiter().GetResult();

	QueryResult<BaseItem> GetItems(InternalItemsQuery query) => GetItemsAsync(query).GetAwaiter().GetResult();

	List<Guid> GetItemIdsList(InternalItemsQuery query) => GetItemIdsListAsync(query).GetAwaiter().GetResult();

	List<PersonInfo> GetPeople(InternalPeopleQuery query) => GetPeopleAsync(query).GetAwaiter().GetResult();

	List<string> GetPeopleNames(InternalPeopleQuery query) => GetPeopleNamesAsync(query).GetAwaiter().GetResult();

	List<Tuple<Guid, string>> GetItemIdsWithPath(InternalItemsQuery query) => GetItemIdsWithPathAsync(query).GetAwaiter().GetResult();

	List<BaseItem> GetItemList(InternalItemsQuery query) => GetItemListAsync(query).GetAwaiter().GetResult();

	int GetCount(InternalItemsQuery query) => GetCountAsync(query).GetAwaiter().GetResult();

	QueryResult<Tuple<BaseItem, ItemCounts>> GetGenres(InternalItemsQuery query) => GetGenresAsync(query).GetAwaiter().GetResult();

	QueryResult<Tuple<BaseItem, ItemCounts>> GetMusicGenres(InternalItemsQuery query) => GetMusicGenresAsync(query).GetAwaiter().GetResult();

	QueryResult<Tuple<BaseItem, ItemCounts>> GetGameGenres(InternalItemsQuery query) => GetGameGenresAsync(query).GetAwaiter().GetResult();

	QueryResult<Tuple<BaseItem, ItemCounts>> GetStudios(InternalItemsQuery query) => GetStudiosAsync(query).GetAwaiter().GetResult();

	QueryResult<Tuple<BaseItem, ItemCounts>> GetArtists(InternalItemsQuery query) => GetArtistsAsync(query).GetAwaiter().GetResult();

	QueryResult<Tuple<BaseItem, ItemCounts>> GetAlbumArtists(InternalItemsQuery query) => GetAlbumArtistsAsync(query).GetAwaiter().GetResult();

	QueryResult<Tuple<BaseItem, ItemCounts>> GetAllArtists(InternalItemsQuery query) => GetAllArtistsAsync(query).GetAwaiter().GetResult();

	List<string> GetGameGenreNames() => GetGameGenreNamesAsync().GetAwaiter().GetResult();

	List<string> GetMusicGenreNames() => GetMusicGenreNamesAsync().GetAwaiter().GetResult();

	List<string> GetStudioNames() => GetStudioNamesAsync().GetAwaiter().GetResult();

	List<string> GetGenreNames() => GetGenreNamesAsync().GetAwaiter().GetResult();

	List<string> GetAllArtistNames() => GetAllArtistNamesAsync().GetAwaiter().GetResult();
}
