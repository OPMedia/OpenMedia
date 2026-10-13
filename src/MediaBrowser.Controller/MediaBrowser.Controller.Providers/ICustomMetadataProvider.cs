using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace MediaBrowser.Controller.Providers;

public interface ICustomMetadataProvider : IMetadataProvider
{
}
public interface ICustomMetadataProvider<TItemType> : IMetadataProvider<TItemType>, IMetadataProvider, ICustomMetadataProvider where TItemType : BaseItem
{
	Task<ItemUpdateType> FetchAsync(TItemType item, MetadataRefreshOptions options, CancellationToken cancellationToken);
}
