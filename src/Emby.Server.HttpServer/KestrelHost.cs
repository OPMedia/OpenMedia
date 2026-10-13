using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.HttpServer.Logging;
using Emby.Server.HttpServer.Middleware;
using Emby.Server.HttpServer.RequestResponse;
using Emby.Server.HttpServer.Results;
using Emby.Server.HttpServer.Routing;
using Emby.Server.HttpServer.WebSockets;
using MediaBrowser.Common.Events;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Emby.Server.HttpServer
{
    /// <summary>
    /// Kestrel 实现的 IHttpServer：替代旧 Emby.Server.Implementations.HttpServer.HttpListenerHost + SocketHttpListener。
    /// 构造函数与旧 HttpListenerHost 完全同参，ApplicationHost 仅需替换类名。
    /// </summary>
    public class KestrelHost : IHttpServer
    {
        private readonly string _defaultRedirectPath;
        private readonly ILogger _logger;
        private readonly IServerConfigurationManager _config;
        private readonly INetworkManager _networkManager;
        private readonly IServerApplicationHost _appHost;
        private readonly ITextEncoding _textEncoding;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IXmlSerializer _xmlSerializer;
        private readonly Func<Type, Func<string, object>> _funcParseFn;

        private IHost _webHost;
        private CancellationTokenSource _shutdownCts;

        private IWebSocketListener[] _webSocketListeners = Array.Empty<IWebSocketListener>();
        private readonly List<IWebSocketConnection> _webSocketConnections = new List<IWebSocketConnection>();
        private readonly object _webSocketLock = new object();

        private RouteTable _routeTable;

        public static KestrelHost Instance { get; private set; }

        public KestrelHost(IServerApplicationHost applicationHost,
            ILogger logger,
            IServerConfigurationManager config,
            string defaultRedirectPath,
            INetworkManager networkManager,
            ITextEncoding textEncoding,
            IJsonSerializer jsonSerializer,
            IXmlSerializer xmlSerializer,
            Func<Type, Func<string, object>> funcParseFn)
        {
            Instance = this;

            _appHost = applicationHost;
            _defaultRedirectPath = defaultRedirectPath;
            _networkManager = networkManager;
            _textEncoding = textEncoding;
            _jsonSerializer = jsonSerializer;
            _xmlSerializer = xmlSerializer;
            _config = config;
            _logger = logger;
            _funcParseFn = funcParseFn ?? TypeParser.GetParseFn;

            ResponseFilters = new Action<IRequest, IResponse, object>[] { };
        }

        public string[] UrlPrefixes { get; private set; }

        public string GlobalResponse { get; set; }

        /// <summary>
        /// WebSocket 连接前的允许检查（等价旧 IHttpListener.WebSocketConnecting；未订阅时默认允许）。
        /// </summary>
        public Action<WebSocketConnectingEventArgs> WebSocketConnecting { get; set; }

        public event EventHandler<GenericEventArgs<IWebSocketConnection>> WebSocketConnected;

        public Action<IRequest, IResponse, object>[] ResponseFilters { get; set; }

        public RouteTable RouteTable => _routeTable;

        /// <summary>
        /// 注册服务并构建路由表（等价旧 ServiceController.Init + GetRouteAttributes 三前缀克隆）。
        /// </summary>
        public void Init(IEnumerable<IService> services, IEnumerable<IWebSocketListener> listeners)
        {
            _webSocketListeners = listeners.ToArray();

            _routeTable = new RouteTable(_appHost.CreateInstance, _funcParseFn ?? TypeParser.GetParseFn);

            var types = services.Select(r => r.GetType()).ToArray();

            foreach (var serviceType in types)
            {
                _routeTable.RegisterService(serviceType);
            }

            ResponseFilters = new Action<IRequest, IResponse, object>[]
            {
                new ResponseFilter(_logger).FilterResponse
            };

            _logger.Info("Kestrel host initialized with {0} services, {1} rest paths", types.Length, _routeTable.RestPathCount);
        }

        /// <summary>
        /// 启动 Kestrel。等价旧 StartServer(urlPrefixes, IHttpListener)——IHttpListener 随 SocketHttpListener 退役。
        /// </summary>
        public void StartServer(string[] urlPrefixes, X509Certificate certificate = null)
        {
            UrlPrefixes = urlPrefixes;

            _shutdownCts = new CancellationTokenSource();

            var hostBuilder = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseKestrel(options =>
                    {
                        ConfigureListenEndpoints(options, urlPrefixes, certificate);
                    });

                    webBuilder.Configure(app =>
                    {
                        app.UseMiddleware<WebSocketMiddleware>(this);
                        app.UseMiddleware<ServiceRoutingMiddleware>(this);
                    });
                });

            _webHost = hostBuilder.Build();

            _logger.Info("Starting Kestrel host on {0}", string.Join(",", urlPrefixes));

            _webHost.Start();

            _logger.Info("Kestrel host started");
        }

        private void ConfigureListenEndpoints(Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions options, string[] urlPrefixes, X509Certificate certificate)
        {
            foreach (var prefix in urlPrefixes)
            {
                var binding = ParseUrlPrefix(prefix);

                var isHttps = string.Equals(binding.Scheme, "https", StringComparison.OrdinalIgnoreCase);

                if (isHttps && certificate != null)
                {
                    options.Listen(binding.Address, binding.Port, listenOptions =>
                    {
                        listenOptions.UseHttps(new X509Certificate2(certificate));
                    });
                }
                else
                {
                    options.Listen(binding.Address, binding.Port);
                }

                _logger.Info("Kestrel listening on {0}", prefix);
            }
        }

        private (string Scheme, IPAddress Address, int Port) ParseUrlPrefix(string prefix)
        {
            // 格式：http://+:8096/  http://*:8096/  http://localhost:8096/  http://192.168.1.4:8096/
            var trimmed = prefix.TrimEnd('/');
            var schemePos = trimmed.IndexOf("://", StringComparison.OrdinalIgnoreCase);
            var scheme = schemePos == -1 ? "http" : trimmed.Substring(0, schemePos);
            var rest = schemePos == -1 ? trimmed : trimmed.Substring(schemePos + 3);
            var colonPos = rest.LastIndexOf(':');

            string host;
            int port;
            if (colonPos == -1)
            {
                host = rest;
                port = string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
            }
            else
            {
                host = rest.Substring(0, colonPos);
                port = int.Parse(rest.Substring(colonPos + 1));
            }

            if (string.IsNullOrEmpty(host) || host == "+" || host == "*")
            {
                return (scheme, IPAddress.Any, port);
            }

            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return (scheme, IPAddress.Loopback, port);
            }

            if (IPAddress.TryParse(host, out var address))
            {
                return (scheme, address, port);
            }

            // DNS 主机名：解析为地址（与旧 HttpListener 行为一致）
            try
            {
                var addresses = Dns.GetHostAddresses(host);
                return (scheme, addresses.FirstOrDefault() ?? IPAddress.Any, port);
            }
            catch
            {
                return (scheme, IPAddress.Any, port);
            }
        }

        /// <summary>
        /// 停止 HTTP 服务并断开全部 WebSocket 连接。
        /// </summary>
        public void Stop()
        {
            List<IWebSocketConnection> connections;

            lock (_webSocketLock)
            {
                connections = _webSocketConnections.ToList();
                _webSocketConnections.Clear();
            }

            foreach (var connection in connections)
            {
                try
                {
                    connection.Dispose();
                }
                catch
                {
                }
            }

            _shutdownCts?.Cancel();

            if (_webHost != null)
            {
                _logger.Info("Stopping Kestrel host...");
                try
                {
                    _webHost.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error stopping Kestrel host", ex);
                }
                finally
                {
                    _webHost.Dispose();
                    _webHost = null;
                }
                _logger.Info("Kestrel host stopped");
            }
        }

        internal void AddWebSocketConnection(IWebSocketConnection connection)
        {
            lock (_webSocketLock)
            {
                _webSocketConnections.Add(connection);
            }
        }

        internal void RemoveWebSocketConnection(IWebSocketConnection connection)
        {
            lock (_webSocketLock)
            {
                _webSocketConnections.Remove(connection);
            }
        }

        internal void OnWebSocketConnected(IWebSocketConnection connection)
        {
            EventHelper.FireEventIfNotNull(WebSocketConnected, this, new GenericEventArgs<IWebSocketConnection>(connection), _logger);
        }

        /// <summary>
        /// 广播 WebSocket 消息给全部监听器（等价旧 ProcessWebSocketMessageReceived）。
        /// </summary>
        internal Task ProcessWebSocketMessageReceived(WebSocketMessageInfo result)
        {
            var tasks = _webSocketListeners.Select(i => Task.Run(async () =>
            {
                try
                {
                    await i.ProcessMessage(result).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("{0} failed processing WebSocket message {1}", ex, i.GetType().Name, result.MessageType ?? string.Empty);
                }
            }));

            return Task.WhenAll(tasks);
        }

        public object CreateInstance(Type type)
        {
            return _appHost.CreateInstance(type);
        }

        public Func<string, object> GetParseFn(Type propertyType)
        {
            return _funcParseFn(propertyType);
        }

        public string DefaultRedirectPath => _defaultRedirectPath;

        public IServerConfigurationManager ConfigurationManager => _config;

        public INetworkManager NetworkManager => _networkManager;

        public ILogger Logger => _logger;

        public ITextEncoding TextEncoding => _textEncoding;

        public MediaBrowser.Model.Serialization.IJsonSerializer JsonSerializer => _jsonSerializer;

        public MediaBrowser.Model.Serialization.IXmlSerializer XmlSerializer => _xmlSerializer;

        public bool IsShuttingDown => _shutdownCts != null && _shutdownCts.IsCancellationRequested;

        /// <summary>
        /// 是否启用 HTTPS（透传 IServerApplicationHost.EnableHttps，供 ValidateSsl 使用）。
        /// </summary>
        public bool EnableHttps => _appHost.EnableHttps;

        public void SerializeToJson(object o, Stream stream)
        {
            _jsonSerializer.SerializeToStream(o, stream);
        }

        public void SerializeToXml(object o, Stream stream)
        {
            _xmlSerializer.SerializeToStream(o, stream);
        }

        public Task<object> DeserializeXml(Type type, Stream stream)
        {
            return Task.FromResult(_xmlSerializer.DeserializeFromStream(type, stream));
        }

        public Task<object> DeserializeJson(Type type, Stream stream)
        {
            return _jsonSerializer.DeserializeFromStreamAsync(stream, type);
        }

        private bool _disposed;
        private readonly object _disposeLock = new object();

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            lock (_disposeLock)
            {
                if (_disposed) return;

                _disposed = true;

                if (disposing)
                {
                    Stop();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
