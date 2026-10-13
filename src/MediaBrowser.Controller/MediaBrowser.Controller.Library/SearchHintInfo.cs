using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Library;

public class SearchHintInfo
{
	public BaseItem Item { get; set; }

	public string MatchedTerm { get; set; }
}
