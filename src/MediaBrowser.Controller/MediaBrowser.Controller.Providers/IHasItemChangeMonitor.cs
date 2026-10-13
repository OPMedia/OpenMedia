using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Providers;

public interface IHasItemChangeMonitor
{
	bool HasChanged(BaseItem item, IDirectoryService directoryService);
}
