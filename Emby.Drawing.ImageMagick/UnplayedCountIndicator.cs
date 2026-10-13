using ImageMagick;
using ImageMagick.Drawing;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Drawing;
using System.Globalization;
using MediaBrowser.Model.IO;

namespace Emby.Drawing.ImageMagick
{
    public class UnplayedCountIndicator
    {
        private const int OffsetFromTopRightCorner = 38;

        private readonly IApplicationPaths _appPaths;
        private readonly IFileSystem _fileSystem;

        public UnplayedCountIndicator(IApplicationPaths appPaths, IFileSystem fileSystem)
        {
            _appPaths = appPaths;
            _fileSystem = fileSystem;
        }

        public void DrawUnplayedCountIndicator(MagickImage image, ImageSize imageSize, int count)
        {
            var x = imageSize.Width - OffsetFromTopRightCorner;
            var text = count.ToString(CultureInfo.InvariantCulture);

            var draw = new Drawables();

            // Old PixelWand: Color="#52B54B", Opacity=0.2 (20% transparent => 80% opaque)
            draw.FillColor(new MagickColor("#52B54B"))
                .FillOpacity(new Percentage(80))
                .Circle(x, OffsetFromTopRightCorner, x - 20, OffsetFromTopRightCorner - 20);

            draw.FillColor(new MagickColor("white"))
                .Font(PlayedIndicatorDrawer.ExtractFont("robotoregular.ttf", _appPaths, _fileSystem))
                .TextAlignment(global::ImageMagick.TextAlignment.Center)
                .EnableTextAntialias();

            var fontSize = 30;
            var y = OffsetFromTopRightCorner + 11;

            if (text.Length == 1)
            {
                x += 1;
            }
            else if (text.Length == 2)
            {
                x += 1;
            }
            else if (text.Length >= 3)
            {
                //x += 1;
                y -= 2;
                fontSize = 24;
            }

            draw.FontPointSize(fontSize)
                .Text(x, y, text);

            image.Draw(draw);
        }
    }
}
