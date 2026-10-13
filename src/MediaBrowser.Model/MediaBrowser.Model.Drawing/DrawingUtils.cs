namespace MediaBrowser.Model.Drawing;

public static class DrawingUtils
{
	public static ImageSize Resize(ImageSize size, double width, double height, double maxWidth, double maxHeight)
	{
		double num = size.Width;
		double num2 = size.Height;
		if (width > 0.0 && height > 0.0)
		{
			num = width;
			num2 = height;
		}
		else if (height > 0.0)
		{
			num = GetNewWidth(num2, num, height);
			num2 = height;
		}
		else if (width > 0.0)
		{
			num2 = GetNewHeight(num2, num, width);
			num = width;
		}
		if (maxHeight > 0.0 && maxHeight < num2)
		{
			num = GetNewWidth(num2, num, maxHeight);
			num2 = maxHeight;
		}
		if (maxWidth > 0.0 && maxWidth < num)
		{
			num2 = GetNewHeight(num2, num, maxWidth);
			num = maxWidth;
		}
		return new ImageSize
		{
			Width = num,
			Height = num2
		};
	}

	private static double GetNewWidth(double currentHeight, double currentWidth, double newHeight)
	{
		return newHeight / currentHeight * currentWidth;
	}

	private static double GetNewHeight(double currentHeight, double currentWidth, double newWidth)
	{
		return newWidth / currentWidth * currentHeight;
	}
}
