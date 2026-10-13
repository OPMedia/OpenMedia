using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Dto;

public class ImageInfo
{
	public string ImageTag;

	public ImageType ImageType { get; set; }

	public int? ImageIndex { get; set; }

	public string Path { get; set; }

	public int? Height { get; set; }

	public int? Width { get; set; }

	public long Size { get; set; }
}
