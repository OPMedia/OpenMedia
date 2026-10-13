using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Providers;

public interface IRemoteMetadataProvider : IMetadataProvider
{
}
public interface IRemoteMetadataProvider<TItemType, in TLookupInfoType> : IMetadataProvider<TItemType>, IMetadataProvider, IRemoteMetadataProvider, IRemoteSearchProvider<TLookupInfoType>, IRemoteSearchProvider where TItemType : BaseItem, IHasLookupInfo<TLookupInfoType> where TLookupInfoType : ItemLookupInfo, new()
{
	Task<MetadataResult<TItemType>> GetMetadata(TLookupInfoType info, CancellationToken cancellationToken);
}
