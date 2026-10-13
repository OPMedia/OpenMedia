using System.Text;
using MediaBrowser.Common.Events;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Serialization;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Text;

namespace Emby.Server.HttpServer.WebSockets
{
    /// <summary>
    /// WebSocket 连接（等价旧 Emby.Server.Implementations.HttpServer.WebSocketConnection）：
    /// JSON 文本消息协议 WebSocketMessage&lt;T&gt;，编码检测，OnReceive 分发。
    /// </summary>
    public class WebSocketConnection : IWebSocketConnection
    {
        public event EventHandler<EventArgs> Closed;

        private readonly IWebSocket _socket;

        public string RemoteEndPoint { get; private set; }

        private readonly ILogger _logger;

        private readonly IJsonSerializer _jsonSerializer;

        public Func<WebSocketMessageInfo, Task> OnReceive { get; set; }

        public DateTime LastActivityDate { get; private set; }

        public Guid Id { get; private set; }

        public string Url { get; set; }

        public QueryParamCollection QueryString { get; set; }

        private readonly ITextEncoding _textEncoding;

        public WebSocketConnection(IWebSocket socket, string remoteEndPoint, IJsonSerializer jsonSerializer, ILogger logger, ITextEncoding textEncoding)
        {
            if (socket == null)
            {
                throw new ArgumentNullException("socket");
            }
            if (string.IsNullOrEmpty(remoteEndPoint))
            {
                throw new ArgumentNullException("remoteEndPoint");
            }
            if (jsonSerializer == null)
            {
                throw new ArgumentNullException("jsonSerializer");
            }
            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }

            Id = Guid.NewGuid();
            _jsonSerializer = jsonSerializer;
            _socket = socket;
            _socket.OnReceiveBytes = OnReceiveInternal;

            RemoteEndPoint = remoteEndPoint;
            _logger = logger;
            _textEncoding = textEncoding;

            socket.Closed += socket_Closed;
        }

        void socket_Closed(object sender, EventArgs e)
        {
            EventHelper.FireEventIfNotNull(Closed, this, EventArgs.Empty, _logger);
        }

        /// <summary>
        /// Called when [receive].
        /// </summary>
        private void OnReceiveInternal(byte[] bytes)
        {
            LastActivityDate = DateTime.UtcNow;

            if (OnReceive == null)
            {
                return;
            }

            string charset;
            try
            {
                charset = _textEncoding.GetDetectedEncodingName(bytes, bytes.Length, null, false);
            }
            catch
            {
                charset = null;
            }

            if (string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase))
            {
                OnReceiveInternal(Encoding.UTF8.GetString(bytes, 0, bytes.Length));
            }
            else
            {
                OnReceiveInternal(_textEncoding.GetASCIIEncoding().GetString(bytes, 0, bytes.Length));
            }
        }

        /// <summary>
        /// Called when [receive].
        /// </summary>
        private void OnReceiveInternal(string message)
        {
            LastActivityDate = DateTime.UtcNow;

            if (!message.StartsWith("{", StringComparison.OrdinalIgnoreCase))
            {
                // This info is useful sometimes but also clogs up the log
                return;
            }

            if (OnReceive == null)
            {
                return;
            }

            try
            {
                var stub = (WebSocketMessage<object>)_jsonSerializer.DeserializeFromString(message, typeof(WebSocketMessage<object>));

                var info = new WebSocketMessageInfo
                {
                    MessageType = stub.MessageType,
                    Data = stub.Data == null ? null : stub.Data.ToString(),
                    Connection = this
                };

                OnReceive(info);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error processing web socket message", ex);
            }
        }

        /// <summary>
        /// Sends a message asynchronously.
        /// </summary>
        public Task SendAsync<T>(WebSocketMessage<T> message, CancellationToken cancellationToken)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            var json = _jsonSerializer.SerializeToString(message);

            return SendAsync(json, cancellationToken);
        }

        /// <summary>
        /// Sends a message asynchronously.
        /// </summary>
        public Task SendAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }

            cancellationToken.ThrowIfCancellationRequested();

            return _socket.SendAsync(buffer, true, cancellationToken);
        }

        public Task SendAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentNullException("text");
            }

            cancellationToken.ThrowIfCancellationRequested();

            return _socket.SendAsync(text, true, cancellationToken);
        }

        /// <summary>
        /// Gets the state.
        /// </summary>
        public WebSocketState State
        {
            get { return _socket.State; }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        protected virtual void Dispose(bool dispose)
        {
            if (dispose)
            {
                _socket.Dispose();
            }
        }
    }
}
