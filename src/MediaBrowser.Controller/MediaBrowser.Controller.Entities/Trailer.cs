using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class Trailer : Video, IHasLookupInfo<TrailerInfo>
{
	public TrailerType[] TrailerTypes { get; set; }

	[IgnoreDataMember]
	public override bool StopRefreshIfLocalMetadataFound => false;

	public Trailer()
	{
		TrailerTypes = Array.Empty<TrailerType>();
	}

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 2.0 / 3.0;
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)1;
	}

	public TrailerInfo GetLookupInfo()
	{
		TrailerInfo itemLookupInfo = GetItemLookupInfo<TrailerInfo>();
		if (!IsInMixedFolder && IsFileProtocol)
		{
			itemLookupInfo.Name = System.IO.Path.GetFileName(ContainingFolderPath);
		}
		return itemLookupInfo;
	}

	public override bool BeforeMetadataRefresh(bool replaceAllMetdata)
	{
		bool result = base.BeforeMetadataRefresh(replaceAllMetdata);
		if (!ProductionYear.HasValue)
		{
			int? year = BaseItem.LibraryManager.ParseName(Name).Year;
			if (year.HasValue)
			{
				ProductionYear = year;
				result = true;
			}
			else if (!IsInMixedFolder)
			{
				year = BaseItem.LibraryManager.ParseName(System.IO.Path.GetFileName(ContainingFolderPath)).Year;
				if (year.HasValue)
				{
					ProductionYear = year;
					result = true;
				}
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
				Url = $"https://trakt.tv/movies/{providerId}"
			});
		}
		return relatedUrls;
	}
}
