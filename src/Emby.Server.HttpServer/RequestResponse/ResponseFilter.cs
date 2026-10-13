using System;
using System.Globalization;
using System.Text;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

namespace Emby.Server.HttpServer.RequestResponse
{
    /// <summary>
    /// 响应过滤器（等价旧 ResponseFilter）：CORS 头、Server 头、Content-Length、异常错误码头。
    /// </summary>
    public class ResponseFilter
    {
        private static readonly CultureInfo UsCulture = new CultureInfo("en-US");
        private readonly ILogger _logger;

        public ResponseFilter(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Filters the response.
        /// </summary>
        public void FilterResponse(IRequest req, IResponse res, object dto)
        {
            res.AddHeader("Access-Control-Allow-Headers", "Accept, Accept-Language, Authorization, Cache-Control, Content-Disposition, Content-Encoding, Content-Language, Content-Length, Content-MD5, Content-Range, Content-Type, Date, Host, If-Match, If-Modified-Since, If-None-Match, If-Unmodified-Since, Origin, OriginToken, Pragma, Range, Slug, Transfer-Encoding, Want-Digest, X-MediaBrowser-Token, X-Emby-Authorization");
            res.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, PATCH, OPTIONS");
            res.AddHeader("Access-Control-Allow-Origin", "*");

            var exception = dto as Exception;

            if (exception != null)
            {
                _logger.ErrorException("Error processing request for {0}", exception, req.RawUrl);

                if (!string.IsNullOrEmpty(exception.Message))
                {
                    var error = exception.Message.Replace(Environment.NewLine, " ");
                    error = RemoveControlCharacters(error);

                    res.AddHeader("X-Application-Error-Code", error);
                }
            }

            var hasHeaders = dto as IHasHeaders;

            if (hasHeaders != null)
            {
                if (!hasHeaders.Headers.ContainsKey("Server"))
                {
                    hasHeaders.Headers["Server"] = "Microsoft-NetCore/2.0, UPnP/1.0 DLNADOC/1.50";
                }

                // Content length has to be explicitly set on on the response or it won't be happy
                string contentLength;

                if (hasHeaders.Headers.TryGetValue("Content-Length", out contentLength) && !string.IsNullOrEmpty(contentLength))
                {
                    var length = long.Parse(contentLength, UsCulture);

                    if (length > 0)
                    {
                        res.SetContentLength(length);
                        res.SendChunked = false;
                    }
                }
            }
        }

        /// <summary>
        /// Removes the control characters.
        /// </summary>
        public static string RemoveControlCharacters(string inString)
        {
            if (inString == null) return null;

            var newString = new StringBuilder();

            foreach (var ch in inString)
            {
                if (!char.IsControl(ch))
                {
                    newString.Append(ch);
                }
            }
            return newString.ToString();
        }
    }
}
