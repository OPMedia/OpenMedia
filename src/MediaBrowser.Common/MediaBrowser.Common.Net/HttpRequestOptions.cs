using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MediaBrowser.Common.Net;

public class HttpRequestOptions
{
	public string Url { get; set; }

	public CompressionMethod? DecompressionMethod { get; set; }

	public string AcceptHeader
	{
		get
		{
			return GetHeaderValue("Accept");
		}
		set
		{
			RequestHeaders["Accept"] = value;
		}
	}

	public CancellationToken CancellationToken { get; set; }

	public SemaphoreSlim ResourcePool { get; set; }

	public string UserAgent
	{
		get
		{
			return GetHeaderValue("User-Agent");
		}
		set
		{
			RequestHeaders["User-Agent"] = value;
		}
	}

	public string Referer { get; set; }

	public string Host { get; set; }

	public IProgress<double> Progress { get; set; }

	public bool EnableHttpCompression { get; set; }

	public Dictionary<string, string> RequestHeaders { get; private set; }

	public string RequestContentType { get; set; }

	public string RequestContent { get; set; }

	public byte[] RequestContentBytes { get; set; }

	public bool BufferContent { get; set; }

	public bool LogRequest { get; set; }

	public bool LogRequestAsDebug { get; set; }

	public bool LogErrors { get; set; }

	public bool LogResponse { get; set; }

	public bool LogResponseHeaders { get; set; }

	public bool LogErrorResponseBody { get; set; }

	public bool EnableKeepAlive { get; set; }

	public CacheMode CacheMode { get; set; }

	public TimeSpan CacheLength { get; set; }

	public int TimeoutMs { get; set; }

	public bool EnableDefaultUserAgent { get; set; }

	public bool AppendCharsetToMimeType { get; set; }

	public string DownloadFilePath { get; set; }

	private string GetHeaderValue(string name)
	{
		RequestHeaders.TryGetValue(name, out var value);
		return value;
	}

	public HttpRequestOptions()
	{
		EnableHttpCompression = true;
		RequestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		LogRequest = true;
		LogErrors = true;
		CacheMode = CacheMode.None;
		TimeoutMs = 20000;
	}

	public void SetPostData(IDictionary<string, string> values)
	{
		IEnumerable<string> source = values.Keys.Select((string key) => $"{key}={values[key]}");
		string requestContent = string.Join("&", source.ToArray());
		RequestContent = requestContent;
		RequestContentType = "application/x-www-form-urlencoded";
	}
}
