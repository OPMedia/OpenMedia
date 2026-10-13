using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Users;

namespace MediaBrowser.Controller.Entities.Audio;

public class MusicArtist : Folder, IItemByName, IHasMusicGenres, IHasDualAccess, IHasLookupInfo<ArtistInfo>
{
	[IgnoreDataMember]
	public bool IsAccessedByName => ParentId.Equals(Guid.Empty);

	[IgnoreDataMember]
	public override bool IsFolder => !IsAccessedByName;

	[IgnoreDataMember]
	public override bool SupportsExternalTransfer => CanDownloadAsFolder();

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => false;

	[IgnoreDataMember]
	public override bool SupportsCumulativeRunTimeTicks => true;

	[IgnoreDataMember]
	public override bool IsDisplayedAsFolder => true;

	[IgnoreDataMember]
	public override bool SupportsAddingToPlaylist => true;

	[IgnoreDataMember]
	public override bool SupportsPlayedStatus => false;

	[IgnoreDataMember]
	public override IEnumerable<BaseItem> Children
	{
		get
		{
			if (IsAccessedByName)
			{
				return new List<BaseItem>();
			}
			return base.Children;
		}
	}

	[IgnoreDataMember]
	public override string ContainingFolderPath => Path;

	[IgnoreDataMember]
	public override bool SupportsPeople => false;

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 1.0;
	}

	public override bool CanDelete()
	{
		return !IsAccessedByName;
	}

	public IList<BaseItem> GetTaggedItems(InternalItemsQuery query)
	{
		if (query.IncludeItemTypes.Length == 0)
		{
			query.IncludeItemTypes = new string[3]
			{
				typeof(Audio).Name,
				typeof(MusicVideo).Name,
				typeof(MusicAlbum).Name
			};
			query.ArtistIds = new Guid[1] { Id };
		}
		return BaseItem.LibraryManager.GetItemList(query);
	}

	public override int GetChildCount(User user)
	{
		if (IsAccessedByName)
		{
			return 0;
		}
		return base.GetChildCount(user);
	}

	public override bool IsSaveLocalMetadataEnabled()
	{
		if (IsAccessedByName)
		{
			return true;
		}
		return base.IsSaveLocalMetadataEnabled();
	}

	protected override Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
	{
		if (IsAccessedByName)
		{
			return Task.CompletedTask;
		}
		return base.ValidateChildrenInternal(progress, cancellationToken, recursive, refreshChildMetadata, refreshOptions, directoryService);
	}

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		userDataKeys.InsertRange(0, GetUserDataKeys(this));
		return userDataKeys;
	}

	private static List<string> GetUserDataKeys(MusicArtist item)
	{
		List<string> list = new List<string>();
		string providerId = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)item, (MetadataProviders)10);
		if (!string.IsNullOrEmpty(providerId))
		{
			list.Add("Artist-Musicbrainz-" + providerId);
		}
		list.Add("Artist-" + (item.Name ?? string.Empty).RemoveDiacritics());
		return list;
	}

	public override string CreatePresentationUniqueKey()
	{
		return "Artist-" + (Name ?? string.Empty).RemoveDiacritics();
	}

	protected override bool GetBlockUnratedValue(UserPolicy config)
	{
		return Enumerable.Contains(config.BlockUnratedItems, (UnratedItem)3);
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)3;
	}

	public ArtistInfo GetLookupInfo()
	{
		ArtistInfo itemLookupInfo = GetItemLookupInfo<ArtistInfo>();
		itemLookupInfo.SongInfos = (from Audio i in GetRecursiveChildren((BaseItem i) => i is Audio)
			select i.GetLookupInfo()).ToList();
		return itemLookupInfo;
	}

	public static string GetPath(string name)
	{
		return GetPath(name, normalizeName: true);
	}

	public static string GetPath(string name, bool normalizeName)
	{
		string path = (normalizeName ? BaseItem.FileSystem.GetValidFilename(name).Trim().TrimEnd(new char[1] { '.' }) : name);
		return System.IO.Path.Combine(BaseItem.ConfigurationManager.ApplicationPaths.ArtistsPath, path);
	}
}
