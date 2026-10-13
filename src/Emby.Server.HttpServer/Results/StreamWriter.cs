using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

namespace Emby.Server.HttpServer.Results
{
    /// <summary>
    /// 流/字节结果（等价旧 Emby.Server.Implementations.HttpServer.StreamWriter）。
    /// </summary>
    public class StreamWriter : IAsyncStreamWriter, IHasHeaders
    {
        private ILogger Logger { get; set; }

        private static readonly CultureInfo UsCulture = new CultureInfo("en-US");

        /// <summary>
        /// Gets or sets the source stream.
        /// </summary>
        private Stream SourceStream { get; set; }

        private byte[] SourceBytes { get; set; }

        private readonly IDictionary<string, string> _options = new Dictionary<string, string>();

        public IDictionary<string, string> Headers
        {
            get { return _options; }
        }

        public Action OnComplete { get; set; }
        public Action OnError { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StreamWriter" /> class.
        /// </summary>
        public StreamWriter(Stream source, string contentType, ILogger logger)
        {
            if (string.IsNullOrEmpty(contentType))
            {
                throw new ArgumentNullException("contentType");
            }

            SourceStream = source;
            Logger = logger;

            Headers["Content-Type"] = contentType;

            if (source.CanSeek)
            {
                Headers["Content-Length"] = source.Length.ToString(UsCulture);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StreamWriter"/> class.
        /// </summary>
        public StreamWriter(byte[] source, string contentType, int contentLength, ILogger logger)
        {
            if (string.IsNullOrEmpty(contentType))
            {
                throw new ArgumentNullException("contentType");
            }

            SourceBytes = source;
            Logger = logger;

            Headers["Content-Type"] = contentType;

            Headers["Content-Length"] = contentLength.ToString(UsCulture);
        }

        public async Task WriteToAsync(Stream responseStream, CancellationToken cancellationToken)
        {
            try
            {
                var bytes = SourceBytes;

                if (bytes != null)
                {
                    await responseStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                }
                else
                {
                    using (var src = SourceStream)
                    {
                        await src.CopyToAsync(responseStream).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                if (OnError != null)
                {
                    OnError();
                }

                throw;
            }
            finally
            {
                if (OnComplete != null)
                {
                    OnComplete();
                }
            }
        }
    }
}
