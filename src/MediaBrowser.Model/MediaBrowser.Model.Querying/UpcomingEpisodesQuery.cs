using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Querying;

public class UpcomingEpisodesQuery
{
	public string UserId { get; set; }

	public string ParentId { get; set; }

	public int? StartIndex { get; set; }

	public int? Limit { get; set; }

	public ItemFields[] Fields { get; set; }

	public bool? EnableImages { get; set; }

	public int? ImageTypeLimit { get; set; }

	public ImageType[] EnableImageTypes { get; set; }

	public UpcomingEpisodesQuery()
	{
		EnableImageTypes = new ImageType[0];
	}
}
