using System;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Querying;

public class NextUpQuery
{
	public Guid UserId { get; set; }

	public string ParentId { get; set; }

	public string SeriesId { get; set; }

	public int? StartIndex { get; set; }

	public int? Limit { get; set; }

	public ItemFields[] Fields { get; set; }

	public bool? EnableImages { get; set; }

	public int? ImageTypeLimit { get; set; }

	public ImageType[] EnableImageTypes { get; set; }

	public bool EnableTotalRecordCount { get; set; }

	public NextUpQuery()
	{
		EnableImageTypes = new ImageType[0];
		EnableTotalRecordCount = true;
	}
}
