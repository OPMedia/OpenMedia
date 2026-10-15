using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Progress;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class Folder : BaseItem
{
	public static IUserViewManager UserViewManager { get; set; }

	public bool IsRoot { get; set; }

	public LinkedChild[] LinkedChildren { get; set; }

	[IgnoreDataMember]
	public DateTime? DateLastMediaAdded { get; set; }

	[IgnoreDataMember]
	public override bool SupportsThemeMedia => true;

	[IgnoreDataMember]
	public virtual bool IsPreSorted => false;

	[IgnoreDataMember]
	public virtual bool IsPhysicalRoot => false;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => true;

	[IgnoreDataMember]
	public override bool SupportsPlayedStatus => true;

	[IgnoreDataMember]
	public override bool IsFolder => true;

	[IgnoreDataMember]
	public override bool IsDisplayedAsFolder => true;

	[IgnoreDataMember]
	public virtual bool SupportsCumulativeRunTimeTicks => false;

	[IgnoreDataMember]
	public virtual bool SupportsDateLastMediaAdded => false;

	[IgnoreDataMember]
	public override bool SupportsExternalTransfer => CanDownloadAsFolder();

	[IgnoreDataMember]
	public override string FileNameWithoutExtension
	{
		get
		{
			if (IsFileProtocol)
			{
				return System.IO.Path.GetFileName(Path);
			}
			return null;
		}
	}

	[IgnoreDataMember]
	protected virtual bool SupportsShortcutChildren => false;

	[IgnoreDataMember]
	public virtual IEnumerable<BaseItem> Children => LoadChildren();

	[IgnoreDataMember]
	public IEnumerable<BaseItem> RecursiveChildren => GetRecursiveChildren();

	public static ICollectionManager CollectionManager { get; set; }

	protected virtual bool FilterLinkedChildrenPerUser => false;

	[IgnoreDataMember]
	protected override bool SupportsOwnedItems
	{
		get
		{
			if (!base.SupportsOwnedItems)
			{
				return SupportsShortcutChildren;
			}
			return true;
		}
	}

	[IgnoreDataMember]
	public virtual bool SupportsUserDataFromChildren
	{
		get
		{
			if (this is ICollectionFolder)
			{
				return false;
			}
			if (this is UserView)
			{
				return false;
			}
			if (this is UserRootFolder)
			{
				return false;
			}
			if (this is Channel)
			{
				return false;
			}
			if (SourceType != SourceType.Library)
			{
				return false;
			}
			if (this is IItemByName && !(this is IHasDualAccess { IsAccessedByName: false }))
			{
				return false;
			}
			return true;
		}
	}

	public Folder()
	{
		LinkedChildren = Array.Empty<LinkedChild>();
	}

	public override bool CanDelete()
	{
		if (IsRoot)
		{
			return false;
		}
		return base.CanDelete();
	}

	public override bool RequiresRefresh()
	{
		bool result = base.RequiresRefresh();
		if (SupportsCumulativeRunTimeTicks && !RunTimeTicks.HasValue)
		{
			result = true;
		}
		return result;
	}

	protected override bool IsAllowTagFilterEnforced()
	{
		if (this is ICollectionFolder)
		{
			return false;
		}
		if (this is UserView)
		{
			return false;
		}
		return true;
	}

	public void AddChild(BaseItem item, CancellationToken cancellationToken)
	{
		item.SetParent(this);
		if (item.Id.Equals(Guid.Empty))
		{
			item.Id = BaseItem.LibraryManager.GetNewItemId(item.Path, item.GetType());
		}
		if (item.DateCreated == DateTime.MinValue)
		{
			item.DateCreated = DateTime.UtcNow;
		}
		if (item.DateModified == DateTime.MinValue)
		{
			item.DateModified = DateTime.UtcNow;
		}
		BaseItem.LibraryManager.CreateItem(item, this);
	}

	public override bool IsVisible(User user)
	{
		if (this is ICollectionFolder && !(this is BasePluginFolder))
		{
			if (user.Policy.BlockedMediaFolders != null)
			{
				if (user.Policy.BlockedMediaFolders.Contains(Id.ToString("N"), StringComparer.OrdinalIgnoreCase) || user.Policy.BlockedMediaFolders.Contains(Name, StringComparer.OrdinalIgnoreCase))
				{
					return false;
				}
			}
			else if (!user.Policy.EnableAllFolders && !user.Policy.EnabledFolders.Contains(Id.ToString("N"), StringComparer.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		return base.IsVisible(user);
	}

	protected virtual List<BaseItem> LoadChildren()
	{
		return GetCachedChildren();
	}

	public override double? GetRefreshProgress()
	{
		return BaseItem.ProviderManager.GetRefreshProgress(Id);
	}

	public Task ValidateChildren(IProgress<double> progress, CancellationToken cancellationToken)
	{
		return ValidateChildren(progress, cancellationToken, new MetadataRefreshOptions((IDirectoryService)new DirectoryService(BaseItem.Logger, BaseItem.FileSystem)));
	}

	public Task ValidateChildren(IProgress<double> progress, CancellationToken cancellationToken, MetadataRefreshOptions metadataRefreshOptions, bool recursive = true)
	{
		return ValidateChildrenInternal(progress, cancellationToken, recursive, refreshChildMetadata: true, metadataRefreshOptions, metadataRefreshOptions.DirectoryService);
	}

	private Dictionary<Guid, BaseItem> GetActualChildrenDictionary()
	{
		Dictionary<Guid, BaseItem> dictionary = new Dictionary<Guid, BaseItem>();
		foreach (BaseItem item in Children.ToList())
		{
			Guid id = item.Id;
			if (dictionary.ContainsKey(id))
			{
				BaseItem.Logger.Error("Found folder containing items with duplicate id. Path: {0}, Child Name: {1}", new object[2]
				{
					Path ?? Name,
					item.Path ?? item.Name
				});
			}
			else
			{
				dictionary[id] = item;
			}
		}
		return dictionary;
	}

	protected override void TriggerOnRefreshStart()
	{
	}

	protected override void TriggerOnRefreshComplete()
	{
	}

	protected virtual async Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
	{
		if (recursive)
		{
			BaseItem.ProviderManager.OnRefreshStart(this);
		}
		try
		{
			await ValidateChildrenInternal2(progress, cancellationToken, recursive, refreshChildMetadata, refreshOptions, directoryService).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			if (recursive)
			{
				BaseItem.ProviderManager.OnRefreshComplete(this);
			}
		}
	}

	private async Task ValidateChildrenInternal2(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
	{
		cancellationToken.ThrowIfCancellationRequested();
		List<BaseItem> validChildren = new List<BaseItem>();
		bool validChildrenNeedGeneration = false;
		if (IsFileProtocol)
		{
			IEnumerable<BaseItem> nonCachedChildren;
			try
			{
				nonCachedChildren = GetNonCachedChildren(directoryService);
			}
			catch (Exception)
			{
				return;
			}
			progress.Report(5.0);
			if (recursive)
			{
				BaseItem.ProviderManager.OnRefreshProgress(this, 5.0);
			}
			Dictionary<Guid, BaseItem> actualChildrenDictionary = GetActualChildrenDictionary();
			List<BaseItem> list = new List<BaseItem>();
			cancellationToken.ThrowIfCancellationRequested();
			foreach (BaseItem item in nonCachedChildren)
			{
				if (actualChildrenDictionary.TryGetValue(item.Id, out var value))
				{
					validChildren.Add(value);
					if (value.UpdateFromResolvedItem(item) > ItemUpdateType.None)
					{
						value.UpdateToRepository(ItemUpdateType.MetadataImport, cancellationToken);
					}
				}
				else
				{
					item.SetParent(this);
					list.Add(item);
					validChildren.Add(item);
				}
			}
			if (list.Count > 0 || actualChildrenDictionary.Count != validChildren.Count)
			{
				// T4.7 根 GUID 扫描侧守卫 v3（phase4-design §3.1 选项 C）：
				// 共享 DB 多实例拓扑下根行跨实例同 GUID（GetNewItemId 的 ProgramData 前缀剥离），
				// 本实例配置里没有的库属于其他实例。剪枝分两层：UserRootFolder 的子项是
				// CollectionFolder（v2 按 Id 判外库），AggregateFolder 的子项是各库的物理
				// 媒体文件夹（v2 护不到——08:50/09:02 两轮复现实证：库行保住、物理文件夹
				// 仍被剪枝级联清库）。v3 统一规则：根级文件夹（Aggregate/UserRoot）的剪枝
				// 受害者，仅当其路径在本实例可解析位置（nonCachedChildren 路径）之下才可删；
				// 否则跳过 + 警告。守卫语义"实例只管理自己知道的库"。删库正道走 API
				// （RemoveVirtualFolder 同步清 DB 行）；手动删磁盘目录场景同降级为警告+
				// 条目保留（设计文档明示）。非根级文件夹（库内部子树）剪枝不受影响。
				var isRootLevelFolder = this is AggregateFolder || this is UserRootFolder;

				foreach (BaseItem item2 in actualChildrenDictionary.Values.Except(validChildren).ToList())
				{
					if (item2.IsFileProtocol)
					{
						if (isRootLevelFolder)
						{
							var prunable = nonCachedChildren.Any(n =>
								BaseItem.FileSystem.AreEqual(item2.Path, n.Path) ||
								BaseItem.FileSystem.ContainsSubPath(n.Path, item2.Path));

							if (!prunable)
							{
								BaseItem.Logger.Warn("Root-level pruning skipped for {0} ({1}): path is not under this instance's library locations (foreign instance library or manually removed folder). Preserving items; use the library API to remove libraries.", new object[2] { item2.Name, item2.Path });
								continue;
							}
						}
						BaseItem.Logger.Debug("Removed item: " + item2.Path, Array.Empty<object>());
						item2.SetParent(null);
						BaseItem.LibraryManager.DeleteItem(item2, new DeleteOptions
						{
							DeleteFileLocation = false
						}, this, notifyParentItem: false);
					}
				}
				BaseItem.LibraryManager.CreateItems(list, this, cancellationToken);
			}
		}
		else
		{
			validChildrenNeedGeneration = true;
		}
		progress.Report(10.0);
		if (recursive)
		{
			BaseItem.ProviderManager.OnRefreshProgress(this, 10.0);
		}
		cancellationToken.ThrowIfCancellationRequested();
		if (recursive)
		{
			ActionableProgress<double> val = new ActionableProgress<double>();
			Folder folder = this;
			val.RegisterAction((Action<double>)((double p) =>
			{
				double num = 0.8 * p + 10.0;
				progress.Report(num);
				BaseItem.ProviderManager.OnRefreshProgress(folder, num);
			}));
			if (validChildrenNeedGeneration)
			{
				validChildren = Children.ToList();
				validChildrenNeedGeneration = false;
			}
			await ValidateSubFolders(validChildren.OfType<Folder>().ToList(), directoryService, (IProgress<double>)val, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (!refreshChildMetadata)
		{
			return;
		}
		progress.Report(90.0);
		if (recursive)
		{
			BaseItem.ProviderManager.OnRefreshProgress(this, 90.0);
		}
		IMetadataContainer metadataContainer = this as IMetadataContainer;
		ActionableProgress<double> val2 = new ActionableProgress<double>();
		Folder folder2 = this;
		val2.RegisterAction((Action<double>)((double p) =>
		{
			double num = 0.1 * p + 90.0;
			progress.Report(num);
			if (recursive)
			{
				BaseItem.ProviderManager.OnRefreshProgress(folder2, num);
			}
		}));
		if (metadataContainer != null)
		{
			await RefreshAllMetadataForContainer(metadataContainer, refreshOptions, (IProgress<double>)val2, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		if (validChildrenNeedGeneration)
		{
			validChildren = Children.ToList();
		}
		await RefreshMetadataRecursive(validChildren, refreshOptions, recursive, (IProgress<double>)val2, cancellationToken);
	}

	private async Task RefreshMetadataRecursive(List<BaseItem> children, MetadataRefreshOptions refreshOptions, bool recursive, IProgress<double> progress, CancellationToken cancellationToken)
	{
		int numComplete = 0;
		int count = children.Count;
		double num = 0.0;
		foreach (BaseItem child in children)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ActionableProgress<double> val = new ActionableProgress<double>();
			double currentInnerPercent = num;
			val.RegisterAction((Action<double>)((double p) =>
			{
				double num3 = currentInnerPercent;
				num3 += p / (double)count;
				progress.Report(num3);
			}));
			await RefreshChildMetadata(child, refreshOptions, recursive && child.IsFolder, (IProgress<double>)val, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			numComplete++;
			double num2 = numComplete;
			num2 /= (double)count;
			num2 *= 100.0;
			num = num2;
			progress.Report(num2);
		}
	}

	private async Task RefreshAllMetadataForContainer(IMetadataContainer container, MetadataRefreshOptions refreshOptions, IProgress<double> progress, CancellationToken cancellationToken)
	{
		if (container is Series series)
		{
			await series.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		await container.RefreshAllMetadata(refreshOptions, progress, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task RefreshChildMetadata(BaseItem child, MetadataRefreshOptions refreshOptions, bool recursive, IProgress<double> progress, CancellationToken cancellationToken)
	{
		if (child is IMetadataContainer container)
		{
			await RefreshAllMetadataForContainer(container, refreshOptions, progress, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		if (refreshOptions.RefreshItem(child))
		{
			await child.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (recursive && child is Folder folder)
		{
			await folder.RefreshMetadataRecursive(folder.Children.ToList(), refreshOptions, recursive: true, progress, cancellationToken);
		}
	}

	private async Task ValidateSubFolders(IList<Folder> children, IDirectoryService directoryService, IProgress<double> progress, CancellationToken cancellationToken)
	{
		int numComplete = 0;
		int count = children.Count;
		double num = 0.0;
		foreach (Folder child in children)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ActionableProgress<double> val = new ActionableProgress<double>();
			double currentInnerPercent = num;
			val.RegisterAction((Action<double>)((double p) =>
			{
				double num3 = currentInnerPercent;
				num3 += p / (double)count;
				progress.Report(num3);
			}));
			await child.ValidateChildrenInternal((IProgress<double>)val, cancellationToken, recursive: true, refreshChildMetadata: false, null, directoryService).ConfigureAwait(continueOnCapturedContext: false);
			numComplete++;
			double num2 = numComplete;
			num2 /= (double)count;
			num2 *= 100.0;
			num = num2;
			progress.Report(num2);
		}
	}

	protected virtual IEnumerable<BaseItem> GetNonCachedChildren(IDirectoryService directoryService)
	{
		string contentType = BaseItem.LibraryManager.GetContentType(this);
		LibraryOptions libraryOptions = BaseItem.LibraryManager.GetLibraryOptions(this);
		return BaseItem.LibraryManager.ResolvePaths(GetFileSystemChildren(directoryService), directoryService, this, libraryOptions, contentType);
	}

	protected List<BaseItem> GetCachedChildren()
	{
		return BaseItem.ItemRepository.GetItemList(new InternalItemsQuery
		{
			Parent = this,
			GroupByPresentationUniqueKey = false,
			DtoOptions = new DtoOptions(allFields: true)
		});
	}

	public virtual int GetChildCount(User user)
	{
		if (LinkedChildren.Length != 0 && !(this is ICollectionFolder))
		{
			return GetChildren(user, includeLinkedChildren: true).Count;
		}
		return GetItems(new InternalItemsQuery(user)
		{
			Recursive = false,
			Limit = 0,
			Parent = this,
			DtoOptions = new DtoOptions(allFields: false)
			{
				EnableImages = false
			}
		}).TotalRecordCount;
	}

	// P7 批次：GetRecursiveChildCount 方法级 TTL 缓存（FillUserDataDtoValues 的
	// 结果缓存实测仍有 miss 路径——不同 DtoOptions 的调用链绕过；方法级兜底，
	// trace 实测 15 次/两请求 → 缓存后 1 次/30s）
	private static readonly Dictionary<string, Tuple<int, DateTime>> _recursiveChildCountCache =
		new Dictionary<string, Tuple<int, DateTime>>(StringComparer.OrdinalIgnoreCase);
	private static readonly object _recursiveChildCountCacheLock = new object();
	private static readonly TimeSpan RecursiveChildCountCacheTtl = TimeSpan.FromSeconds(30);

	public virtual int GetRecursiveChildCount(User user)
	{
		var cacheKey = (user == null ? string.Empty : user.Id.ToString("N")) + "|" + Id.ToString("N");

		lock (_recursiveChildCountCacheLock)
		{
			if (_recursiveChildCountCache.TryGetValue(cacheKey, out var cached)
				&& (DateTime.UtcNow - cached.Item2) < RecursiveChildCountCacheTtl)
			{
				return cached.Item1;
			}
		}

		var count = GetItems(new InternalItemsQuery(user)
		{
			Recursive = true,
			IsFolder = false,
			IsVirtualItem = false,
			EnableTotalRecordCount = true,
			Limit = 0,
			DtoOptions = new DtoOptions(allFields: false)
			{
				EnableImages = false
			}
		}).TotalRecordCount;

		lock (_recursiveChildCountCacheLock)
		{
			_recursiveChildCountCache[cacheKey] = new Tuple<int, DateTime>(count, DateTime.UtcNow);
		}

		return count;
	}

	public QueryResult<BaseItem> QueryRecursive(InternalItemsQuery query)
	{
		User user = query.User;
		if (!query.ForceDirect && RequiresPostFiltering(query))
		{
			int? startIndex = query.StartIndex;
			int? limit = query.Limit;
			query.StartIndex = null;
			query.Limit = null;
			Func<BaseItem, bool> filter = (BaseItem i) => UserViewBuilder.Filter(i, user, query, BaseItem.UserDataManager, BaseItem.LibraryManager);
			IEnumerable<BaseItem> items = ((query.User != null) ? GetRecursiveChildren(user, query) : GetRecursiveChildren(filter));
			query.StartIndex = startIndex;
			query.Limit = limit;
			return PostFilterAndSort(items, query, enableSorting: true);
		}
		if (!(this is UserRootFolder) && !(this is AggregateFolder) && query.ParentId.Equals(Guid.Empty))
		{
			query.Parent = this;
		}
		// 匿名无类型查询改走 SQL 后（CollapseBoxSetItems 匿名短路）需限定库树：
		// 原 SQL 路径无 TopParentIds 过滤会全表扫描，把 Person 等 IBN 孤儿条目
		// （parent 为空、top_parent 为空）带进结果——与内存路径的库树语义不一致。
		// 仅限"无类型/媒体类型过滤"的裸递归查询（带类型过滤的查询维持全表现状，
		// 如匿名+MusicArtist=IBN 艺人 3 条的既有行为）。
		// Reviewer 阻塞项：必须限定 this 为根（UserRootFolder/AggregateFolder）——
		// 非根文件夹的匿名裸递归语义是"父文件夹子树"，注入全库 TopParentIds 会把
		// 子树查询变全库（FilterService /Filters?ParentId=X 无 UserId 无类型是
		// 现成入口，改动前走内存路径被 this 正确圈定）。
		if (user == null && query.ParentId.Equals(Guid.Empty)
			&& (this is UserRootFolder || this is AggregateFolder)
			&& query.IncludeItemTypes.Length == 0 && query.ExcludeItemTypes.Length == 0
			&& query.MediaTypes.Length == 0
			&& query.TopParentIds.Length == 0 && query.AncestorIds.Length == 0
			&& query.ItemIds.Length == 0)
		{
			// 直接读 root/default 下的 .mblink（与 CollectionFolder 解析器同源的真相）：
			// 根的 Children 实例的 PhysicalFolderIds/PhysicalLocations 依赖刷新时序
			// （解析实例未刷新为空、缓存实例可能未水合），不可靠；mblink 文件是
			// 库配置的唯一持久真相。FindByPath 走 DB 拿物理文件夹 Id。
			// P1 回归修复（Reviewer 预言的债务）：解析结果进程内缓存（TTL 30s）——
			// 原实现每请求 FS 枚举 + 逐 mblink 读文件 + FindByPath DB 探测，
			// 高频匿名查询下成为热点；库配置变更低频，TTL 兜底失效足够。
			var libraryTopParents = GetLibraryTopParentsCached();
			if (libraryTopParents != null && libraryTopParents.Length > 0)
			{
				query.TopParentIds = libraryTopParents;
			}
		}
		if (query.User != null && !query.User.Policy.EnableAllFolders && query.IncludeItemTypes.Length == 1 && string.Equals(query.IncludeItemTypes[0], typeof(BoxSet).Name, StringComparison.OrdinalIgnoreCase))
		{
			query.BoxSetLibraryFolders = (from i in BaseItem.LibraryManager.GetUserRootFolder().GetChildren(user, includeLinkedChildren: true)
				select i.Id).ToArray();
		}
		return BaseItem.LibraryManager.GetItemsResult(query);
	}

	/// <summary>
	/// 匿名库树限定：mblink 解析结果的进程内缓存（TTL 30s）。
	/// 原实现每请求做 FS 枚举 + 逐 mblink 读文件 + FindByPath DB 探测——
	/// 高频匿名查询下的热点（P1 部署后 L1 回归的头号嫌疑，Reviewer 审 P1 时
	/// 预言过"库多会线性涨"）。库配置变更低频，TTL 兜底失效足够；
	/// 解析失败也缓存（负缓存防抖，避免坏配置下每请求重试 FS+DB）。
	/// </summary>
	private static Guid[] _libraryTopParentsCache;
	private static DateTime _libraryTopParentsCacheTime;
	private static readonly object _libraryTopParentsCacheLock = new object();
	private static readonly TimeSpan LibraryTopParentsCacheTtl = TimeSpan.FromSeconds(30);

	/// <summary>
	/// 库配置变更钩子：AddVirtualFolder/RemoveVirtualFolder 后主动失效
	/// mblink 解析缓存（消除 TTL 窗口内的旧值）。
	/// </summary>
	public static void InvalidateLibraryTopParentsCache()
	{
		lock (_libraryTopParentsCacheLock)
		{
			_libraryTopParentsCache = null;
			_libraryTopParentsCacheTime = DateTime.MinValue;
		}
	}

	private static Guid[] GetLibraryTopParentsCached()
	{
		lock (_libraryTopParentsCacheLock)
		{
			if (_libraryTopParentsCache != null
				&& (DateTime.UtcNow - _libraryTopParentsCacheTime) < LibraryTopParentsCacheTtl)
			{
				return _libraryTopParentsCache;
			}
		}

		var result = new List<Guid>();
		try
		{
			var defaultDir = System.IO.Path.Combine(BaseItem.ConfigurationManager.ApplicationPaths.RootFolderPath, "default");

			if (System.IO.Directory.Exists(defaultDir))
			{
				foreach (var mblink in System.IO.Directory.GetFiles(defaultDir, "*.mblink", System.IO.SearchOption.AllDirectories))
				{
					string target;
					try { target = BaseItem.FileSystem.ReadAllText(mblink); }
					catch { continue; }

					if (string.IsNullOrWhiteSpace(target)) continue;

					var folder = BaseItem.LibraryManager.FindByPath(target, true);
					if (folder != null)
					{
						result.Add(folder.Id);
					}
				}
			}
		}
		catch
		{
			// 读取失败退化为全表（现状语义）
		}

		var distinct = result.Distinct().ToArray();

		lock (_libraryTopParentsCacheLock)
		{
			_libraryTopParentsCache = distinct;
			_libraryTopParentsCacheTime = DateTime.UtcNow;
		}

		if (distinct.Length > 0)
		{
			BaseItem.Logger.Debug("Anonymous recursive query limited to library top parents: {0}", string.Join(",", distinct.Select(i => i.ToString("N")).ToArray()));
		}
		else
		{
			BaseItem.Logger.Debug("Anonymous recursive query library top parents EMPTY (mblink read failed?), falling back to full-table");
		}

		return distinct;
	}

	private bool RequiresPostFiltering(InternalItemsQuery query)
	{
		if (LinkedChildren.Length != 0 && !(this is ICollectionFolder))
		{
			BaseItem.Logger.Debug("Query requires post-filtering due to LinkedChildren. Type: " + GetType().Name, Array.Empty<object>());
			return true;
		}
		if (CollapseBoxSetItems(query, this, query.User, BaseItem.ConfigurationManager))
		{
			BaseItem.Logger.Debug("Query requires post-filtering due to CollapseBoxSetItems", Array.Empty<object>());
			return true;
		}
		return false;
	}

	public QueryResult<BaseItem> GetItems(InternalItemsQuery query)
	{
		if (query.ItemIds.Length != 0)
		{
			if (query.OrderBy.Length == 0)
			{
				int? limit = query.Limit;
				query.Limit = null;
				QueryResult<BaseItem> itemsResult = BaseItem.LibraryManager.GetItemsResult(query);
				List<Guid> ids = query.ItemIds.ToList();
				itemsResult.Items = itemsResult.Items.OrderBy((BaseItem i) => ids.IndexOf(i.Id)).Take(limit ?? int.MaxValue).ToArray();
				return itemsResult;
			}
			return BaseItem.LibraryManager.GetItemsResult(query);
		}
		return GetItemsInternal(query);
	}

	public BaseItem[] GetItemList(InternalItemsQuery query)
	{
		query.EnableTotalRecordCount = false;
		if (query.ItemIds.Length != 0)
		{
			if (query.OrderBy.Length == 0)
			{
				int? limit = query.Limit;
				query.Limit = null;
				List<BaseItem> itemList = BaseItem.LibraryManager.GetItemList(query);
				List<Guid> ids = query.ItemIds.ToList();
				return itemList.OrderBy((BaseItem i) => ids.IndexOf(i.Id)).Take(limit ?? int.MaxValue).ToArray();
			}
			return BaseItem.LibraryManager.GetItemList(query).ToArray();
		}
		return GetItemsInternal(query).Items;
	}

	protected virtual QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)
	{
		if (SourceType == SourceType.Channel)
		{
			try
			{
				query.Parent = this;
				query.ChannelIds = new Guid[1] { ChannelId };
				return BaseItem.ChannelManager.GetChannelItemsInternal(query, (IProgress<double>)new SimpleProgress<double>(), CancellationToken.None).Result;
			}
			catch
			{
				return new QueryResult<BaseItem>();
			}
		}
		if (query.Recursive)
		{
			return QueryRecursive(query);
		}
		User user = query.User;
		Func<BaseItem, bool> predicate = (BaseItem i) => UserViewBuilder.Filter(i, user, query, BaseItem.UserDataManager, BaseItem.LibraryManager);
		IEnumerable<BaseItem> items = ((query.User != null) ? GetChildren(user, includeLinkedChildren: true).Where(predicate) : Children.Where(predicate));
		return PostFilterAndSort(items, query, enableSorting: true);
	}

	protected QueryResult<BaseItem> PostFilterAndSort(IEnumerable<BaseItem> items, InternalItemsQuery query, bool enableSorting)
	{
		User user = query.User;
		if (user != null)
		{
			items = CollapseBoxSetItemsIfNeeded(items, query, this, user, BaseItem.ConfigurationManager, CollectionManager);
		}
		if (!string.IsNullOrEmpty(query.NameStartsWithOrGreater))
		{
			items = items.Where((BaseItem i) => string.Compare(query.NameStartsWithOrGreater, i.SortName, StringComparison.CurrentCultureIgnoreCase) < 1);
		}
		if (!string.IsNullOrEmpty(query.NameStartsWith))
		{
			items = items.Where((BaseItem i) => i.SortName.StartsWith(query.NameStartsWith, StringComparison.OrdinalIgnoreCase));
		}
		if (!string.IsNullOrEmpty(query.NameLessThan))
		{
			items = items.Where((BaseItem i) => string.Compare(query.NameLessThan, i.SortName, StringComparison.CurrentCultureIgnoreCase) == 1);
		}
		return UserViewBuilder.SortAndPage(items, null, query, BaseItem.LibraryManager, enableSorting);
	}

	private static IEnumerable<BaseItem> CollapseBoxSetItemsIfNeeded(IEnumerable<BaseItem> items, InternalItemsQuery query, BaseItem queryParent, User user, IServerConfigurationManager configurationManager, ICollectionManager collectionManager)
	{
		if (items == null)
		{
			throw new ArgumentNullException("items");
		}
		if (CollapseBoxSetItems(query, queryParent, user, configurationManager))
		{
			items = collectionManager.CollapseItemsWithinBoxSets(items, user);
		}
		return items;
	}

	private static bool CollapseBoxSetItems(InternalItemsQuery query, BaseItem queryParent, User user, IServerConfigurationManager configurationManager)
	{
		if (queryParent is BoxSet)
		{
			return false;
		}
		if (queryParent is Series)
		{
			return false;
		}
		if (queryParent is Season)
		{
			return false;
		}
		if (queryParent is MusicAlbum)
		{
			return false;
		}
		if (queryParent is MusicArtist)
		{
			return false;
		}
		bool? flag = query.CollapseBoxSetItems;
		if (!flag.HasValue)
		{
			// 匿名查询（user==null）无用户视图上下文，BoxSet 折叠（把合集内电影藏进 BoxSet）
			// 无意义；且原逻辑把"匿名+无类型过滤"劫持进内存后过滤路径（RequiresPostFiltering），
			// 小库即 250ms（perf-baseline L1b P95 657ms 主因），SQL 路径仅 40ms。
			// 用户路径语义不变（EnableGroupingIntoCollections 开关照旧生效）。
			if (user == null || !configurationManager.Configuration.EnableGroupingIntoCollections)
			{
				return false;
			}
			if (query.IncludeItemTypes.Length == 0 || query.IncludeItemTypes.Contains("Movie", StringComparer.OrdinalIgnoreCase))
			{
				flag = true;
			}
		}
		if (flag.HasValue && flag.Value)
		{
			return AllowBoxSetCollapsing(query);
		}
		return false;
	}

	private static bool AllowBoxSetCollapsing(InternalItemsQuery request)
	{
		if (request.IsFavorite.HasValue)
		{
			return false;
		}
		if (request.IsFavoriteOrLiked.HasValue)
		{
			return false;
		}
		if (request.IsLiked.HasValue)
		{
			return false;
		}
		if (request.IsPlayed.HasValue)
		{
			return false;
		}
		if (request.IsResumable.HasValue)
		{
			return false;
		}
		if (request.IsFolder.HasValue)
		{
			return false;
		}
		if (request.Genres.Length != 0)
		{
			return false;
		}
		if (request.GenreIds.Length != 0)
		{
			return false;
		}
		if (request.HasImdbId.HasValue)
		{
			return false;
		}
		if (request.HasOfficialRating.HasValue)
		{
			return false;
		}
		if (request.HasOverview.HasValue)
		{
			return false;
		}
		if (request.HasParentalRating.HasValue)
		{
			return false;
		}
		if (request.HasSpecialFeature.HasValue)
		{
			return false;
		}
		if (request.HasSubtitles.HasValue)
		{
			return false;
		}
		if (request.HasThemeSong.HasValue)
		{
			return false;
		}
		if (request.HasTmdbId.HasValue)
		{
			return false;
		}
		if (request.HasTrailer.HasValue)
		{
			return false;
		}
		if (request.ImageTypes.Length != 0)
		{
			return false;
		}
		if (request.IsLocked.HasValue)
		{
			return false;
		}
		if (request.IsPlaceHolder.HasValue)
		{
			return false;
		}
		if (request.IsPlayed.HasValue)
		{
			return false;
		}
		if (!string.IsNullOrWhiteSpace(request.Person))
		{
			return false;
		}
		if (request.PersonIds.Length != 0)
		{
			return false;
		}
		if (request.ItemIds.Length != 0)
		{
			return false;
		}
		if (request.StudioIds.Length != 0)
		{
			return false;
		}
		if (request.GenreIds.Length != 0)
		{
			return false;
		}
		if (request.VideoTypes.Length != 0)
		{
			return false;
		}
		if (request.Years.Length != 0)
		{
			return false;
		}
		if (request.Tags.Length != 0)
		{
			return false;
		}
		if (request.OfficialRatings.Length != 0)
		{
			return false;
		}
		if (request.MinPlayers.HasValue)
		{
			return false;
		}
		if (request.MaxPlayers.HasValue)
		{
			return false;
		}
		if (request.MinCommunityRating.HasValue)
		{
			return false;
		}
		if (request.MinCriticRating.HasValue)
		{
			return false;
		}
		if (request.MinIndexNumber.HasValue)
		{
			return false;
		}
		return true;
	}

	public List<BaseItem> GetChildren(User user, bool includeLinkedChildren)
	{
		return GetChildren(user, includeLinkedChildren, null);
	}

	public virtual List<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery query)
	{
		if (user == null)
		{
			throw new ArgumentNullException();
		}
		if (IsPhysicalRoot)
		{
			return BaseItem.LibraryManager.GetUserRootFolder().GetChildren(user, includeLinkedChildren);
		}
		Dictionary<Guid, BaseItem> dictionary = new Dictionary<Guid, BaseItem>();
		AddChildren(user, includeLinkedChildren, dictionary, recursive: false, query);
		return dictionary.Values.ToList();
	}

	protected virtual IEnumerable<BaseItem> GetEligibleChildrenForRecursiveChildren(User user)
	{
		return Children;
	}

	private void AddChildren(User user, bool includeLinkedChildren, Dictionary<Guid, BaseItem> result, bool recursive, InternalItemsQuery query)
	{
		foreach (BaseItem eligibleChildrenForRecursiveChild in GetEligibleChildrenForRecursiveChildren(user))
		{
			bool? flag = null;
			if (query == null || UserViewBuilder.FilterItem(eligibleChildrenForRecursiveChild, query))
			{
				flag = eligibleChildrenForRecursiveChild.IsVisible(user);
				if (flag.Value)
				{
					result[eligibleChildrenForRecursiveChild.Id] = eligibleChildrenForRecursiveChild;
				}
			}
			if ((flag ?? eligibleChildrenForRecursiveChild.IsVisible(user)) && recursive && eligibleChildrenForRecursiveChild.IsFolder)
			{
				((Folder)eligibleChildrenForRecursiveChild).AddChildren(user, includeLinkedChildren, result, recursive: true, query);
			}
		}
		if (!includeLinkedChildren)
		{
			return;
		}
		foreach (BaseItem linkedChild in GetLinkedChildren(user))
		{
			if ((query == null || UserViewBuilder.FilterItem(linkedChild, query)) && linkedChild.IsVisible(user))
			{
				result[linkedChild.Id] = linkedChild;
			}
		}
	}

	public IEnumerable<BaseItem> GetRecursiveChildren(User user, bool includeLinkedChildren = true)
	{
		return GetRecursiveChildren(user, null);
	}

	public virtual IEnumerable<BaseItem> GetRecursiveChildren(User user, InternalItemsQuery query)
	{
		if (user == null)
		{
			throw new ArgumentNullException("user");
		}
		Dictionary<Guid, BaseItem> dictionary = new Dictionary<Guid, BaseItem>();
		AddChildren(user, includeLinkedChildren: true, dictionary, recursive: true, query);
		return dictionary.Values;
	}

	public IList<BaseItem> GetRecursiveChildren()
	{
		return GetRecursiveChildren(includeLinkedChildren: true);
	}

	public IList<BaseItem> GetRecursiveChildren(bool includeLinkedChildren)
	{
		return GetRecursiveChildren((BaseItem i) => true, includeLinkedChildren);
	}

	public IList<BaseItem> GetRecursiveChildren(Func<BaseItem, bool> filter)
	{
		return GetRecursiveChildren(filter, includeLinkedChildren: true);
	}

	public IList<BaseItem> GetRecursiveChildren(Func<BaseItem, bool> filter, bool includeLinkedChildren)
	{
		Dictionary<Guid, BaseItem> dictionary = new Dictionary<Guid, BaseItem>();
		AddChildrenToList(dictionary, includeLinkedChildren, recursive: true, filter);
		return dictionary.Values.ToList();
	}

	private void AddChildrenToList(Dictionary<Guid, BaseItem> result, bool includeLinkedChildren, bool recursive, Func<BaseItem, bool> filter)
	{
		foreach (BaseItem child in Children)
		{
			if (filter == null || filter(child))
			{
				result[child.Id] = child;
			}
			if (recursive && child.IsFolder)
			{
				((Folder)child).AddChildrenToList(result, includeLinkedChildren: false, recursive: true, filter);
			}
		}
		if (!includeLinkedChildren)
		{
			return;
		}
		foreach (BaseItem linkedChild in GetLinkedChildren())
		{
			if (filter == null || filter(linkedChild))
			{
				result[linkedChild.Id] = linkedChild;
			}
		}
	}

	public List<BaseItem> GetLinkedChildren()
	{
		LinkedChild[] linkedChildren = LinkedChildren;
		List<BaseItem> list = new List<BaseItem>(linkedChildren.Length);
		LinkedChild[] array = linkedChildren;
		foreach (LinkedChild info in array)
		{
			BaseItem linkedChild = GetLinkedChild(info);
			if (linkedChild != null)
			{
				list.Add(linkedChild);
			}
		}
		return list;
	}

	public bool ContainsLinkedChildByItemId(Guid itemId)
	{
		LinkedChild[] linkedChildren = LinkedChildren;
		foreach (LinkedChild linkedChild in linkedChildren)
		{
			if (linkedChild.ItemId.HasValue && linkedChild.ItemId.Value == itemId)
			{
				return true;
			}
			BaseItem linkedChild2 = GetLinkedChild(linkedChild);
			if (linkedChild2 != null && linkedChild2.Id == itemId)
			{
				return true;
			}
		}
		return false;
	}

	public List<BaseItem> GetLinkedChildren(User user)
	{
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		if (!FilterLinkedChildrenPerUser || user == null)
		{
			return GetLinkedChildren();
		}
		LinkedChild[] linkedChildren = LinkedChildren;
		List<BaseItem> list = new List<BaseItem>(linkedChildren.Length);
		if (linkedChildren.Length == 0)
		{
			return list;
		}
		List<Folder> list2 = BaseItem.LibraryManager.GetUserRootFolder().GetChildren(user, includeLinkedChildren: true).OfType<Folder>()
			.ToList();
		List<Guid> list3 = list2.Select((Folder i) => i.Id).ToList();
		LinkedChild[] array = linkedChildren;
		foreach (LinkedChild info in array)
		{
			BaseItem linkedChild = GetLinkedChild(info);
			if (linkedChild == null)
			{
				continue;
			}
			BaseItem baseItem = linkedChild.GetOwner() ?? linkedChild;
			if (baseItem != null && !(linkedChild is IItemByName))
			{
				MediaProtocol? pathProtocol = baseItem.PathProtocol;
				if (!pathProtocol.HasValue || (int)pathProtocol.Value != 0)
				{
					if (!baseItem.IsVisibleStandalone(user))
					{
						continue;
					}
				}
				else if (!(from f in BaseItem.LibraryManager.GetCollectionFolders(baseItem, list2)
					select f.Id).Any(list3.Contains))
				{
					continue;
				}
			}
			list.Add(linkedChild);
		}
		return list;
	}

	public IEnumerable<Tuple<LinkedChild, BaseItem>> GetLinkedChildrenInfos()
	{
		return from i in LinkedChildren
			select new Tuple<LinkedChild, BaseItem>(i, GetLinkedChild(i)) into i
			where i.Item2 != null
			select i;
	}

	protected override async Task<bool> RefreshedOwnedItems(MetadataRefreshOptions options, List<FileSystemMetadata> fileSystemChildren, CancellationToken cancellationToken)
	{
		bool changesFound = false;
		if (IsFileProtocol && RefreshLinkedChildren(fileSystemChildren))
		{
			changesFound = true;
		}
		return await base.RefreshedOwnedItems(options, fileSystemChildren, cancellationToken).ConfigureAwait(continueOnCapturedContext: false) | changesFound;
	}

	protected virtual bool RefreshLinkedChildren(IEnumerable<FileSystemMetadata> fileSystemChildren)
	{
		if (SupportsShortcutChildren)
		{
			List<LinkedChild> list = (from i in fileSystemChildren.Where((FileSystemMetadata i) => !i.IsDirectory && BaseItem.FileSystem.IsShortcut(i.FullName)).Select((FileSystemMetadata i) =>
				{
					try
					{
						BaseItem.Logger.Debug("Found shortcut at {0}", new object[1] { i.FullName });
						string text = CollectionFolder.ApplicationHost.ExpandVirtualPath(BaseItem.FileSystem.ResolveShortcut(i.FullName));
						if (!string.IsNullOrEmpty(text))
						{
							return new LinkedChild
							{
								Path = text,
								Type = LinkedChildType.Shortcut
							};
						}
						BaseItem.Logger.Error("Error resolving shortcut {0}", new object[1] { i.FullName });
						return (LinkedChild)null;
					}
					catch (IOException ex)
					{
						BaseItem.Logger.ErrorException("Error resolving shortcut {0}", (Exception)ex, new object[1] { i.FullName });
						return (LinkedChild)null;
					}
				})
				where i != null
				select i).ToList();
			List<LinkedChild> second = LinkedChildren.Where((LinkedChild i) => i.Type == LinkedChildType.Shortcut).ToList();
			if (!list.SequenceEqual(second, new LinkedChildComparer(BaseItem.FileSystem)))
			{
				BaseItem.Logger.Info("Shortcut links have changed for {0}", new object[1] { Path });
				list.AddRange(LinkedChildren.Where((LinkedChild i) => i.Type == LinkedChildType.Manual));
				LinkedChildren = LinqExtensions.ToArray<LinkedChild>((IEnumerable<LinkedChild>)list, list.Count);
				return true;
			}
		}
		LinkedChild[] linkedChildren = LinkedChildren;
		for (int num = 0; num < linkedChildren.Length; num++)
		{
			linkedChildren[num].ItemId = null;
		}
		return false;
	}

	public override void MarkPlayed(User user, DateTime? datePlayed, bool resetPosition)
	{
		InternalItemsQuery internalItemsQuery = new InternalItemsQuery
		{
			User = user,
			Recursive = true,
			IsFolder = false,
			EnableTotalRecordCount = false
		};
		if (!user.Configuration.DisplayMissingEpisodes)
		{
			internalItemsQuery.IsVirtualItem = false;
		}
		BaseItem[] itemList = GetItemList(internalItemsQuery);
		foreach (BaseItem baseItem in itemList)
		{
			if (!baseItem.IsVirtualItem || !(baseItem is Episode { IsUnaired: not false }))
			{
				baseItem.MarkPlayed(user, datePlayed, resetPosition);
			}
		}
	}

	public override void MarkUnplayed(User user)
	{
		BaseItem[] itemList = GetItemList(new InternalItemsQuery
		{
			User = user,
			Recursive = true,
			IsFolder = false,
			EnableTotalRecordCount = false
		});
		for (int i = 0; i < itemList.Length; i++)
		{
			itemList[i].MarkUnplayed(user);
		}
	}

	public override bool IsPlayed(User user)
	{
		return GetItemList(new InternalItemsQuery(user)
		{
			Recursive = true,
			IsFolder = false,
			IsVirtualItem = false,
			EnableTotalRecordCount = false
		}).All((BaseItem i) => i.IsPlayed(user));
	}

	public override bool IsUnplayed(User user)
	{
		return !IsPlayed(user);
	}

	// 水合批量化（L1 残余修复）：Folder 的 UserData 计数（RecursiveItemCount +
	// UnplayedItemCount）在 DTO 批量序列化时逐 Folder 触发两个完整查询
	// （DISTINCT ON 包装，实测 7 Folder × 2 = 14 次/请求，占 L1 语句数 93%）。
	// 跨请求 TTL 缓存（user+folder 维度）：用户播放后徽章 30s 内延迟更新，可接受。
	private static readonly Dictionary<string, Tuple<int, int, DateTime>> _folderUserDataCountsCache =
		new Dictionary<string, Tuple<int, int, DateTime>>(StringComparer.OrdinalIgnoreCase);
	private static readonly object _folderUserDataCountsCacheLock = new object();
	private static readonly TimeSpan FolderUserDataCountsCacheTtl = TimeSpan.FromSeconds(30);

	public override void FillUserDataDtoValues(UserItemDataDto dto, UserItemData userData, BaseItemDto itemDto, User user, DtoOptions options)
	{
		if (!SupportsUserDataFromChildren)
		{
			return;
		}

		var cacheKey = user == null ? Id.ToString("N") : user.Id.ToString("N") + "|" + Id.ToString("N");
		Tuple<int, int, DateTime> cached = null;

		lock (_folderUserDataCountsCacheLock)
		{
			if (_folderUserDataCountsCache.TryGetValue(cacheKey, out cached)
				&& (DateTime.UtcNow - cached.Item3) < FolderUserDataCountsCacheTtl)
			{
				// 命中：直接用缓存的两个计数，跳过两次完整查询
			}
			else
			{
				cached = null;
			}
		}

		int recursiveCount;
		int unplayedCount;

		if (cached != null)
		{
			recursiveCount = cached.Item1;
			unplayedCount = cached.Item2;
		}
		else
		{
			recursiveCount = GetRecursiveChildCount(user);

			if (!SupportsPlayedStatus)
			{
				if (itemDto != null && options.ContainsField((ItemFields)26))
				{
					itemDto.RecursiveItemCount = recursiveCount;
				}
				return;
			}

			QueryResult<BaseItem> items = GetItems(new InternalItemsQuery(user)
			{
				Recursive = true,
				IsFolder = false,
				IsVirtualItem = false,
				EnableTotalRecordCount = true,
				Limit = 0,
				IsPlayed = false,
				DtoOptions = new DtoOptions(allFields: false)
				{
					EnableImages = false
				}
			});
			unplayedCount = items.TotalRecordCount;

			lock (_folderUserDataCountsCacheLock)
			{
				_folderUserDataCountsCache[cacheKey] = new Tuple<int, int, DateTime>(recursiveCount, unplayedCount, DateTime.UtcNow);
			}
		}

		if (itemDto != null && options.ContainsField((ItemFields)26))
		{
			itemDto.RecursiveItemCount = recursiveCount;
		}
		if (!SupportsPlayedStatus)
		{
			return;
		}

		double num = unplayedCount;
		dto.UnplayedItemCount = unplayedCount;
		if (itemDto != null && itemDto.RecursiveItemCount.HasValue)
		{
			if (itemDto.RecursiveItemCount.Value > 0)
			{
				double num2 = num / (double)itemDto.RecursiveItemCount.Value * 100.0;
				dto.PlayedPercentage = 100.0 - num2;
				dto.Played = dto.PlayedPercentage.Value >= 100.0;
			}
		}
		else
		{
			dto.Played = (dto.UnplayedItemCount ?? 0) == 0;
		}
	}
}
