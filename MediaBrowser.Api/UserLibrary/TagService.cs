using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Services;

namespace MediaBrowser.Api.UserLibrary
{
    /// <summary>
    /// Emby 4.1 tag/metadata dictionary endpoints.
    /// </summary>
    public class TagItem
    {
        public string Name { get; set; }
    }

    public class OfficialRatingItem
    {
        public string Name { get; set; }
    }

    [Route("/AudioCodecs", "GET", Summary = "Gets all audio codecs from the library")]
    [Authenticated]
    public class GetAudioCodecs : BaseItemsRequest, IReturn<QueryResult<TagItem>>
    {
    }

    [Route("/VideoCodecs", "GET", Summary = "Gets all video codecs from the library")]
    [Authenticated]
    public class GetVideoCodecs : BaseItemsRequest, IReturn<QueryResult<TagItem>>
    {
    }

    [Route("/SubtitleCodecs", "GET", Summary = "Gets all subtitle codecs from the library")]
    [Authenticated]
    public class GetSubtitleCodecs : BaseItemsRequest, IReturn<QueryResult<TagItem>>
    {
    }

    [Route("/Containers", "GET", Summary = "Gets all containers from the library")]
    [Authenticated]
    public class GetContainers : BaseItemsRequest, IReturn<QueryResult<TagItem>>
    {
    }

    [Route("/Tags", "GET", Summary = "Gets all tags from the library")]
    [Authenticated]
    public class GetTags : BaseItemsRequest, IReturn<QueryResult<TagItem>>
    {
    }

    [Route("/OfficialRatings", "GET", Summary = "Gets all official ratings from the library")]
    [Authenticated]
    public class GetOfficialRatings : BaseItemsRequest, IReturn<QueryResult<OfficialRatingItem>>
    {
    }

    [Route("/Artists/Prefixes", "GET", Summary = "Gets artist name prefixes from the library")]
    [Authenticated]
    public class GetArtistsPrefixes : BaseItemsRequest, IReturn<NameValuePair[]>
    {
    }

    [Route("/Items/Prefixes", "GET", Summary = "Gets item name prefixes from the library")]
    [Authenticated]
    public class GetItemsPrefixes : BaseItemsRequest, IReturn<NameValuePair[]>
    {
    }

    [Authenticated]
    public class TagService : BaseApiService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;

        public TagService(ILibraryManager libraryManager, IUserManager userManager)
        {
            _libraryManager = libraryManager;
            _userManager = userManager;
        }

        public object Get(GetAudioCodecs request)
        {
            return GetTagValues(request, i => GetCodecs(i, MediaStreamType.Audio));
        }

        public object Get(GetVideoCodecs request)
        {
            return GetTagValues(request, i => GetCodecs(i, MediaStreamType.Video));
        }

        public object Get(GetSubtitleCodecs request)
        {
            return GetTagValues(request, i => GetCodecs(i, MediaStreamType.Subtitle));
        }

        public object Get(GetContainers request)
        {
            return GetTagValues(request, i => i.Container == null ? Enumerable.Empty<string>() : new[] { i.Container });
        }

        public object Get(GetTags request)
        {
            return GetTagValues(request, i => i.Tags ?? Enumerable.Empty<string>());
        }

        public object Get(GetOfficialRatings request)
        {
            var result = GetTagValues(request, i => string.IsNullOrWhiteSpace(i.OfficialRating) ? Enumerable.Empty<string>() : new[] { i.OfficialRating });
            // remap TagItem -> OfficialRatingItem to match the spec schema
            if (result is QueryResult<TagItem> tagResult)
            {
                return ToOptimizedResult(new QueryResult<OfficialRatingItem>
                {
                    Items = tagResult.Items.Select(t => new OfficialRatingItem { Name = t.Name }).ToArray(),
                    TotalRecordCount = tagResult.TotalRecordCount
                });
            }
            return result;
        }

        public object Get(GetArtistsPrefixes request)
        {
            return GetPrefixes(request, new[] { typeof(MediaBrowser.Controller.Entities.Audio.MusicArtist).Name });
        }

        public object Get(GetItemsPrefixes request)
        {
            return GetPrefixes(request, null);
        }

        private static IEnumerable<string> GetCodecs(BaseItem item, MediaStreamType streamType)
        {
            var streams = item.GetMediaStreams();
            if (streams == null)
            {
                return Enumerable.Empty<string>();
            }

            return streams.Where(s => s.Type == streamType && !string.IsNullOrWhiteSpace(s.Codec)).Select(s => s.Codec);
        }

        private object GetTagValues(BaseItemsRequest request, Func<BaseItem, IEnumerable<string>> selector)
        {
            var items = GetRequestItems(request);

            var names = items.SelectMany(selector)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var totalRecordCount = names.Count;

            var startIndex = request.StartIndex ?? 0;
            if (startIndex > 0)
            {
                names = names.Skip(startIndex).ToList();
            }

            if (request.Limit.HasValue)
            {
                names = names.Take(request.Limit.Value).ToList();
            }

            return ToOptimizedResult(new QueryResult<TagItem>
            {
                Items = names.Select(n => new TagItem { Name = n }).ToArray(),
                TotalRecordCount = request.EnableTotalRecordCount ? totalRecordCount : names.Count
            });
        }

        private object GetPrefixes(BaseItemsRequest request, string[] includeItemTypes)
        {
            var query = BuildPrefixQuery(request, includeItemTypes);
            var items = _libraryManager.GetItemList(query);

            var prefixes = items.Select(i => i.SortName ?? i.Name ?? string.Empty)
                .Where(n => n.Length > 0)
                .Select(n => n.Substring(0, 1).ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => new NameValuePair { Name = n, Value = n })
                .ToArray();

            return ToOptimizedResult(prefixes);
        }

        private InternalItemsQuery BuildPrefixQuery(BaseItemsRequest request, string[] includeItemTypes)
        {
            var user = request.UserId.Equals(Guid.Empty) ? null : _userManager.GetUserById(request.UserId);

            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = includeItemTypes ?? request.GetIncludeItemTypes(),
                ExcludeItemTypes = request.GetExcludeItemTypes(),
                MediaTypes = request.GetMediaTypes(),
                Genres = request.GetGenres(),
                OfficialRatings = request.GetOfficialRatings(),
                Tags = request.GetTags(),
                Years = request.GetYears(),
                IsFavorite = request.IsFavorite,
                DtoOptions = new DtoOptions(false)
                {
                    EnableImages = false,
                    EnableUserData = false
                },
                EnableTotalRecordCount = false
            };

            if (!string.IsNullOrWhiteSpace(request.ParentId))
            {
                query.AncestorIds = new[] { new Guid(request.ParentId) };
            }

            return query;
        }

        private List<BaseItem> GetRequestItems(BaseItemsRequest request)
        {
            var query = BuildPrefixQuery(request, null);
            return _libraryManager.GetItemList(query);
        }
    }
}
