using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Playlists;

public class Playlist : Folder, IHasShares
{
	public static string[] SupportedExtensions = new string[5] { ".m3u", ".m3u8", ".pls", ".wpl", ".zpl" };

	public Guid OwnerUserId { get; set; }

	public Share[] Shares { get; set; }

	[IgnoreDataMember]
	public bool IsFile => IsPlaylistFile(Path);

	[IgnoreDataMember]
	public override string FileNameWithoutExtension
	{
		get
		{
			if (IsPlaylistFile(Path))
			{
				return System.IO.Path.GetFileNameWithoutExtension(Path);
			}
			return base.FileNameWithoutExtension;
		}
	}

	[IgnoreDataMember]
	public override string ContainingFolderPath
	{
		get
		{
			string path = Path;
			if (IsPlaylistFile(path))
			{
				return BaseItem.FileSystem.GetDirectoryName(path);
			}
			return path;
		}
	}

	[IgnoreDataMember]
	protected override bool FilterLinkedChildrenPerUser => true;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => false;

	[IgnoreDataMember]
	public override bool SupportsPlayedStatus => string.Equals(MediaType, "Video", StringComparison.OrdinalIgnoreCase);

	[IgnoreDataMember]
	public override bool AlwaysScanInternalMetadataPath => true;

	[IgnoreDataMember]
	public override bool SupportsCumulativeRunTimeTicks => true;

	[IgnoreDataMember]
	public override bool IsPreSorted => true;

	public string PlaylistMediaType { get; set; }

	[IgnoreDataMember]
	public override string MediaType => PlaylistMediaType;

	[IgnoreDataMember]
	private bool IsSharedItem
	{
		get
		{
			string path = Path;
			if (string.IsNullOrEmpty(path))
			{
				return false;
			}
			return BaseItem.FileSystem.ContainsSubPath(((IApplicationPaths)BaseItem.ConfigurationManager.ApplicationPaths).DataPath, path);
		}
	}

	public Playlist()
	{
		Shares = Array.Empty<Share>();
	}

	public static bool IsPlaylistFile(string path)
	{
		return System.IO.Path.HasExtension(path);
	}

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 1.0;
	}

	public override bool IsAuthorizedToDelete(User user, List<Folder> allCollectionFolders)
	{
		return true;
	}

	public override bool IsSaveLocalMetadataEnabled()
	{
		return true;
	}

	protected override List<BaseItem> LoadChildren()
	{
		return new List<BaseItem>();
	}

	protected override Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
	{
		return Task.CompletedTask;
	}

	public override List<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery query)
	{
		return GetPlayableItems(user, query);
	}

	protected override IEnumerable<BaseItem> GetNonCachedChildren(IDirectoryService directoryService)
	{
		return new List<BaseItem>();
	}

	public override IEnumerable<BaseItem> GetRecursiveChildren(User user, InternalItemsQuery query)
	{
		return GetPlayableItems(user, query);
	}

	public IEnumerable<Tuple<LinkedChild, BaseItem>> GetManageableItems()
	{
		return GetLinkedChildrenInfos();
	}

	private List<BaseItem> GetPlayableItems(User user, InternalItemsQuery query)
	{
		if (query == null)
		{
			query = new InternalItemsQuery(user);
		}
		query.IsFolder = false;
		return base.GetChildren(user, includeLinkedChildren: true, query);
	}

	public static List<BaseItem> GetPlaylistItems(string playlistMediaType, IEnumerable<BaseItem> inputItems, User user, DtoOptions options)
	{
		if (user != null)
		{
			inputItems = inputItems.Where((BaseItem i) => i.IsVisible(user));
		}
		List<BaseItem> list = new List<BaseItem>();
		foreach (BaseItem inputItem in inputItems)
		{
			IEnumerable<BaseItem> playlistItems = GetPlaylistItems(inputItem, user, playlistMediaType, options);
			list.AddRange(playlistItems);
		}
		return list;
	}

	private static IEnumerable<BaseItem> GetPlaylistItems(BaseItem item, User user, string mediaType, DtoOptions options)
	{
		if (item is MusicGenre musicGenre)
		{
			return BaseItem.LibraryManager.GetItemList(new InternalItemsQuery(user)
			{
				Recursive = true,
				IncludeItemTypes = new string[1] { typeof(Audio).Name },
				GenreIds = new Guid[1] { musicGenre.Id },
				OrderBy = new string[3] { "AlbumArtist", "Album", "SortName" }.Select((string i) => (i, (SortOrder)0)).ToArray(),
				DtoOptions = options
			});
		}
		if (item is MusicArtist musicArtist)
		{
			return BaseItem.LibraryManager.GetItemList(new InternalItemsQuery(user)
			{
				Recursive = true,
				IncludeItemTypes = new string[1] { typeof(Audio).Name },
				ArtistIds = new Guid[1] { musicArtist.Id },
				OrderBy = new string[3] { "AlbumArtist", "Album", "SortName" }.Select((string i) => (i, (SortOrder)0)).ToArray(),
				DtoOptions = options
			});
		}
		if (!(item is Folder folder))
		{
			return new BaseItem[1] { item };
		}
		InternalItemsQuery internalItemsQuery = new InternalItemsQuery(user);
		internalItemsQuery.Recursive = true;
		internalItemsQuery.IsFolder = false;
		internalItemsQuery.OrderBy = new string[1] { "SortName" }.Select((string i) => (i, (SortOrder)0)).ToArray();
		internalItemsQuery.MediaTypes = new string[1] { mediaType };
		internalItemsQuery.EnableTotalRecordCount = false;
		internalItemsQuery.DtoOptions = options;
		InternalItemsQuery query = internalItemsQuery;
		return folder.GetItemList(query);
	}

	public void SetMediaType(string value)
	{
		PlaylistMediaType = value;
	}

	public override bool IsVisible(User user)
	{
		if (!IsSharedItem)
		{
			return base.IsVisible(user);
		}
		if (user.Id == OwnerUserId)
		{
			return true;
		}
		Share[] shares = Shares;
		if (shares.Length == 0)
		{
			return base.IsVisible(user);
		}
		string b = user.Id.ToString("N");
		Share[] array = shares;
		for (int i = 0; i < array.Length; i++)
		{
			if (string.Equals(array[i].UserId, b, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	public override bool IsVisibleStandalone(User user)
	{
		if (!IsSharedItem)
		{
			return base.IsVisibleStandalone(user);
		}
		return IsVisible(user);
	}
}
