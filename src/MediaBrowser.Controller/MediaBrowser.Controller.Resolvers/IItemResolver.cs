using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace MediaBrowser.Controller.Resolvers;

public interface IItemResolver
{
	ResolverPriority Priority { get; }

	BaseItem ResolvePath(ItemResolveArgs args);
}
