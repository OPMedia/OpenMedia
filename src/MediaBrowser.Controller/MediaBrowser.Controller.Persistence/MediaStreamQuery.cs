using System;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Persistence;

public class MediaStreamQuery
{
	public MediaStreamType? Type { get; set; }

	public int? Index { get; set; }

	public Guid ItemId { get; set; }
}
