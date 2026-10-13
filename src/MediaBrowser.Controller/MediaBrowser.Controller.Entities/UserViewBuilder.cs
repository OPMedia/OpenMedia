using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.Entities;

public class UserViewBuilder
{
	private readonly IUserViewManager _userViewManager;

	private readonly ILibraryManager _libraryManager;

	private readonly ILogger _logger;

	private readonly IUserDataManager _userDataManager;

	private readonly ITVSeriesManager _tvSeriesManager;

	private readonly IServerConfigurationManager _config;

	private readonly IPlaylistManager _playlistManager;

	public UserViewBuilder(IUserViewManager userViewManager, ILibraryManager libraryManager, ILogger logger, IUserDataManager userDataManager, ITVSeriesManager tvSeriesManager, IServerConfigurationManager config, IPlaylistManager playlistManager)
	{
		_userViewManager = userViewManager;
		_libraryManager = libraryManager;
		_logger = logger;
		_userDataManager = userDataManager;
		_tvSeriesManager = tvSeriesManager;
		_config = config;
		_playlistManager = playlistManager;
	}

	public QueryResult<BaseItem> GetUserItems(Folder queryParent, Folder displayParent, string viewType, InternalItemsQuery query)
	{
		User user = query.User;
		switch (viewType)
		{
		case "folders":
			return GetResult(_libraryManager.GetUserRootFolder().GetChildren(user, includeLinkedChildren: true), queryParent, query);
		case "tvshows":
			return GetTvView(queryParent, user, query);
		case "movies":
			return GetMovieFolders(queryParent, user, query);
		case "TvShowSeries":
			return GetTvSeries(queryParent, user, query);
		case "TvGenres":
			return GetTvGenres(queryParent, user, query);
		case "TvGenre":
			return GetTvGenreItems(queryParent, displayParent, user, query);
		case "TvResume":
			return GetTvResume(queryParent, user, query);
		case "TvNextUp":
			return GetTvNextUp(queryParent, query);
		case "TvLatest":
			return GetTvLatest(queryParent, user, query);
		case "MovieLatest":
			return GetMovieLatest(queryParent, user, query);
		case "MovieGenres":
			return GetMovieGenres(queryParent, user, query);
		case "MovieGenre":
			return GetMovieGenreItems(queryParent, displayParent, user, query);
		case "MovieResume":
			return GetMovieResume(queryParent, user, query);
		case "MovieMovies":
			return GetMovieMovies(queryParent, user, query);
		case "MovieCollections":
			return GetMovieCollections(queryParent, user, query);
		default:
			if (queryParent is UserView)
			{
				return GetResult(GetMediaFolders(user).OfType<Folder>().SelectMany((Folder i) => i.GetChildren(user, includeLinkedChildren: true)), queryParent, query);
			}
			return queryParent.GetItems(query);
		}
	}

	private int GetSpecialItemsLimit()
	{
		return 50;
	}

	private QueryResult<BaseItem> GetMovieFolders(Folder parent, User user, InternalItemsQuery query)
	{
		if (query.Recursive)
		{
			query.Recursive = true;
			query.SetUser(user);
			if (query.IncludeItemTypes.Length == 0)
			{
				query.IncludeItemTypes = new string[1] { typeof(Movie).Name };
			}
			return parent.QueryRecursive(query);
		}
		List<BaseItem> list = new List<BaseItem>();
		list.Add(GetUserView("MovieResume", "HeaderContinueWatching", "0", parent));
		list.Add(GetUserView("MovieLatest", "Latest", "1", parent));
		list.Add(GetUserView("MovieMovies", "Movies", "2", parent));
		list.Add(GetUserView("MovieCollections", "Collections", "3", parent));
		list.Add(GetUserView("MovieGenres", "Genres", "5", parent));
		return GetResult(list, parent, query);
	}

	private QueryResult<BaseItem> GetFavoriteMovies(Folder parent, User user, InternalItemsQuery query)
	{
		query.Recursive = true;
		query.Parent = parent;
		query.SetUser(user);
		query.IsFavorite = true;
		query.IncludeItemTypes = new string[1] { typeof(Movie).Name };
		return _libraryManager.GetItemsResult(query);
	}

	private QueryResult<BaseItem> GetMovieMovies(Folder parent, User user, InternalItemsQuery query)
	{
		query.Recursive = true;
		query.Parent = parent;
		query.SetUser(user);
		query.IncludeItemTypes = new string[1] { typeof(Movie).Name };
		return _libraryManager.GetItemsResult(query);
	}

	private QueryResult<BaseItem> GetMovieCollections(Folder parent, User user, InternalItemsQuery query)
	{
		query.Parent = null;
		query.IncludeItemTypes = new string[1] { typeof(BoxSet).Name };
		query.SetUser(user);
		query.Recursive = true;
		return _libraryManager.GetItemsResult(query);
	}

	private QueryResult<BaseItem> GetMovieLatest(Folder parent, User user, InternalItemsQuery query)
	{
		query.OrderBy = new string[2] { "DateCreated", "SortName" }.Select((string i) => (i, (SortOrder)1)).ToArray();
		query.Recursive = true;
		query.Parent = parent;
		query.SetUser(user);
		query.Limit = GetSpecialItemsLimit();
		query.IncludeItemTypes = new string[1] { typeof(Movie).Name };
		return ConvertToResult(_libraryManager.GetItemList(query));
	}

	private QueryResult<BaseItem> GetMovieResume(Folder parent, User user, InternalItemsQuery query)
	{
		query.OrderBy = new string[2] { "DatePlayed", "SortName" }.Select((string i) => (i, (SortOrder)1)).ToArray();
		query.IsResumable = true;
		query.Recursive = true;
		query.Parent = parent;
		query.SetUser(user);
		query.Limit = GetSpecialItemsLimit();
		query.IncludeItemTypes = new string[1] { typeof(Movie).Name };
		return ConvertToResult(_libraryManager.GetItemList(query));
	}

	private QueryResult<BaseItem> ConvertToResult(List<BaseItem> items)
	{
		BaseItem[] array = items.ToArray();
		return new QueryResult<BaseItem>
		{
			Items = array,
			TotalRecordCount = array.Length
		};
	}

	private QueryResult<BaseItem> GetMovieGenres(Folder parent, User user, InternalItemsQuery query)
	{
		IEnumerable<UserView> items = from i in parent.QueryRecursive(new InternalItemsQuery(user)
			{
				IncludeItemTypes = new string[1] { typeof(Movie).Name },
				Recursive = true,
				EnableTotalRecordCount = false
			}).Items.SelectMany((BaseItem i) => i.Genres).DistinctNames().Select((string i) =>
			{
				try
				{
					return _libraryManager.GetGenre(i);
				}
				catch
				{
					_logger.Error("Error getting genre", Array.Empty<object>());
					return (Genre)null;
				}
			})
			where i != null
			select GetUserViewWithName(i.Name, "MovieGenre", i.SortName, parent);
		return GetResult(items, parent, query);
	}

	private QueryResult<BaseItem> GetMovieGenreItems(Folder queryParent, Folder displayParent, User user, InternalItemsQuery query)
	{
		query.Recursive = true;
		query.Parent = queryParent;
		query.GenreIds = new Guid[1] { displayParent.Id };
		query.SetUser(user);
		query.IncludeItemTypes = new string[1] { typeof(Movie).Name };
		return _libraryManager.GetItemsResult(query);
	}

	private QueryResult<BaseItem> GetTvView(Folder parent, User user, InternalItemsQuery query)
	{
		if (query.Recursive)
		{
			query.Recursive = true;
			query.SetUser(user);
			if (query.IncludeItemTypes.Length == 0)
			{
				query.IncludeItemTypes = new string[3]
				{
					typeof(Series).Name,
					typeof(Season).Name,
					typeof(Episode).Name
				};
			}
			return parent.QueryRecursive(query);
		}
		List<BaseItem> list = new List<BaseItem>();
		list.Add(GetUserView("TvResume", "HeaderContinueWatching", "0", parent));
		list.Add(GetUserView("TvNextUp", "HeaderNextUp", "1", parent));
		list.Add(GetUserView("TvLatest", "Latest", "2", parent));
		list.Add(GetUserView("TvShowSeries", "Shows", "3", parent));
		list.Add(GetUserView("TvGenres", "Genres", "6", parent));
		return GetResult(list, parent, query);
	}

	private QueryResult<BaseItem> GetTvLatest(Folder parent, User user, InternalItemsQuery query)
	{
		query.OrderBy = new string[2] { "DateCreated", "SortName" }.Select((string i) => (i, (SortOrder)1)).ToArray();
		query.Recursive = true;
		query.Parent = parent;
		query.SetUser(user);
		query.Limit = GetSpecialItemsLimit();
		query.IncludeItemTypes = new string[1] { typeof(Episode).Name };
		query.IsVirtualItem = false;
		return ConvertToResult(_libraryManager.GetItemList(query));
	}

	private QueryResult<BaseItem> GetTvNextUp(Folder parent, InternalItemsQuery query)
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Expected Obj, but got Unknown
		BaseItem[] mediaFolders = GetMediaFolders(parent, query.User, new string[2]
		{
			"tvshows",
			string.Empty
		});
		return _tvSeriesManager.GetNextUp(new NextUpQuery
		{
			Limit = query.Limit,
			StartIndex = query.StartIndex,
			UserId = query.User.Id
		}, mediaFolders, query.DtoOptions);
	}

	private QueryResult<BaseItem> GetTvResume(Folder parent, User user, InternalItemsQuery query)
	{
		query.OrderBy = new string[2] { "DatePlayed", "SortName" }.Select((string i) => (i, (SortOrder)1)).ToArray();
		query.IsResumable = true;
		query.Recursive = true;
		query.Parent = parent;
		query.SetUser(user);
		query.Limit = GetSpecialItemsLimit();
		query.IncludeItemTypes = new string[1] { typeof(Episode).Name };
		return ConvertToResult(_libraryManager.GetItemList(query));
	}

	private QueryResult<BaseItem> GetTvSeries(Folder parent, User user, InternalItemsQuery query)
	{
		query.Recursive = true;
		query.Parent = parent;
		query.SetUser(user);
		query.IncludeItemTypes = new string[1] { typeof(Series).Name };
		return _libraryManager.GetItemsResult(query);
	}

	private QueryResult<BaseItem> GetTvGenres(Folder parent, User user, InternalItemsQuery query)
	{
		IEnumerable<UserView> items = from i in parent.QueryRecursive(new InternalItemsQuery(user)
			{
				IncludeItemTypes = new string[1] { typeof(Series).Name },
				Recursive = true,
				EnableTotalRecordCount = false
			}).Items.SelectMany((BaseItem i) => i.Genres).DistinctNames().Select((string i) =>
			{
				try
				{
					return _libraryManager.GetGenre(i);
				}
				catch
				{
					_logger.Error("Error getting genre", Array.Empty<object>());
					return (Genre)null;
				}
			})
			where i != null
			select GetUserViewWithName(i.Name, "TvGenre", i.SortName, parent);
		return GetResult(items, parent, query);
	}

	private QueryResult<BaseItem> GetTvGenreItems(Folder queryParent, Folder displayParent, User user, InternalItemsQuery query)
	{
		query.Recursive = true;
		query.Parent = queryParent;
		query.GenreIds = new Guid[1] { displayParent.Id };
		query.SetUser(user);
		query.IncludeItemTypes = new string[1] { typeof(Series).Name };
		return _libraryManager.GetItemsResult(query);
	}

	private QueryResult<BaseItem> GetResult<T>(QueryResult<T> result) where T : BaseItem
	{
		QueryResult<BaseItem> val = new QueryResult<BaseItem>();
		BaseItem[] items = result.Items;
		val.Items = items;
		val.TotalRecordCount = result.TotalRecordCount;
		return val;
	}

	private QueryResult<BaseItem> GetResult<T>(IEnumerable<T> items, BaseItem queryParent, InternalItemsQuery query) where T : BaseItem
	{
		items = items.Where((T i) => Filter(i, query.User, query, _userDataManager, _libraryManager));
		return SortAndPage(items, null, query, _libraryManager, enableSorting: true);
	}

	public static bool FilterItem(BaseItem item, InternalItemsQuery query)
	{
		return Filter(item, query.User, query, BaseItem.UserDataManager, BaseItem.LibraryManager);
	}

	public static QueryResult<BaseItem> SortAndPage(IEnumerable<BaseItem> items, int? totalRecordLimit, InternalItemsQuery query, ILibraryManager libraryManager, bool enableSorting)
	{
		if (enableSorting && query.OrderBy.Length != 0)
		{
			items = libraryManager.Sort(items, query.User, query.OrderBy);
		}
		BaseItem[] array = (totalRecordLimit.HasValue ? items.Take(totalRecordLimit.Value).ToArray() : items.ToArray());
		int totalRecordCount = array.Length;
		if (query.Limit.HasValue)
		{
			array = array.Skip(query.StartIndex ?? 0).Take(query.Limit.Value).ToArray();
		}
		else if (query.StartIndex.HasValue)
		{
			array = array.Skip(query.StartIndex.Value).ToArray();
		}
		return new QueryResult<BaseItem>
		{
			TotalRecordCount = totalRecordCount,
			Items = array
		};
	}

	public static bool Filter(BaseItem item, User user, InternalItemsQuery query, IUserDataManager userDataManager, ILibraryManager libraryManager)
	{
		//IL_0896: Unknown result type (might be due to invalid IL or missing references)
		if (query.MediaTypes.Length != 0 && !query.MediaTypes.Contains(item.MediaType ?? string.Empty, StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}
		if (query.IncludeItemTypes.Length != 0 && !query.IncludeItemTypes.Contains(item.GetClientTypeName(), StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}
		if (query.ExcludeItemTypes.Length != 0 && query.ExcludeItemTypes.Contains(item.GetClientTypeName(), StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}
		if (query.IsVirtualItem.HasValue && item.IsVirtualItem != query.IsVirtualItem.Value)
		{
			return false;
		}
		if (query.IsFolder.HasValue && query.IsFolder.Value != item.IsFolder)
		{
			return false;
		}
		UserItemData userItemData = null;
		if (query.IsLiked.HasValue)
		{
			userItemData = userItemData ?? userDataManager.GetUserData(user, item);
			if (!userItemData.Likes.HasValue || userItemData.Likes != query.IsLiked.Value)
			{
				return false;
			}
		}
		if (query.IsFavoriteOrLiked.HasValue)
		{
			userItemData = userItemData ?? userDataManager.GetUserData(user, item);
			if ((userItemData.IsFavorite || (userItemData.Likes ?? false)) != query.IsFavoriteOrLiked.Value)
			{
				return false;
			}
		}
		if (query.IsFavorite.HasValue)
		{
			userItemData = userItemData ?? userDataManager.GetUserData(user, item);
			if (userItemData.IsFavorite != query.IsFavorite.Value)
			{
				return false;
			}
		}
		if (query.IsResumable.HasValue)
		{
			userItemData = userItemData ?? userDataManager.GetUserData(user, item);
			if (userItemData.PlaybackPositionTicks > 0 != query.IsResumable.Value)
			{
				return false;
			}
		}
		if (query.IsPlayed.HasValue && item.IsPlayed(user) != query.IsPlayed.Value)
		{
			return false;
		}
		if (query.Is3D.HasValue)
		{
			bool value = query.Is3D.Value;
			if (!(item is Video video) || value != video.Video3DFormat.HasValue)
			{
				return false;
			}
		}
		if (query.MinWidth.HasValue && item.Width < query.MinWidth.Value)
		{
			return false;
		}
		if (query.MinHeight.HasValue && item.Height < query.MinHeight.Value)
		{
			return false;
		}
		if (query.MaxWidth.HasValue && item.Width > query.MaxWidth.Value)
		{
			return false;
		}
		if (query.MaxHeight.HasValue && item.Height > query.MaxHeight.Value)
		{
			return false;
		}
		if (query.IsHD.HasValue && query.IsHD.Value != item.IsHD)
		{
			return false;
		}
		if (query.Is4K.HasValue && query.Is4K.Value != item.Width >= 3800)
		{
			return false;
		}
		if (query.IsLocked.HasValue)
		{
			bool value2 = query.IsLocked.Value;
			if (item.IsLocked != value2)
			{
				return false;
			}
		}
		if (query.HasOverview.HasValue)
		{
			bool value3 = query.HasOverview.Value;
			if (!string.IsNullOrEmpty(item.Overview) != value3)
			{
				return false;
			}
		}
		if (query.HasImdbId.HasValue)
		{
			bool value4 = query.HasImdbId.Value;
			if (!string.IsNullOrEmpty(ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)item, (MetadataProviders)2)) != value4)
			{
				return false;
			}
		}
		if (query.HasTmdbId.HasValue)
		{
			bool value5 = query.HasTmdbId.Value;
			if (!string.IsNullOrEmpty(ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)item, (MetadataProviders)3)) != value5)
			{
				return false;
			}
		}
		if (query.HasTvdbId.HasValue)
		{
			bool value6 = query.HasTvdbId.Value;
			if (!string.IsNullOrEmpty(ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)item, (MetadataProviders)4)) != value6)
			{
				return false;
			}
		}
		if (query.HasOfficialRating.HasValue)
		{
			bool value7 = query.HasOfficialRating.Value;
			if (!string.IsNullOrEmpty(item.OfficialRating) != value7)
			{
				return false;
			}
		}
		if (query.IsPlaceHolder.HasValue)
		{
			bool value8 = query.IsPlaceHolder.Value;
			bool flag = false;
			if (item is ISupportsPlaceHolders supportsPlaceHolders)
			{
				flag = supportsPlaceHolders.IsPlaceHolder;
			}
			if (flag != value8)
			{
				return false;
			}
		}
		if (query.HasSpecialFeature.HasValue && !(query.HasSpecialFeature.Value ? (item.GetDisplayExtras().Count() > 0) : (item.GetDisplayExtras().Count() == 0)))
		{
			return false;
		}
		if (query.HasSubtitles.HasValue)
		{
			bool value9 = query.HasSubtitles.Value;
			if (!(item is Video video2) || value9 != video2.HasSubtitles)
			{
				return false;
			}
		}
		if (query.HasParentalRating.HasValue)
		{
			bool value10 = query.HasParentalRating.Value;
			string value11 = item.CustomRating;
			if (string.IsNullOrEmpty(value11))
			{
				value11 = item.OfficialRating;
			}
			if (value10)
			{
				if (string.IsNullOrEmpty(value11))
				{
					return false;
				}
			}
			else if (!string.IsNullOrEmpty(value11))
			{
				return false;
			}
		}
		if (query.HasTrailer.HasValue)
		{
			bool value12 = query.HasTrailer.Value;
			int num = item.GetExtras(new ExtraType[1] { (ExtraType)2 }).Count();
			if (!(value12 ? (num > 0) : (num == 0)))
			{
				return false;
			}
		}
		if (query.HasThemeSong.HasValue)
		{
			bool value13 = query.HasThemeSong.Value;
			int num2 = item.ThemeSongIds.Length;
			if (!(value13 ? (num2 > 0) : (num2 == 0)))
			{
				return false;
			}
		}
		if (query.HasThemeVideo.HasValue)
		{
			bool value14 = query.HasThemeVideo.Value;
			int num3 = item.ThemeVideoIds.Length;
			if (!(value14 ? (num3 > 0) : (num3 == 0)))
			{
				return false;
			}
		}
		if (query.Genres.Length != 0 && !query.Genres.Any((string v) => item.Genres.Contains(v, StringComparer.OrdinalIgnoreCase)))
		{
			return false;
		}
		if (query.VideoTypes.Length != 0 && (!(item is Video video3) || !Enumerable.Contains(query.VideoTypes, video3.VideoType)))
		{
			return false;
		}
		if (query.ImageTypes.Length != 0 && !query.ImageTypes.Any(item.HasImage))
		{
			return false;
		}
		if (query.StudioIds.Length != 0 && !query.StudioIds.Any((Guid id) =>
		{
			BaseItem itemById = libraryManager.GetItemById(id);
			return itemById != null && item.Studios.Contains(itemById.Name, StringComparer.OrdinalIgnoreCase);
		}))
		{
			return false;
		}
		if (query.GenreIds.Length != 0 && !query.GenreIds.Any((Guid id) =>
		{
			BaseItem itemById = libraryManager.GetItemById(id);
			return itemById != null && item.Genres.Contains(itemById.Name, StringComparer.OrdinalIgnoreCase);
		}))
		{
			return false;
		}
		if (query.Years.Length != 0 && (!item.ProductionYear.HasValue || !Enumerable.Contains(query.Years, item.ProductionYear.Value)))
		{
			return false;
		}
		if (query.OfficialRatings.Length != 0 && !Enumerable.Contains(query.OfficialRatings, item.OfficialRating ?? string.Empty))
		{
			return false;
		}
		if (query.ItemIds.Length != 0 && !Enumerable.Contains(query.ItemIds, item.Id))
		{
			return false;
		}
		string[] tags = query.Tags;
		if (tags.Length != 0 && !tags.Any((string v) => item.Tags.Contains(v, StringComparer.OrdinalIgnoreCase)))
		{
			return false;
		}
		if (query.MinPlayers.HasValue)
		{
			int value15 = query.MinPlayers.Value;
			if (!(item is Game { PlayersSupported: var playersSupported }))
			{
				return false;
			}
			if ((playersSupported ?? 1) < value15)
			{
				return false;
			}
		}
		if (query.MaxPlayers.HasValue)
		{
			int value16 = query.MaxPlayers.Value;
			if (!(item is Game { PlayersSupported: var playersSupported2 }))
			{
				return false;
			}
			if ((playersSupported2 ?? 1) > value16)
			{
				return false;
			}
		}
		if (query.MinCommunityRating.HasValue)
		{
			double value17 = query.MinCommunityRating.Value;
			if (!item.CommunityRating.HasValue || !((double?)item.CommunityRating >= value17))
			{
				return false;
			}
		}
		if (query.MinCriticRating.HasValue)
		{
			double value18 = query.MinCriticRating.Value;
			if (!item.CriticRating.HasValue || !((double?)item.CriticRating >= value18))
			{
				return false;
			}
		}
		if (query.MinIndexNumber.HasValue)
		{
			int value19 = query.MinIndexNumber.Value;
			if (!item.IndexNumber.HasValue || item.IndexNumber.Value < value19)
			{
				return false;
			}
		}
		if (query.MinPremiereDate.HasValue)
		{
			DateTime value20 = query.MinPremiereDate.Value;
			if (!item.PremiereDate.HasValue || !(item.PremiereDate.Value >= value20))
			{
				return false;
			}
		}
		if (query.MaxPremiereDate.HasValue)
		{
			DateTime value21 = query.MaxPremiereDate.Value;
			if (!item.PremiereDate.HasValue || !(item.PremiereDate.Value <= value21))
			{
				return false;
			}
		}
		if (query.ParentIndexNumber.HasValue)
		{
			int value22 = query.ParentIndexNumber.Value;
			if (item.ParentIndexNumber.HasValue && item.ParentIndexNumber.Value != value22)
			{
				return false;
			}
		}
		if (query.SeriesStatuses.Length != 0 && !new BaseItem[1] { item }.OfType<Series>().Any((Series p) =>
		{
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			return p.Status.HasValue && Enumerable.Contains(query.SeriesStatuses, p.Status.Value);
		}))
		{
			return false;
		}
		return true;
	}

	private IEnumerable<BaseItem> GetMediaFolders(User user)
	{
		if (user == null)
		{
			return _libraryManager.RootFolder.Children.OfType<Folder>().Where(UserView.IsEligibleForGrouping);
		}
		return from i in _libraryManager.GetUserRootFolder().GetChildren(user, includeLinkedChildren: true).OfType<Folder>()
			where user.IsFolderGrouped(i.Id) && UserView.IsEligibleForGrouping(i)
			select i;
	}

	private BaseItem[] GetMediaFolders(User user, IEnumerable<string> viewTypes)
	{
		if (user == null)
		{
			return (from i in GetMediaFolders(null)
				where i is ICollectionFolder collectionFolder && viewTypes.Contains(collectionFolder.CollectionType ?? string.Empty, StringComparer.OrdinalIgnoreCase)
				select i).ToArray();
		}
		return (from i in GetMediaFolders(user)
			where i is ICollectionFolder collectionFolder && viewTypes.Contains(collectionFolder.CollectionType ?? string.Empty, StringComparer.OrdinalIgnoreCase)
			select i).ToArray();
	}

	private BaseItem[] GetMediaFolders(Folder parent, User user, IEnumerable<string> viewTypes)
	{
		if (parent == null || parent is UserView)
		{
			return GetMediaFolders(user, viewTypes);
		}
		return new BaseItem[1] { parent };
	}

	private UserView GetUserViewWithName(string name, string type, string sortName, BaseItem parent)
	{
		return _userViewManager.GetUserSubView(parent.Id, parent.Id.ToString("N"), type, sortName);
	}

	private UserView GetUserView(string type, string localizationKey, string sortName, BaseItem parent)
	{
		return _userViewManager.GetUserSubView(parent.Id, type, localizationKey, sortName);
	}

	public static IEnumerable<BaseItem> FilterForAdjacency(List<BaseItem> list, string adjacentToId)
	{
		Guid adjacentToIdGuid = new Guid(adjacentToId);
		BaseItem item = list.FirstOrDefault((BaseItem i) => i.Id == adjacentToIdGuid);
		int num = list.IndexOf(item);
		Guid previousId = Guid.Empty;
		Guid nextId = Guid.Empty;
		if (num > 0)
		{
			previousId = list[num - 1].Id;
		}
		if (num < list.Count - 1)
		{
			nextId = list[num + 1].Id;
		}
		return list.Where((BaseItem i) => i.Id == previousId || i.Id == nextId || i.Id == adjacentToIdGuid);
	}
}
