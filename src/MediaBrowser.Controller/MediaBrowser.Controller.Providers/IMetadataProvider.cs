using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Providers;

public interface IMetadataProvider
{
	string Name { get; }
}
public interface IMetadataProvider<TItemType> : IMetadataProvider where TItemType : BaseItem
{
}
