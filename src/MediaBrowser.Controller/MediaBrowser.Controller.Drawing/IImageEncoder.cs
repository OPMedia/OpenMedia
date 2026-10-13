using System;
using MediaBrowser.Model.Drawing;

namespace MediaBrowser.Controller.Drawing;

public interface IImageEncoder
{
	string[] SupportedInputFormats { get; }

	ImageFormat[] SupportedOutputFormats { get; }

	string Name { get; }

	bool SupportsImageCollageCreation { get; }

	bool SupportsImageEncoding { get; }

	string EncodeImage(string inputPath, DateTime dateModified, string outputPath, bool autoOrient, ImageOrientation? orientation, int quality, ImageProcessingOptions options, ImageFormat outputFormat);

	void CreateImageCollage(ImageCollageOptions options);

	ImageSize GetImageSize(string path);
}
