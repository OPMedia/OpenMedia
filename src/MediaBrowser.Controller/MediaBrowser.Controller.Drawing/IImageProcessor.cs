using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Drawing;

public interface IImageProcessor
{
	string[] SupportedInputFormats { get; }

	IImageEnhancer[] ImageEnhancers { get; }

	bool SupportsImageCollageCreation { get; }

	IImageEncoder ImageEncoder { get; set; }

	ImageSize GetImageSize(string path);

	ImageSize GetImageSize(BaseItem item, ItemImageInfo info);

	ImageSize GetImageSize(BaseItem item, ItemImageInfo info, bool allowSlowMethods, bool updateItem);

	void AddParts(IEnumerable<IImageEnhancer> enhancers);

	IImageEnhancer[] GetSupportedEnhancers(BaseItem item, ImageType imageType);

	string GetImageCacheTag(BaseItem item, ItemImageInfo image);

	string GetImageCacheTag(BaseItem item, ChapterInfo chapter);

	string GetImageCacheTag(BaseItem item, ItemImageInfo image, IImageEnhancer[] imageEnhancers);

	Task ProcessImage(ImageProcessingOptions options, Stream toStream);

	Task<Tuple<string, string, DateTime>> ProcessImage(ImageProcessingOptions options);

	Task<string> GetEnhancedImage(BaseItem item, ImageType imageType, int imageIndex);

	ImageFormat[] GetSupportedImageOutputFormats();

	void CreateImageCollage(ImageCollageOptions options);

	bool SupportsTransparency(string path);
}
