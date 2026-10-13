using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Providers;

public class ImageProviderInfo
{
	public string Name { get; set; }

	public ImageType[] SupportedImages { get; set; }

	public ImageProviderInfo()
	{
		SupportedImages = new ImageType[0];
	}
}
