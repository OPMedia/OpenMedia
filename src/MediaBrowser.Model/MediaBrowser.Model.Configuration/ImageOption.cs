using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Configuration;

public class ImageOption
{
	public ImageType Type { get; set; }

	public int Limit { get; set; }

	public int MinWidth { get; set; }

	public ImageOption()
	{
		Limit = 1;
	}
}
