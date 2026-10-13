using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class UserRootFolder : Folder
{
	private List<Guid> _childrenIds;

	private readonly object _childIdsLock = new object();

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => false;

	[IgnoreDataMember]
	public override bool SupportsPlayedStatus => false;

	[IgnoreDataMember]
	protected override bool SupportsShortcutChildren => true;

	[IgnoreDataMember]
	public override bool IsPreSorted => true;

	protected override List<BaseItem> LoadChildren()
	{
		lock (_childIdsLock)
		{
			if (_childrenIds == null)
			{
				List<BaseItem> list = base.LoadChildren();
				_childrenIds = list.Select((BaseItem i) => i.Id).ToList();
				return list;
			}
			return (from i in _childrenIds.Select(BaseItem.LibraryManager.GetItemById)
				where i != null
				select i).ToList();
		}
	}

	private void ClearCache()
	{
		lock (_childIdsLock)
		{
			_childrenIds = null;
		}
	}

	protected override QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Expected Obj, but got Unknown
		if (query.Recursive)
		{
			return QueryRecursive(query);
		}
		Folder[] userViews = Folder.UserViewManager.GetUserViews(new UserViewQuery
		{
			UserId = query.User.Id,
			PresetViews = query.PresetViews
		});
		int totalRecordCount = userViews.Length;
		QueryResult<BaseItem> val = new QueryResult<BaseItem>
		{
			TotalRecordCount = totalRecordCount
		};
		BaseItem[] items = userViews;
		val.Items = items;
		return val;
	}

	public override int GetChildCount(User user)
	{
		return GetChildren(user, includeLinkedChildren: true).Count;
	}

	protected override IEnumerable<BaseItem> GetEligibleChildrenForRecursiveChildren(User user)
	{
		List<BaseItem> list = base.GetEligibleChildrenForRecursiveChildren(user).ToList();
		list.AddRange(BaseItem.LibraryManager.RootFolder.VirtualChildren);
		return list;
	}

	public override bool BeforeMetadataRefresh(bool replaceAllMetdata)
	{
		ClearCache();
		bool result = base.BeforeMetadataRefresh(replaceAllMetdata);
		if (string.Equals("default", Name, StringComparison.OrdinalIgnoreCase))
		{
			Name = "Media Folders";
			result = true;
		}
		return result;
	}

	protected override IEnumerable<BaseItem> GetNonCachedChildren(IDirectoryService directoryService)
	{
		ClearCache();
		return base.GetNonCachedChildren(directoryService);
	}

	protected override async Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
	{
		ClearCache();
		await base.ValidateChildrenInternal(progress, cancellationToken, recursive, refreshChildMetadata, refreshOptions, directoryService).ConfigureAwait(continueOnCapturedContext: false);
		ClearCache();
		foreach (BaseItem child in Children)
		{
			BaseItem.LibraryManager.RegisterItem(child);
		}
	}
}
