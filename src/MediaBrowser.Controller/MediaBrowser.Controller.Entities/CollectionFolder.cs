using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class CollectionFolder : Folder, ICollectionFolder, IHasCollectionType
{
	private static readonly Dictionary<string, LibraryOptions> LibraryOptions = new Dictionary<string, LibraryOptions>();

	private bool _requiresRefresh;

	public static IXmlSerializer XmlSerializer { get; set; }

	public static IJsonSerializer JsonSerializer { get; set; }

	public static IServerApplicationHost ApplicationHost { get; set; }

	[IgnoreDataMember]
	public override bool SupportsPlayedStatus => false;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => false;

	public string CollectionType { get; set; }

	[IgnoreDataMember]
	public override Guid DisplayPreferencesId => Id;

	[IgnoreDataMember]
	public override string[] PhysicalLocations => PhysicalLocationsList;

	public string[] PhysicalLocationsList { get; set; }

	public Guid[] PhysicalFolderIds { get; set; }

	[IgnoreDataMember]
	public override IEnumerable<BaseItem> Children => GetActualChildren();

	[IgnoreDataMember]
	public override bool SupportsPeople => false;

	public CollectionFolder()
	{
		PhysicalLocationsList = Array.Empty<string>();
		PhysicalFolderIds = Array.Empty<Guid>();
	}

	public override bool CanDelete()
	{
		return false;
	}

	public LibraryOptions GetLibraryOptions()
	{
		return GetLibraryOptions(Path);
	}

	private static LibraryOptions LoadLibraryOptions(string path)
	{
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Expected Obj, but got Unknown
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Expected Obj, but got Unknown
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Expected Obj, but got Unknown
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected Obj, but got Unknown
		try
		{
			object obj = XmlSerializer.DeserializeFromFile(typeof(LibraryOptions), GetLibraryOptionsPath(path));
			LibraryOptions val = (LibraryOptions)((obj is LibraryOptions) ? obj : null);
			if (val == null)
			{
				return new LibraryOptions();
			}
			MediaPathInfo[] pathInfos = val.PathInfos;
			foreach (MediaPathInfo val2 in pathInfos)
			{
				if (!string.IsNullOrEmpty(val2.Path))
				{
					val2.Path = ApplicationHost.ExpandVirtualPath(val2.Path);
				}
			}
			return val;
		}
		catch (FileNotFoundException)
		{
			return new LibraryOptions();
		}
		catch (IOException)
		{
			return new LibraryOptions();
		}
		catch (Exception ex3)
		{
			BaseItem.Logger.ErrorException("Error loading library options", ex3, Array.Empty<object>());
			return new LibraryOptions();
		}
	}

	private static string GetLibraryOptionsPath(string path)
	{
		return System.IO.Path.Combine(path, "options.xml");
	}

	public void UpdateLibraryOptions(LibraryOptions options)
	{
		SaveLibraryOptions(Path, options);
	}

	public static LibraryOptions GetLibraryOptions(string path)
	{
		lock (LibraryOptions)
		{
			if (!LibraryOptions.TryGetValue(path, out var value))
			{
				value = LoadLibraryOptions(path);
				LibraryOptions[path] = value;
			}
			return value;
		}
	}

	public static void SaveLibraryOptions(string path, LibraryOptions options)
	{
		lock (LibraryOptions)
		{
			LibraryOptions[path] = options;
			LibraryOptions val = JsonSerializer.DeserializeFromString<LibraryOptions>(JsonSerializer.SerializeToString((object)options));
			MediaPathInfo[] pathInfos = val.PathInfos;
			foreach (MediaPathInfo val2 in pathInfos)
			{
				if (!string.IsNullOrEmpty(val2.Path))
				{
					val2.Path = ApplicationHost.ReverseVirtualPath(val2.Path);
				}
			}
			XmlSerializer.SerializeToFile((object)val, GetLibraryOptionsPath(path));
		}
	}

	public static void OnCollectionFolderChange()
	{
		lock (LibraryOptions)
		{
			LibraryOptions.Clear();
		}
	}

	public override bool IsSaveLocalMetadataEnabled()
	{
		return true;
	}

	protected override FileSystemMetadata[] GetFileSystemChildren(IDirectoryService directoryService)
	{
		return CreateResolveArgs(directoryService, setPhysicalLocations: true).FileSystemChildren;
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
		if (!flag)
		{
			Guid[] physicalFolderIds = PhysicalFolderIds;
			List<Guid> second = (from i in GetPhysicalFolders(enableCache: false)
				select i.Id).ToList();
			if (!physicalFolderIds.SequenceEqual(second))
			{
				flag = true;
			}
		}
		return flag;
	}

	public override bool BeforeMetadataRefresh(bool replaceAllMetdata)
	{
		bool result = base.BeforeMetadataRefresh(replaceAllMetdata) || _requiresRefresh;
		_requiresRefresh = false;
		return result;
	}

	public override double? GetRefreshProgress()
	{
		List<Folder> list = GetPhysicalFolders(enableCache: true).ToList();
		double num = 0.0;
		int num2 = 0;
		foreach (Folder item in list)
		{
			double? refreshProgress = BaseItem.ProviderManager.GetRefreshProgress(item.Id);
			if (refreshProgress.HasValue)
			{
				num += refreshProgress.Value;
				num2++;
			}
		}
		if (num2 == 0)
		{
			return null;
		}
		return num / (double)num2;
	}

	protected override bool RefreshLinkedChildren(IEnumerable<FileSystemMetadata> fileSystemChildren)
	{
		return RefreshLinkedChildrenInternal(setFolders: true);
	}

	private bool RefreshLinkedChildrenInternal(bool setFolders)
	{
		List<Folder> source = GetPhysicalFolders(enableCache: false).ToList();
		List<LinkedChild> list = source.SelectMany((Folder c) => c.LinkedChildren).ToList();
		bool result = !list.SequenceEqual(LinkedChildren, new LinkedChildComparer(BaseItem.FileSystem));
		LinkedChildren = LinqExtensions.ToArray<LinkedChild>((IEnumerable<LinkedChild>)list, list.Count);
		Guid[] physicalFolderIds = PhysicalFolderIds;
		Guid[] array = source.Select((Folder i) => i.Id).ToArray();
		if (!Enumerable.SequenceEqual(physicalFolderIds, array))
		{
			result = true;
			if (setFolders)
			{
				PhysicalFolderIds = array;
			}
		}
		return result;
	}

	private ItemResolveArgs CreateResolveArgs(IDirectoryService directoryService, bool setPhysicalLocations)
	{
		string containingFolderPath = ContainingFolderPath;
		ItemResolveArgs itemResolveArgs = new ItemResolveArgs(BaseItem.ConfigurationManager.ApplicationPaths, directoryService)
		{
			FileInfo = BaseItem.FileSystem.GetDirectoryInfo(containingFolderPath),
			Path = containingFolderPath,
			Parent = (GetParent() as Folder),
			CollectionType = CollectionType
		};
		if (itemResolveArgs.IsDirectory)
		{
			int flattenFolderDepth = 0;
			FileSystemMetadata[] filteredFileSystemEntries = FileData.GetFilteredFileSystemEntries(directoryService, itemResolveArgs.Path, BaseItem.FileSystem, ApplicationHost, BaseItem.Logger, itemResolveArgs, flattenFolderDepth);
			itemResolveArgs.FileSystemChildren = filteredFileSystemEntries;
		}
		_requiresRefresh = _requiresRefresh || !Enumerable.SequenceEqual(itemResolveArgs.PhysicalLocations, PhysicalLocations);
		if (setPhysicalLocations)
		{
			PhysicalLocationsList = itemResolveArgs.PhysicalLocations;
		}
		return itemResolveArgs;
	}

	protected override Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
	{
		return Task.CompletedTask;
	}

	public IEnumerable<BaseItem> GetActualChildren()
	{
		return GetPhysicalFolders(enableCache: true).SelectMany((Folder c) => c.Children);
	}

	public IEnumerable<Folder> GetPhysicalFolders()
	{
		return GetPhysicalFolders(enableCache: true);
	}

	private IEnumerable<Folder> GetPhysicalFolders(bool enableCache)
	{
		if (enableCache)
		{
			return PhysicalFolderIds.Select((Guid i) => BaseItem.LibraryManager.GetItemById(i)).OfType<Folder>();
		}
		List<Folder> rootChildren = BaseItem.LibraryManager.RootFolder.Children.OfType<Folder>().ToList();
		return LinqExtensions.DistinctBy<Folder, Guid>(PhysicalLocations.Where((string i) => !BaseItem.FileSystem.AreEqual(i, Path)).SelectMany((string i) => GetPhysicalParents(i, rootChildren)), (Func<Folder, Guid>)((Folder i) => i.Id));
	}

	private IEnumerable<Folder> GetPhysicalParents(string path, List<Folder> rootChildren)
	{
		List<Folder> list = rootChildren.Where((Folder i) => BaseItem.FileSystem.AreEqual(i.Path, path)).ToList();
		if (list.Count == 0 && BaseItem.LibraryManager.FindByPath(path, true) is Folder item)
		{
			list.Add(item);
		}
		return list;
	}
}
