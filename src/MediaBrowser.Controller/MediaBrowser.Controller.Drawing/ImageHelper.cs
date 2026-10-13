using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Drawing;

public static class ImageHelper
{
	public static IImageProcessor ImageProcessor { get; set; }

	public static ImageSize GetNewImageSize(ImageProcessingOptions options, ImageSize? originalImageSize)
	{
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		if (originalImageSize.HasValue)
		{
			return DrawingUtils.Resize(originalImageSize.Value, (double)(options.Width ?? 0), (double)(options.Height ?? 0), (double)(options.MaxWidth ?? 0), (double)(options.MaxHeight ?? 0));
		}
		return GetSizeEstimate(options);
	}

	private static ImageSize GetSizeEstimate(ImageProcessingOptions options)
	{
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		if (options.Width.HasValue && options.Height.HasValue)
		{
			return new ImageSize(options.Width.Value, options.Height.Value);
		}
		double estimatedAspectRatio = GetEstimatedAspectRatio(options.Image.Type, options.Item);
		int? num = options.Width ?? options.MaxWidth;
		if (num.HasValue)
		{
			double num2 = (double)num.Value / estimatedAspectRatio;
			return new ImageSize((double)num.Value, num2);
		}
		int num3 = options.Height ?? options.MaxHeight ?? 200;
		return new ImageSize(estimatedAspectRatio * (double)num3, (double)num3);
	}

	private static double GetEstimatedAspectRatio(ImageType type, BaseItem item)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Expected I4, but got Unknown
		switch ((int)type)
		{
		case 1:
		case 2:
		case 5:
		case 8:
		case 10:
			return 1.78;
		case 3:
			return 5.4;
		case 6:
		case 7:
		case 9:
		case 11:
			return 1.0;
		case 4:
			return 2.58;
		case 0:
		{
			double defaultPrimaryImageAspectRatio = item.GetDefaultPrimaryImageAspectRatio();
			if (!(defaultPrimaryImageAspectRatio > 0.0))
			{
				return 0.667;
			}
			return defaultPrimaryImageAspectRatio;
		}
		default:
			return 1.0;
		}
	}
}
