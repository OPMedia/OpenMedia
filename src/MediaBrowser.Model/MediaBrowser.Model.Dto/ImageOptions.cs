using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Dto;

public class ImageOptions
{
	public ImageType ImageType { get; set; }

	public int? ImageIndex { get; set; }

	public int? Width { get; set; }

	public int? Height { get; set; }

	public int? MaxWidth { get; set; }

	public int? MaxHeight { get; set; }

	public int? Quality { get; set; }

	public string Tag { get; set; }

	public bool? CropWhitespace { get; set; }

	public bool EnableImageEnhancers { get; set; }

	public ImageFormat? Format { get; set; }

	public bool AddPlayedIndicator { get; set; }

	public int? PercentPlayed { get; set; }

	public int? UnPlayedCount { get; set; }

	public string BackgroundColor { get; set; }

	public ImageOptions()
	{
		EnableImageEnhancers = true;
	}
}
