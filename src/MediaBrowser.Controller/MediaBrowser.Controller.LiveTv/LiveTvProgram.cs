using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.LiveTv;

public class LiveTvProgram : BaseItem, IHasLookupInfo<ItemLookupInfo>, IHasStartDate, IHasProgramAttributes, IHasSeriesName
{
	[IgnoreDataMember]
	public override SourceType SourceType => SourceType.LiveTV;

	[IgnoreDataMember]
	public string SeriesName { get; set; }

	[IgnoreDataMember]
	public DateTime StartDate { get; set; }

	[IgnoreDataMember]
	public bool IsRepeat { get; set; }

	[IgnoreDataMember]
	public string EpisodeTitle { get; set; }

	[IgnoreDataMember]
	public string ShowId { get; set; }

	[IgnoreDataMember]
	public bool IsMovie { get; set; }

	[IgnoreDataMember]
	public bool IsSports => Tags.Contains("Sports", StringComparer.OrdinalIgnoreCase);

	[IgnoreDataMember]
	public bool IsSeries { get; set; }

	[IgnoreDataMember]
	public bool IsLive => Tags.Contains("Live", StringComparer.OrdinalIgnoreCase);

	[IgnoreDataMember]
	public bool IsNews => Tags.Contains("News", StringComparer.OrdinalIgnoreCase);

	[IgnoreDataMember]
	public bool IsKids => Tags.Contains("Kids", StringComparer.OrdinalIgnoreCase);

	[IgnoreDataMember]
	public bool IsPremiere => Tags.Contains("Premiere", StringComparer.OrdinalIgnoreCase);

	[IgnoreDataMember]
	public override string ContainingFolderPath => Path;

	[IgnoreDataMember]
	public bool IsAiring
	{
		get
		{
			DateTime utcNow = DateTime.UtcNow;
			if (utcNow >= StartDate)
			{
				DateTime value = utcNow;
				DateTime? endDate = EndDate;
				return value < endDate;
			}
			return false;
		}
	}

	[IgnoreDataMember]
	public bool HasAired
	{
		get
		{
			DateTime utcNow = DateTime.UtcNow;
			DateTime? endDate = EndDate;
			return utcNow >= endDate;
		}
	}

	[IgnoreDataMember]
	public override bool SupportsPeople
	{
		get
		{
			if (IsNews || IsSports)
			{
				return false;
			}
			return base.SupportsPeople;
		}
	}

	[IgnoreDataMember]
	public override bool SupportsAncestors => false;

	public LiveTvProgram()
	{
		IsVirtualItem = true;
	}

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		if (!IsSeries)
		{
			string providerId = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)2);
			if (!string.IsNullOrEmpty(providerId))
			{
				userDataKeys.Insert(0, providerId);
			}
			providerId = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)3);
			if (!string.IsNullOrEmpty(providerId))
			{
				userDataKeys.Insert(0, providerId);
			}
		}
		else if (!string.IsNullOrEmpty(EpisodeTitle))
		{
			string clientTypeName = GetClientTypeName();
			userDataKeys.Insert(0, clientTypeName + "-" + Name + (EpisodeTitle ?? string.Empty));
		}
		return userDataKeys;
	}

	public override string GetClientTypeName()
	{
		return "Program";
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)7;
	}

	protected override string GetInternalMetadataPath(string basePath)
	{
		return System.IO.Path.Combine(basePath, "livetv", Id.ToString("N"));
	}

	public override bool CanDelete()
	{
		return false;
	}

	public override List<ExternalUrl> GetRelatedUrls()
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Expected Obj, but got Unknown
		List<ExternalUrl> relatedUrls = base.GetRelatedUrls();
		string providerId = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)2);
		if (!string.IsNullOrEmpty(providerId) && IsMovie)
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
