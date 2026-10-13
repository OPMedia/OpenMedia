using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Drawing;

public static class ImageProcessorExtensions
{
	public static string GetImageCacheTag(this IImageProcessor processor, BaseItem item, ImageType imageType)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		return processor.GetImageCacheTag(item, imageType, 0);
	}

	public static string GetImageCacheTag(this IImageProcessor processor, BaseItem item, ImageType imageType, int imageIndex)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		ItemImageInfo imageInfo = item.GetImageInfo(imageType, imageIndex);
		if (imageInfo == null)
		{
			return null;
		}
		return processor.GetImageCacheTag(item, imageInfo);
	}
}
