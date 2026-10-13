using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Providers;

public interface IExtrasProvider
{
	string Name { get; }

	bool Supports(BaseItem item);
}
