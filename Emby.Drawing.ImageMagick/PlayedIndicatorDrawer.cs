using ImageMagick;
using ImageMagick.Drawing;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Drawing;
using System;
using System.IO;
using System.Threading.Tasks;
using MediaBrowser.Common.Progress;
using MediaBrowser.Controller.IO;
using MediaBrowser.Model.IO;

namespace Emby.Drawing.ImageMagick
{
    public class PlayedIndicatorDrawer
    {
        private const int FontSize = 52;
        private const int OffsetFromTopRightCorner = 38;

        private readonly IApplicationPaths _appPaths;
        private readonly IHttpClient _iHttpClient;
        private readonly IFileSystem _fileSystem;

        public PlayedIndicatorDrawer(IApplicationPaths appPaths, IHttpClient iHttpClient, IFileSystem fileSystem)
        {
            _appPaths = appPaths;
            _iHttpClient = iHttpClient;
            _fileSystem = fileSystem;
        }

        public async Task DrawPlayedIndicator(MagickImage image, ImageSize imageSize)
        {
            var x = imageSize.Width - OffsetFromTopRightCorner;

            var draw = new Drawables();

            // Old PixelWand: Color="#52B54B", Opacity=0.2 (20% transparent => 80% opaque)
            draw.FillColor(new MagickColor("#52B54B"))
                .FillOpacity(new Percentage(80))
                .Circle(x, OffsetFromTopRightCorner, x - 20, OffsetFromTopRightCorner - 20);

            var font = await DownloadFont("webdings.ttf", "https://github.com/MediaBrowser/Emby.Resources/raw/master/fonts/webdings.ttf", _appPaths, _iHttpClient, _fileSystem).ConfigureAwait(false);

            draw.FillColor(new MagickColor("white"))
                .Font(font)
                .FontPointSize(FontSize)
                .TextAlignment(global::ImageMagick.TextAlignment.Center)
                .EnableTextAntialias()
                .Text(x + 4, OffsetFromTopRightCorner + 14, "a");

            image.Draw(draw);
        }

        internal static string ExtractFont(string name, IApplicationPaths paths, IFileSystem fileSystem)
        {
            var filePath = Path.Combine(paths.ProgramDataPath, "fonts", name);

            if (fileSystem.FileExists(filePath))
            {
                return filePath;
            }

            var namespacePath = typeof(PlayedIndicatorDrawer).Namespace + ".fonts." + name;
            var tempPath = Path.Combine(paths.TempDirectory, Guid.NewGuid().ToString("N") + ".ttf");
            fileSystem.CreateDirectory(fileSystem.GetDirectoryName(tempPath));

            using (var stream = typeof(PlayedIndicatorDrawer).Assembly.GetManifestResourceStream(namespacePath))
            {
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    stream.CopyTo(fileStream);
                }
            }

            fileSystem.CreateDirectory(fileSystem.GetDirectoryName(filePath));

            try
            {
                fileSystem.CopyFile(tempPath, filePath, false);
            }
            catch (IOException)
            {

            }

            return tempPath;
        }

        internal static async Task<string> DownloadFont(string name, string url, IApplicationPaths paths, IHttpClient httpClient, IFileSystem fileSystem)
        {
            var filePath = Path.Combine(paths.ProgramDataPath, "fonts", name);

            if (fileSystem.FileExists(filePath))
            {
                return filePath;
            }

            var tempPath = await httpClient.GetTempFile(new HttpRequestOptions
            {
                Url = url,
                Progress = new SimpleProgress<double>()

            }).ConfigureAwait(false);

            fileSystem.CreateDirectory(fileSystem.GetDirectoryName(filePath));

            try
            {
                fileSystem.CopyFile(tempPath, filePath, false);
            }
            catch (IOException)
            {

            }

            return tempPath;
        }
    }
}
