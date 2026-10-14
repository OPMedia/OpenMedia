using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.News;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Emby.Server.Implementations.News
{
    public class NewsService : INewsService
    {
        private readonly IApplicationPaths _appPaths;
        private readonly IJsonSerializer _json;

        public NewsService(IApplicationPaths appPaths, IJsonSerializer json)
        {
            _appPaths = appPaths;
            _json = json;
        }

        public QueryResult<NewsItem> GetProductNews(NewsQuery query)
        {
            // OpenMedia（去 Emby 化，用户决策）：产品新闻恒返回空列表。
            // RSS 拉取已在 NewsEntryPoint 移除，旧缓存 news.json 不再透出，web UI 新闻区块自然隐藏。
            return new QueryResult<NewsItem>
            {
                Items = new NewsItem[] { }
            };
        }

        private QueryResult<NewsItem> GetProductNewsInternal(NewsQuery query)
        {
            var path = Path.Combine(_appPaths.CachePath, "news.json");

            var items = GetNewsItems(path).OrderByDescending(i => i.Date);

            var itemsArray = items.ToArray();
            var count = itemsArray.Length;

            if (query.StartIndex.HasValue)
            {
                itemsArray = itemsArray.Skip(query.StartIndex.Value).ToArray();
            }

            if (query.Limit.HasValue)
            {
                itemsArray = itemsArray.Take(query.Limit.Value).ToArray();
            }

            return new QueryResult<NewsItem>
            {
                Items = itemsArray,
                TotalRecordCount = count
            };
        }

        private IEnumerable<NewsItem> GetNewsItems(string path)
        {
            return _json.DeserializeFromFile<List<NewsItem>>(path);
        }
    }
}
