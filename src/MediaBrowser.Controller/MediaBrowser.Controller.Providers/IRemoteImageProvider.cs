using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Controller.Providers;

public interface IRemoteImageProvider : IImageProvider
{
	IEnumerable<ImageType> GetSupportedImages(BaseItem item);

	Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken);

	Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken);
}
