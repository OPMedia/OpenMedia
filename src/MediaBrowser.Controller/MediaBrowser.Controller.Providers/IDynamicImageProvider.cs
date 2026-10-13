using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Providers;

public interface IDynamicImageProvider : IImageProvider
{
	IEnumerable<ImageType> GetSupportedImages(BaseItem item);

	Task<DynamicImageResponse> GetImage(BaseItem item, ImageType type, CancellationToken cancellationToken);
}
