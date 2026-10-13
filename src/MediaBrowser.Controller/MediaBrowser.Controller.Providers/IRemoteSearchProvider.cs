using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Controller.Providers;

public interface IRemoteSearchProvider<in TLookupInfoType> : IRemoteSearchProvider, IMetadataProvider where TLookupInfoType : ItemLookupInfo
{
	Task<IEnumerable<RemoteSearchResult>> GetSearchResults(TLookupInfoType searchInfo, CancellationToken cancellationToken);
}
public interface IRemoteSearchProvider : IMetadataProvider
{
	Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken);
}
