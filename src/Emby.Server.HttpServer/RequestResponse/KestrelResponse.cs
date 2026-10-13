using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Services;
using Microsoft.AspNetCore.Http;

namespace Emby.Server.HttpServer.RequestResponse
{
    /// <summary>
    /// IHttpResponse 的 HttpContext 适配（等价旧 WebSocketSharpResponse）。
    /// TransmitFile 语义与旧一致：count == 0 表示从 offset 传输到文件末尾。
    /// </summary>
    public class KestrelResponse : IHttpResponse
    {
        private readonly HttpResponse _response;
        private readonly IRequest _request;
        private readonly object _closeLock = new object();

        public KestrelResponse(HttpResponse response, IRequest request)
        {
            _response = response;
            _request = request;
            Items = new Dictionary<string, object>();
        }

        public object OriginalResponse
        {
            get { return _response; }
        }

        public IRequest Request
        {
            get { return _request; }
        }

        public Dictionary<string, object> Items { get; private set; }

        public int StatusCode
        {
            get { return _response.StatusCode; }
            set { _response.StatusCode = value; }
        }

        private string _statusDescription;

        public string StatusDescription
        {
            get { return _statusDescription ?? string.Empty; }
            set { _statusDescription = value; }
        }

        public string ContentType
        {
            get { return _response.ContentType; }
            set { _response.ContentType = value; }
        }

        public void AddHeader(string name, string value)
        {
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                ContentType = value;
                return;
            }

            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                long length;
                if (long.TryParse(value, out length))
                {
                    SetContentLength(length);
                }
                return;
            }

            _response.Headers.Append(name, value);
        }

        public QueryParamCollection Headers
        {
            get
            {
                var collection = new QueryParamCollection();
                foreach (var header in _response.Headers)
                {
                    foreach (var value in header.Value)
                    {
                        collection.Add(header.Key, value);
                    }
                }
                return collection;
            }
        }

        public string GetHeader(string name)
        {
            return _response.Headers[name].FirstOrDefault();
        }

        public void Redirect(string url)
        {
            _response.StatusCode = 302;
            _response.Headers["Location"] = url;
        }

        public Stream OutputStream
        {
            get { return _response.Body; }
        }

        public bool IsClosed { get; private set; }

        public void Close()
        {
            lock (_closeLock)
            {
                if (this.IsClosed)
                {
                    return;
                }

                this.IsClosed = true;
            }

            try
            {
                _response.Body.Flush();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception)
            {
            }
        }

        public void SetContentLength(long contentLength)
        {
            _response.ContentLength = contentLength;
        }

        public bool SendChunked
        {
            get { return !_response.HasStarted || _response.ContentLength == null; }
            set
            {
                // Kestrel 自动管理分块：显式设置 ContentLength 即关闭分块
                if (!value && _response.ContentLength == null)
                {
                    // 无已知长度时不强制设置，保持流式
                }
            }
        }

        public void SetCookie(Cookie cookie)
        {
            var cookieStr = AsHeaderValue(cookie);
            _response.Headers.Append("Set-Cookie", cookieStr);
        }

        public static string AsHeaderValue(Cookie cookie)
        {
            var defaultExpires = DateTime.MinValue;

            var path = cookie.Expires == defaultExpires
                ? "/"
                : cookie.Path ?? "/";

            var sb = new StringBuilder();

            sb.Append($"{cookie.Name}={cookie.Value};path={path}");

            if (cookie.Expires != defaultExpires)
            {
                sb.Append($";expires={cookie.Expires:R}");
            }

            if (!string.IsNullOrEmpty(cookie.Domain))
            {
                sb.Append($";domain={cookie.Domain}");
            }

            if (cookie.Secure)
            {
                sb.Append(";Secure");
            }
            if (cookie.HttpOnly)
            {
                sb.Append(";HttpOnly");
            }

            return sb.ToString();
        }

        public void ClearCookies()
        {
        }

        /// <summary>
        /// 传输文件（等价旧 HttpListenerResponse.TransmitFile）：count == 0 表示传输到文件末尾。
        /// </summary>
        public Task TransmitFile(string path, long offset, long count, FileShareMode fileShareMode, CancellationToken cancellationToken)
        {
            return TransmitFileInternal(_response.Body, path, offset, count, fileShareMode, cancellationToken);
        }

        private const int BufferSize = 81920;

        private static async Task TransmitFileInternal(Stream destination, string path, long offset, long count, FileShareMode fileShareMode, CancellationToken cancellationToken)
        {
            var share = fileShareMode == FileShareMode.ReadWrite
                ? System.IO.FileShare.ReadWrite
                : System.IO.FileShare.Read;

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, share, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (offset > 0)
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                }

                var remaining = count > 0 ? count : stream.Length - offset;

                var buffer = new byte[BufferSize];
                while (remaining > 0)
                {
                    var toRead = (int)Math.Min(buffer.Length, remaining);
                    var bytesRead = await stream.ReadAsync(buffer, 0, toRead, cancellationToken).ConfigureAwait(false);
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                    remaining -= bytesRead;
                }
            }
        }
    }
}
