using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Library;

public interface IMetadataFileSaver : IMetadataSaver
{
	string GetSavePath(BaseItem item);
}
