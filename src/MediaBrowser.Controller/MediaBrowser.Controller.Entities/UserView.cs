using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class UserView : Folder, IHasCollectionType
{
	public static ITVSeriesManager TVSeriesManager;

	public static IPlaylistManager PlaylistManager;

	private static string[] ViewTypesEligibleForGrouping = new string[3]
	{
		"movies",
		"tvshows",
		string.Empty
	};

	private static string[] OriginalFolderViewTypes = new string[7] { "games", "books", "musicvideos", "homevideos", "photos", "music", "boxsets" };

	public string ViewType { get; set; }

	public new Guid DisplayParentId { get; set; }

	public Guid? UserId { get; set; }

	[IgnoreDataMember]
	public string CollectionType => ViewType;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => false;

	[IgnoreDataMember]
	public override bool SupportsPlayedStatus => false;

	[IgnoreDataMember]
	public override bool SupportsPeople => false;

	public override IEnumerable<Guid> GetIdsForAncestorQuery()
	{
		List<Guid> list = new List<Guid>();
		if (!DisplayParentId.Equals(Guid.Empty))
		{
			list.Add(DisplayParentId);
		}
		else if (!ParentId.Equals(Guid.Empty))
		{
			list.Add(ParentId);
		}
		else
		{
			list.Add(Id);
		}
		return list;
	}

	public override int GetChildCount(User user)
	{
		return GetChildren(user, includeLinkedChildren: true).Count;
	}

	protected override QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)
	{
		Folder folder = this;
		if (!DisplayParentId.Equals(Guid.Empty))
		{
			folder = (BaseItem.LibraryManager.GetItemById(DisplayParentId) as Folder) ?? folder;
		}
		else if (!ParentId.Equals(Guid.Empty))
		{
			folder = (BaseItem.LibraryManager.GetItemById(ParentId) as Folder) ?? folder;
		}
		return new UserViewBuilder(Folder.UserViewManager, BaseItem.LibraryManager, BaseItem.Logger, BaseItem.UserDataManager, TVSeriesManager, BaseItem.ConfigurationManager, PlaylistManager).GetUserItems(folder, this, CollectionType, query);
	}

	public override List<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery query)
	{
		if (query == null)
		{
			query = new InternalItemsQuery(user);
		}
		query.EnableTotalRecordCount = false;
		return GetItemList(query).ToList();
	}

	public override bool CanDelete()
	{
		return false;
	}

	public override bool IsSaveLocalMetadataEnabled()
	{
		return true;
	}

	public override IEnumerable<BaseItem> GetRecursiveChildren(User user, InternalItemsQuery query)
	{
		query.SetUser(user);
		query.Recursive = true;
		query.EnableTotalRecordCount = false;
		query.ForceDirect = true;
		return GetItemList(query);
	}

	protected override IEnumerable<BaseItem> GetEligibleChildrenForRecursiveChildren(User user)
	{
		return GetChildren(user, includeLinkedChildren: false);
	}

	public static bool IsUserSpecific(Folder folder)
	{
		if (!(folder is ICollectionFolder collectionFolder))
		{
			return false;
		}
		return string.Equals(collectionFolder.CollectionType, "playlists", StringComparison.OrdinalIgnoreCase);
	}

	public static bool IsEligibleForGrouping(Folder folder)
	{
		if (folder is ICollectionFolder collectionFolder)
		{
			return IsEligibleForGrouping(collectionFolder.CollectionType);
		}
		return false;
	}

	public static bool IsEligibleForGrouping(string viewType)
	{
		return ViewTypesEligibleForGrouping.Contains(viewType ?? string.Empty, StringComparer.OrdinalIgnoreCase);
	}

	public static bool EnableOriginalFolder(string viewType)
	{
		return OriginalFolderViewTypes.Contains(viewType ?? string.Empty, StringComparer.OrdinalIgnoreCase);
	}

	protected override Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
	{
		return Task.CompletedTask;
	}
}
