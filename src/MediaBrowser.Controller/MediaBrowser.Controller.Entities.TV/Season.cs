using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Users;

namespace MediaBrowser.Controller.Entities.TV;

public class Season : Folder, IHasSeries, IHasSeriesName, IHasLookupInfo<SeasonInfo>
{
	[IgnoreDataMember]
	public override bool SupportsAddingToPlaylist => true;

	[IgnoreDataMember]
	public override bool IsPreSorted => true;

	[IgnoreDataMember]
	public override bool SupportsDateLastMediaAdded => false;

	[IgnoreDataMember]
	public override bool SupportsPeople => true;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => true;

	[IgnoreDataMember]
	public override Guid DisplayParentId => SeriesId;

	[IgnoreDataMember]
	public Series Series
	{
		get
		{
			Guid id = SeriesId;
			if (id.Equals(Guid.Empty))
			{
				id = FindSeriesId();
			}
			if (id.Equals(Guid.Empty))
			{
				return null;
			}
			return BaseItem.LibraryManager.GetItemById(id) as Series;
		}
	}

	[IgnoreDataMember]
	public string SeriesPath
	{
		get
		{
			Series series = Series;
			if (series != null)
			{
				return series.Path;
			}
			return BaseItem.FileSystem.GetDirectoryName(Path);
		}
	}

	[IgnoreDataMember]
	public string SeriesPresentationUniqueKey { get; set; }

	[IgnoreDataMember]
	public string SeriesName { get; set; }

	[IgnoreDataMember]
	public Guid SeriesId { get; set; }

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 2.0 / 3.0;
	}

	public string FindSeriesSortName()
	{
		Series series = Series;
		if (series != null)
		{
			return series.SortName;
		}
		return SeriesName;
	}

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		Series series = Series;
		if (series != null)
		{
			userDataKeys.InsertRange(0, from i in series.GetUserDataKeys()
				select i + (IndexNumber ?? 0).ToString("000"));
		}
		return userDataKeys;
	}

	public override int GetChildCount(User user)
	{
		return GetChildren(user, includeLinkedChildren: true).Count;
	}

	public override string CreatePresentationUniqueKey()
	{
		if (IndexNumber.HasValue)
		{
			Series series = Series;
			if (series != null)
			{
				return series.PresentationUniqueKey + "-" + (IndexNumber ?? 0).ToString("000");
			}
		}
		return base.CreatePresentationUniqueKey();
	}

	protected override string CreateSortName()
	{
		if (!IndexNumber.HasValue)
		{
			return Name;
		}
		return IndexNumber.Value.ToString("0000");
	}

	protected override QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)
	{
		if (query.User == null)
		{
			return base.GetItemsInternal(query);
		}
		User user = query.User;
		Func<BaseItem, bool> predicate = (BaseItem i) => UserViewBuilder.Filter(i, user, query, BaseItem.UserDataManager, BaseItem.LibraryManager);
		IEnumerable<BaseItem> items = GetEpisodes(user, query.DtoOptions).Where(predicate);
		bool enableSorting = query.OrderBy.Length != 0 && !string.Equals(query.OrderBy[0].Item1, "SortName", StringComparison.OrdinalIgnoreCase);
		return PostFilterAndSort(items, query, enableSorting);
	}

	public List<BaseItem> GetEpisodes(User user, DtoOptions options)
	{
		return GetEpisodes(Series, user, options);
	}

	public List<BaseItem> GetEpisodes(Series series, User user, DtoOptions options)
	{
		return GetEpisodes(series, user, null, options);
	}

	public List<BaseItem> GetEpisodes(Series series, User user, IEnumerable<Episode> allSeriesEpisodes, DtoOptions options)
	{
		return series.GetSeasonEpisodes(this, user, allSeriesEpisodes, options);
	}

	public List<BaseItem> GetEpisodes()
	{
		return Series.GetSeasonEpisodes(this, null, null, new DtoOptions(allFields: true));
	}

	public override List<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery query)
	{
		return GetEpisodes(user, new DtoOptions(allFields: true));
	}

	protected override bool GetBlockUnratedValue(UserPolicy config)
	{
		return false;
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)2;
	}

	public string FindSeriesPresentationUniqueKey()
	{
		return Series?.PresentationUniqueKey;
	}

	public string FindSeriesName()
	{
		Series series = Series;
		if (series != null)
		{
			return series.Name;
		}
		return SeriesName;
	}

	public Guid FindSeriesId()
	{
		return FindParent<Series>()?.Id ?? Guid.Empty;
	}

	public SeasonInfo GetLookupInfo()
	{
		SeasonInfo itemLookupInfo = GetItemLookupInfo<SeasonInfo>();
		Series series = Series;
		if (series != null)
		{
			itemLookupInfo.SeriesProviderIds = series.ProviderIds;
		}
		return itemLookupInfo;
	}

	public override bool BeforeMetadataRefresh(bool replaceAllMetdata)
	{
		bool result = base.BeforeMetadataRefresh(replaceAllMetdata);
		if (!IndexNumber.HasValue && !string.IsNullOrEmpty(Path))
		{
			IndexNumber = IndexNumber ?? BaseItem.LibraryManager.GetSeasonNumberFromPath(Path);
			if (IndexNumber.HasValue)
			{
				result = true;
			}
		}
		return result;
	}
}
