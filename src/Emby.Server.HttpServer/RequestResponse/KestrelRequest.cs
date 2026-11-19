using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Emby.Server.HttpServer.Routing;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.VisualStudio.Threading;
using Microsoft.Extensions.Primitives;
using IHttpFile = MediaBrowser.Model.Services.IHttpFile;
using IHttpRequest = MediaBrowser.Model.Services.IHttpRequest;
using IHttpResponse = MediaBrowser.Model.Services.IHttpResponse;
using IResponse = MediaBrowser.Model.Services.IResponse;

namespace Emby.Server.HttpServer.RequestResponse
{
    /// <summary>
    /// IHttpRequest 的 HttpContext 适配（等价旧 WebSocketSharpRequest）。
    /// </summary>
    public class KestrelRequest : IHttpRequest
    {
        // IHttpRequest.Files 是同步属性；表单解析在同步边界用 JTF.Run 完成。
        private static readonly JoinableTaskFactory _joinableTaskFactory = new JoinableTaskFactory(new JoinableTaskContext());

        private readonly HttpContext _context;
        private readonly IHttpResponse _response;
        private readonly IReadOnlyList<IPNetwork> _knownProxies;
        private readonly INetworkManager _networkManager;
        private bool? _isTrustedProxy;

        private QueryParamCollection _headers;
        private QueryParamCollection _queryString;
        private Dictionary<string, object> _items;
        private Dictionary<string, Cookie> _cookies;
        private string _responseContentType;
        private string _pathInfo;
        private string _remoteIp;
        private string _httpMethod;
        private IHttpFile[] _httpFiles;
        private QueryParamCollection _formData;
        private Uri _absoluteUri;

        public KestrelRequest(HttpContext context, string operationName)
            : this(context, operationName, null, null, null)
        {
        }

        /// <summary>
        /// 带部署配置的构造函数。knownProxies 为空表示忽略所有 X-Forwarded-*；
        /// baseUrl 用于代理剥掉路径前缀时仍生成带前缀的绝对 URL。
        /// </summary>
        public KestrelRequest(
            HttpContext context,
            string operationName,
            IEnumerable<string> knownProxies,
            INetworkManager networkManager = null,
            string baseUrl = null)
        {
            _context = context;
            OperationName = operationName;
            _response = new KestrelResponse(context.Response, this);
            _knownProxies = ForwardedHeadersPolicy.ParseKnownProxies(knownProxies);
            _networkManager = networkManager;

            var normalizedBaseUrl = BaseUrlHelper.Normalize(baseUrl);
            if (normalizedBaseUrl.Length > 0 && !HasBaseUrlPrefix(context.Request.Path.Value, normalizedBaseUrl))
            {
                // 反向代理常见写法：location /media/ { proxy_pass http://backend/; }
                // 此时 Kestrel 收到的 Path 不含 /media，用 PathBase 补回，
                // 使 GetUri()/AbsoluteUri 生成的绝对 URL 与公网路径一致。
                context.Request.PathBase = new PathString(normalizedBaseUrl);
            }

            // 允许 body 重复读取（form 解析 + JSON 反序列化 + IRequiresRequestStream）
            context.Request.EnableBuffering();
        }

        private static bool HasBaseUrlPrefix(string path, string baseUrl)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return string.Equals(path, baseUrl, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(baseUrl + "/", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsTrustedProxy
        {
            get
            {
                if (_isTrustedProxy == null)
                {
                    _isTrustedProxy = ForwardedHeadersPolicy.IsTrustedPeer(
                        _context.Connection.RemoteIpAddress,
                        _knownProxies);
                }

                return _isTrustedProxy.Value;
            }
        }

        public HttpContext HttpContext => _context;

        public object OriginalRequest
        {
            get { return _context; }
        }

        public IResponse Response
        {
            get { return _response; }
        }

        public IHttpResponse HttpResponse
        {
            get { return _response; }
        }

        public string OperationName { get; set; }

        public object Dto { get; set; }

        public string RawUrl
        {
            get
            {
                return _context.Request.Path.Value + _context.Request.QueryString.Value;
            }
        }

        public string AbsoluteUri
        {
            get
            {
                if (_absoluteUri == null)
                {
                    _absoluteUri = GetUri();
                }
                return _absoluteUri.AbsoluteUri.TrimEnd('/');
            }
        }

        public Uri GetUri()
        {
            var request = _context.Request;

            var scheme = request.Scheme;
            var forwardedProto = XForwardedProtocol;
            if (string.Equals(forwardedProto, "https", StringComparison.OrdinalIgnoreCase)
                || string.Equals(forwardedProto, "http", StringComparison.OrdinalIgnoreCase))
            {
                scheme = forwardedProto;
            }

            return new Uri(UriHelper.BuildAbsolute(scheme, request.Host, request.PathBase, request.Path, request.QueryString));
        }

        public string UserHostAddress
        {
            get
            {
                return _context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            }
        }

        // 这四个属性是转发头的「可信视图」：直连对端不在 KnownProxies 里时返回 null。
        public string XForwardedFor
        {
            get
            {
                return IsTrustedProxy ? _context.Request.Headers["X-Forwarded-For"].FirstOrDefault() : null;
            }
        }

        public int? XForwardedPort
        {
            get
            {
                if (!IsTrustedProxy)
                {
                    return null;
                }

                var value = _context.Request.Headers["X-Forwarded-Port"].FirstOrDefault();
                return int.TryParse(value, out var port) ? (int?)port : null;
            }
        }

        public string XForwardedProtocol
        {
            get
            {
                return IsTrustedProxy ? _context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() : null;
            }
        }

        public string XRealIp
        {
            get
            {
                return IsTrustedProxy ? _context.Request.Headers["X-Real-IP"].FirstOrDefault() : null;
            }
        }

        public string RemoteIp
        {
            get
            {
                if (_remoteIp == null)
                {
                    _remoteIp = ForwardedHeadersPolicy.GetClientAddress(
                                    _context.Request.Headers["X-Forwarded-For"].FirstOrDefault(),
                                    _context.Request.Headers["X-Real-IP"].FirstOrDefault(),
                                    _context.Connection.RemoteIpAddress,
                                    _knownProxies)
                                ?? (_context.Connection.RemoteIpAddress != null ? NormalizeIp(_context.Connection.RemoteIpAddress.ToString()) : null)
                                ?? string.Empty;
                }
                return _remoteIp;
            }
        }

        private static string NormalizeIp(string ip)
        {
            if (!string.IsNullOrWhiteSpace(ip))
            {
                // Handle ipv4 mapped to ipv6
                const string srch = "::ffff:";
                var index = ip.IndexOf(srch, StringComparison.OrdinalIgnoreCase);
                if (index == 0)
                {
                    ip = ip.Substring(srch.Length);
                }
            }

            return ip;
        }

        public bool IsSecureConnection
        {
            get
            {
                return _context.Request.IsHttps
                    || string.Equals(XForwardedProtocol, "https", StringComparison.OrdinalIgnoreCase);
            }
        }

        public string[] AcceptTypes
        {
            get
            {
                var accept = Accept;
                if (string.IsNullOrEmpty(accept))
                {
                    return null;
                }

                return accept.Split(',')
                    .Select(i => i.Trim())
                    .Where(i => i.Length > 0)
                    .ToArray();
            }
        }

        public string Accept
        {
            get
            {
                return _context.Request.Headers["Accept"].FirstOrDefault();
            }
        }

        public string Authorization
        {
            get
            {
                return _context.Request.Headers["Authorization"].FirstOrDefault();
            }
        }

        public Dictionary<string, object> Items
        {
            get { return _items ?? (_items = new Dictionary<string, object>()); }
        }

        public string ResponseContentType
        {
            get
            {
                return _responseContentType
                    ?? (_responseContentType = GetResponseContentType(this));
            }
            set
            {
                _responseContentType = value;
            }
        }

        public const string FormUrlEncoded = "application/x-www-form-urlencoded";
        public const string MultiPartFormData = "multipart/form-data";
        public const string Soap11 = "text/xml; charset=utf-8";
        public const string Xml = "application/xml";

        public static string GetResponseContentType(IRequest httpReq)
        {
            var specifiedContentType = GetQueryStringContentType(httpReq);
            if (!string.IsNullOrEmpty(specifiedContentType)) return specifiedContentType;

            var serverDefaultContentType = "application/json";

            var acceptContentTypes = httpReq.AcceptTypes;
            string defaultContentType = null;
            if (HasAnyOfContentTypes(httpReq, FormUrlEncoded, MultiPartFormData))
            {
                defaultContentType = serverDefaultContentType;
            }

            var acceptsAnything = false;
            var hasDefaultContentType = !string.IsNullOrEmpty(defaultContentType);
            if (acceptContentTypes != null)
            {
                foreach (var acceptsType in acceptContentTypes)
                {
                    var contentType = Results.KestrelHttpResultFactory.GetRealContentType(acceptsType);
                    acceptsAnything = acceptsAnything || contentType == "*/*";
                }

                if (acceptsAnything)
                {
                    if (hasDefaultContentType)
                        return defaultContentType;
                    if (serverDefaultContentType != null)
                        return serverDefaultContentType;
                }
            }

            if (acceptContentTypes == null && httpReq.ContentType == Soap11)
            {
                return Soap11;
            }

            //We could also send a '406 Not Acceptable', but this is allowed also
            return serverDefaultContentType;
        }

        private static string GetQueryStringContentType(IRequest httpReq)
        {
            var format = httpReq.QueryString["format"];
            if (format == null)
            {
                const int formatMaxLength = 4;
                var pi = httpReq.PathInfo;
                if (pi == null || pi.Length <= formatMaxLength) return null;
                if (pi[0] == '/') pi = pi.Substring(1);
                format = LeftPart(pi, '/');
                if (format.Length > formatMaxLength) return null;
            }

            format = LeftPart(format, '.').ToLower();
            if (format.Contains("json")) return "application/json";
            if (format.Contains("xml")) return Xml;

            return null;
        }

        public static bool HasAnyOfContentTypes(IRequest request, params string[] contentTypes)
        {
            if (contentTypes == null || request.ContentType == null) return false;
            foreach (var contentType in contentTypes)
            {
                if (IsContentType(request, contentType)) return true;
            }
            return false;
        }

        public static bool IsContentType(IRequest request, string contentType)
        {
            return request.ContentType.StartsWith(contentType, StringComparison.OrdinalIgnoreCase);
        }

        public static string LeftPart(string strVal, char needle)
        {
            if (strVal == null) return null;
            var pos = strVal.IndexOf(needle);
            return pos == -1
                ? strVal
                : strVal.Substring(0, pos);
        }

        public QueryParamCollection Headers
        {
            get
            {
                if (_headers == null)
                {
                    _headers = new QueryParamCollection();
                    foreach (var header in _context.Request.Headers)
                    {
                        foreach (var value in header.Value)
                        {
                            _headers.Add(header.Key, value);
                        }
                    }
                }
                return _headers;
            }
        }

        public QueryParamCollection QueryString
        {
            get
            {
                if (_queryString == null)
                {
                    _queryString = QueryHelpers.ParseQuery(_context.Request.QueryString.Value ?? string.Empty).ToQueryParamCollection();
                }
                return _queryString;
            }
        }

        public bool IsLocal
        {
            get
            {
                var remote = _context.Connection.RemoteIpAddress;
                if (remote == null)
                {
                    return true;
                }

                if (IPAddress.IsLoopback(remote))
                {
                    return true;
                }

                // 未配置可信代理时保持原有语义（仅直连 loopback 算本地）。
                // 只有可信代理提供的地址才参与局域网判定。
                if (!IsTrustedProxy)
                {
                    return false;
                }

                var forwardedAddress = ForwardedHeadersPolicy.GetClientAddress(
                    _context.Request.Headers["X-Forwarded-For"].FirstOrDefault(),
                    _context.Request.Headers["X-Real-IP"].FirstOrDefault(),
                    remote,
                    _knownProxies);

                if (string.IsNullOrEmpty(forwardedAddress))
                {
                    return false;
                }

                if (IPAddress.TryParse(forwardedAddress, out var address) && IPAddress.IsLoopback(address))
                {
                    return true;
                }

                return _networkManager != null && _networkManager.IsInLocalNetwork(forwardedAddress);
            }
        }

        public string HttpMethod
        {
            get
            {
                return _httpMethod
                    ?? (_httpMethod = _context.Request.Method);
            }
        }

        public string Verb
        {
            get { return HttpMethod; }
        }

        public string ContentType
        {
            get { return _context.Request.ContentType; }
        }

        public Uri UrlReferrer
        {
            get
            {
                var referrer = _context.Request.Headers["Referer"].FirstOrDefault();
                if (string.IsNullOrEmpty(referrer))
                {
                    return null;
                }

                Uri uri;
                if (Uri.TryCreate(referrer, UriKind.Absolute, out uri))
                {
                    return uri;
                }
                return null;
            }
        }

        public Stream InputStream
        {
            get
            {
                var body = _context.Request.Body;
                if (body.CanSeek && body.Position != 0)
                {
                    body.Position = 0;
                }
                return body;
            }
        }

        public long ContentLength
        {
            get { return _context.Request.ContentLength ?? 0; }
        }

        public IHttpFile[] Files
        {
            get
            {
                if (_httpFiles == null)
                {
                    var form = _joinableTaskFactory.Run(() => GetParsedFormAsync());
                    if (form == null || form.Files.Count == 0)
                    {
                        return _httpFiles = Array.Empty<IHttpFile>();
                    }

                    _httpFiles = form.Files.Select(f => new HttpFile
                    {
                        Name = f.Name,
                        FileName = f.FileName,
                        ContentLength = f.Length,
                        ContentType = f.ContentType,
                        InputStream = f.OpenReadStream()
                    }).ToArray();
                }
                return _httpFiles;
            }
        }

        public string PathInfo
        {
            get
            {
                if (this._pathInfo == null)
                {
                    // ASP.NET Core Path.Value 已是解码后的路径（等价旧 UrlDecode(RawUrl path 部分)）
                    this._pathInfo = _context.Request.Path.Value ?? "/";
                }
                return this._pathInfo;
            }
        }

        public IDictionary<string, Cookie> Cookies
        {
            get
            {
                if (_cookies == null)
                {
                    _cookies = new Dictionary<string, Cookie>();
                    foreach (var headerValue in _context.Request.Headers["Cookie"])
                    {
                        if (string.IsNullOrEmpty(headerValue))
                        {
                            continue;
                        }

                        var segments = headerValue.Split(';');
                        foreach (var segment in segments)
                        {
                            var pair = segment.Trim();
                            if (pair.Length == 0)
                            {
                                continue;
                            }

                            var equalsPos = pair.IndexOf('=');
                            if (equalsPos == -1)
                            {
                                continue;
                            }

                            var name = pair.Substring(0, equalsPos).Trim();
                            var value = pair.Substring(equalsPos + 1).Trim();
                            if (name.Length > 0 && !_cookies.ContainsKey(name))
                            {
                                _cookies[name] = new Cookie(name, value);
                            }
                        }
                    }
                }

                return _cookies;
            }
        }

        public string UserAgent
        {
            get { return _context.Request.Headers["User-Agent"].FirstOrDefault(); }
        }

        public Task<QueryParamCollection> GetFormData()
        {
            if (_formData != null)
            {
                return Task.FromResult(_formData);
            }

            return GetFormDataAsync();
        }

        private async Task<QueryParamCollection> GetFormDataAsync()
        {
            var contentType = _context.Request.ContentType;
            if (string.IsNullOrEmpty(contentType))
            {
                return _formData = new QueryParamCollection();
            }

            if (contentType.StartsWith(MultiPartFormData, StringComparison.OrdinalIgnoreCase))
            {
                var form = await GetParsedFormAsync().ConfigureAwait(false);
                var result = new QueryParamCollection();
                if (form != null)
                {
                    foreach (var key in form.Keys)
                    {
                        foreach (var value in form[key])
                        {
                            result.Add(key, value);
                        }
                    }
                }
                return _formData = result;
            }

            if (contentType.StartsWith(FormUrlEncoded, StringComparison.OrdinalIgnoreCase))
            {
                string bodyText;
                using (var reader = new StreamReader(InputStream, Encoding.UTF8, leaveOpen: true))
                {
                    bodyText = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                // 重置位置，允许后续 JSON 反序列化 / IRequiresRequestStream 再读
                if (_context.Request.Body.CanSeek)
                {
                    _context.Request.Body.Position = 0;
                }

                return _formData = QueryHelpers.ParseQuery(bodyText).ToQueryParamCollection();
            }

            return _formData = new QueryParamCollection();
        }

        private async Task<IFormCollection> GetParsedFormAsync()
        {
            if (_context.Request.HasFormContentType)
            {
                try
                {
                    return await _context.Request.ReadFormAsync().ConfigureAwait(false);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }
    }

    public class HttpFile : IHttpFile
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public long ContentLength { get; set; }
        public string ContentType { get; set; }
        public Stream InputStream { get; set; }
    }
}
