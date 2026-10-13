using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Controller.Resolvers;

public interface IMultiItemResolver
{
	MultiItemResolverResult ResolveMultiple(Folder parent, List<FileSystemMetadata> files, string collectionType, IDirectoryService directoryService);
}
