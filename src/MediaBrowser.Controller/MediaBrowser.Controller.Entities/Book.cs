using System;
using System.Linq;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class Book : BaseItem, IHasLookupInfo<BookInfo>, IHasSeries, IHasSeriesName
{
	[IgnoreDataMember]
	public override string MediaType => "Book";

	[IgnoreDataMember]
	public string SeriesPresentationUniqueKey { get; set; }

	[IgnoreDataMember]
	public string SeriesName { get; set; }

	[IgnoreDataMember]
	public Guid SeriesId { get; set; }

	[IgnoreDataMember]
	public override bool SupportsExternalTransfer => CanDownloadAsSingleMedia();

	public string FindSeriesSortName()
	{
		return SeriesName;
	}

	public string FindSeriesName()
	{
		return SeriesName;
	}

	public string FindSeriesPresentationUniqueKey()
	{
		return SeriesPresentationUniqueKey;
	}

	public Guid FindSeriesId()
	{
		return SeriesId;
	}

	public override bool CanDownload()
	{
		return CanDownloadAsSingleMedia();
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)5;
	}

	public BookInfo GetLookupInfo()
	{
		BookInfo itemLookupInfo = GetItemLookupInfo<BookInfo>();
		if (string.IsNullOrEmpty(SeriesName))
		{
			itemLookupInfo.SeriesName = (from i in GetParents()
				select i.Name).FirstOrDefault();
		}
		else
		{
			itemLookupInfo.SeriesName = SeriesName;
		}
		return itemLookupInfo;
	}
}
