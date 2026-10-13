using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities.TV;

public class Episode : Video, IHasLookupInfo<EpisodeInfo>, IHasSeries, IHasSeriesName
{
	public int? AirsBeforeSeasonNumber { get; set; }

	public int? AirsAfterSeasonNumber { get; set; }

	public int? AirsBeforeEpisodeNumber { get; set; }

	public int? IndexNumberEnd { get; set; }

	[IgnoreDataMember]
	protected override bool SupportsOwnedItems
	{
		get
		{
			if (!IsStacked)
			{
				return MediaSourceCount > 1;
			}
			return true;
		}
	}

	[IgnoreDataMember]
	public override bool SupportsLocalTrailers => true;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => true;

	[IgnoreDataMember]
	public override bool SupportsPeople => true;

	[IgnoreDataMember]
	public int? AiredSeasonNumber => AirsAfterSeasonNumber ?? AirsBeforeSeasonNumber ?? ParentIndexNumber;

	[IgnoreDataMember]
	public override Folder LatestItemsIndexContainer => Series;

	[IgnoreDataMember]
	public override Guid DisplayParentId => SeasonId;

	[IgnoreDataMember]
	protected override bool EnableDefaultVideoUserDataKeys => false;

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
	public Season Season
	{
		get
		{
			Guid id = SeasonId;
			if (id.Equals(Guid.Empty))
			{
				id = FindSeasonId();
			}
			if (id.Equals(Guid.Empty))
			{
				return null;
			}
			return BaseItem.LibraryManager.GetItemById(id) as Season;
		}
	}

	[IgnoreDataMember]
	public bool IsInSeasonFolder => FindParent<Season>() != null;

	[IgnoreDataMember]
	public string SeriesPresentationUniqueKey { get; set; }

	[IgnoreDataMember]
	public string SeriesName { get; set; }

	[IgnoreDataMember]
	public string SeasonName { get; set; }

	[IgnoreDataMember]
	public override bool SupportsRemoteImageDownloading
	{
		get
		{
			if (IsMissingEpisode)
			{
				return false;
			}
			return true;
		}
	}

	[IgnoreDataMember]
	public bool IsMissingEpisode
	{
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Invalid comparison between Unknown and I4
			return (int)LocationType == 2;
		}
	}

	[IgnoreDataMember]
	public Guid SeasonId { get; set; }

	[IgnoreDataMember]
	public Guid SeriesId { get; set; }

	public string FindSeriesSortName()
	{
		Series series = Series;
		if (series != null)
		{
			return series.SortName;
		}
		return SeriesName;
	}

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		if (SourceType == SourceType.Channel)
		{
			return 0.0;
		}
		return 16.0 / 9.0;
	}

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		Series series = Series;
		if (series != null && ParentIndexNumber.HasValue && IndexNumber.HasValue)
		{
			List<string> userDataKeys2 = series.GetUserDataKeys();
			int num = userDataKeys2.Count;
			if (userDataKeys2.Count > 1)
			{
				num--;
			}
			userDataKeys.InsertRange(0, from i in userDataKeys2.Take(num)
				select i + ParentIndexNumber.Value.ToString("000") + IndexNumber.Value.ToString("000"));
		}
		return userDataKeys;
	}

	public string FindSeriesPresentationUniqueKey()
	{
		return Series?.PresentationUniqueKey;
	}

	public string FindSeasonName()
	{
		Season season = Season;
		if (season == null)
		{
			if (ParentIndexNumber.HasValue)
			{
				return "Season " + ParentIndexNumber.Value.ToString(CultureInfo.InvariantCulture);
			}
			return "Season Unknown";
		}
		return season.Name;
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

	public Guid FindSeasonId()
	{
		Season season = FindParent<Season>();
		if (season == null)
		{
			Series series = Series;
			if (series != null && ParentIndexNumber.HasValue)
			{
				int findNumber = ParentIndexNumber.Value;
				season = series.Children.OfType<Season>().FirstOrDefault((Season i) => i.IndexNumber.HasValue && i.IndexNumber.Value == findNumber);
			}
		}
		return season?.Id ?? Guid.Empty;
	}

	protected override string CreateSortName()
	{
		return (ParentIndexNumber.HasValue ? ParentIndexNumber.Value.ToString("000 - ") : "") + (IndexNumber.HasValue ? IndexNumber.Value.ToString("0000 - ") : "") + Name;
	}

	public bool ContainsEpisodeNumber(int number)
	{
		if (IndexNumber.HasValue)
		{
			if (IndexNumberEnd.HasValue)
			{
				if (number >= IndexNumber.Value)
				{
					return number <= IndexNumberEnd.Value;
				}
				return false;
			}
			return IndexNumber.Value == number;
		}
		return false;
	}

	public Guid FindSeriesId()
	{
		return FindParent<Series>()?.Id ?? Guid.Empty;
	}

	public override IEnumerable<Guid> GetAncestorIds()
	{
		List<Guid> list = base.GetAncestorIds().ToList();
		Guid seasonId = SeasonId;
		if (!seasonId.Equals(Guid.Empty) && !list.Contains(seasonId))
		{
			list.Add(seasonId);
		}
		return list;
	}

	public override IEnumerable<FileSystemMetadata> GetDeletePaths()
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

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)2;
	}

	public EpisodeInfo GetLookupInfo()
	{
		EpisodeInfo itemLookupInfo = GetItemLookupInfo<EpisodeInfo>();
		Series series = Series;
		if (series != null)
		{
			itemLookupInfo.SeriesProviderIds = series.ProviderIds;
			itemLookupInfo.SeriesDisplayOrder = series.DisplayOrder;
		}
		itemLookupInfo.IsMissingEpisode = IsMissingEpisode;
		itemLookupInfo.IndexNumberEnd = IndexNumberEnd;
		return itemLookupInfo;
	}

	public override bool BeforeMetadataRefresh(bool replaceAllMetdata)
	{
		bool result = base.BeforeMetadataRefresh(replaceAllMetdata);
		if (!IsLocked && SourceType == SourceType.Library)
		{
			try
			{
				if (BaseItem.LibraryManager.FillMissingEpisodeNumbersFromPath(this, replaceAllMetdata))
				{
					result = true;
				}
			}
			catch (Exception ex)
			{
				BaseItem.Logger.ErrorException("Error in FillMissingEpisodeNumbersFromPath. Episode: {0}", ex, new object[1] { Path ?? Name ?? Id.ToString() });
			}
		}
		return result;
	}
}
