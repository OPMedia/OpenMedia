using System;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Controller.Providers;

public class MetadataRefreshOptions : ImageRefreshOptions
{
	public bool ReplaceAllMetadata { get; set; }

	public MetadataRefreshMode MetadataRefreshMode { get; set; }

	public RemoteSearchResult SearchResult { get; set; }

	public string[] RefreshPaths { get; set; }

	public bool ForceSave { get; set; }

	public bool EnableRemoteContentProbe { get; set; }

	public MetadataRefreshOptions(IFileSystem fileSystem)
		: this((IDirectoryService)new DirectoryService((ILogger)new NullLogger(), fileSystem))
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
	}

	public MetadataRefreshOptions(IDirectoryService directoryService)
		: base(directoryService)
	{
		MetadataRefreshMode = MetadataRefreshMode.Default;
	}

	public MetadataRefreshOptions(MetadataRefreshOptions copy)
		: base(copy.DirectoryService)
	{
		MetadataRefreshMode = copy.MetadataRefreshMode;
		ForceSave = copy.ForceSave;
		ReplaceAllMetadata = copy.ReplaceAllMetadata;
		EnableRemoteContentProbe = copy.EnableRemoteContentProbe;
		ImageRefreshMode = copy.ImageRefreshMode;
		ReplaceAllImages = copy.ReplaceAllImages;
		ReplaceImages = copy.ReplaceImages;
		SearchResult = copy.SearchResult;
		if (copy.RefreshPaths != null && copy.RefreshPaths.Length != 0)
		{
			if (RefreshPaths == null)
			{
				RefreshPaths = Array.Empty<string>();
			}
			RefreshPaths = copy.RefreshPaths.ToArray();
		}
	}

	public bool RefreshItem(BaseItem item)
	{
		if (RefreshPaths != null && RefreshPaths.Length != 0)
		{
			return RefreshPaths.Contains(item.Path ?? string.Empty, StringComparer.OrdinalIgnoreCase);
		}
		return true;
	}
}
