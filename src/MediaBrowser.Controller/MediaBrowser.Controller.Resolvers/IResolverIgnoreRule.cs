using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Controller.Resolvers;

public interface IResolverIgnoreRule
{
	bool ShouldIgnore(FileSystemMetadata fileInfo, BaseItem parent);
}
