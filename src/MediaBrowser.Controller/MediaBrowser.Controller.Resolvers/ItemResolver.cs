using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace MediaBrowser.Controller.Resolvers;

public abstract class ItemResolver<T> : IItemResolver where T : BaseItem, new()
{
	public virtual ResolverPriority Priority => ResolverPriority.First;

	protected virtual T Resolve(ItemResolveArgs args)
	{
		return null;
	}

	protected virtual void SetInitialItemValues(T item, ItemResolveArgs args)
	{
	}

	BaseItem IItemResolver.ResolvePath(ItemResolveArgs args)
	{
		T val = Resolve(args);
		if (val != null)
		{
			SetInitialItemValues(val, args);
		}
		return val;
	}
}
