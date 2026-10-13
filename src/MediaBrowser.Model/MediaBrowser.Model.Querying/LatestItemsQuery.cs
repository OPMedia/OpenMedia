using System;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Querying;

public class LatestItemsQuery
{
	public Guid UserId { get; set; }

	public Guid ParentId { get; set; }

	public int? StartIndex { get; set; }

	public int? Limit { get; set; }

	public ItemFields[] Fields { get; set; }

	public string[] IncludeItemTypes { get; set; }

	public bool? IsPlayed { get; set; }

	public bool GroupItems { get; set; }

	public bool? EnableImages { get; set; }

	public int? ImageTypeLimit { get; set; }

	public ImageType[] EnableImageTypes { get; set; }

	public LatestItemsQuery()
	{
		EnableImageTypes = new ImageType[0];
	}
}
