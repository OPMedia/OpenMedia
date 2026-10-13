using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Server.HttpServer.WebSockets
{
    /// <summary>
    /// 内部 WebSocket 抽象（等价旧 Emby.Server.Implementations.Net.IWebSocket）。
    /// </summary>
    public interface IWebSocket : IDisposable
    {
        event EventHandler<EventArgs> Closed;

        WebSocketState State { get; }

        Action<byte[]> OnReceiveBytes { get; set; }

        Task SendAsync(byte[] bytes, bool endOfMessage, CancellationToken cancellationToken);

        Task SendAsync(string text, bool endOfMessage, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 原生 System.Net.WebSockets.WebSocket 适配（等价旧 SharpWebSocket）：
    /// 内部接收循环聚合消息帧，关闭时触发 Closed。
    /// </summary>
    public class KestrelWebSocket : IWebSocket
    {
        private readonly System.Net.WebSockets.WebSocket _socket;
        private readonly TaskCompletionSource<bool> _taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public event EventHandler<EventArgs> Closed;

        public Action<byte[]> OnReceiveBytes { get; set; }

        public KestrelWebSocket(System.Net.WebSockets.WebSocket socket)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        }

        public WebSocketState State => _socket.State;

        /// <summary>
        /// 启动接收循环；返回的 Task 在连接关闭时完成（等价旧 SharpWebSocket.StartReceive）。
        /// </summary>
        public Task StartReceive(CancellationToken cancellationToken)
        {
            _ = ReceiveLoopAsync(cancellationToken);
            return _taskCompletionSource.Task;
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[16 * 1024];

            try
            {
                while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    using (var messageStream = new MemoryStream())
                    {
                        do
                        {
                            result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await CloseAsync(cancellationToken).ConfigureAwait(false);
                                return;
                            }

                            messageStream.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        if (OnReceiveBytes != null && messageStream.Length > 0)
                        {
                            OnReceiveBytes(messageStream.ToArray());
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception)
            {
            }
            finally
            {
                _taskCompletionSource.TrySetResult(true);
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task CloseAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 发送二进制帧。
        /// </summary>
        public async Task SendAsync(byte[] bytes, bool endOfMessage, CancellationToken cancellationToken)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            cancellationToken.ThrowIfCancellationRequested();

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// 发送文本帧。
        /// </summary>
        public async Task SendAsync(string text, bool endOfMessage, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentNullException(nameof(text));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var bytes = Encoding.UTF8.GetBytes(text);

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private bool _disposed;

        protected virtual void Dispose(bool dispose)
        {
            if (dispose && !_disposed)
            {
                _disposed = true;

                try
                {
                    if (_socket.State == WebSocketState.Open)
                    {
                        _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", CancellationToken.None).GetAwaiter().GetResult();
                    }
                }
                catch
                {
                }

                _socket.Dispose();
                _sendLock.Dispose();
            }
        }
    }
}
