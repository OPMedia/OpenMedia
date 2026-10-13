using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Providers;

public interface ILocalMetadataProvider : IMetadataProvider
{
}
public interface ILocalMetadataProvider<TItemType> : IMetadataProvider<TItemType>, IMetadataProvider, ILocalMetadataProvider where TItemType : BaseItem
{
	Task<MetadataResult<TItemType>> GetMetadata(ItemInfo info, IDirectoryService directoryService, CancellationToken cancellationToken);
}
