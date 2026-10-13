using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Controller.IO;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace Emby.Server.Implementations.HttpClientManager
{
    /// <summary>
    /// Class HttpClientManager
    /// </summary>
    public class HttpClientManager : IHttpClient
    {
        /// <summary>
        /// When one request to a host times out, we'll ban all other requests for this period of time, to prevent scans from stalling
        /// </summary>
        private const int TimeoutSeconds = 30;

        /// <summary>
        /// The _logger
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// The _app paths
        /// </summary>
        private readonly IApplicationPaths _appPaths;

        private readonly IFileSystem _fileSystem;
        private readonly Func<string> _defaultUserAgentFn;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpClientManager" /> class.
        /// </summary>
        public HttpClientManager(IApplicationPaths appPaths, ILogger logger, IFileSystem fileSystem, Func<string> defaultUserAgentFn)
        {
            if (appPaths == null)
            {
                throw new ArgumentNullException("appPaths");
            }
            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }

            _logger = logger;
            _fileSystem = fileSystem;
            _appPaths = appPaths;
            _defaultUserAgentFn = defaultUserAgentFn;
        }

        /// <summary>
        /// Holds a dictionary of http clients by host.  Use GetHttpClient(host) to retrieve or create a client for web requests.
        /// DON'T dispose it after use.
        /// </summary>
        /// <value>The HTTP clients.</value>
        private readonly ConcurrentDictionary<string, HttpClientInfo> _httpClients = new ConcurrentDictionary<string, HttpClientInfo>();

        /// <summary>
        /// Gets
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="enableHttpCompression">if set to <c>true</c> [enable HTTP compression].</param>
        /// <returns>HttpClient.</returns>
        /// <exception cref="System.ArgumentNullException">host</exception>
        private HttpClientInfo GetHttpClient(string host, bool enableHttpCompression)
        {
            if (string.IsNullOrEmpty(host))
            {
                throw new ArgumentNullException("host");
            }

            HttpClientInfo client;

            var key = host + enableHttpCompression;

            if (!_httpClients.TryGetValue(key, out client))
            {
                client = new HttpClientInfo();

                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = enableHttpCompression
                        ? DecompressionMethods.GZip | DecompressionMethods.Deflate
                        : DecompressionMethods.None,
                    UseCookies = false
                };

                client.HttpClient = new HttpClient(handler);
                client.HttpClient.Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite);

                _httpClients.TryAdd(key, client);
            }

            return client;
        }

        private HttpRequestMessage CreateRequest(HttpRequestOptions options, string method)
        {
            var url = options.Url;

            var uriAddress = new Uri(url);
            var userInfo = uriAddress.UserInfo;
            if (!string.IsNullOrWhiteSpace(userInfo))
            {
                _logger.Info("Found userInfo in url: {0} ... url: {1}", userInfo, url);
                url = url.Replace(userInfo + "@", string.Empty);
            }

            var request = new HttpRequestMessage(new HttpMethod(method), url);

            AddRequestHeaders(request, options);

            if (!string.IsNullOrWhiteSpace(userInfo))
            {
                var parts = userInfo.Split(':');
                if (parts.Length == 2)
                {
                    var byteArray = Encoding.ASCII.GetBytes(parts[0] + ":" + parts[1]);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                }
            }

            return request;
        }

        private void AddRequestHeaders(HttpRequestMessage request, HttpRequestOptions options)
        {
            var hasUserAgent = false;

            foreach (var header in options.RequestHeaders.ToList())
            {
                if (string.Equals(header.Key, "Accept", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Accept.ParseAdd(header.Value);
                }
                else if (string.Equals(header.Key, "User-Agent", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.UserAgent.ParseAdd(header.Value);
                    hasUserAgent = true;
                }
                else if (string.Equals(header.Key, "Referer", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Referrer = new Uri(header.Value);
                }
                else if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Host = header.Value;
                }
                else
                {
                    if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        // Some headers need to be added to content headers
                        // We'll handle this when setting content
                    }
                }
            }

            if (!hasUserAgent && options.EnableDefaultUserAgent)
            {
                request.Headers.UserAgent.ParseAdd(_defaultUserAgentFn());
            }
        }

        /// <summary>
        /// Gets the response internal.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <returns>Task{HttpResponseInfo}.</returns>
        public Task<HttpResponseInfo> GetResponse(HttpRequestOptions options)
        {
            return SendAsync(options, "GET");
        }

        /// <summary>
        /// Performs a GET request and returns the resulting stream
        /// </summary>
        /// <param name="options">The options.</param>
        /// <returns>Task{Stream}.</returns>
        public async Task<Stream> Get(HttpRequestOptions options)
        {
            var response = await GetResponse(options).ConfigureAwait(false);
            return response.Content;
        }

        /// <summary>
        /// send as an asynchronous operation.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <returns>Task{HttpResponseInfo}.</returns>
        /// <exception cref="HttpException">
        /// </exception>
        public async Task<HttpResponseInfo> SendAsync(HttpRequestOptions options, string httpMethod)
        {
            if (options.CacheMode == CacheMode.None)
            {
                return await SendAsyncInternal(options, httpMethod).ConfigureAwait(false);
            }

            var url = options.Url;
            var urlHash = url.ToLower().GetMD5().ToString("N");

            var responseCachePath = Path.Combine(_appPaths.CachePath, "httpclient", urlHash);

            var response = await GetCachedResponse(responseCachePath, options.CacheLength, url).ConfigureAwait(false);
            if (response != null)
            {
                return response;
            }

            response = await SendAsyncInternal(options, httpMethod).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                await CacheResponse(response, responseCachePath).ConfigureAwait(false);
            }

            return response;
        }

        private async Task<HttpResponseInfo> GetCachedResponse(string responseCachePath, TimeSpan cacheLength, string url)
        {
            try
            {
                if (_fileSystem.GetLastWriteTimeUtc(responseCachePath).Add(cacheLength) > DateTime.UtcNow)
                {
                    using (var stream = _fileSystem.GetFileStream(responseCachePath, FileOpenMode.Open, FileAccessMode.Read, FileShareMode.Read, true))
                    {
                        var memoryStream = new MemoryStream();

                        await stream.CopyToAsync(memoryStream).ConfigureAwait(false);
                        memoryStream.Position = 0;

                        return new HttpResponseInfo
                        {
                            ResponseUrl = url,
                            Content = memoryStream,
                            StatusCode = HttpStatusCode.OK,
                            ContentLength = memoryStream.Length
                        };
                    }
                }
            }
            catch (FileNotFoundException)
            {

            }
            catch (DirectoryNotFoundException)
            {

            }

            return null;
        }

        private async Task CacheResponse(HttpResponseInfo response, string responseCachePath)
        {
            _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(responseCachePath));

            using (var responseStream = response.Content)
            {
                var memoryStream = new MemoryStream();
                await responseStream.CopyToAsync(memoryStream).ConfigureAwait(false);
                memoryStream.Position = 0;

                using (var fileStream = _fileSystem.GetFileStream(responseCachePath, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.None, true))
                {
                    await memoryStream.CopyToAsync(fileStream).ConfigureAwait(false);

                    memoryStream.Position = 0;
                    response.Content = memoryStream;
                }
            }
        }

        private async Task<HttpResponseInfo> SendAsyncInternal(HttpRequestOptions options, string httpMethod)
        {
            ValidateParams(options);

            options.CancellationToken.ThrowIfCancellationRequested();

            var client = GetHttpClient(GetHostFromUrl(options.Url), options.EnableHttpCompression);

            if ((DateTime.UtcNow - client.LastTimeout).TotalSeconds < TimeoutSeconds)
            {
                throw new HttpException(string.Format("Cancelling connection to {0} due to a previous timeout.", options.Url))
                {
                    IsTimedOut = true
                };
            }

            var request = CreateRequest(options, httpMethod);

            if (options.RequestContentBytes != null ||
                !string.IsNullOrEmpty(options.RequestContent) ||
                string.Equals(httpMethod, "post", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var requestEncoding = Encoding.UTF8;

                    var bytes = options.RequestContentBytes ?? requestEncoding.GetBytes(options.RequestContent ?? string.Empty);

                    var contentType = options.RequestContentType ?? "application/x-www-form-urlencoded";

                    if (options.AppendCharsetToMimeType)
                    {
                        contentType = contentType.TrimEnd(';') + "; charset=\"utf-8\"";
                    }

                    var content = new ByteArrayContent(bytes);
                    content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                    request.Content = content;
                }
                catch (Exception ex)
                {
                    throw new HttpException(ex.Message) { IsTimedOut = true };
                }
            }

            if (options.ResourcePool != null)
            {
                await options.ResourcePool.WaitAsync(options.CancellationToken).ConfigureAwait(false);
            }

            if ((DateTime.UtcNow - client.LastTimeout).TotalSeconds < TimeoutSeconds)
            {
                if (options.ResourcePool != null)
                {
                    options.ResourcePool.Release();
                }

                throw new HttpException(string.Format("Connection to {0} timed out", options.Url)) { IsTimedOut = true };
            }

            if (options.LogRequest)
            {
                if (options.LogRequestAsDebug)
                {
                    _logger.Debug("HttpClientManager {0}: {1}", httpMethod.ToUpper(), options.Url);
                }
                else
                {
                    _logger.Info("HttpClientManager {0}: {1}", httpMethod.ToUpper(), options.Url);
                }
            }

            try
            {
                options.CancellationToken.ThrowIfCancellationRequested();

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken))
                {
                    cts.CancelAfter(options.TimeoutMs);

                    using (var response = await client.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                    {
                        EnsureSuccessStatusCode(client, response, options);

                        options.CancellationToken.ThrowIfCancellationRequested();

                        if (!options.BufferContent)
                        {
                            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

                            return GetResponseInfo(response, stream, response.Content.Headers.ContentLength, null);
                        }

                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        {
                            var memoryStream = new MemoryStream();

                            await stream.CopyToAsync(memoryStream).ConfigureAwait(false);

                            memoryStream.Position = 0;

                            return GetResponseInfo(response, memoryStream, memoryStream.Length, null);
                        }
                    }
                }
            }
            catch (OperationCanceledException ex)
            {
                throw GetCancellationException(options, client, options.CancellationToken, ex);
            }
            catch (Exception ex)
            {
                throw GetException(ex, options, client);
            }
            finally
            {
                if (options.ResourcePool != null)
                {
                    options.ResourcePool.Release();
                }
            }
        }

        private HttpResponseInfo GetResponseInfo(HttpResponseMessage httpResponse, Stream content, long? contentLength, IDisposable disposable)
        {
            var responseInfo = new HttpResponseInfo(disposable)
            {
                Content = content,

                StatusCode = httpResponse.StatusCode,

                ContentType = httpResponse.Content?.Headers?.ContentType?.ToString(),

                ContentLength = contentLength,

                ResponseUrl = httpResponse.RequestMessage?.RequestUri?.ToString()
            };

            if (httpResponse.Headers != null)
            {
                SetHeaders(httpResponse.Headers, responseInfo);
            }

            if (httpResponse.Content?.Headers != null)
            {
                SetHeaders(httpResponse.Content.Headers, responseInfo);
            }

            return responseInfo;
        }

        private HttpResponseInfo GetResponseInfo(HttpResponseMessage httpResponse, string tempFile, long? contentLength)
        {
            var responseInfo = new HttpResponseInfo
            {
                TempFilePath = tempFile,

                StatusCode = httpResponse.StatusCode,

                ContentType = httpResponse.Content?.Headers?.ContentType?.ToString(),

                ContentLength = contentLength
            };

            if (httpResponse.Headers != null)
            {
                SetHeaders(httpResponse.Headers, responseInfo);
            }

            return responseInfo;
        }

        private void SetHeaders(HttpHeaders headers, HttpResponseInfo responseInfo)
        {
            foreach (var header in headers)
            {
                responseInfo.Headers[header.Key] = string.Join(", ", header.Value);
            }
        }

        public Task<HttpResponseInfo> Post(HttpRequestOptions options)
        {
            return SendAsync(options, "POST");
        }

        /// <summary>
        /// Performs a POST request
        /// </summary>
        /// <param name="options">The options.</param>
        /// <param name="postData">Params to add to the POST data.</param>
        /// <returns>stream on success, null on failure</returns>
        public async Task<Stream> Post(HttpRequestOptions options, Dictionary<string, string> postData)
        {
            options.SetPostData(postData);

            var response = await Post(options).ConfigureAwait(false);

            return response.Content;
        }

        /// <summary>
        /// Downloads the contents of a given url into a temporary location
        /// </summary>
        /// <param name="options">The options.</param>
        /// <returns>Task{System.String}.</returns>
        public async Task<string> GetTempFile(HttpRequestOptions options)
        {
            var response = await GetTempFileResponse(options).ConfigureAwait(false);

            return response.TempFilePath;
        }

        public async Task<HttpResponseInfo> GetTempFileResponse(HttpRequestOptions options)
        {
            ValidateParams(options);

            _fileSystem.CreateDirectory(_appPaths.TempDirectory);

            var tempFile = Path.Combine(_appPaths.TempDirectory, Guid.NewGuid() + ".tmp");

            if (options.Progress == null)
            {
                throw new ArgumentNullException("progress");
            }

            options.CancellationToken.ThrowIfCancellationRequested();

            var client = GetHttpClient(GetHostFromUrl(options.Url), options.EnableHttpCompression);

            var request = CreateRequest(options, "GET");

            if (options.ResourcePool != null)
            {
                await options.ResourcePool.WaitAsync(options.CancellationToken).ConfigureAwait(false);
            }

            options.Progress.Report(0);

            if (options.LogRequest)
            {
                if (options.LogRequestAsDebug)
                {
                    _logger.Debug("HttpClientManager.GetTempFileResponse url: {0}", options.Url);
                }
                else
                {
                    _logger.Info("HttpClientManager.GetTempFileResponse url: {0}", options.Url);
                }
            }

            try
            {
                options.CancellationToken.ThrowIfCancellationRequested();

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken))
                {
                    cts.CancelAfter(options.TimeoutMs);

                    using (var response = await client.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                    {
                        EnsureSuccessStatusCode(client, response, options);

                        options.CancellationToken.ThrowIfCancellationRequested();

                        var contentLength = response.Content?.Headers?.ContentLength;

                        if (!contentLength.HasValue || contentLength.Value == 0)
                        {
                            // We're not able to track progress
                            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            {
                                using (var fs = _fileSystem.GetFileStream(tempFile, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.Read, true))
                                {
                                    await stream.CopyToAsync(fs, StreamDefaults.DefaultCopyToBufferSize, options.CancellationToken).ConfigureAwait(false);
                                }
                            }
                        }
                        else
                        {
                            using (var fs = _fileSystem.GetFileStream(tempFile, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.Read, true))
                            {
                                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                {
                                    await stream.CopyToAsync(fs, StreamDefaults.DefaultCopyToBufferSize, options.CancellationToken).ConfigureAwait(false);
                                }
                            }
                        }

                        options.Progress.Report(100);

                        return GetResponseInfo(response, tempFile, contentLength);
                    }
                }
            }
            catch (Exception ex)
            {
                DeleteTempFile(tempFile);
                throw GetException(ex, options, client);
            }
            finally
            {
                if (options.ResourcePool != null)
                {
                    options.ResourcePool.Release();
                }
            }
        }

        protected static readonly CultureInfo UsCulture = new CultureInfo("en-US");

        private Exception GetException(Exception ex, HttpRequestOptions options, HttpClientInfo client)
        {
            if (ex is HttpException)
            {
                return ex;
            }

            var httpRequestException = ex as HttpRequestException
                                       ?? ex.InnerException as HttpRequestException;

            if (httpRequestException != null)
            {
                if (options.LogErrors)
                {
                    _logger.ErrorException("Error getting response from " + options.Url, httpRequestException);
                }

                var exception = new HttpException(httpRequestException.Message, httpRequestException);

                var taskCanceledException = ex as TaskCanceledException ?? ex.InnerException as TaskCanceledException;
                if (taskCanceledException != null)
                {
                    exception.IsTimedOut = true;
                    client.LastTimeout = DateTime.UtcNow;
                }

                return exception;
            }

            var operationCanceledException = ex as OperationCanceledException
                                             ?? ex.InnerException as OperationCanceledException;

            if (operationCanceledException != null)
            {
                return GetCancellationException(options, client, options.CancellationToken, operationCanceledException);
            }

            if (options.LogErrors)
            {
                _logger.ErrorException("Error getting response from " + options.Url, ex);
            }

            return ex;
        }

        private void DeleteTempFile(string file)
        {
            try
            {
                _fileSystem.DeleteFile(file);
            }
            catch (IOException)
            {
                // Might not have been created at all. No need to worry.
            }
        }

        private void ValidateParams(HttpRequestOptions options)
        {
            if (string.IsNullOrEmpty(options.Url))
            {
                throw new ArgumentNullException("options");
            }
        }

        /// <summary>
        /// Gets the host from URL.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <returns>System.String.</returns>
        private string GetHostFromUrl(string url)
        {
            var index = url.IndexOf("://", StringComparison.OrdinalIgnoreCase);

            if (index != -1)
            {
                url = url.Substring(index + 3);
                var host = url.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(host))
                {
                    return host;
                }
            }

            return url;
        }

        /// <summary>
        /// Throws the cancellation exception.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <param name="client">The client.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="exception">The exception.</param>
        /// <returns>Exception.</returns>
        private Exception GetCancellationException(HttpRequestOptions options, HttpClientInfo client, CancellationToken cancellationToken, OperationCanceledException exception)
        {
            // If the HttpClient's timeout is reached, it will cancel the Task internally
            if (!cancellationToken.IsCancellationRequested)
            {
                var msg = string.Format("Connection to {0} timed out", options.Url);

                if (options.LogErrors)
                {
                    _logger.Error(msg);
                }

                client.LastTimeout = DateTime.UtcNow;

                // Throw an HttpException so that the caller doesn't think it was cancelled by user code
                return new HttpException(msg, exception)
                {
                    IsTimedOut = true
                };
            }

            return exception;
        }

        private void EnsureSuccessStatusCode(HttpClientInfo client, HttpResponseMessage response, HttpRequestOptions options)
        {
            var statusCode = response.StatusCode;

            var isSuccessful = statusCode >= HttpStatusCode.OK && statusCode <= (HttpStatusCode)299;

            if (!isSuccessful)
            {
                if (options.LogErrorResponseBody)
                {
                    try
                    {
                        var content = response.Content;
                        if (content != null)
                        {
                            var msg = content.ReadAsStringAsync().GetAwaiter().GetResult();
                            _logger.Error(msg);
                        }
                    }
                    catch
                    {

                    }
                }
                throw new HttpException(response.ReasonPhrase)
                {
                    StatusCode = response.StatusCode
                };
            }
        }
    }
}