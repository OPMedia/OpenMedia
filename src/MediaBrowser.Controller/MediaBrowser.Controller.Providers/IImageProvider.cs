using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Providers;

public interface IImageProvider
{
	string Name { get; }

	bool Supports(BaseItem item);
}
