using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public abstract class BasePluginFolder : Folder, ICollectionFolder, IHasCollectionType
{
	[IgnoreDataMember]
	public virtual string CollectionType => null;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => false;

	[IgnoreDataMember]
	public override bool SupportsPeople => false;

	public override bool CanDelete()
	{
		return false;
	}

	public override bool IsSaveLocalMetadataEnabled()
	{
		return true;
	}
}
