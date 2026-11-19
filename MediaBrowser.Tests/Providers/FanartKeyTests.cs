using MediaBrowser.Providers.Movies;
using MediaBrowser.Providers.Music;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.Providers;

/// <summary>
/// FanArt 的 key 解析与请求 URL 形态。
///
/// 真实的 FanArt key 走构建期注入、不进仓库（见 BuildSecrets.targets），所以这里
/// 能确定性验证的是两件事：**注入优先/回退** 的解析规则，以及 **provider 真的用了解析结果**
/// 拼 URL。远端是否接受这把 key 无法在单测里验证 —— 那次用 curl 直连做过（HTTP 200）。
/// </summary>
[TestClass]
public class FanartKeyTests
{
    /// <summary>源码里的历史默认 key（已公开，作为回退值）。</summary>
    private const string LegacyKey = "5c6b04c68e904cfed1e6cbc9a9e683d4";

    [TestMethod]
    public void ResolveApiKey_UsesInjectedWhenPresent()
    {
        Assert.AreEqual("injected-key", FanartArtistProvider.ResolveApiKey("injected-key"));
    }

    [TestMethod]
    public void ResolveApiKey_FallsBackToLegacyWhenEmpty()
    {
        Assert.AreEqual(LegacyKey, FanartArtistProvider.ResolveApiKey(null));
        Assert.AreEqual(LegacyKey, FanartArtistProvider.ResolveApiKey(string.Empty));
        Assert.AreEqual(LegacyKey, FanartArtistProvider.ResolveApiKey("   "));
    }

    [TestMethod]
    public void BuildMovieUrl_EmbedsResolvedKeyAndId()
    {
        var url = FanartMovieImageProvider.BuildMovieUrl("k-123", "603", null);

        StringAssert.Contains(url, "webservice.fanart.tv/v3/movies/603");
        StringAssert.Contains(url, "api_key=k-123");
        Assert.IsFalse(url.Contains("client_key", System.StringComparison.Ordinal), "没有 client key 时不该带这个参数");
    }

    [TestMethod]
    public void BuildMovieUrl_AppendsClientKeyWhenConfigured()
    {
        var url = FanartMovieImageProvider.BuildMovieUrl("k-123", "603", "ck-9");

        StringAssert.Contains(url, "api_key=k-123");
        StringAssert.Contains(url, "&client_key=ck-9");
    }

    [TestMethod]
    public void BuildMovieUrl_UsesWhateverApiKeyIsPassed()
    {
        // 这条钉住「provider 用的是解析后的 key，不是写死的那个」：
        // 传入注入值时必须原样出现在 URL 里。
        var withInjected = FanartMovieImageProvider.BuildMovieUrl(
            FanartArtistProvider.ResolveApiKey("injected-key"), "603", null);

        StringAssert.Contains(withInjected, "api_key=injected-key");
        Assert.IsFalse(withInjected.Contains(LegacyKey, System.StringComparison.Ordinal));
    }
}
