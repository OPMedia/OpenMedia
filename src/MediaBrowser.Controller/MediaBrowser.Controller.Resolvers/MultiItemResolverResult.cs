using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Controller.Resolvers;

public class MultiItemResolverResult
{
	public List<BaseItem> Items { get; set; }

	public List<FileSystemMetadata> ExtraFiles { get; set; }

	public MultiItemResolverResult()
	{
		Items = new List<BaseItem>();
		ExtraFiles = new List<FileSystemMetadata>();
	}
}
