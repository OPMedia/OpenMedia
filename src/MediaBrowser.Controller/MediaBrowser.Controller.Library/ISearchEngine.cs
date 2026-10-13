using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Search;

namespace MediaBrowser.Controller.Library;

public interface ISearchEngine
{
	QueryResult<SearchHintInfo> GetSearchHints(SearchQuery query);
}
