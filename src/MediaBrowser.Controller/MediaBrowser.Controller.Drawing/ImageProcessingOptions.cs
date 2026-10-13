using System;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Drawing;

namespace MediaBrowser.Controller.Drawing;

public class ImageProcessingOptions
{
	public Guid ItemId { get; set; }

	public BaseItem Item { get; set; }

	public ItemImageInfo Image { get; set; }

	public int ImageIndex { get; set; }

	public bool CropWhiteSpace { get; set; }

	public int? Width { get; set; }

	public int? Height { get; set; }

	public int? MaxWidth { get; set; }

	public int? MaxHeight { get; set; }

	public int Quality { get; set; }

	public IImageEnhancer[] Enhancers { get; set; }

	public ImageFormat[] SupportedOutputFormats { get; set; }

	public bool AddPlayedIndicator { get; set; }

	public int? UnplayedCount { get; set; }

	public int? Blur { get; set; }

	public double PercentPlayed { get; set; }

	public string BackgroundColor { get; set; }

	public string ForegroundLayer { get; set; }

	public bool RequiresAutoOrientation { get; set; }

	public ImageProcessingOptions()
	{
		RequiresAutoOrientation = true;
	}

	private bool HasDefaultOptions(string originalImagePath)
	{
		if (HasDefaultOptionsWithoutSize(originalImagePath) && !Width.HasValue && !Height.HasValue && !MaxWidth.HasValue)
		{
			return !MaxHeight.HasValue;
		}
		return false;
	}

	public bool HasDefaultOptions(string originalImagePath, ImageSize? size)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		if (!size.HasValue)
		{
			return HasDefaultOptions(originalImagePath);
		}
		if (!HasDefaultOptionsWithoutSize(originalImagePath))
		{
			return false;
		}
		ImageSize value = size.Value;
		if (Width.HasValue && !value.Width.Equals(Width.Value))
		{
			return false;
		}
		if (Height.HasValue && !value.Height.Equals(Height.Value))
		{
			return false;
		}
		if (MaxWidth.HasValue && value.Width > (double)MaxWidth.Value)
		{
			return false;
		}
		if (MaxHeight.HasValue && value.Height > (double)MaxHeight.Value)
		{
			return false;
		}
		return true;
	}

	private bool HasDefaultOptionsWithoutSize(string originalImagePath)
	{
		if (Quality >= 90 && IsFormatSupported(originalImagePath) && !AddPlayedIndicator && PercentPlayed.Equals(0.0) && !UnplayedCount.HasValue && !Blur.HasValue && !CropWhiteSpace && string.IsNullOrEmpty(BackgroundColor))
		{
			return string.IsNullOrEmpty(ForegroundLayer);
		}
		return false;
	}

	private bool IsFormatSupported(string originalImagePath)
	{
		string ext = Path.GetExtension(originalImagePath);
		return SupportedOutputFormats.Any((ImageFormat outputFormat) =>
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			return string.Equals(ext, "." + outputFormat, StringComparison.OrdinalIgnoreCase);
		});
	}
}
