using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Updates;

namespace MediaBrowser.Common.Updates;

public class GithubUpdater
{
	public class Uploader
	{
		public string login { get; set; }

		public int id { get; set; }

		public string avatar_url { get; set; }

		public string gravatar_id { get; set; }

		public string url { get; set; }

		public string html_url { get; set; }

		public string followers_url { get; set; }

		public string following_url { get; set; }

		public string gists_url { get; set; }

		public string starred_url { get; set; }

		public string subscriptions_url { get; set; }

		public string organizations_url { get; set; }

		public string repos_url { get; set; }

		public string events_url { get; set; }

		public string received_events_url { get; set; }

		public string type { get; set; }

		public bool site_admin { get; set; }
	}

	public class Asset
	{
		public string url { get; set; }

		public int id { get; set; }

		public string name { get; set; }

		public object label { get; set; }

		public Uploader uploader { get; set; }

		public string content_type { get; set; }

		public string state { get; set; }

		public int size { get; set; }

		public int download_count { get; set; }

		public string created_at { get; set; }

		public string updated_at { get; set; }

		public string browser_download_url { get; set; }
	}

	public class Author
	{
		public string login { get; set; }

		public int id { get; set; }

		public string avatar_url { get; set; }

		public string gravatar_id { get; set; }

		public string url { get; set; }

		public string html_url { get; set; }

		public string followers_url { get; set; }

		public string following_url { get; set; }

		public string gists_url { get; set; }

		public string starred_url { get; set; }

		public string subscriptions_url { get; set; }

		public string organizations_url { get; set; }

		public string repos_url { get; set; }

		public string events_url { get; set; }

		public string received_events_url { get; set; }

		public string type { get; set; }

		public bool site_admin { get; set; }
	}

	public class RootObject
	{
		public string url { get; set; }

		public string assets_url { get; set; }

		public string upload_url { get; set; }

		public string html_url { get; set; }

		public int id { get; set; }

		public string tag_name { get; set; }

		public string target_commitish { get; set; }

		public string name { get; set; }

		public bool draft { get; set; }

		public Author author { get; set; }

		public bool prerelease { get; set; }

		public string created_at { get; set; }

		public string published_at { get; set; }

		public List<Asset> assets { get; set; }

		public string tarball_url { get; set; }

		public string zipball_url { get; set; }

		public string body { get; set; }
	}

	private readonly IHttpClient _httpClient;

	private readonly IJsonSerializer _jsonSerializer;

	public GithubUpdater(IHttpClient httpClient, IJsonSerializer jsonSerializer)
	{
		_httpClient = httpClient;
		_jsonSerializer = jsonSerializer;
	}

	public async Task<CheckForUpdateResult> CheckForUpdateResult(string organzation, string repository, Version minVersion, PackageVersionClass updateLevel, string assetFilename, string packageName, string targetFilename, TimeSpan cacheLength, CancellationToken cancellationToken)
	{
		string url = $"https://api.github.com/repos/{organzation}/{repository}/releases";
		HttpRequestOptions httpRequestOptions = new HttpRequestOptions
		{
			Url = url,
			EnableKeepAlive = false,
			CancellationToken = cancellationToken,
			UserAgent = "Emby/3.0",
			BufferContent = false
		};
		if (cacheLength.Ticks > 0)
		{
			httpRequestOptions.CacheMode = CacheMode.Unconditional;
			httpRequestOptions.CacheLength = cacheLength;
		}
		using HttpResponseInfo response = await _httpClient.SendAsync(httpRequestOptions, "GET").ConfigureAwait(continueOnCapturedContext: false);
		using Stream stream = response.Content;
		return CheckForUpdateResult(await _jsonSerializer.DeserializeFromStreamAsync<RootObject[]>(stream).ConfigureAwait(continueOnCapturedContext: false), minVersion, updateLevel, assetFilename, packageName, targetFilename);
	}

	private CheckForUpdateResult CheckForUpdateResult(RootObject[] obj, Version minVersion, PackageVersionClass updateLevel, string assetFilename, string packageName, string targetFilename)
	{
		switch (updateLevel)
		{
		case PackageVersionClass.Release:
			obj = obj.Where((RootObject i) => !i.prerelease && !i.name.EndsWith("-beta", StringComparison.OrdinalIgnoreCase) && !i.name.EndsWith("-dev", StringComparison.OrdinalIgnoreCase)).ToArray();
			break;
		case PackageVersionClass.Beta:
			obj = obj.Where((RootObject i) => i.prerelease && i.name.EndsWith("-beta", StringComparison.OrdinalIgnoreCase)).ToArray();
			break;
		case PackageVersionClass.Dev:
			obj = obj.Where((RootObject i) => !i.prerelease || i.name.EndsWith("-beta", StringComparison.OrdinalIgnoreCase) || i.name.EndsWith("-dev", StringComparison.OrdinalIgnoreCase)).ToArray();
			break;
		}
		return (from i in obj
			select CheckForUpdateResult(i, minVersion, assetFilename, packageName, targetFilename) into i
			where i != null
			orderby Version.Parse(i.AvailableVersion) descending
			select i).FirstOrDefault() ?? new CheckForUpdateResult
		{
			IsUpdateAvailable = false
		};
	}

	private bool MatchesUpdateLevel(RootObject i, PackageVersionClass updateLevel)
	{
		switch (updateLevel)
		{
		case PackageVersionClass.Beta:
			if (i.prerelease)
			{
				return i.name.EndsWith("-beta", StringComparison.OrdinalIgnoreCase);
			}
			return false;
		case PackageVersionClass.Dev:
			if (i.prerelease && !i.name.EndsWith("-beta", StringComparison.OrdinalIgnoreCase))
			{
				return i.name.EndsWith("-dev", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		default:
			if (!i.prerelease && !i.name.EndsWith("-beta", StringComparison.OrdinalIgnoreCase))
			{
				return !i.name.EndsWith("-dev", StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}
	}

	public async Task<List<RootObject>> GetLatestReleases(string organzation, string repository, string assetFilename, CancellationToken cancellationToken)
	{
		List<RootObject> list = new List<RootObject>();
		string url = $"https://api.github.com/repos/{organzation}/{repository}/releases";
		HttpRequestOptions options = new HttpRequestOptions
		{
			Url = url,
			EnableKeepAlive = false,
			CancellationToken = cancellationToken,
			UserAgent = "Emby/3.0",
			BufferContent = false
		};
		using HttpResponseInfo response = await _httpClient.SendAsync(options, "GET").ConfigureAwait(continueOnCapturedContext: false);
		using Stream stream = response.Content;
		RootObject[] source = (await _jsonSerializer.DeserializeFromStreamAsync<RootObject[]>(stream).ConfigureAwait(continueOnCapturedContext: false)).Where((RootObject i) => (i.assets ?? new List<Asset>()).Any((Asset a) => IsAsset(a, assetFilename, i.tag_name))).ToArray();
		list.AddRange(source.Where((RootObject i) => MatchesUpdateLevel(i, PackageVersionClass.Release)).OrderByDescending(GetVersion).Take(1));
		list.AddRange(source.Where((RootObject i) => MatchesUpdateLevel(i, PackageVersionClass.Beta)).OrderByDescending(GetVersion).Take(1));
		list.AddRange(source.Where((RootObject i) => MatchesUpdateLevel(i, PackageVersionClass.Dev)).OrderByDescending(GetVersion).Take(1));
		return list;
	}

	public Version GetVersion(RootObject obj)
	{
		if (!Version.TryParse(obj.tag_name, out Version result))
		{
			return new Version(1, 0);
		}
		return result;
	}

	private CheckForUpdateResult CheckForUpdateResult(RootObject obj, Version minVersion, string assetFilename, string packageName, string targetFilename)
	{
		string versionString = obj.tag_name;
		if (!Version.TryParse(versionString, out Version result))
		{
			return null;
		}
		if (result < minVersion)
		{
			return null;
		}
		Asset asset = (obj.assets ?? new List<Asset>()).FirstOrDefault((Asset i) => IsAsset(i, assetFilename, versionString));
		if (asset == null)
		{
			return null;
		}
		return new CheckForUpdateResult
		{
			AvailableVersion = result.ToString(),
			IsUpdateAvailable = (result > minVersion),
			Package = new PackageVersionInfo
			{
				classification = (obj.prerelease ? ((!obj.name.EndsWith("-dev", StringComparison.OrdinalIgnoreCase)) ? PackageVersionClass.Beta : PackageVersionClass.Dev) : PackageVersionClass.Release),
				name = packageName,
				sourceUrl = asset.browser_download_url,
				targetFilename = targetFilename,
				versionStr = result.ToString(),
				requiredVersionStr = "1.0.0",
				description = obj.body,
				infoUrl = obj.html_url
			}
		};
	}

	private bool IsAsset(Asset asset, string assetFilename, string version)
	{
		string text = Path.GetFileName(asset.browser_download_url) ?? string.Empty;
		assetFilename = assetFilename.Replace("{version}", version);
		if (text.IndexOf(assetFilename, StringComparison.OrdinalIgnoreCase) != -1)
		{
			return true;
		}
		return string.Equals(assetFilename, text, StringComparison.OrdinalIgnoreCase);
	}
}
