using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Users;

namespace MediaBrowser.Controller.Entities.TV;

public class Series : Folder, IHasDisplayOrder, IHasLookupInfo<SeriesInfo>, IMetadataContainer
{
	public DayOfWeek[] AirDays { get; set; }

	public string AirTime { get; set; }

	[IgnoreDataMember]
	public override bool SupportsAddingToPlaylist => true;

	[IgnoreDataMember]
	public override bool IsPreSorted => true;

	[IgnoreDataMember]
	public override bool SupportsLocalTrailers => true;

	[IgnoreDataMember]
	public override bool SupportsDateLastMediaAdded => true;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => false;

	[IgnoreDataMember]
	public override bool SupportsPeople => true;

	public string DisplayOrder { get; set; }

	public SeriesStatus? Status { get; set; }

	[IgnoreDataMember]
	public override bool StopRefreshIfLocalMetadataFound => false;

	public Series()
	{
		AirDays = new DayOfWeek[0];
	}

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 2.0 / 3.0;
	}

	public override string CreatePresentationUniqueKey()
	{
		if (BaseItem.LibraryManager.GetLibraryOptions(this).EnableAutomaticSeriesGrouping)
		{
			List<string> userDataKeys = GetUserDataKeys();
			if (userDataKeys.Count > 1)
			{
				return AddLibrariesToPresentationUniqueKey(userDataKeys[0]);
			}
		}
		return base.CreatePresentationUniqueKey();
	}

	private string AddLibrariesToPresentationUniqueKey(string key)
	{
		string preferredMetadataLanguage = GetPreferredMetadataLanguage();
		if (!string.IsNullOrEmpty(preferredMetadataLanguage))
		{
			key = key + "-" + preferredMetadataLanguage;
		}
		string[] array = (from i in BaseItem.LibraryManager.GetCollectionFolders(this)
			select i.Id.ToString("N")).ToArray();
		if (array.Length == 0)
		{
			return key;
		}
		return key + "-" + string.Join("-", array);
	}

	private static string GetUniqueSeriesKey(BaseItem series)
	{
		return series.GetPresentationUniqueKey();
	}

	public override int GetChildCount(User user)
	{
		string uniqueSeriesKey = GetUniqueSeriesKey(this);
		return BaseItem.LibraryManager.GetCount(new InternalItemsQuery(user)
		{
			AncestorWithPresentationUniqueKey = null,
			SeriesPresentationUniqueKey = uniqueSeriesKey,
			IncludeItemTypes = new string[1] { typeof(Season).Name },
			IsVirtualItem = false,
			Limit = 0,
			DtoOptions = new DtoOptions(allFields: false)
			{
				EnableImages = false
			}
		});
	}

	public override int GetRecursiveChildCount(User user)
	{
		// P7 批次：方法级 TTL 缓存（与 Folder 基类同款——本重写走 GetCount 路径，
		// 基类缓存覆盖不到；trace 实测 Series 的 count-per-folder 是 12 条大头之一）
		var cacheKey = (user == null ? string.Empty : user.Id.ToString("N")) + "|" + Id.ToString("N");

		lock (RecursiveChildCountSeriesCacheLock)
		{
			if (RecursiveChildCountSeriesCache.TryGetValue(cacheKey, out var cached)
				&& (DateTime.UtcNow - cached.Item2) < RecursiveChildCountSeriesCacheTtl)
			{
				return cached.Item1;
			}
		}

		string uniqueSeriesKey = GetUniqueSeriesKey(this);
		InternalItemsQuery internalItemsQuery = new InternalItemsQuery(user)
		{
			AncestorWithPresentationUniqueKey = null,
			SeriesPresentationUniqueKey = uniqueSeriesKey,
			DtoOptions = new DtoOptions(allFields: false)
			{
				EnableImages = false
			}
		};
		if (internalItemsQuery.IncludeItemTypes.Length == 0)
		{
			internalItemsQuery.IncludeItemTypes = new string[1] { typeof(Episode).Name };
		}
		internalItemsQuery.IsVirtualItem = false;
		internalItemsQuery.Limit = 0;
		var count = BaseItem.LibraryManager.GetCount(internalItemsQuery);

		lock (RecursiveChildCountSeriesCacheLock)
		{
			RecursiveChildCountSeriesCache[cacheKey] = new Tuple<int, DateTime>(count, DateTime.UtcNow);
		}

		return count;
	}

	private static readonly Dictionary<string, Tuple<int, DateTime>> RecursiveChildCountSeriesCache =
		new Dictionary<string, Tuple<int, DateTime>>(StringComparer.OrdinalIgnoreCase);
	private static readonly object RecursiveChildCountSeriesCacheLock = new object();
	private static readonly TimeSpan RecursiveChildCountSeriesCacheTtl = TimeSpan.FromSeconds(30);

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		string providerId = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)2);
		if (!string.IsNullOrEmpty(providerId))
		{
			userDataKeys.Insert(0, providerId);
		}
		providerId = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)4);
		if (!string.IsNullOrEmpty(providerId))
		{
			userDataKeys.Insert(0, providerId);
		}
		return userDataKeys;
	}

	public override List<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery query)
	{
		return GetSeasons(user, new DtoOptions(allFields: true));
	}

	public List<BaseItem> GetSeasons(User user, DtoOptions options)
	{
		InternalItemsQuery query = new InternalItemsQuery(user)
		{
			DtoOptions = options
		};
		SetSeasonQueryOptions(query, user);
		return BaseItem.LibraryManager.GetItemList(query);
	}

	private void SetSeasonQueryOptions(InternalItemsQuery query, User user)
	{
		string uniqueSeriesKey = GetUniqueSeriesKey(this);
		query.AncestorWithPresentationUniqueKey = null;
		query.SeriesPresentationUniqueKey = uniqueSeriesKey;
		query.IncludeItemTypes = new string[1] { typeof(Season).Name };
		query.OrderBy = new string[1] { "SortName" }.Select((string i) => (i, (SortOrder)0)).ToArray();
		if (user != null && !user.Configuration.DisplayMissingEpisodes)
		{
			query.IsMissing = false;
		}
	}

	protected override QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)
	{
		User user = query.User;
		if (query.Recursive)
		{
			string uniqueSeriesKey = GetUniqueSeriesKey(this);
			query.AncestorWithPresentationUniqueKey = null;
			query.SeriesPresentationUniqueKey = uniqueSeriesKey;
			if (query.OrderBy.Length == 0)
			{
				query.OrderBy = new string[1] { "SortName" }.Select((string i) => (i, (SortOrder)0)).ToArray();
			}
			if (query.IncludeItemTypes.Length == 0)
			{
				query.IncludeItemTypes = new string[2]
				{
					typeof(Episode).Name,
					typeof(Season).Name
				};
			}
			query.IsVirtualItem = false;
			return BaseItem.LibraryManager.GetItemsResult(query);
		}
		SetSeasonQueryOptions(query, user);
		return BaseItem.LibraryManager.GetItemsResult(query);
	}

	public IEnumerable<BaseItem> GetEpisodes(User user, DtoOptions options)
	{
		string uniqueSeriesKey = GetUniqueSeriesKey(this);
		InternalItemsQuery internalItemsQuery = new InternalItemsQuery(user);
		internalItemsQuery.AncestorWithPresentationUniqueKey = null;
		internalItemsQuery.SeriesPresentationUniqueKey = uniqueSeriesKey;
		internalItemsQuery.IncludeItemTypes = new string[2]
		{
			typeof(Episode).Name,
			typeof(Season).Name
		};
		internalItemsQuery.OrderBy = new string[1] { "SortName" }.Select((string i) => (i, (SortOrder)0)).ToArray();
		internalItemsQuery.DtoOptions = options;
		InternalItemsQuery internalItemsQuery2 = internalItemsQuery;
		if (!user.Configuration.DisplayMissingEpisodes)
		{
			internalItemsQuery2.IsMissing = false;
		}
		List<BaseItem> itemList = BaseItem.LibraryManager.GetItemList(internalItemsQuery2);
		List<Episode> allSeriesEpisodes = itemList.OfType<Episode>().ToList();
		return Enumerable.DistinctBy<BaseItem, Guid>(itemList.OfType<Season>().SelectMany((Season i) => i.GetEpisodes(this, user, allSeriesEpisodes, options)).Reverse(), (Func<BaseItem, Guid>)((BaseItem i) => i.Id)).Reverse();
	}

	public async Task RefreshAllMetadata(MetadataRefreshOptions refreshOptions, IProgress<double> progress, CancellationToken cancellationToken)
	{
		IList<BaseItem> items = GetRecursiveChildren();
		int totalItems = items.Count;
		int numComplete = 0;
		foreach (BaseItem item in items)
		{
			if (item is Season)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (refreshOptions.RefreshItem(item))
				{
					await item.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				numComplete++;
				double num = numComplete;
				num /= (double)totalItems;
				progress.Report(num * 100.0);
			}
		}
		foreach (BaseItem item2 in items)
		{
			if (!(item2 is Season))
			{
				cancellationToken.ThrowIfCancellationRequested();
				bool flag = false;
				if (item2 is Episode episode && refreshOptions.MetadataRefreshMode != MetadataRefreshMode.FullRefresh && !refreshOptions.ReplaceAllMetadata && episode.IsMissingEpisode && (int)episode.LocationType == 2 && episode.PremiereDate.HasValue && (DateTime.UtcNow - episode.PremiereDate.Value).TotalDays > 30.0)
				{
					flag = true;
				}
				if (!flag && refreshOptions.RefreshItem(item2))
				{
					await item2.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				numComplete++;
				double num2 = numComplete;
				num2 /= (double)totalItems;
				progress.Report(num2 * 100.0);
			}
		}
		refreshOptions = new MetadataRefreshOptions(refreshOptions);
		await BaseItem.ProviderManager.RefreshSingleItem(this, refreshOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	public List<BaseItem> GetSeasonEpisodes(Season parentSeason, User user, DtoOptions options)
	{
		bool displaySpecialsWithinSeasons = BaseItem.ConfigurationManager.Configuration.DisplaySpecialsWithinSeasons;
		string text = (displaySpecialsWithinSeasons ? GetUniqueSeriesKey(this) : GetUniqueSeriesKey(parentSeason));
		InternalItemsQuery internalItemsQuery = new InternalItemsQuery(user);
		internalItemsQuery.AncestorWithPresentationUniqueKey = (displaySpecialsWithinSeasons ? null : text);
		internalItemsQuery.SeriesPresentationUniqueKey = (displaySpecialsWithinSeasons ? text : null);
		internalItemsQuery.IncludeItemTypes = new string[1] { typeof(Episode).Name };
		internalItemsQuery.OrderBy = new string[1] { "SortName" }.Select((string i) => (i, (SortOrder)0)).ToArray();
		internalItemsQuery.DtoOptions = options;
		InternalItemsQuery internalItemsQuery2 = internalItemsQuery;
		if (user != null && !user.Configuration.DisplayMissingEpisodes)
		{
			internalItemsQuery2.IsMissing = false;
		}
		List<BaseItem> itemList = BaseItem.LibraryManager.GetItemList(internalItemsQuery2);
		return GetSeasonEpisodes(parentSeason, user, itemList, options);
	}

	public List<BaseItem> GetSeasonEpisodes(Season parentSeason, User user, IEnumerable<BaseItem> allSeriesEpisodes, DtoOptions options)
	{
		if (allSeriesEpisodes == null)
		{
			return GetSeasonEpisodes(parentSeason, user, options);
		}
		IEnumerable<BaseItem> items = FilterEpisodesBySeason(allSeriesEpisodes, parentSeason, BaseItem.ConfigurationManager.Configuration.DisplaySpecialsWithinSeasons);
		string text = (((parentSeason.IndexNumber ?? (-1)) == 0) ? "SortName" : "AiredEpisodeOrder");
		return BaseItem.LibraryManager.Sort(items, user, new string[1] { text }, (SortOrder)0).ToList();
	}

	public static IEnumerable<BaseItem> FilterEpisodesBySeason(IEnumerable<BaseItem> episodes, Season parentSeason, bool includeSpecials)
	{
		int? seasonNumber = parentSeason.IndexNumber;
		string seasonPresentationKey = GetUniqueSeriesKey(parentSeason);
		bool supportSpecialsInSeason = includeSpecials && seasonNumber.HasValue && seasonNumber.Value != 0;
		return episodes.Where((BaseItem episode) =>
		{
			//IL_0066: Unknown result type (might be due to invalid IL or missing references)
			//IL_006c: Invalid comparison between Unknown and I4
			Episode episode2 = (Episode)episode;
			int? num = (supportSpecialsInSeason ? episode2.AiredSeasonNumber : episode.ParentIndexNumber);
			if (num.HasValue && seasonNumber.HasValue && num.Value == seasonNumber.Value)
			{
				return true;
			}
			if (!num.HasValue && !seasonNumber.HasValue && (int)parentSeason.LocationType == 2)
			{
				return true;
			}
			Season season = episode2.Season;
			return season != null && string.Equals(GetUniqueSeriesKey(season), seasonPresentationKey, StringComparison.OrdinalIgnoreCase);
		});
	}

	public static IEnumerable<Episode> FilterEpisodesBySeason(IEnumerable<Episode> episodes, int seasonNumber, bool includeSpecials)
	{
		if (!includeSpecials || seasonNumber < 1)
		{
			return episodes.Where((Episode i) => (i.ParentIndexNumber ?? (-1)) == seasonNumber);
		}
		return episodes.Where((Episode i) =>
		{
			if (i != null)
			{
				int? airedSeasonNumber = i.AiredSeasonNumber;
				if (airedSeasonNumber.HasValue)
				{
					return airedSeasonNumber.Value == seasonNumber;
				}
				return false;
			}
			return false;
		});
	}

	protected override bool GetBlockUnratedValue(UserPolicy config)
	{
		return Enumerable.Contains(config.BlockUnratedItems, (UnratedItem)2);
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)2;
	}

	public SeriesInfo GetLookupInfo()
	{
		return GetItemLookupInfo<SeriesInfo>();
	}

	public override bool BeforeMetadataRefresh(bool replaceAllMetadata)
	{
		bool result = base.BeforeMetadataRefresh(replaceAllMetadata);
		if (!ProductionYear.HasValue)
		{
			int? year = BaseItem.LibraryManager.ParseName(Name).Year;
			if (year.HasValue)
			{
				ProductionYear = year;
				result = true;
			}
		}
		return result;
	}

	public override List<ExternalUrl> GetRelatedUrls()
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Expected Obj, but got Unknown
		List<ExternalUrl> relatedUrls = base.GetRelatedUrls();
		string providerId = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)2);
		if (!string.IsNullOrEmpty(providerId))
		{
			relatedUrls.Add(new ExternalUrl
			{
				Name = "Trakt",
				Url = $"https://trakt.tv/shows/{providerId}"
			});
		}
		return relatedUrls;
	}
}
