using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class Video : BaseItem, IHasAspectRatio, ISupportsPlaceHolders, IHasMediaSources
{
	[IgnoreDataMember]
	public string PrimaryVersionId { get; set; }

	public string[] AdditionalParts { get; set; }

	public string[] LocalAlternateVersions { get; set; }

	public LinkedChild[] LinkedAlternateVersions { get; set; }

	[IgnoreDataMember]
	public override bool SupportsPlayedStatus => true;

	[IgnoreDataMember]
	public override bool SupportsPeople => true;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => true;

	[IgnoreDataMember]
	public override bool SupportsPositionTicksResume
	{
		get
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Invalid comparison between Unknown and I4
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0025: Invalid comparison between Unknown and I4
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Invalid comparison between Unknown and I4
			ExtraType? extraType = ExtraType;
			if (extraType.HasValue)
			{
				if ((int)extraType.Value == 7)
				{
					return false;
				}
				if ((int)extraType.Value == 9)
				{
					return false;
				}
				if ((int)extraType.Value == 2)
				{
					return false;
				}
			}
			return true;
		}
	}

	[IgnoreDataMember]
	public override bool SupportsThemeMedia => true;

	public TransportStreamTimestamp? Timestamp { get; set; }

	public string[] SubtitleFiles { get; set; }

	public bool HasSubtitles { get; set; }

	public bool IsPlaceHolder { get; set; }

	public int? DefaultVideoStreamIndex { get; set; }

	public VideoType VideoType
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

	public IsoType? IsoType { get; set; }

	public Video3DFormat? Video3DFormat { get; set; }

	public string AspectRatio { get; set; }

	[IgnoreDataMember]
	public override bool SupportsExternalTransfer => CanDownload();

	[IgnoreDataMember]
	public override bool SupportsAddingToPlaylist => true;

	[IgnoreDataMember]
	public int MediaSourceCount
	{
		get
		{
			if (!string.IsNullOrEmpty(PrimaryVersionId) && BaseItem.LibraryManager.GetItemById(PrimaryVersionId) is Video video)
			{
				return video.MediaSourceCount;
			}
			return LinkedAlternateVersions.Length + LocalAlternateVersions.Length + 1;
		}
	}

	[IgnoreDataMember]
	public bool IsStacked => AdditionalParts.Length != 0;

	[IgnoreDataMember]
	public override bool HasLocalAlternateVersions => LocalAlternateVersions.Length != 0;

	public static ILiveTvManager LiveTvManager { get; set; }

	[IgnoreDataMember]
	public override SourceType SourceType
	{
		get
		{
			if (IsActiveRecording())
			{
				return SourceType.LiveTV;
			}
			return base.SourceType;
		}
	}

	[IgnoreDataMember]
	public bool IsCompleteMedia
	{
		get
		{
			if (SourceType == SourceType.Channel)
			{
				return !Tags.Contains("livestream", StringComparer.OrdinalIgnoreCase);
			}
			return !IsActiveRecording();
		}
	}

	[IgnoreDataMember]
	protected virtual bool EnableDefaultVideoUserDataKeys => true;

	[IgnoreDataMember]
	public override string ContainingFolderPath
	{
		get
		{
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Invalid comparison between Unknown and I4
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Invalid comparison between Unknown and I4
			if (IsStacked)
			{
				return BaseItem.FileSystem.GetDirectoryName(Path);
			}
			if (!IsPlaceHolder && ((int)VideoType == 3 || (int)VideoType == 2))
			{
				return Path;
			}
			return base.ContainingFolderPath;
		}
	}

	[IgnoreDataMember]
	public override string FileNameWithoutExtension
	{
		get
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Invalid comparison between Unknown and I4
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Invalid comparison between Unknown and I4
			if (IsFileProtocol)
			{
				if ((int)VideoType == 3 || (int)VideoType == 2)
				{
					return System.IO.Path.GetFileName(Path);
				}
				return System.IO.Path.GetFileNameWithoutExtension(Path);
			}
			return null;
		}
	}

	[IgnoreDataMember]
	public bool Is3D => Video3DFormat.HasValue;

	[IgnoreDataMember]
	public override string MediaType => "Video";

	public void SetPrimaryVersionId(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			PrimaryVersionId = null;
		}
		else
		{
			PrimaryVersionId = id;
		}
		PresentationUniqueKey = CreatePresentationUniqueKey();
	}

	public override string CreatePresentationUniqueKey()
	{
		if (!string.IsNullOrEmpty(PrimaryVersionId))
		{
			return PrimaryVersionId;
		}
		return base.CreatePresentationUniqueKey();
	}

	public string[] GetPlayableStreamFileNames(IMediaEncoder mediaEncoder)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Invalid comparison between Unknown and I4
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Invalid comparison between Unknown and I4
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		VideoType videoType = VideoType;
		if ((int)videoType == 1 && IsoType == (IsoType?)1)
		{
			videoType = (VideoType)3;
		}
		else
		{
			if ((int)videoType != 1 || IsoType != (IsoType?)0)
			{
				return Array.Empty<string>();
			}
			videoType = (VideoType)2;
		}
		return mediaEncoder.GetPlayableStreamFileNames(Path, videoType);
	}

	public Video()
	{
		AdditionalParts = Array.Empty<string>();
		LocalAlternateVersions = Array.Empty<string>();
		SubtitleFiles = Array.Empty<string>();
		LinkedAlternateVersions = Array.Empty<LinkedChild>();
	}

	public override bool CanDownload()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Invalid comparison between Unknown and I4
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Invalid comparison between Unknown and I4
		VideoType videoType = VideoType;
		if ((int)videoType == 2 || (int)videoType == 3)
		{
			return false;
		}
		return CanDownloadAsSingleMedia();
	}

	public IEnumerable<Guid> GetAdditionalPartIds()
	{
		return AdditionalParts.Select((string i) => BaseItem.LibraryManager.GetNewItemId(i, typeof(Video)));
	}

	public IEnumerable<Guid> GetLocalAlternateVersionIds()
	{
		return LocalAlternateVersions.Select((string i) => BaseItem.LibraryManager.GetNewItemId(i, typeof(Video)));
	}

	protected override bool IsActiveRecording()
	{
		return LiveTvManager.GetActiveRecordingInfo(Path) != null;
	}

	public override bool CanDelete()
	{
		if (IsActiveRecording())
		{
			return false;
		}
		return base.CanDelete();
	}

	public override long GetRunTimeTicksForPlayState()
	{
		ActiveRecordingInfo activeRecordingInfo = LiveTvManager.GetActiveRecordingInfo(Path);
		if (activeRecordingInfo != null)
		{
			TimerInfo timer = activeRecordingInfo.Timer;
			if (timer != null)
			{
				DateTime dateTime = timer.StartDate.AddSeconds(-timer.PrePaddingSeconds);
				return (timer.EndDate.AddSeconds(timer.PostPaddingSeconds) - dateTime).Ticks;
			}
		}
		return base.GetRunTimeTicksForPlayState();
	}

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		if (EnableDefaultVideoUserDataKeys)
		{
			if (ExtraType.HasValue)
			{
				string providerId = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)3);
				if (!string.IsNullOrEmpty(providerId))
				{
					userDataKeys.Insert(0, GetUserDataKey(providerId));
				}
				providerId = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)2);
				if (!string.IsNullOrEmpty(providerId))
				{
					userDataKeys.Insert(0, GetUserDataKey(providerId));
				}
			}
			else
			{
				string providerId2 = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)2);
				if (!string.IsNullOrEmpty(providerId2))
				{
					userDataKeys.Insert(0, providerId2);
				}
				providerId2 = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)3);
				if (!string.IsNullOrEmpty(providerId2))
				{
					userDataKeys.Insert(0, providerId2);
				}
			}
		}
		return userDataKeys;
	}

	private string GetUserDataKey(string providerId)
	{
		string text = providerId + "-" + ExtraType.ToString().ToLower();
		if (RunTimeTicks.HasValue)
		{
			text = text + "-" + RunTimeTicks.Value.ToString(CultureInfo.InvariantCulture);
		}
		return text;
	}

	public IEnumerable<Video> GetLinkedAlternateVersions()
	{
		return from i in (from i in LinkedAlternateVersions.Select(GetLinkedChild)
				where i != null
				select i).OfType<Video>()
			orderby i.SortName
			select i;
	}

	public IEnumerable<Video> GetAdditionalParts()
	{
		return from i in (from i in GetAdditionalPartIds()
				select BaseItem.LibraryManager.GetItemById(i) into i
				where i != null
				select i).OfType<Video>()
			orderby i.SortName
			select i;
	}

	internal override ItemUpdateType UpdateFromResolvedItem(BaseItem newItem)
	{
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		ItemUpdateType itemUpdateType = base.UpdateFromResolvedItem(newItem);
		if (newItem is Video video)
		{
			if (!Enumerable.SequenceEqual(AdditionalParts, video.AdditionalParts, StringComparer.Ordinal))
			{
				AdditionalParts = video.AdditionalParts;
				itemUpdateType |= ItemUpdateType.MetadataImport;
			}
			if (!Enumerable.SequenceEqual(LocalAlternateVersions, video.LocalAlternateVersions, StringComparer.Ordinal))
			{
				LocalAlternateVersions = video.LocalAlternateVersions;
				itemUpdateType |= ItemUpdateType.MetadataImport;
			}
			if (VideoType != video.VideoType)
			{
				VideoType = video.VideoType;
				itemUpdateType |= ItemUpdateType.MetadataImport;
			}
		}
		return itemUpdateType;
	}

	public static string[] QueryPlayableStreamFiles(string rootPath, VideoType videoType)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Invalid comparison between Unknown and I4
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Invalid comparison between Unknown and I4
		if ((int)videoType == 2)
		{
			return (from i in (from i in BaseItem.FileSystem.GetFiles(rootPath, new string[1] { ".vob" }, false, true)
					orderby i.Length descending, i.FullName
					select i).Take(1)
				select i.FullName).ToArray();
		}
		if ((int)videoType == 3)
		{
			return (from i in (from i in BaseItem.FileSystem.GetFiles(rootPath, new string[1] { ".m2ts" }, false, true)
					orderby i.Length descending, i.FullName
					select i).Take(1)
				select i.FullName).ToArray();
		}
		return Array.Empty<string>();
	}

	protected override async Task<bool> RefreshedOwnedItems(MetadataRefreshOptions options, List<FileSystemMetadata> fileSystemChildren, CancellationToken cancellationToken)
	{
		bool hasChanges = await base.RefreshedOwnedItems(options, fileSystemChildren, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (IsStacked)
		{
			await Task.WhenAll(AdditionalParts.Select((string i) => RefreshMetadataForOwnedVideo(options, copyTitleMetadata: true, i, cancellationToken))).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (IsFileProtocol && SupportsOwnedItems && !IsStacked)
		{
			RefreshLinkedAlternateVersions();
			await Task.WhenAll(LocalAlternateVersions.Select((string i) => RefreshMetadataForOwnedVideo(options, copyTitleMetadata: false, i, cancellationToken))).ConfigureAwait(continueOnCapturedContext: false);
		}
		return hasChanges;
	}

	private void RefreshLinkedAlternateVersions()
	{
		LinkedChild[] linkedAlternateVersions = LinkedAlternateVersions;
		foreach (LinkedChild linkedChild in linkedAlternateVersions)
		{
			if (linkedChild.ItemId.HasValue && linkedChild.ItemId.Value.Equals(Guid.Empty))
			{
				linkedChild.ItemId = null;
			}
		}
	}

	public override void UpdateToRepository(ItemUpdateType updateReason, CancellationToken cancellationToken)
	{
		base.UpdateToRepository(updateReason, cancellationToken);
		foreach (BaseItem item in from i in GetLocalAlternateVersionIds()
			select BaseItem.LibraryManager.GetItemById(i) into i
			where i != null
			select i)
		{
			item.ImageInfos = ImageInfos;
			item.Overview = Overview;
			item.ProductionYear = ProductionYear;
			item.PremiereDate = PremiereDate;
			item.CommunityRating = CommunityRating;
			item.OfficialRating = OfficialRating;
			item.Genres = Genres;
			item.ProviderIds = ProviderIds;
			item.UpdateToRepository(ItemUpdateType.MetadataDownload, cancellationToken);
		}
	}

	public override IEnumerable<FileSystemMetadata> GetDeletePaths()
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected Obj, but got Unknown
		if (!IsInMixedFolder)
		{
			return new FileSystemMetadata[1]
			{
				new FileSystemMetadata
				{
					FullName = ContainingFolderPath,
					IsDirectory = true
				}
			};
		}
		return base.GetDeletePaths();
	}

	public virtual MediaStream GetDefaultVideoStream()
	{
		if (!DefaultVideoStreamIndex.HasValue)
		{
			return null;
		}
		return BaseItem.MediaSourceManager.GetMediaStreams(new MediaStreamQuery
		{
			ItemId = Id,
			Index = DefaultVideoStreamIndex.Value
		}).FirstOrDefault();
	}

	protected override List<Tuple<BaseItem, MediaSourceType>> GetAllItemsForMediaSources()
	{
		List<Tuple<BaseItem, MediaSourceType>> list = new List<Tuple<BaseItem, MediaSourceType>>();
		list.Add(new Tuple<BaseItem, MediaSourceType>(this, (MediaSourceType)0));
		list.AddRange(from i in GetLinkedAlternateVersions()
			select new Tuple<BaseItem, MediaSourceType>(i, (MediaSourceType)1));
		if (!string.IsNullOrEmpty(PrimaryVersionId) && BaseItem.LibraryManager.GetItemById(PrimaryVersionId) is Video video)
		{
			List<Guid> existingIds = list.Select((Tuple<BaseItem, MediaSourceType> i) => i.Item1.Id).ToList();
			list.Add(new Tuple<BaseItem, MediaSourceType>(video, (MediaSourceType)1));
			list.AddRange(from i in video.GetLinkedAlternateVersions()
				where !existingIds.Contains(i.Id)
				select new Tuple<BaseItem, MediaSourceType>(i, (MediaSourceType)1));
		}
		List<BaseItem> source = (from i in list.SelectMany((Tuple<BaseItem, MediaSourceType> i) => (i.Item1 is Video video2) ? video2.GetLocalAlternateVersionIds() : new List<Guid>()).Select(BaseItem.LibraryManager.GetItemById)
			where i != null
			select i).ToList();
		list.AddRange(source.Select((BaseItem i) => new Tuple<BaseItem, MediaSourceType>(i, (MediaSourceType)0)));
		return list;
	}
}
