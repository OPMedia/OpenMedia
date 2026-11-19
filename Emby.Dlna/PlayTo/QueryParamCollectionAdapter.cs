using System.Collections.Generic;
using MediaBrowser.Model.Services;
using Microsoft.Extensions.Primitives;

namespace Emby.Dlna.PlayTo;

internal static class QueryParamCollectionAdapter
{
    public static QueryParamCollection ToQueryParamCollection(this Dictionary<string, StringValues> query)
    {
        var result = new QueryParamCollection();
        foreach (var pair in query)
        {
            foreach (var value in pair.Value)
            {
                result.Add(pair.Key, value);
            }
        }

        return result;
    }
}
