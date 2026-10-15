using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.HttpServer.Logging;
using Emby.Server.HttpServer.RequestResponse;
using Emby.Server.HttpServer.Routing;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Security;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using Microsoft.AspNetCore.Http;

namespace Emby.Server.HttpServer.Middleware
{
    /// <summary>
    /// 核心分发中间件（等价旧 HttpListenerHost.RequestHandler + ServiceHandler.ProcessRequestAsync）：
    /// 验证 → CORS → 重定向 → 路由匹配 → DTO 绑定 → 认证过滤器 → 服务执行 → 响应过滤器 → 响应写出 → 异常处理 → 日志。
    /// </summary>
    public class ServiceRoutingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly KestrelHost _host;

        public ServiceRoutingMiddleware(RequestDelegate next, KestrelHost host)
        {
            _next = next;
            _host = host;
        }

        public async Task Invoke(HttpContext context)
        {
            var pathSegments = context.Request.Path.Value?.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            var operationName = pathSegments.Length > 0 ? pathSegments[pathSegments.Length - 1] : string.Empty;

            var httpReq = new KestrelRequest(context, operationName);

            var uri = httpReq.GetUri();
            var urlString = uri.OriginalString;
            var host = uri.Host;
            var localPath = httpReq.PathInfo;

            await RequestHandler(httpReq, urlString, host, localPath, context.RequestAborted).ConfigureAwait(false);
        }

        private async Task RequestHandler(IHttpRequest httpReq, string urlString, string host, string localPath, CancellationToken cancellationToken)
        {
            var date = DateTime.Now;
            var httpRes = httpReq.Response;
            bool enableLog = false;
            bool logHeaders = false;
            string urlToLog = null;
            string remoteIp = httpReq.RemoteIp;

            try
            {
                if (_host.IsShuttingDown)
                {
                    httpRes.StatusCode = 503;
                    httpRes.ContentType = "text/plain";
                    await Write(httpRes, "Server shutting down").ConfigureAwait(false);
                    return;
                }

                if (!ValidateHost(host))
                {
                    httpRes.StatusCode = 400;
                    httpRes.ContentType = "text/plain";
                    await Write(httpRes, "Invalid host").ConfigureAwait(false);
                    return;
                }

                if (!ValidateRequest(remoteIp, httpReq.IsLocal))
                {
                    httpRes.StatusCode = 403;
                    httpRes.ContentType = "text/plain";
                    await Write(httpRes, "Forbidden").ConfigureAwait(false);
                    return;
                }

                if (!ValidateSsl(httpReq.RemoteIp, urlString))
                {
                    RedirectToSecureUrl(httpReq, httpRes, urlString);
                    return;
                }

                if (string.Equals(httpReq.Verb, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    httpRes.StatusCode = 200;
                    httpRes.AddHeader("Access-Control-Allow-Origin", "*");
                    httpRes.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, PATCH, OPTIONS");
                    httpRes.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, Range, X-MediaBrowser-Token, X-Emby-Authorization");
                    httpRes.ContentType = "text/plain";
                    await Write(httpRes, string.Empty).ConfigureAwait(false);
                    return;
                }

                var operationName = httpReq.OperationName;

                enableLog = EnableLogging(urlString, localPath);
                urlToLog = urlString;
                logHeaders = enableLog && urlToLog.IndexOf("/videos/", StringComparison.OrdinalIgnoreCase) != -1;

                if (enableLog)
                {
                    urlToLog = GetUrlToLog(urlString);

                    LoggerUtils.LogRequest(_host.Logger, urlToLog, httpReq.HttpMethod, httpReq.UserAgent, logHeaders ? httpReq.Headers : null);
                }

                if (string.Equals(localPath, "/emby/", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(localPath, "/mediabrowser/", StringComparison.OrdinalIgnoreCase))
                {
                    RedirectToUrl(httpRes, _host.DefaultRedirectPath);
                    return;
                }
                if (string.Equals(localPath, "/emby", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(localPath, "/mediabrowser", StringComparison.OrdinalIgnoreCase))
                {
                    RedirectToUrl(httpRes, "emby/" + _host.DefaultRedirectPath);
                    return;
                }

                if (string.Equals(localPath, "/mediabrowser/", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(localPath, "/mediabrowser", StringComparison.OrdinalIgnoreCase) ||
                    localPath.IndexOf("mediabrowser/web", StringComparison.OrdinalIgnoreCase) != -1)
                {
                    httpRes.StatusCode = 200;
                    httpRes.ContentType = "text/html";
                    var newUrl = urlString.Replace("mediabrowser", "emby", StringComparison.OrdinalIgnoreCase)
                        .Replace("/dashboard/", "/web/", StringComparison.OrdinalIgnoreCase);

                    if (!string.Equals(newUrl, urlString, StringComparison.OrdinalIgnoreCase))
                    {
                        await Write(httpRes,
                            "<!doctype html><html><head><title>Emby</title></head><body>Please update your Emby bookmark to <a href=\"" +
                            newUrl + "\">" + newUrl + "</a></body></html>").ConfigureAwait(false);
                        return;
                    }
                }

                if (localPath.IndexOf("dashboard/", StringComparison.OrdinalIgnoreCase) != -1 &&
                    localPath.IndexOf("web/dashboard", StringComparison.OrdinalIgnoreCase) == -1)
                {
                    httpRes.StatusCode = 200;
                    httpRes.ContentType = "text/html";
                    var newUrl = urlString.Replace("mediabrowser", "emby", StringComparison.OrdinalIgnoreCase)
                        .Replace("/dashboard/", "/web/", StringComparison.OrdinalIgnoreCase);

                    if (!string.Equals(newUrl, urlString, StringComparison.OrdinalIgnoreCase))
                    {
                        await Write(httpRes,
                            "<!doctype html><html><head><title>Emby</title></head><body>Please update your Emby bookmark to <a href=\"" +
                            newUrl + "\">" + newUrl + "</a></body></html>").ConfigureAwait(false);
                        return;
                    }
                }

                if (string.Equals(localPath, "/web", StringComparison.OrdinalIgnoreCase))
                {
                    RedirectToUrl(httpRes, _host.DefaultRedirectPath);
                    return;
                }
                if (string.Equals(localPath, "/web/", StringComparison.OrdinalIgnoreCase))
                {
                    RedirectToUrl(httpRes, "../" + _host.DefaultRedirectPath);
                    return;
                }
                if (string.Equals(localPath, "/", StringComparison.OrdinalIgnoreCase))
                {
                    RedirectToUrl(httpRes, _host.DefaultRedirectPath);
                    return;
                }
                if (string.IsNullOrEmpty(localPath))
                {
                    RedirectToUrl(httpRes, "/" + _host.DefaultRedirectPath);
                    return;
                }

                if (!string.Equals(httpReq.QueryString["r"], "0", StringComparison.OrdinalIgnoreCase))
                {
                    if (localPath.EndsWith("web/dashboard.html", StringComparison.OrdinalIgnoreCase))
                    {
                        RedirectToUrl(httpRes, "index.html#!/dashboard.html");
                    }

                    if (localPath.EndsWith("web/home.html", StringComparison.OrdinalIgnoreCase))
                    {
                        RedirectToUrl(httpRes, "index.html");
                    }
                }

                if (!string.IsNullOrEmpty(_host.GlobalResponse))
                {
                    // We don't want the address pings in ApplicationHost to fail
                    if (localPath.IndexOf("system/ping", StringComparison.OrdinalIgnoreCase) == -1)
                    {
                        httpRes.StatusCode = 503;
                        httpRes.ContentType = "text/html";
                        await Write(httpRes, _host.GlobalResponse).ConfigureAwait(false);
                        return;
                    }
                }

                var handler = GetServiceHandler(httpReq);

                if (handler != null)
                {
                    await ProcessServiceRequestAsync(handler.RestPath, handler.ResponseContentType, httpReq, httpRes, operationName, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await ErrorHandler(new FileNotFoundException(), httpReq, false, false).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException ex)
            {
                await ErrorHandler(ex, httpReq, false, false).ConfigureAwait(false);
            }

            catch (IOException ex)
            {
                await ErrorHandler(ex, httpReq, false, false).ConfigureAwait(false);
            }

            catch (SocketException ex)
            {
                await ErrorHandler(ex, httpReq, false, false).ConfigureAwait(false);
            }

            catch (SecurityException ex)
            {
                await ErrorHandler(ex, httpReq, false, true).ConfigureAwait(false);
            }

            catch (Exception ex)
            {
                var logException = !string.Equals(ex.GetType().Name, "SocketException", StringComparison.OrdinalIgnoreCase);

                await ErrorHandler(ex, httpReq, logException, false).ConfigureAwait(false);
            }
            finally
            {
                httpRes.Close();

                if (enableLog)
                {
                    var statusCode = httpRes.StatusCode;

                    var duration = DateTime.Now - date;

                    LoggerUtils.LogResponse(_host.Logger, statusCode, urlToLog, remoteIp, duration, logHeaders ? httpRes.Headers : null);
                }
            }
        }

        /// <summary>
        /// 路由匹配（等价旧 GetServiceHandler + FindMatchingRestPath）。
        /// </summary>
        private ServiceHandlerInfo GetServiceHandler(IHttpRequest httpReq)
        {
            var pathInfo = httpReq.PathInfo;

            var pathParts = pathInfo.TrimStart('/').Split('/');
            if (pathParts.Length == 0)
            {
                _host.Logger.Error("Path parts empty for PathInfo: {0}, Url: {1}", pathInfo, httpReq.RawUrl);
                return null;
            }

            string contentType;
            var restPath = FindMatchingRestPath(httpReq.HttpMethod, pathInfo, out contentType);

            if (restPath != null)
            {
                return new ServiceHandlerInfo
                {
                    RestPath = restPath,
                    ResponseContentType = contentType
                };
            }

            _host.Logger.Error("Could not find handler for {0}", pathInfo);
            return null;
        }

        private class ServiceHandlerInfo
        {
            public RestPath RestPath { get; set; }
            public string ResponseContentType { get; set; }
        }

        private RestPath FindMatchingRestPath(string httpMethod, string pathInfo, out string contentType)
        {
            pathInfo = DtoBinder.GetSanitizedPathInfo(pathInfo, out contentType);

            return _host.RouteTable.GetRestPathForRequest(httpMethod, pathInfo, _host.Logger);
        }

        /// <summary>
        /// 服务请求处理（等价旧 ServiceHandler.ProcessRequestAsync）。
        /// </summary>
        private async Task ProcessServiceRequestAsync(RestPath restPath, string responseContentType, IRequest httpReq, IResponse httpRes, string operationName, CancellationToken cancellationToken)
        {
            SetRoute(httpReq, restPath);

            if (responseContentType != null)
                httpReq.ResponseContentType = responseContentType;

            var request = httpReq.Dto = await DtoBinder.CreateRequest(
                _host.CreateInstance,
                (type, stream) => _host.DeserializeJson(type, stream),
                (type, stream) => _host.DeserializeXml(type, stream),
                httpReq,
                restPath).ConfigureAwait(false);

            ApplyRequestFilters(httpReq, httpRes, request);

            var response = await _host.RouteTable.Execute(request, httpReq).ConfigureAwait(false);

            // Apply response filters
            foreach (var responseFilter in _host.ResponseFilters)
            {
                responseFilter(httpReq, httpRes, response);
            }

            await ResponseWriter.WriteToResponse(httpRes, httpReq, response, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Applies the request filters（等价旧 ApplyRequestFilters：DTO + 服务类型特性，Priority 排序）。
        /// </summary>
        private void ApplyRequestFilters(IRequest req, IResponse res, object requestDto)
        {
            //Exec all RequestFilter attributes with Priority < 0
            var attributes = GetRequestFilterAttributes(requestDto.GetType());
            var i = 0;
            var count = attributes.Count;

            for (; i < count && attributes[i].Priority < 0; i++)
            {
                var attribute = attributes[i];
                attribute.RequestFilter(req, res, requestDto);
            }

            //Exec remaining RequestFilter attributes with Priority >= 0
            for (; i < count && attributes[i].Priority >= 0; i++)
            {
                var attribute = attributes[i];
                attribute.RequestFilter(req, res, requestDto);
            }
        }

        private List<IHasRequestFilter> GetRequestFilterAttributes(Type requestDtoType)
        {
            var attributes = requestDtoType.GetTypeInfo().GetCustomAttributes(true).OfType<IHasRequestFilter>().ToList();

            var serviceType = _host.RouteTable.GetServiceTypeByRequest(requestDtoType);
            if (serviceType != null)
            {
                attributes.AddRange(serviceType.GetTypeInfo().GetCustomAttributes(true).OfType<IHasRequestFilter>());
            }

            attributes.Sort((x, y) => x.Priority - y.Priority);

            return attributes;
        }

        private static void SetRoute(IRequest req, RestPath route)
        {
            req.Items["__route"] = route;
        }

        private readonly Dictionary<Type, int> _mapExceptionToStatusCode = new Dictionary<Type, int>
            {
                {typeof (ResourceNotFoundException), 404},
                {typeof (RemoteServiceUnavailableException), 502},
                {typeof (FileNotFoundException), 404},
                {typeof (SecurityException), 401},
                {typeof (PaymentRequiredException), 402},
                {typeof (ArgumentException), 400},
                // 二期 T1.1：用户输入解析失败（int/long/Uri/bool TryParse 之外的漏网点）兜底 400；
                // IOE/NRE/InvalidCastException 保持 500 作回归探测器（docs/phase2-design.md §1.2）
                {typeof (FormatException), 400},
                // P8：查询门满（50 并发长查询在跑）——服务过载快速失败不雪崩（U2 止损层）
                {typeof (MediaBrowser.Model.Net.PoolExhaustedException), 503}
            };

        private Exception GetActualException(Exception ex)
        {
            var agg = ex as AggregateException;
            if (agg != null)
            {
                var inner = agg.InnerException;
                if (inner != null)
                {
                    return GetActualException(inner);
                }
                else
                {
                    var inners = agg.InnerExceptions;
                    if (inners != null && inners.Count > 0)
                    {
                        return GetActualException(inners[0]);
                    }
                }
            }

            return ex;
        }

        private int GetStatusCode(Exception ex)
        {
            if (ex is ArgumentException)
            {
                return 400;
            }

            // 派生类兜底（UriFormatException 等）：字典是精确类型匹配，与 ArgumentException 同模式前置检查
            if (ex is FormatException)
            {
                return 400;
            }

            var exceptionType = ex.GetType();

            int statusCode;
            if (!_mapExceptionToStatusCode.TryGetValue(exceptionType, out statusCode))
            {
                if (ex is DirectoryNotFoundException)
                {
                    statusCode = 404;
                }
                else
                {
                    statusCode = 500;
                }
            }

            return statusCode;
        }

        private async Task ErrorHandler(Exception ex, IRequest httpReq, bool logExceptionStackTrace, bool logExceptionMessage)
        {
            try
            {
                ex = GetActualException(ex);

                if (logExceptionStackTrace)
                {
                    _host.Logger.ErrorException("Error processing request", ex);
                }
                else if (logExceptionMessage)
                {
                    _host.Logger.Error(ex.Message);
                }

                var httpRes = httpReq.Response;

                if (httpRes.IsClosed)
                {
                    return;
                }

                var statusCode = GetStatusCode(ex);
                httpRes.StatusCode = statusCode;

                httpRes.ContentType = "text/html";
                await Write(httpRes, NormalizeExceptionMessage(ex.Message)).ConfigureAwait(false);
            }
            catch
            {
                //_logger.ErrorException("Error this.ProcessRequest(context)(Exception while writing error to the response)", errorEx);
            }
        }

        private string NormalizeExceptionMessage(string msg)
        {
            if (msg == null)
            {
                return string.Empty;
            }

            // Strip any information we don't want to reveal

            var applicationPaths = _host.ConfigurationManager.ApplicationPaths;
            msg = msg.Replace(applicationPaths.ProgramSystemPath, string.Empty, StringComparison.OrdinalIgnoreCase);
            msg = msg.Replace(applicationPaths.ProgramDataPath, string.Empty, StringComparison.OrdinalIgnoreCase);

            return msg;
        }

        private Task Write(IResponse response, string text)
        {
            var bOutput = Encoding.UTF8.GetBytes(text);
            response.SetContentLength(bOutput.Length);

            return response.OutputStream.WriteAsync(bOutput, 0, bOutput.Length);
        }

        private void RedirectToSecureUrl(IHttpRequest httpReq, IResponse httpRes, string url)
        {
            int currentPort;
            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                currentPort = uri.Port;
                var builder = new UriBuilder(uri);
                builder.Port = _host.ConfigurationManager.Configuration.PublicHttpsPort;
                builder.Scheme = "https";
                url = builder.Uri.ToString();

                RedirectToUrl(httpRes, url);
            }
            else
            {
                var httpsUrl = url
                    .Replace("http://", "https://", StringComparison.OrdinalIgnoreCase)
                    .Replace(":" + _host.ConfigurationManager.Configuration.PublicPort.ToString(CultureInfo.InvariantCulture), ":" + _host.ConfigurationManager.Configuration.PublicHttpsPort.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

                RedirectToUrl(httpRes, url);
            }
        }

        public static void RedirectToUrl(IResponse httpRes, string url)
        {
            httpRes.StatusCode = 302;
            httpRes.AddHeader("Location", url);
        }

        private readonly Dictionary<string, int> _skipLogExtensions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            {".js", 0},
            {".css", 0},
            {".woff", 0},
            {".woff2", 0},
            {".ttf", 0},
            {".html", 0}
        };

        private bool EnableLogging(string url, string localPath)
        {
            var extension = GetExtension(url);

            if (string.IsNullOrEmpty(extension) || !_skipLogExtensions.ContainsKey(extension))
            {
                if (string.IsNullOrEmpty(localPath) || localPath.IndexOf("system/ping", StringComparison.OrdinalIgnoreCase) == -1)
                {
                    return true;
                }
            }

            return false;
        }

        private string GetExtension(string url)
        {
            var parts = url.Split(new[] { '?' }, 2);

            return Path.GetExtension(parts[0]);
        }

        public static string RemoveQueryStringByKey(string url, string key)
        {
            var uri = new Uri(url);

            // this gets all the query string key value pairs as a collection
            var newQueryString = MyHttpUtility.ParseQueryString(uri.Query);

            var originalCount = newQueryString.Count;

            if (originalCount == 0)
            {
                return url;
            }

            // this removes the key if exists
            newQueryString.Remove(key);

            if (originalCount == newQueryString.Count)
            {
                return url;
            }

            // this gets the page path from root without QueryString
            string pagePathWithoutQueryString = url.Split(new[] { '?' }, StringSplitOptions.RemoveEmptyEntries)[0];

            return newQueryString.Count > 0
                ? String.Format("{0}?{1}", pagePathWithoutQueryString, newQueryString)
                : pagePathWithoutQueryString;
        }

        private string GetUrlToLog(string url)
        {
            url = RemoveQueryStringByKey(url, "api_key");

            return url;
        }

        private string NormalizeConfiguredLocalAddress(string address)
        {
            var index = address.Trim('/').IndexOf('/');

            if (index != -1)
            {
                address = address.Substring(index + 1);
            }

            return address.Trim('/');
        }

        private bool ValidateHost(string host)
        {
            var hosts = _host.ConfigurationManager
                .Configuration
                .LocalNetworkAddresses
                .Select(NormalizeConfiguredLocalAddress)
                .ToList();

            if (hosts.Count == 0)
            {
                return true;
            }

            host = host ?? string.Empty;

            if (_host.NetworkManager.IsInPrivateAddressSpace(host))
            {
                hosts.Add("localhost");
                hosts.Add("127.0.0.1");

                return hosts.Any(i => host.IndexOf(i, StringComparison.OrdinalIgnoreCase) != -1);
            }

            return true;
        }

        private bool ValidateRequest(string remoteIp, bool isLocal)
        {
            if (isLocal)
            {
                return true;
            }

            if (_host.ConfigurationManager.Configuration.EnableRemoteAccess)
            {
                var addressFilter = _host.ConfigurationManager.Configuration.RemoteIPFilter.Where(i => !string.IsNullOrWhiteSpace(i)).ToArray();

                if (addressFilter.Length > 0 && !_host.NetworkManager.IsInLocalNetwork(remoteIp))
                {
                    if (_host.ConfigurationManager.Configuration.IsRemoteIPFilterBlacklist)
                    {
                        return !_host.NetworkManager.IsAddressInSubnets(remoteIp, addressFilter);
                    }
                    else
                    {
                        return _host.NetworkManager.IsAddressInSubnets(remoteIp, addressFilter);
                    }
                }
            }
            else
            {
                if (!_host.NetworkManager.IsInLocalNetwork(remoteIp))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ValidateSsl(string remoteIp, string urlString)
        {
            if (_host.ConfigurationManager.Configuration.RequireHttps && _host.EnableHttps && !_host.ConfigurationManager.Configuration.IsBehindProxy)
            {
                if (urlString.IndexOf("https://", StringComparison.OrdinalIgnoreCase) == -1)
                {
                    // These are hacks, but if these ever occur on ipv6 in the local network they could be incorrectly redirected
                    if (urlString.IndexOf("system/ping", StringComparison.OrdinalIgnoreCase) != -1 ||
                        urlString.IndexOf("dlna/", StringComparison.OrdinalIgnoreCase) != -1)
                    {
                        return true;
                    }

                    if (!_host.NetworkManager.IsInLocalNetwork(remoteIp))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
