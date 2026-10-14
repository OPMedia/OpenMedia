using MediaBrowser.Model.IO;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common;

namespace MediaBrowser.Providers.Omdb
{
    public class OmdbImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IHttpClient _httpClient;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly IApplicationHost _appHost;

        public OmdbImageProvider(IJsonSerializer jsonSerializer, IApplicationHost appHost, IHttpClient httpClient, IFileSystem fileSystem, IServerConfigurationManager configurationManager)
        {
            _jsonSerializer = jsonSerializer;
            _httpClient = httpClient;
            _fileSystem = fileSystem;
            _configurationManager = configurationManager;
            _appHost = appHost;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new List<ImageType>
            {
                ImageType.Primary
            };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var imdbId = item.GetProviderId(MetadataProviders.Imdb);

            var list = new List<RemoteImageInfo>();

            var provider = new OmdbProvider(_jsonSerializer, _httpClient, _fileSystem, _appHost, _configurationManager);

            if (!string.IsNullOrWhiteSpace(imdbId))
            {
                OmdbProvider.RootObject rootObject = await provider.GetRootObject(imdbId, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(rootObject.Poster))
                {
                    // img.omdbapi.com 已死（实测 404，8 年前注释已记录），统一走
                    // 主 API 返回的 Poster 字段（m.media-amazon.com，实测可用）
                    list.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Url = rootObject.Poster
                    });
                }
            }

            return list;
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClient.GetResponse(new HttpRequestOptions
            {
                CancellationToken = cancellationToken,
                Url = url
            });
        }

        public string Name
        {
            get { return "The Open Movie Database"; }
        }

        public bool Supports(BaseItem item)
        {
            return item is Movie || item is Trailer || item is Episode;
        }

        public int Order
        {
            get
            {
                // After other internet providers, because they're better
                // But before fallback providers like screengrab
                return 90;
            }
        }
    }
}
