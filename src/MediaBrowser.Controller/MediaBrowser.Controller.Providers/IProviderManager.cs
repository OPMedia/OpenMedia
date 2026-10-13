using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Controller.Providers;

public interface IProviderManager
{
	event EventHandler<GenericEventArgs<BaseItem>> RefreshStarted;

	event EventHandler<GenericEventArgs<BaseItem>> RefreshCompleted;

	event EventHandler<GenericEventArgs<Tuple<BaseItem, double>>> RefreshProgress;

	void QueueRefresh(Guid itemId, MetadataRefreshOptions options, RefreshPriority priority);

	Task RefreshFullItem(BaseItem item, MetadataRefreshOptions options, CancellationToken cancellationToken);

	Task<ItemUpdateType> RefreshSingleItem(BaseItem item, MetadataRefreshOptions options, CancellationToken cancellationToken);

	Task SaveImage(BaseItem item, string url, ImageType type, int? imageIndex, CancellationToken cancellationToken);

	Task SaveImage(BaseItem item, Stream source, string mimeType, ImageType type, int? imageIndex, CancellationToken cancellationToken);

	Task SaveImage(BaseItem item, string source, string mimeType, ImageType type, int? imageIndex, bool? saveLocallyWithMedia, CancellationToken cancellationToken);

	void AddParts(IEnumerable<IImageProvider> imageProviders, IEnumerable<IMetadataService> metadataServices, IEnumerable<IMetadataProvider> metadataProviders, IEnumerable<IMetadataSaver> savers, IEnumerable<IExternalId> externalIds);

	Task<IEnumerable<RemoteImageInfo>> GetAvailableRemoteImages(BaseItem item, RemoteImageQuery query, CancellationToken cancellationToken);

	IEnumerable<ImageProviderInfo> GetRemoteImageProviderInfo(BaseItem item);

	MetadataPluginSummary[] GetAllMetadataPlugins();

	IEnumerable<ExternalUrl> GetExternalUrls(BaseItem item);

	IEnumerable<ExternalIdInfo> GetExternalIdInfos(IHasProviderIds item);

	void SaveMetadata(BaseItem item, ItemUpdateType updateType);

	void SaveMetadata(BaseItem item, ItemUpdateType updateType, IEnumerable<string> savers);

	MetadataOptions GetMetadataOptions(BaseItem item);

	Task<IEnumerable<RemoteSearchResult>> GetRemoteSearchResults<TItemType, TLookupType>(RemoteSearchQuery<TLookupType> searchInfo, CancellationToken cancellationToken) where TItemType : BaseItem, new() where TLookupType : ItemLookupInfo;

	Task<HttpResponseInfo> GetSearchImage(string providerName, string url, CancellationToken cancellationToken);

	Dictionary<Guid, Guid> GetRefreshQueue();

	void OnRefreshStart(BaseItem item);

	void OnRefreshProgress(BaseItem item, double progress);

	void OnRefreshComplete(BaseItem item);

	double? GetRefreshProgress(Guid id);
}
