using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Users;

namespace MediaBrowser.Controller.Entities.Movies;

public class BoxSet : Folder, IHasDisplayOrder, IHasLookupInfo<BoxSetInfo>
{
	[IgnoreDataMember]
	protected override bool FilterLinkedChildrenPerUser => true;

	[IgnoreDataMember]
	public override bool SupportsLocalTrailers => true;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => false;

	[IgnoreDataMember]
	public override bool SupportsPeople => true;

	public string DisplayOrder { get; set; }

	[IgnoreDataMember]
	private bool IsLegacyBoxSet
	{
		get
		{
			if (string.IsNullOrEmpty(Path))
			{
				return false;
			}
			if (LinkedChildren.Length != 0)
			{
				return false;
			}
			return !BaseItem.FileSystem.ContainsSubPath(((IApplicationPaths)BaseItem.ConfigurationManager.ApplicationPaths).DataPath, Path);
		}
	}

	[IgnoreDataMember]
	public override bool IsPreSorted => true;

	public Guid[] LibraryFolderIds { get; set; }

	public BoxSet()
	{
		DisplayOrder = "PremiereDate";
	}

	protected override bool GetBlockUnratedValue(UserPolicy config)
	{
		return Enumerable.Contains(config.BlockUnratedItems, (UnratedItem)0);
	}

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 2.0 / 3.0;
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)0;
	}

	protected override IEnumerable<BaseItem> GetNonCachedChildren(IDirectoryService directoryService)
	{
		if (IsLegacyBoxSet)
		{
			return base.GetNonCachedChildren(directoryService);
		}
		return new List<BaseItem>();
	}

	protected override List<BaseItem> LoadChildren()
	{
		if (IsLegacyBoxSet)
		{
			return base.LoadChildren();
		}
		return new List<BaseItem>();
	}

	public override bool IsAuthorizedToDelete(User user, List<Folder> allCollectionFolders)
	{
		return true;
	}

	public override bool IsSaveLocalMetadataEnabled()
	{
		return true;
	}

	public override List<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery query)
	{
		List<BaseItem> children = base.GetChildren(user, includeLinkedChildren, query);
		if (string.Equals(DisplayOrder, "SortName", StringComparison.OrdinalIgnoreCase))
		{
			return BaseItem.LibraryManager.Sort(children, user, new string[1] { "SortName" }, (SortOrder)0).ToList();
		}
		string.Equals(DisplayOrder, "PremiereDate", StringComparison.OrdinalIgnoreCase);
		return BaseItem.LibraryManager.Sort(children, user, new string[3] { "ProductionYear", "PremiereDate", "SortName" }, (SortOrder)0).ToList();
	}

	public override IEnumerable<BaseItem> GetRecursiveChildren(User user, InternalItemsQuery query)
	{
		IEnumerable<BaseItem> recursiveChildren = base.GetRecursiveChildren(user, query);
		if (string.Equals(DisplayOrder, "PremiereDate", StringComparison.OrdinalIgnoreCase))
		{
			return BaseItem.LibraryManager.Sort(recursiveChildren, user, new string[3] { "ProductionYear", "PremiereDate", "SortName" }, (SortOrder)0).ToList();
		}
		return recursiveChildren;
	}

	public BoxSetInfo GetLookupInfo()
	{
		return GetItemLookupInfo<BoxSetInfo>();
	}

	public override bool IsVisible(User user)
	{
		if (IsLegacyBoxSet)
		{
			return base.IsVisible(user);
		}
		if (base.IsVisible(user))
		{
			if (LinkedChildren.Length == 0)
			{
				return true;
			}
			Guid[] libraryFolderIds = GetLibraryFolderIds(user);
			Guid[] libraryFolderIds2 = LibraryFolderIds ?? GetLibraryFolderIds();
			if (libraryFolderIds2.Length == 0)
			{
				return true;
			}
			return libraryFolderIds.Any((Guid i) => Enumerable.Contains(libraryFolderIds2, i));
		}
		return false;
	}

	public override bool IsVisibleStandalone(User user)
	{
		if (IsLegacyBoxSet)
		{
			return base.IsVisibleStandalone(user);
		}
		return IsVisible(user);
	}

	private Guid[] GetLibraryFolderIds(User user)
	{
		return (from i in BaseItem.LibraryManager.GetUserRootFolder().GetChildren(user, includeLinkedChildren: true)
			select i.Id).ToArray();
	}

	public Guid[] GetLibraryFolderIds()
	{
		List<Guid> expandedFolders = new List<Guid>();
		return (from i in FlattenItems(this, expandedFolders).SelectMany((BaseItem i) => BaseItem.LibraryManager.GetCollectionFolders(i))
			select i.Id).Distinct().ToArray();
	}

	private IEnumerable<BaseItem> FlattenItems(IEnumerable<BaseItem> items, List<Guid> expandedFolders)
	{
		return items.SelectMany((BaseItem i) => FlattenItems(i, expandedFolders));
	}

	private IEnumerable<BaseItem> FlattenItems(BaseItem item, List<Guid> expandedFolders)
	{
		if (!(item is BoxSet boxSet))
		{
			return new BaseItem[1] { item };
		}
		if (!expandedFolders.Contains(item.Id))
		{
			expandedFolders.Add(item.Id);
			return FlattenItems(boxSet.GetLinkedChildren(), expandedFolders);
		}
		return Array.Empty<BaseItem>();
	}
}
