using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.LiveTv;

public class LiveTvChannel : BaseItem, IHasMediaSources, IHasProgramAttributes
{
	[IgnoreDataMember]
	public override bool SupportsPositionTicksResume => false;

	[IgnoreDataMember]
	public string ServiceName { get; set; }

	[IgnoreDataMember]
	public override SourceType SourceType => SourceType.LiveTV;

	[IgnoreDataMember]
	public override bool EnableRememberingTrackSelections => false;

	public string Number { get; set; }

	public ChannelType ChannelType
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	[IgnoreDataMember]
	public override LocationType LocationType => (LocationType)1;

	[IgnoreDataMember]
	public override string MediaType
	{
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Invalid comparison between Unknown and I4
			if ((int)ChannelType != 1)
			{
				return "Video";
			}
			return "Audio";
		}
	}

	[IgnoreDataMember]
	public bool IsMovie { get; set; }

	[IgnoreDataMember]
	public bool IsSports { get; set; }

	[IgnoreDataMember]
	public bool IsSeries { get; set; }

	[IgnoreDataMember]
	public bool IsNews { get; set; }

	[IgnoreDataMember]
	public bool IsKids => Tags.Contains("Kids", StringComparer.OrdinalIgnoreCase);

	[IgnoreDataMember]
	public bool IsRepeat { get; set; }

	[IgnoreDataMember]
	public string EpisodeTitle { get; set; }

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		if (!BaseItem.ConfigurationManager.Configuration.DisableLiveTvChannelUserDataName)
		{
			userDataKeys.Insert(0, GetClientTypeName() + "-" + Name);
		}
		return userDataKeys;
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)6;
	}

	protected override string CreateSortName()
	{
		if (!string.IsNullOrEmpty(Number))
		{
			double result = 0.0;
			if (double.TryParse(Number, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
			{
				return $"{result:00000.0}" + "-" + (Name ?? string.Empty);
			}
		}
		return (Number ?? string.Empty) + "-" + (Name ?? string.Empty);
	}

	public override string GetClientTypeName()
	{
		return "TvChannel";
	}

	public IEnumerable<BaseItem> GetTaggedItems(IEnumerable<BaseItem> inputItems)
	{
		return new List<BaseItem>();
	}

	public override List<MediaSourceInfo> GetMediaSources(bool enablePathSubstitution)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Expected Obj, but got Unknown
		List<MediaSourceInfo> list = new List<MediaSourceInfo>();
		MediaSourceInfo item = new MediaSourceInfo
		{
			Id = Id.ToString("N"),
			Protocol = (MediaProtocol)(((int?)PathProtocol) ?? 0),
			MediaStreams = new List<MediaStream>(),
			Name = Name,
			Path = Path,
			RunTimeTicks = RunTimeTicks,
			Type = (MediaSourceType)2,
			IsInfiniteStream = !RunTimeTicks.HasValue
		};
		list.Add(item);
		return list;
	}

	public override List<MediaStream> GetMediaStreams()
	{
		return new List<MediaStream>();
	}

	protected override string GetInternalMetadataPath(string basePath)
	{
		return System.IO.Path.Combine(basePath, "livetv", Id.ToString("N"), "metadata");
	}

	public override bool CanDelete()
	{
		return false;
	}
}
