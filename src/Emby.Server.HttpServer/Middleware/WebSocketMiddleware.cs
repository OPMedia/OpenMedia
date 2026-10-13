using System;
using System.Threading.Tasks;
using Emby.Server.HttpServer.WebSockets;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using Microsoft.AspNetCore.Http;

namespace Emby.Server.HttpServer.Middleware
{
    /// <summary>
    /// WebSocket 中间件（等价旧 WebSocketSharpListener.ProcessWebSocketRequest）：
    /// 任意路径的 upgrade 请求 → WebSocketConnecting 检查 → Accept → 连接分发 → 接收循环。
    /// </summary>
    public class WebSocketMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly KestrelHost _host;

        public WebSocketMiddleware(RequestDelegate next, KestrelHost host)
        {
            _next = next;
            _host = host;
        }

        public async Task Invoke(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            await ProcessWebSocketRequest(context).ConfigureAwait(false);
        }

        private async Task ProcessWebSocketRequest(HttpContext context)
        {
            try
            {
                var endpoint = context.Connection.RemoteIpAddress != null
                    ? context.Connection.RemoteIpAddress + ":" + context.Connection.RemotePort
                    : "unknown";

                var url = context.Request.Path.Value + context.Request.QueryString.Value;

                var queryString = MyHttpUtility.ParseQueryString(context.Request.QueryString.Value ?? string.Empty);

                var connectingArgs = new WebSocketConnectingEventArgs
                {
                    Url = url,
                    QueryString = queryString,
                    Endpoint = endpoint
                };

                var connecting = _host.WebSocketConnecting;
                if (connecting != null)
                {
                    connecting(connectingArgs);
                }

                if (!connectingArgs.AllowConnection)
                {
                    _host.Logger.Warn("Web socket connection not allowed");
                    context.Response.StatusCode = 401;
                    return;
                }

                _host.Logger.Debug("Web socket connection allowed");

                var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

                var socket = new KestrelWebSocket(webSocket);

                var connection = new WebSocketConnection(socket, endpoint, GetJsonSerializer(), _host.Logger, _host.TextEncoding)
                {
                    OnReceive = _host.ProcessWebSocketMessageReceived,
                    Url = url,
                    QueryString = queryString
                };

                connection.Closed += Connection_Closed;

                _host.AddWebSocketConnection(connection);

                _host.OnWebSocketConnected(connection);

                // 接收循环：连接关闭时返回
                await socket.StartReceive(context.RequestAborted).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _host.Logger.ErrorException("AcceptWebSocketAsync error", ex);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = 500;
                }
            }
        }

        private void Connection_Closed(object sender, EventArgs e)
        {
            _host.RemoveWebSocketConnection((IWebSocketConnection)sender);
        }

        private MediaBrowser.Model.Serialization.IJsonSerializer GetJsonSerializer()
        {
            // 经 KestrelHost 的序列化委托间接获取：反序列化 WebSocketMessage<object>
            // 这里直接复用 host 持有的实例
            return _host.JsonSerializer;
        }
    }
}
