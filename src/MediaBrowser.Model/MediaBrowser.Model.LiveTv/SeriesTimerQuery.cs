using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.LiveTv;

public class SeriesTimerQuery
{
	public string SortBy { get; set; }

	public SortOrder SortOrder { get; set; }
}
