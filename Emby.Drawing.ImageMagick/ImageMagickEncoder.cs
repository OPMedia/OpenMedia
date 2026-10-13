using System.Threading.Tasks;
using ImageMagick;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using System;
using System.IO;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.System;

namespace Emby.Drawing.ImageMagick
{
    public class ImageMagickEncoder : IImageEncoder, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IApplicationPaths _appPaths;
        private readonly Func<IHttpClient> _httpClientFactory;
        private readonly IFileSystem _fileSystem;
        private readonly IEnvironmentInfo _environment;

        public ImageMagickEncoder(ILogger logger, IApplicationPaths appPaths, Func<IHttpClient> httpClientFactory, IFileSystem fileSystem, IEnvironmentInfo environment)
        {
            _logger = logger;
            _appPaths = appPaths;
            _httpClientFactory = httpClientFactory;
            _fileSystem = fileSystem;
            _environment = environment;

            LogVersion();
        }

        public string[] SupportedInputFormats
        {
            get
            {
                // Some common file name extensions for RAW picture files include: .cr2, .crw, .dng, .nef, .orf, .rw2, .pef, .arw, .sr2, .srf, and .tif.
                return new[]
                {
                    "tiff",
                    "tif",
                    "jpeg",
                    "jpg",
                    "png",
                    "aiff",
                    "cr2",
                    "crw",
                    "dng",

                    // Remove until supported
                    //"nef",
                    "orf",
                    "pef",
                    "arw",
                    "webp",
                    "gif",
                    "bmp",
                    "erf",
                    "raf",
                    "rw2",
                    "nrw"
                };
            }
        }

        public ImageFormat[] SupportedOutputFormats
        {
            get
            {
                if (_webpAvailable)
                {
                    return new[] { ImageFormat.Webp, ImageFormat.Gif, ImageFormat.Jpg, ImageFormat.Png };
                }
                return new[] { ImageFormat.Gif, ImageFormat.Jpg, ImageFormat.Png };
            }
        }

        private void LogVersion()
        {
            _logger.Info("ImageMagick version: " + GetVersion());
            TestWebp();
        }

        public static string GetVersion()
        {
            return MagickNET.Version;
        }

        private bool _webpAvailable = true;
        private void TestWebp()
        {
            try
            {
                var tmpPath = Path.Combine(_appPaths.TempDirectory, Guid.NewGuid() + ".webp");
                _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(tmpPath));

                // 1x1 transparent image, saved as webp to verify the delegate is present
                using (var image = new MagickImage(new MagickColor("none"), 1, 1))
                {
                    image.Write(tmpPath);
                }
            }
            catch
            {
                //_logger.ErrorException("Error loading webp: ", ex);
                _webpAvailable = false;
            }
        }

        public ImageSize GetImageSize(string path)
        {
            CheckDisposed();

            // Ping semantics: only reads the image header
            var info = new MagickImageInfo(path);

            return new ImageSize
            {
                Width = info.Width,
                Height = info.Height
            };
        }

        private bool HasTransparency(string path)
        {
            var ext = Path.GetExtension(path);

            return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase);
        }

        public string EncodeImage(string inputPath, DateTime dateModified, string outputPath, bool autoOrient, ImageOrientation? orientation, int quality, ImageProcessingOptions options, ImageFormat selectedOutputFormat)
        {
            // Even if the caller specified 100, don't use it because it takes forever
            quality = Math.Min(quality, 99);

            if (string.IsNullOrEmpty(options.BackgroundColor) || !HasTransparency(inputPath))
            {
                using (var originalImage = new MagickImage(inputPath))
                {
                    if (options.CropWhiteSpace)
                    {
                        originalImage.Trim();
                    }

                    var originalImageSize = new ImageSize(originalImage.Width, originalImage.Height);

                    if (!options.CropWhiteSpace && options.HasDefaultOptions(inputPath, originalImageSize) && !autoOrient)
                    {
                        // Just spit out the original file if all the options are default
                        return inputPath;
                    }

                    var newImageSize = ImageHelper.GetNewImageSize(options, originalImageSize);

                    var width = Convert.ToInt32(Math.Round(newImageSize.Width));
                    var height = Convert.ToInt32(Math.Round(newImageSize.Height));

                    ScaleImage(originalImage, width, height, options.Blur ?? 0);

                    if (autoOrient)
                    {
                        AutoOrientImage(originalImage);
                    }

                    AddForegroundLayer(originalImage, options);
                    DrawIndicator(originalImage, width, height, options);

                    originalImage.Quality = (uint)quality;
                    originalImage.Strip();

                    _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(outputPath));

                    originalImage.Write(outputPath);
                }
            }
            else
            {
                using (var originalImage = new MagickImage(inputPath))
                {
                    var originalImageSize = new ImageSize(originalImage.Width, originalImage.Height);

                    var newImageSize = ImageHelper.GetNewImageSize(options, originalImageSize);

                    var width = Convert.ToInt32(Math.Round(newImageSize.Width));
                    var height = Convert.ToInt32(Math.Round(newImageSize.Height));

                    using (var wand = new MagickImage(new MagickColor(options.BackgroundColor), (uint)width, (uint)height))
                    {
                        ScaleImage(originalImage, width, height, options.Blur ?? 0);

                        if (autoOrient)
                        {
                            AutoOrientImage(originalImage);
                        }

                        wand.Composite(originalImage, 0, 0, CompositeOperator.Over);

                        AddForegroundLayer(wand, options);
                        DrawIndicator(wand, width, height, options);

                        wand.Quality = (uint)quality;
                        wand.Strip();

                        wand.Write(outputPath);
                    }
                }
            }

            return outputPath;
        }

        private void AddForegroundLayer(MagickImage image, ImageProcessingOptions options)
        {
            if (string.IsNullOrEmpty(options.ForegroundLayer))
            {
                return;
            }

            Double opacity;
            if (!Double.TryParse(options.ForegroundLayer, out opacity)) opacity = .4;

            // Old PixelWand("#000", opacity): black at (1 - opacity) opacity
            using (var overlay = new MagickImage(MagickColors.Black, (uint)image.Width, (uint)image.Height))
            {
                overlay.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, 1 - opacity);
                image.Composite(overlay, 0, 0, CompositeOperator.Over);
            }
        }

        private void AutoOrientImage(MagickImage image)
        {
            image.AutoOrient();
        }

        public static void RotateImage(MagickImage image, float angle)
        {
            image.Rotate(angle);
        }

        private void ScaleImage(MagickImage image, int width, int height, int blur)
        {
            var useResize = blur > 1;

            if (useResize)
            {
                image.FilterType = FilterType.Gaussian;
            }

            image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
        }

        /// <summary>
        /// Draws the indicator.
        /// </summary>
        /// <param name="image">The image.</param>
        /// <param name="imageWidth">Width of the image.</param>
        /// <param name="imageHeight">Height of the image.</param>
        /// <param name="options">The options.</param>
        private void DrawIndicator(MagickImage image, int imageWidth, int imageHeight, ImageProcessingOptions options)
        {
            if (!options.AddPlayedIndicator && !options.UnplayedCount.HasValue && options.PercentPlayed.Equals(0))
            {
                return;
            }

            try
            {
                if (options.AddPlayedIndicator)
                {
                    var currentImageSize = new ImageSize(imageWidth, imageHeight);

                    var task = new PlayedIndicatorDrawer(_appPaths, _httpClientFactory(), _fileSystem).DrawPlayedIndicator(image, currentImageSize);
                    Task.WaitAll(task);
                }
                else if (options.UnplayedCount.HasValue)
                {
                    var currentImageSize = new ImageSize(imageWidth, imageHeight);

                    new UnplayedCountIndicator(_appPaths, _fileSystem).DrawUnplayedCountIndicator(image, currentImageSize, options.UnplayedCount.Value);
                }

                if (options.PercentPlayed > 0)
                {
                    new PercentPlayedDrawer().Process(image, options.PercentPlayed);
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error drawing indicator overlay", ex);
            }
        }

        public void CreateImageCollage(ImageCollageOptions options)
        {
            double ratio = options.Width;
            ratio /= options.Height;

            if (ratio >= 1.4)
            {
                new StripCollageBuilder(_appPaths, _fileSystem).BuildThumbCollage(options.InputPaths, options.OutputPath, options.Width, options.Height);
            }
            else if (ratio >= .9)
            {
                new StripCollageBuilder(_appPaths, _fileSystem).BuildSquareCollage(options.InputPaths, options.OutputPath, options.Width, options.Height);
            }
            else
            {
                new StripCollageBuilder(_appPaths, _fileSystem).BuildPosterCollage(options.InputPaths, options.OutputPath, options.Width, options.Height);
            }
        }

        public string Name
        {
            get { return "ImageMagick"; }
        }

        private bool _disposed;
        public void Dispose()
        {
            _disposed = true;
        }

        private void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        public bool SupportsImageCollageCreation
        {
            get
            {
                return true;
            }
        }

        public bool SupportsImageEncoding
        {
            get { return true; }
        }
    }
}
