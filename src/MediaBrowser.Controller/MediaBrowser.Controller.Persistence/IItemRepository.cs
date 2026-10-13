using System;
using System.Collections.Generic;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.Persistence;

public interface IItemRepository : IRepository, IDisposable
{
	void SaveItem(BaseItem item, CancellationToken cancellationToken);

	void DeleteItem(Guid id, CancellationToken cancellationToken);

	void SaveItems(List<BaseItem> items, CancellationToken cancellationToken);

	void SaveImages(BaseItem item);

	BaseItem RetrieveItem(Guid id);

	List<ChapterInfo> GetChapters(BaseItem item);

	ChapterInfo GetChapter(BaseItem item, int index);

	void SaveChapters(Guid id, List<ChapterInfo> chapters);

	List<MediaStream> GetMediaStreams(MediaStreamQuery query);

	void SaveMediaStreams(Guid id, List<MediaStream> streams, CancellationToken cancellationToken);

	QueryResult<Guid> GetItemIds(InternalItemsQuery query);

	QueryResult<BaseItem> GetItems(InternalItemsQuery query);

	List<Guid> GetItemIdsList(InternalItemsQuery query);

	List<PersonInfo> GetPeople(InternalPeopleQuery query);

	void UpdatePeople(Guid itemId, List<PersonInfo> people);

	List<string> GetPeopleNames(InternalPeopleQuery query);

	List<Tuple<Guid, string>> GetItemIdsWithPath(InternalItemsQuery query);

	List<BaseItem> GetItemList(InternalItemsQuery query);

	void UpdateInheritedValues(CancellationToken cancellationToken);

	int GetCount(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetGenres(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetMusicGenres(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetGameGenres(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetStudios(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetArtists(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetAlbumArtists(InternalItemsQuery query);

	QueryResult<Tuple<BaseItem, ItemCounts>> GetAllArtists(InternalItemsQuery query);

	List<string> GetGameGenreNames();

	List<string> GetMusicGenreNames();

	List<string> GetStudioNames();

	List<string> GetGenreNames();

	List<string> GetAllArtistNames();
}
