using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Providers.TV;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.Providers;

/// <summary>
/// TheTVDB v4 客户端的回归测试。
///
/// 背景：原来的 v1 客户端已经半死 —— 名称搜索返回空 &lt;Data&gt;&lt;/Data&gt;、
/// 增量更新永远 &lt;Time&gt;0&lt;/Time&gt;（定时任务每次白跑）。迁到 v4 之后这几个行为
/// 必须被钉住，否则下次谁改都能悄悄退回「搜不到、刷不动」。
///
/// 全部用假 HTTP handler，不发真实网络请求（没有真实 API key）。
/// </summary>
[TestClass]
public class TvdbV4ClientTests
{
    private const string LoginBody =
        "{\"status\":\"success\",\"data\":{\"token\":\"tok-1\",\"expiresIn\":2678400}}";

    [TestMethod]
    public async Task Unconfigured_DoesNotTouchNetwork()
    {
        var calls = new List<string>();
        var client = CreateClient(apiKey: string.Empty, calls, _ => (HttpStatusCode.OK, "{}"));

        var results = await client.SearchByName("breaking bad", null, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(0, results.Count);
        Assert.AreEqual(0, calls.Count, "没有配 API key 时不该发任何请求");
    }

    [TestMethod]
    public async Task Search_LogsInThenParsesResults()
    {
        var calls = new List<string>();
        var client = CreateClient("key-1", calls, url => url.Contains("/login", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, LoginBody)
            : (HttpStatusCode.OK, "{\"status\":\"success\",\"data\":[{\"id\":\"81189\",\"tvdb_id\":\"81189\",\"title\":\"Breaking Bad\"}]}"));

        var results = await client.SearchByName("breaking bad", null, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Breaking Bad", results[0].Title);
        Assert.AreEqual(2, calls.Count);
        StringAssert.Contains(calls[0], "/login");
        StringAssert.Contains(calls[1], "/search");
        StringAssert.Contains(calls[1], "type=series");
    }

    [TestMethod]
    public async Task Login_ToleratesExpiresInAsString()
    {
        var calls = new List<string>();
        var client = CreateClient("key-1", calls, url => url.Contains("/login", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, "{\"status\":\"success\",\"data\":{\"token\":\"tok-1\",\"expiresIn\":\"P1M\"}}")
            : (HttpStatusCode.OK, "{\"status\":\"success\",\"data\":[{\"id\":\"1\",\"tvdb_id\":\"1\",\"title\":\"X\"}]}"));

        // 真实 API 到底给数字还是 ISO-8601 字符串，没有 key 无法确认；
        // 两种都必须能登录成功，解析不出来时退回默认 30 天。
        var results = await client.SearchByName("x", null, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, results.Count);
    }

    [TestMethod]
    public async Task Unauthorized_ReloginsOnceAndReplays()
    {
        var calls = new List<string>();
        var searchAttempts = 0;

        var client = CreateClient("key-1", calls, url =>
        {
            if (url.Contains("/login", StringComparison.Ordinal))
            {
                return (HttpStatusCode.OK, LoginBody);
            }

            searchAttempts++;
            return searchAttempts == 1
                ? (HttpStatusCode.Unauthorized, "{}")
                : (HttpStatusCode.OK, "{\"status\":\"success\",\"data\":[{\"id\":\"1\",\"tvdb_id\":\"1\",\"title\":\"X\"}]}");
        });

        var results = await client.SearchByName("x", null, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, results.Count, "401 之后应当重登并重放一次");
        Assert.AreEqual(2, searchAttempts);
        Assert.AreEqual(2, calls.Count(c => c.Contains("/login", StringComparison.Ordinal)), "应当重新登录一次");
    }

    [TestMethod]
    public async Task Episodes_FollowsPagination()
    {
        var calls = new List<string>();
        var client = CreateClient("key-1", calls, url =>
        {
            if (url.Contains("/login", StringComparison.Ordinal))
            {
                return (HttpStatusCode.OK, LoginBody);
            }

            // 客户端的停页条件是「本页不足 100 条」，所以第一页必须给满。
            var page = url.Contains("page=1", StringComparison.Ordinal) ? 1 : 0;
            var count = page == 0 ? 100 : 2;
            var startId = page == 0 ? 1 : 1000;
            var episodes = string.Join(",", Enumerable.Range(0, count)
                .Select(i => "{\"id\":" + (startId + i) + ",\"name\":\"Ep" + (startId + i) + "\"}"));
            return (HttpStatusCode.OK, "{\"status\":\"success\",\"data\":{\"episodes\":[" + episodes + "]}}");
        });

        var episodes = await client.GetEpisodes(1, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(102, episodes.Count, "第一页满 100 条时必须继续翻页");
        Assert.AreEqual("Ep1", episodes[0].Name);
        Assert.AreEqual("Ep1000", episodes[100].Name);
        Assert.IsTrue(calls.Any(c => c.Contains("page=1", StringComparison.Ordinal)), "应当请求了第二页");
    }

    [TestMethod]
    public async Task Updates_ParsesRecordIdsAndKeepsCursor()
    {
        var calls = new List<string>();
        var client = CreateClient("key-1", calls, url => url.Contains("/login", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, LoginBody)
            : (HttpStatusCode.OK,
                "{\"status\":\"success\",\"data\":[{\"recordId\":81189,\"timeStamp\":1700000000,\"entityType\":\"series\"}]}"));

        var result = await client.GetUpdates(1699999999, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, result.Updates.Count);
        Assert.AreEqual(81189L, result.Updates[0].RecordId);
        Assert.AreEqual(1700000000L, result.NewTime, "游标要跟着服务端返回的 timeStamp 前进，不能停在 0");
        StringAssert.Contains(calls.Last(), "since=1699999999");
    }

    [TestMethod]
    public void CredentialResolution_ConfigWinsThenBuiltInThenEmpty()
    {
        // 配置优先
        Assert.AreEqual("from-config", TvdbV4Client.Resolve("from-config", "built-in"));
        // 配置为空 → 用内置
        Assert.AreEqual("built-in", TvdbV4Client.Resolve("", "built-in"));
        Assert.AreEqual("built-in", TvdbV4Client.Resolve("   ", "built-in"));
        // 两边都没有 → 空（provider 明确禁用）
        Assert.AreEqual(string.Empty, TvdbV4Client.Resolve(null, string.Empty));
        Assert.AreEqual(string.Empty, TvdbV4Client.Resolve("  ", null));
        // 内置值两端空白要裁掉
        Assert.AreEqual("built-in", TvdbV4Client.Resolve(null, "  built-in  "));
    }

    private static TvdbV4Client CreateClient(
        string apiKey,
        List<string> calls,
        Func<string, (HttpStatusCode Status, string Body)> router)
    {
        var httpClient = TestDoubles.ProxyStub<IHttpClient>.Create(("SendAsync", args =>
        {
            var options = (HttpRequestOptions)args[0];
            calls.Add(options.Url);

            var (status, body) = router(options.Url);
            return Task.FromResult(new HttpResponseInfo
            {
                StatusCode = status,
                ContentType = "application/json",
                Content = new MemoryStream(Encoding.UTF8.GetBytes(body))
            });
        }));

        var configuration = new ServerConfiguration
        {
            TheTvdb = new TheTvdbConfiguration { ApiKey = apiKey }
        };

        var configManager = TestDoubles.ProxyStub<IServerConfigurationManager>.Create(
            ("get_Configuration", _ => configuration));

        return new TvdbV4Client(httpClient, configManager, new MediaBrowser.Tests.PgData.FakeLogManager().GetLogger("tvdb-tests"));
    }
}
