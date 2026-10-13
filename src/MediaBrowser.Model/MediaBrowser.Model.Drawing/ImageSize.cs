using System.Globalization;

namespace MediaBrowser.Model.Drawing;

public struct ImageSize
{
	private double _height;

	private double _width;

	public double Height
	{
		get
		{
			return _height;
		}
		set
		{
			_height = value;
		}
	}

	public double Width
	{
		get
		{
			return _width;
		}
		set
		{
			_width = value;
		}
	}

	public bool Equals(ImageSize size)
	{
		if (Width.Equals(size.Width))
		{
			return Height.Equals(size.Height);
		}
		return false;
	}

	public override string ToString()
	{
		return $"{Width}-{Height}";
	}

	public ImageSize(string value)
	{
		_width = 0.0;
		_height = 0.0;
		ParseValue(value);
	}

	public ImageSize(int width, int height)
	{
		_width = width;
		_height = height;
	}

	public ImageSize(double width, double height)
	{
		_width = width;
		_height = height;
	}

	private void ParseValue(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return;
		}
		string[] array = value.Split(new char[1] { '-' });
		if (array.Length == 2)
		{
			if (double.TryParse(array[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
			{
				_width = result;
			}
			if (double.TryParse(array[1], NumberStyles.Any, CultureInfo.InvariantCulture, out result))
			{
				_height = result;
			}
		}
	}
}
