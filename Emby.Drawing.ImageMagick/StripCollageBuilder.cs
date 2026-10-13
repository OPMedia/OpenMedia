using ImageMagick;
using MediaBrowser.Common.Configuration;
using System;
using System.Collections.Generic;

using MediaBrowser.Controller.IO;
using MediaBrowser.Model.IO;

namespace Emby.Drawing.ImageMagick
{
    public class StripCollageBuilder
    {
        private readonly IApplicationPaths _appPaths;
        private readonly IFileSystem _fileSystem;

        public StripCollageBuilder(IApplicationPaths appPaths, IFileSystem fileSystem)
        {
            _appPaths = appPaths;
            _fileSystem = fileSystem;
        }

        public void BuildPosterCollage(string[] paths, string outputPath, int width, int height)
        {
            using (var wand = BuildPosterCollageWand(paths, width, height))
            {
                wand.Write(outputPath);
            }
        }

        public void BuildSquareCollage(string[] paths, string outputPath, int width, int height)
        {
            using (var wand = BuildSquareCollageWand(paths, width, height))
            {
                wand.Write(outputPath);
            }
        }

        public void BuildThumbCollage(string[] paths, string outputPath, int width, int height)
        {
            using (var wand = BuildThumbCollageWand(paths, width, height))
            {
                wand.Write(outputPath);
            }
        }

        private MagickImage BuildPosterCollageWand(string[] paths, int width, int height)
        {
            var inputPaths = ImageHelpers.ProjectPaths(paths, 3);
            using (var wandImages = new MagickImageCollection())
            {
                foreach (var path in inputPaths)
                {
                    wandImages.Add(new MagickImage(path));
                }

                // Old code opened "gradient:#111111-#111111" (a solid dark background)
                var wand = new MagickImage(new MagickColor("#111111"), (uint)width, (uint)height);

                var iSlice = Convert.ToInt32(width * 0.3);
                int iTrans = Convert.ToInt32(height * .25);
                int iHeight = Convert.ToInt32(height * .65);
                var horizontalImagePadding = Convert.ToInt32(width * 0.0366);

                foreach (var element in wandImages)
                {
                    int iWidth = (int)Math.Abs(iHeight * element.Width / (double)element.Height);
                    element.BackgroundColor = MagickColors.Black;
                    element.Resize(new MagickGeometry((uint)iWidth, (uint)iHeight));
                    int ix = (int)Math.Abs((iWidth - iSlice) / 2);
                    element.Crop(new MagickGeometry(ix, 0, (uint)iSlice, (uint)iHeight), Gravity.Center);

                    element.Extent(0 - horizontalImagePadding, 0, (uint)iSlice, (uint)iHeight);
                }

                using (var wandList = (MagickImage)wandImages.AppendHorizontally())
                {
                    wandList.Trim();

                    using (var mwr = (MagickImage)wandList.Clone())
                    {
                        mwr.Resize(new MagickGeometry((uint)wandList.Width, (uint)(wandList.Height / 2)));
                        mwr.Flip();

                        mwr.Alpha(AlphaOption.Opaque);
                        // Old ColorizeImage(black, grey70): 70% blend toward black
                        mwr.Colorize(MagickColors.Black, new Percentage(70));

                        using (var mwg = new MagickImage(new MagickColor("none"), (uint)wandList.Width, (uint)iTrans))
                        {
                            var verticalSpacing = Convert.ToInt32(height * 0.01111111111111111111111111111111);
                            mwr.Composite(mwg, 0, verticalSpacing, CompositeOperator.CopyAlpha);

                            // Old: wandList.AddImage(mwr); AppendImages(true) => vertical stack of strips + reflection
                            using (var stack = new MagickImageCollection())
                            {
                                stack.Add(wandList.Clone());
                                stack.Add(mwr.Clone());
                                using (var appended = (MagickImage)stack.AppendVertically())
                                {
                                    int ex = (int)(wand.Width - mwg.Width) / 2;
                                    wand.Composite(appended, ex, Convert.ToInt32(height * .05), CompositeOperator.Atop);
                                }
                            }
                        }
                    }
                }

                return wand;
            }
        }

        private MagickImage BuildThumbCollageWand(string[] paths, int width, int height)
        {
            var inputPaths = ImageHelpers.ProjectPaths(paths, 4);
            using (var wandImages = new MagickImageCollection())
            {
                foreach (var path in inputPaths)
                {
                    wandImages.Add(new MagickImage(path));
                }

                // Old code opened "gradient:#111111-#111111" (a solid dark background)
                var wand = new MagickImage(new MagickColor("#111111"), (uint)width, (uint)height);

                var iSlice = Convert.ToInt32(width * 0.24125);
                int iTrans = Convert.ToInt32(height * .25);
                int iHeight = Convert.ToInt32(height * .70);
                var horizontalImagePadding = Convert.ToInt32(width * 0.0125);

                foreach (var element in wandImages)
                {
                    int iWidth = (int)Math.Abs(iHeight * element.Width / (double)element.Height);
                    element.BackgroundColor = MagickColors.Black;
                    element.Resize(new MagickGeometry((uint)iWidth, (uint)iHeight));
                    int ix = (int)Math.Abs((iWidth - iSlice) / 2);
                    element.Crop(new MagickGeometry(ix, 0, (uint)iSlice, (uint)iHeight), Gravity.Center);

                    element.Extent(0 - horizontalImagePadding, 0, (uint)iSlice, (uint)iHeight);
                }

                using (var wandList = (MagickImage)wandImages.AppendHorizontally())
                {
                    wandList.Trim();

                    using (var mwr = (MagickImage)wandList.Clone())
                    {
                        mwr.Resize(new MagickGeometry((uint)wandList.Width, (uint)(wandList.Height / 2)));
                        mwr.Flip();

                        mwr.Alpha(AlphaOption.Opaque);
                        // Old ColorizeImage(black, grey70): 70% blend toward black
                        mwr.Colorize(MagickColors.Black, new Percentage(70));

                        using (var mwg = new MagickImage(new MagickColor("none"), (uint)wandList.Width, (uint)iTrans))
                        {
                            var verticalSpacing = Convert.ToInt32(height * 0.01111111111111111111111111111111);
                            mwr.Composite(mwg, 0, verticalSpacing, CompositeOperator.CopyAlpha);

                            // Old: wandList.AddImage(mwr); AppendImages(true) => vertical stack of strips + reflection
                            using (var stack = new MagickImageCollection())
                            {
                                stack.Add(wandList.Clone());
                                stack.Add(mwr.Clone());
                                using (var appended = (MagickImage)stack.AppendVertically())
                                {
                                    int ex = (int)(wand.Width - mwg.Width) / 2;
                                    wand.Composite(appended, ex, Convert.ToInt32(height * .045), CompositeOperator.Atop);
                                }
                            }
                        }
                    }
                }

                return wand;
            }
        }

        private MagickImage BuildSquareCollageWand(string[] paths, int width, int height)
        {
            var inputPaths = ImageHelpers.ProjectPaths(paths, 4);
            var outputWand = new MagickImage(new MagickColor("none"), (uint)width, (uint)height);
            var imageIndex = 0;
            var cellWidth = width / 2;
            var cellHeight = height / 2;

            for (var x = 0; x < 2; x++)
            {
                for (var y = 0; y < 2; y++)
                {
                    using (var temp = new MagickImage(inputPaths[imageIndex]))
                    {
                        // Old ScaleImage forced the exact cell size (no aspect ratio preserved)
                        temp.Resize(new MagickGeometry((uint)cellWidth, (uint)cellHeight) { IgnoreAspectRatio = true });
                        // draw this image into the strip at the next position
                        var xPos = x * cellWidth;
                        var yPos = y * cellHeight;
                        outputWand.Composite(temp, xPos, yPos, CompositeOperator.Over);
                    }
                    imageIndex++;
                }
            }

            return outputWand;
        }
    }
}
