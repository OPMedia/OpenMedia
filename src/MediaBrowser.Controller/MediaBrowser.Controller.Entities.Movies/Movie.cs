using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities.Movies;

public class Movie : Video, IHasLookupInfo<MovieInfo>, ISupportsBoxSetGrouping
{
	public string TmdbCollectionName { get; set; }

	[IgnoreDataMember]
	public string CollectionName
	{
		get
		{
			return TmdbCollectionName;
		}
		set
		{
			TmdbCollectionName = value;
		}
	}

	[IgnoreDataMember]
	public override bool SupportsLocalTrailers => true;

	[IgnoreDataMember]
	public override bool StopRefreshIfLocalMetadataFound => false;

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		if (SourceType == SourceType.Channel)
		{
			return 0.0;
		}
		return 2.0 / 3.0;
	}

	protected override Dictionary<string, string> GetProviderIdsForLinkedTrailerSearch()
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> providerId in ProviderIds)
		{
			if (!string.Equals(providerId.Key, ((object)(MetadataProviders)7/*cast due to constrained. prefix*/).ToString(), StringComparison.OrdinalIgnoreCase))
			{
				dictionary[providerId.Key] = providerId.Value;
			}
		}
		return dictionary;
	}

	protected override BaseItem[] LoadExtras(List<FileSystemMetadata> fileSystemChildren, IDirectoryService directoryService)
	{
		if (IsFileProtocol && SupportsOwnedItems && !IsInMixedFolder)
		{
			return BaseItem.LibraryManager.FindExtras(this, fileSystemChildren, directoryService).ToArray();
		}
		return base.LoadExtras(fileSystemChildren, directoryService);
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)0;
	}

	public MovieInfo GetLookupInfo()
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Invalid comparison between Unknown and I4
		MovieInfo itemLookupInfo = GetItemLookupInfo<MovieInfo>();
		if (!IsInMixedFolder)
		{
			string text = System.IO.Path.GetFileName(ContainingFolderPath);
			if (((int)VideoType == 0 || (int)VideoType == 1) && string.Equals(text, System.IO.Path.GetFileName(Path), StringComparison.OrdinalIgnoreCase))
			{
				text = System.IO.Path.GetFileNameWithoutExtension(text);
			}
			itemLookupInfo.Name = text;
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
