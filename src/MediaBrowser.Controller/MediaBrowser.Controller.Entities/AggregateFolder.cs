using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class AggregateFolder : Folder
{
	private readonly ConcurrentBag<BaseItem> _virtualChildren = new ConcurrentBag<BaseItem>();

	private Guid[] _childrenIds;

	private readonly object _childIdsLock = new object();

	private bool _requiresRefresh;

	[IgnoreDataMember]
	public override bool IsPhysicalRoot => true;

	[IgnoreDataMember]
	public override bool SupportsPlayedStatus => false;

	public ConcurrentBag<BaseItem> VirtualChildren => _virtualChildren;

	[IgnoreDataMember]
	public override string[] PhysicalLocations => PhysicalLocationsList;

	public string[] PhysicalLocationsList { get; set; }

	public AggregateFolder()
	{
		PhysicalLocationsList = Array.Empty<string>();
	}

	public override bool CanDelete()
	{
		return false;
	}

	protected override FileSystemMetadata[] GetFileSystemChildren(IDirectoryService directoryService)
	{
		return CreateResolveArgs(directoryService, setPhysicalLocations: true).FileSystemChildren;
	}

	protected override List<BaseItem> LoadChildren()
	{
		lock (_childIdsLock)
		{
			if (_childrenIds == null || _childrenIds.Length == 0)
			{
				List<BaseItem> list = base.LoadChildren();
				_childrenIds = list.Select((BaseItem i) => i.Id).ToArray();
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

	public override bool RequiresRefresh()
	{
		bool flag = base.RequiresRefresh() || _requiresRefresh;
		if (!flag)
		{
			string[] physicalLocations = PhysicalLocations;
			string[] physicalLocations2 = CreateResolveArgs(new DirectoryService(BaseItem.Logger, BaseItem.FileSystem), setPhysicalLocations: false).PhysicalLocations;
			if (!Enumerable.SequenceEqual(physicalLocations, physicalLocations2))
			{
				flag = true;
			}
		}
		return flag;
	}

	public override bool BeforeMetadataRefresh(bool replaceAllMetdata)
	{
		ClearCache();
		bool result = base.BeforeMetadataRefresh(replaceAllMetdata) || _requiresRefresh;
		_requiresRefresh = false;
		return result;
	}

	private ItemResolveArgs CreateResolveArgs(IDirectoryService directoryService, bool setPhysicalLocations)
	{
		ClearCache();
		string containingFolderPath = ContainingFolderPath;
		ItemResolveArgs itemResolveArgs = new ItemResolveArgs(BaseItem.ConfigurationManager.ApplicationPaths, directoryService)
		{
			FileInfo = BaseItem.FileSystem.GetDirectoryInfo(containingFolderPath),
			Path = containingFolderPath
		};
		if (itemResolveArgs.IsDirectory)
		{
			int flattenFolderDepth = 2;
			FileSystemMetadata[] filteredFileSystemEntries = FileData.GetFilteredFileSystemEntries(directoryService, itemResolveArgs.Path, BaseItem.FileSystem, CollectionFolder.ApplicationHost, BaseItem.Logger, itemResolveArgs, flattenFolderDepth);
			filteredFileSystemEntries = BaseItem.LibraryManager.NormalizeRootPathList(filteredFileSystemEntries).ToArray();
			itemResolveArgs.FileSystemChildren = filteredFileSystemEntries;
		}
		_requiresRefresh = _requiresRefresh || !Enumerable.SequenceEqual(itemResolveArgs.PhysicalLocations, PhysicalLocations);
		if (setPhysicalLocations)
		{
			PhysicalLocationsList = itemResolveArgs.PhysicalLocations;
		}
		return itemResolveArgs;
	}

	protected override IEnumerable<BaseItem> GetNonCachedChildren(IDirectoryService directoryService)
	{
		return base.GetNonCachedChildren(directoryService).Concat(_virtualChildren);
	}

	protected override async Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
	{
		ClearCache();
		await base.ValidateChildrenInternal(progress, cancellationToken, recursive, refreshChildMetadata, refreshOptions, directoryService).ConfigureAwait(continueOnCapturedContext: false);
		ClearCache();
	}

	public void AddVirtualChild(BaseItem child)
	{
		if (child == null)
		{
			throw new ArgumentNullException();
		}
		_virtualChildren.Add(child);
	}

	public BaseItem FindVirtualChild(Guid id)
	{
		if (id.Equals(Guid.Empty))
		{
			throw new ArgumentNullException("id");
		}
		foreach (BaseItem virtualChild in _virtualChildren)
		{
			if (virtualChild.Id == id)
			{
				return virtualChild;
			}
		}
		return null;
	}
}
