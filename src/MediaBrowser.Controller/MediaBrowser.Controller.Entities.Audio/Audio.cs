using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities.Audio;

public class Audio : BaseItem, IHasAlbumArtist, IHasArtist, IHasMusicGenres, IHasLookupInfo<SongInfo>, IHasMediaSources
{
	[IgnoreDataMember]
	public string[] Artists { get; set; }

	[IgnoreDataMember]
	public string[] AlbumArtists { get; set; }

	[IgnoreDataMember]
	public override bool SupportsPlayedStatus => true;

	[IgnoreDataMember]
	public override bool SupportsPeople => false;

	[IgnoreDataMember]
	public override bool SupportsAddingToPlaylist => true;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => true;

	[IgnoreDataMember]
	protected override bool SupportsOwnedItems => false;

	[IgnoreDataMember]
	public override Folder LatestItemsIndexContainer => AlbumEntity;

	[IgnoreDataMember]
	public override bool SupportsExternalTransfer => CanDownloadAsSingleMedia();

	[IgnoreDataMember]
	public string[] AllArtists
	{
		get
		{
			string[] array = new string[AlbumArtists.Length + Artists.Length];
			int num = 0;
			string[] albumArtists = AlbumArtists;
			foreach (string text in albumArtists)
			{
				array[num] = text;
				num++;
			}
			albumArtists = Artists;
			foreach (string text2 in albumArtists)
			{
				array[num] = text2;
				num++;
			}
			return array;
		}
	}

	[IgnoreDataMember]
	public MusicAlbum AlbumEntity => FindParent<MusicAlbum>();

	[IgnoreDataMember]
	public override string MediaType => "Audio";

	public Audio()
	{
		Artists = Array.Empty<string>();
		AlbumArtists = Array.Empty<string>();
	}

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 1.0;
	}

	public override bool CanDownload()
	{
		return CanDownloadAsSingleMedia();
	}

	protected override string CreateSortName()
	{
		return (ParentIndexNumber.HasValue ? ParentIndexNumber.Value.ToString("0000 - ") : "") + (IndexNumber.HasValue ? IndexNumber.Value.ToString("0000 - ") : "") + Name;
	}

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		string text = (IndexNumber.HasValue ? IndexNumber.Value.ToString("0000") : string.Empty);
		if (ParentIndexNumber.HasValue)
		{
			text = ParentIndexNumber.Value.ToString("0000") + "-" + text;
		}
		text += Name;
		if (!string.IsNullOrEmpty(Album))
		{
			text = Album + "-" + text;
		}
		string text2 = ((AlbumArtists.Length == 0) ? null : AlbumArtists[0]);
		if (!string.IsNullOrEmpty(text2))
		{
			text = text2 + "-" + text;
		}
		userDataKeys.Insert(0, text);
		return userDataKeys;
	}

	public override UnratedItem GetBlockUnratedType()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		if (SourceType == SourceType.Library)
		{
			return (UnratedItem)3;
		}
		return base.GetBlockUnratedType();
	}

	public List<MediaStream> GetMediaStreams(MediaStreamType type)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		return BaseItem.MediaSourceManager.GetMediaStreams(new MediaStreamQuery
		{
			ItemId = Id,
			Type = type
		});
	}

	public SongInfo GetLookupInfo()
	{
		SongInfo itemLookupInfo = GetItemLookupInfo<SongInfo>();
		itemLookupInfo.AlbumArtists = AlbumArtists;
		itemLookupInfo.Album = Album;
		itemLookupInfo.Artists = Artists;
		return itemLookupInfo;
	}

	protected override List<Tuple<BaseItem, MediaSourceType>> GetAllItemsForMediaSources()
	{
		return new List<Tuple<BaseItem, MediaSourceType>>
		{
			new Tuple<BaseItem, MediaSourceType>(this, (MediaSourceType)0)
		};
	}
}
