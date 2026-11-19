using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Providers.Music;
using MediaBrowser.Tests.TestDoubles;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace MediaBrowser.Tests.Providers;

/// <summary>
/// AudioDB 搜索必须走公开 key 能用的 search/searchalbum 端点，并把结果映射成 RemoteSearchResult。
/// </summary>
[TestClass]
public class AudioDbSearchTests
{
    private const string ArtistJson = "{\"artists\":[{\"idArtist\":\"111238\",\"strArtist\":\"Queen\",\"strMusicBrainzID\":\"0383dadf-2a4d-4e10-a44a-81ba016d09c0\",\"strArtistThumb\":\"https://example/queen.jpg\"}]}";
    private const string AlbumJson = "{\"album\":[{\"idAlbum\":\"2109599\",\"idArtist\":\"111238\",\"strAlbum\":\"Sheer Heart Attack\",\"strArtist\":\"Queen\",\"intYearReleased\":\"1974\",\"strAlbumThumb\":\"https://example/album.jpg\",\"strMusicBrainzID\":\"9e7a3b4d-1111-2222-3333-444455556666\"}]}";

    [TestMethod]
    public async Task ArtistSearch_UsesSearchEndpoint()
    {
        string url = null;
        var provider = CreateArtistProvider(ArtistJson, captured => url = captured);

        var results = (await provider.GetSearchResults(
            new ArtistInfo { Name = "Queen" }, CancellationToken.None)).ToList();

        StringAssert.Contains(url, "/123/search.php?s=Queen");
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Queen", results[0].Name);
        Assert.AreEqual("111238", results[0].GetProviderId(MetadataProviders.AudioDbArtist));
        Assert.AreEqual("https://example/queen.jpg", results[0].ImageUrl);
    }

    [TestMethod]
    public async Task AlbumSearch_UsesSearchAlbumEndpointAndDegradesWithoutArtist()
    {
        var urls = new List<string>();
        var provider = CreateAlbumProvider(AlbumJson, urls.Add);

        var results = (await provider.GetSearchResults(
            new AlbumInfo { Name = "Sheer Heart Attack", AlbumArtists = new[] { "Queen" } },
            CancellationToken.None)).ToList();

        StringAssert.Contains(urls[0], "/123/searchalbum.php?s=Queen&a=Sheer+Heart+Attack");
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Sheer Heart Attack", results[0].Name);
        Assert.AreEqual("2109599", results[0].GetProviderId(MetadataProviders.AudioDbAlbum));
        Assert.AreEqual(1974, results[0].ProductionYear);
        Assert.AreEqual("Queen", results[0].AlbumArtist.Name);

        await provider.GetSearchResults(new AlbumInfo { Name = "Sheer Heart Attack" }, CancellationToken.None);

        StringAssert.Contains(urls[1], "/123/searchalbum.php?a=Sheer+Heart+Attack");
    }

    private static AudioDbArtistProvider CreateArtistProvider(string json, System.Action<string> capture)
    {
        return new AudioDbArtistProvider(
            null, null, CreateHttpClient(json, capture),
            ProxyStub<IJsonSerializer>.Create(
                ("DeserializeFromStreamAsync", args => Task.FromResult(JsonSerializer.Deserialize<AudioDbArtistProvider.RootObject>((Stream)args[0])))));
    }

    private static AudioDbAlbumProvider CreateAlbumProvider(string json, System.Action<string> capture)
    {
        return new AudioDbAlbumProvider(
            null, null, CreateHttpClient(json, capture),
            ProxyStub<IJsonSerializer>.Create(
                ("DeserializeFromStreamAsync", args => Task.FromResult(JsonSerializer.Deserialize<AudioDbAlbumProvider.RootObject>((Stream)args[0])))));
    }

    private static IHttpClient CreateHttpClient(string json, System.Action<string> capture)
    {
        return ProxyStub<IHttpClient>.Create(("SendAsync", args =>
        {
            var options = (HttpRequestOptions)args[0];
            capture(options.Url);
            return Task.FromResult(new HttpResponseInfo
            {
                StatusCode = HttpStatusCode.OK,
                Content = new MemoryStream(Encoding.UTF8.GetBytes(json))
            });
        }));
    }
}
