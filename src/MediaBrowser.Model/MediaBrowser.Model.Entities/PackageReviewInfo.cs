using System;

namespace MediaBrowser.Model.Entities;

public class PackageReviewInfo
{
	public int id { get; set; }

	public int rating { get; set; }

	public bool recommend { get; set; }

	public string title { get; set; }

	public string review { get; set; }

	public DateTime timestamp { get; set; }
}
