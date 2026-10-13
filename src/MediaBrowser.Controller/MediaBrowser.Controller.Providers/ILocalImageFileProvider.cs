using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Providers;

public interface ILocalImageFileProvider : ILocalImageProvider, IImageProvider
{
	List<LocalImageInfo> GetImages(BaseItem item, IDirectoryService directoryService);
}
