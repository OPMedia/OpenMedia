using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Providers;

public interface IImageEnhancer
{
	MetadataProviderPriority Priority { get; }

	bool Supports(BaseItem item, ImageType imageType);

	string GetConfigurationCacheKey(BaseItem item, ImageType imageType);

	ImageSize GetEnhancedImageSize(BaseItem item, ImageType imageType, int imageIndex, ImageSize originalImageSize);

	EnhancedImageInfo GetEnhancedImageInfo(BaseItem item, string inputFile, ImageType imageType, int imageIndex);

	Task EnhanceImageAsync(BaseItem item, string inputFile, string outputFile, ImageType imageType, int imageIndex);
}
