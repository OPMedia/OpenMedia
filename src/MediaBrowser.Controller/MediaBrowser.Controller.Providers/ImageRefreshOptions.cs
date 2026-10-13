using System;
using System.Linq;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Providers;

public class ImageRefreshOptions
{
	public MetadataRefreshMode ImageRefreshMode { get; set; }

	public IDirectoryService DirectoryService { get; private set; }

	public bool ReplaceAllImages { get; set; }

	public ImageType[] ReplaceImages { get; set; }

	public bool IsAutomated { get; set; }

	public ImageRefreshOptions(IDirectoryService directoryService)
	{
		ImageRefreshMode = MetadataRefreshMode.Default;
		DirectoryService = directoryService;
		ReplaceImages = Array.Empty<ImageType>();
		IsAutomated = true;
	}

	public bool IsReplacingImage(ImageType type)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		if (ImageRefreshMode == MetadataRefreshMode.FullRefresh)
		{
			if (!ReplaceAllImages)
			{
				return Enumerable.Contains(ReplaceImages, type);
			}
			return true;
		}
		return false;
	}
}
