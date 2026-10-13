using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Users;

namespace MediaBrowser.Controller.Entities.Audio;

public class MusicAlbum : Folder, IHasAlbumArtist, IHasArtist, IHasMusicGenres, IHasLookupInfo<AlbumInfo>, IMetadataContainer
{
	public string[] AlbumArtists { get; set; }

	public string[] Artists { get; set; }

	[IgnoreDataMember]
	public override bool SupportsAddingToPlaylist => true;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => true;

	[IgnoreDataMember]
	public MusicArtist MusicArtist => GetMusicArtist(new DtoOptions(allFields: true));

	[IgnoreDataMember]
	public override bool SupportsPlayedStatus => false;

	[IgnoreDataMember]
	public override bool SupportsCumulativeRunTimeTicks => true;

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
	public string AlbumArtist
	{
		get
		{
			if (AlbumArtists.Length != 0)
			{
				return AlbumArtists[0];
			}
			return null;
		}
	}

	[IgnoreDataMember]
	public override bool SupportsPeople => false;

	[IgnoreDataMember]
	public IEnumerable<BaseItem> Tracks => GetRecursiveChildren((BaseItem i) => i is Audio);

	public MusicAlbum()
	{
		Artists = Array.Empty<string>();
		AlbumArtists = Array.Empty<string>();
	}

	public MusicArtist GetMusicArtist(DtoOptions options)
	{
		foreach (BaseItem parent in GetParents())
		{
			if (parent is MusicArtist result)
			{
				return result;
			}
		}
		string albumArtist = AlbumArtist;
		if (!string.IsNullOrEmpty(albumArtist))
		{
			return BaseItem.LibraryManager.GetArtist(albumArtist, options);
		}
		return null;
	}

	protected override IEnumerable<BaseItem> GetEligibleChildrenForRecursiveChildren(User user)
	{
		return Tracks;
	}

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 1.0;
	}

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		string albumArtist = AlbumArtist;
		if (!string.IsNullOrEmpty(albumArtist))
		{
			userDataKeys.Insert(0, albumArtist + "-" + Name);
		}
		string providerId = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)8);
		if (!string.IsNullOrEmpty(providerId))
		{
			userDataKeys.Insert(0, "MusicAlbum-Musicbrainz-" + providerId);
		}
		providerId = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)11);
		if (!string.IsNullOrEmpty(providerId))
		{
			userDataKeys.Insert(0, "MusicAlbum-MusicBrainzReleaseGroup-" + providerId);
		}
		return userDataKeys;
	}

	protected override bool GetBlockUnratedValue(UserPolicy config)
	{
		return Enumerable.Contains(config.BlockUnratedItems, (UnratedItem)3);
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)3;
	}

	public AlbumInfo GetLookupInfo()
	{
		AlbumInfo itemLookupInfo = GetItemLookupInfo<AlbumInfo>();
		itemLookupInfo.AlbumArtists = AlbumArtists;
		MusicArtist musicArtist = GetMusicArtist(new DtoOptions(allFields: false));
		if (musicArtist != null)
		{
			itemLookupInfo.ArtistProviderIds = musicArtist.ProviderIds;
		}
		itemLookupInfo.SongInfos = (from Audio i in GetRecursiveChildren((BaseItem i) => i is Audio)
			select i.GetLookupInfo()).ToList();
		string text = itemLookupInfo.SongInfos.Select((SongInfo i) => i.Album).FirstOrDefault((string i) => !string.IsNullOrEmpty(i));
		if (!string.IsNullOrEmpty(text))
		{
			itemLookupInfo.Name = text;
		}
		return itemLookupInfo;
	}

	public async Task RefreshAllMetadata(MetadataRefreshOptions refreshOptions, IProgress<double> progress, CancellationToken cancellationToken)
	{
		IList<BaseItem> recursiveChildren = GetRecursiveChildren();
		int totalItems = recursiveChildren.Count;
		int numComplete = 0;
		ItemUpdateType childUpdateType = ItemUpdateType.None;
		foreach (BaseItem item in recursiveChildren)
		{
			cancellationToken.ThrowIfCancellationRequested();
			childUpdateType |= await item.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			numComplete++;
			double num = numComplete;
			num /= (double)totalItems;
			progress.Report(num * 95.0);
		}
		MetadataRefreshOptions metadataRefreshOptions = refreshOptions;
		if (childUpdateType > ItemUpdateType.None)
		{
			metadataRefreshOptions = new MetadataRefreshOptions(refreshOptions);
			metadataRefreshOptions.MetadataRefreshMode = MetadataRefreshMode.FullRefresh;
		}
		await RefreshMetadata(metadataRefreshOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (!refreshOptions.IsAutomated)
		{
			await RefreshArtists(refreshOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private async Task RefreshArtists(MetadataRefreshOptions refreshOptions, CancellationToken cancellationToken)
	{
		string[] allArtists = AllArtists;
		string[] array = allArtists;
		foreach (string text in array)
		{
			if (!string.IsNullOrEmpty(text))
			{
				MusicArtist artist = BaseItem.LibraryManager.GetArtist(text);
				if (artist.IsAccessedByName)
				{
					await artist.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
	}
}
