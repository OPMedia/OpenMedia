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
				foreach (BaseItem item2 in actualChildrenDictionary.Values.Except(validChildren).ToList())
				{
					if (item2.IsFileProtocol)
					{
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

	public virtual int GetRecursiveChildCount(User user)
	{
		return GetItems(new InternalItemsQuery(user)
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
		if (query.User != null && !query.User.Policy.EnableAllFolders && query.IncludeItemTypes.Length == 1 && string.Equals(query.IncludeItemTypes[0], typeof(BoxSet).Name, StringComparison.OrdinalIgnoreCase))
		{
			query.BoxSetLibraryFolders = (from i in BaseItem.LibraryManager.GetUserRootFolder().GetChildren(user, includeLinkedChildren: true)
				select i.Id).ToArray();
		}
		return BaseItem.LibraryManager.GetItemsResult(query);
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
			if (user != null && !configurationManager.Configuration.EnableGroupingIntoCollections)
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

	public override void FillUserDataDtoValues(UserItemDataDto dto, UserItemData userData, BaseItemDto itemDto, User user, DtoOptions options)
	{
		if (!SupportsUserDataFromChildren)
		{
			return;
		}
		if (itemDto != null && options.ContainsField((ItemFields)26))
		{
			itemDto.RecursiveItemCount = GetRecursiveChildCount(user);
		}
		if (!SupportsPlayedStatus)
		{
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
		double num = items.TotalRecordCount;
		dto.UnplayedItemCount = items.TotalRecordCount;
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
