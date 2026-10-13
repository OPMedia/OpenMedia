using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace MediaBrowser.Controller.Providers;

public interface IMetadataService
{
	int Order { get; }

	bool CanRefresh(BaseItem item);

	bool CanRefreshPrimary(Type type);

	Task<ItemUpdateType> RefreshMetadata(BaseItem item, MetadataRefreshOptions refreshOptions, CancellationToken cancellationToken);
}
