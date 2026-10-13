using System;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Controller.Entities;

public static class BaseItemExtensions
{
	public static string GetImagePath(this BaseItem item, ImageType imageType)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		return item.GetImagePath(imageType, 0);
	}

	public static bool HasImage(this BaseItem item, ImageType imageType)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		return item.HasImage(imageType, 0);
	}

	public static void SetImagePath(this BaseItem item, ImageType imageType, FileSystemMetadata file)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		item.SetImagePath(imageType, 0, file);
	}

	public static void SetImagePath(this BaseItem item, ImageType imageType, string file)
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		if (file.StartsWith("http", StringComparison.OrdinalIgnoreCase))
		{
			item.SetImage(new ItemImageInfo
			{
				Path = file,
				Type = imageType
			}, 0);
		}
		else
		{
			item.SetImagePath(imageType, BaseItem.FileSystem.GetFileInfo(file));
		}
	}
}
