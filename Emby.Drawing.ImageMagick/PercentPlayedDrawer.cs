using ImageMagick;
using ImageMagick.Drawing;
using System;

namespace Emby.Drawing.ImageMagick
{
    public class PercentPlayedDrawer
    {
        private const int IndicatorHeight = 8;

        public void Process(MagickImage image, double percent)
        {
            var height = image.Height;

            var endX = image.Width - 1;
            var endY = height - 1;

            var draw = new Drawables();

            // Old PixelWand: Color="black", Opacity=0.4 (40% transparent => 60% opaque)
            draw.FillColor(new MagickColor("black"))
                .FillOpacity(new Percentage(60))
                .Rectangle(0, endY - IndicatorHeight, endX, endY);

            double foregroundWidth = endX;
            foregroundWidth *= percent;
            foregroundWidth /= 100;

            draw.FillColor(new MagickColor("#52B54B"))
                .FillOpacity(new Percentage(100))
                .Rectangle(0, endY - IndicatorHeight, Convert.ToInt32(Math.Round(foregroundWidth)), endY);

            image.Draw(draw);
        }
    }
}
