using MediaBrowser.Model.Querying;

namespace MediaBrowser.Model.News;

public interface INewsService
{
	QueryResult<NewsItem> GetProductNews(NewsQuery query);
}
