using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MediaBrowser.Model.Services;

namespace Emby.Server.HttpServer.Routing
{
    /// <summary>
    /// DTO 绑定器（等价旧 ServiceHandler 的 CreateRequest / GetFlattenedRequestParams / GetSanitizedPathInfo）：
    /// path 变量 + query + form + body(JSON/XML) → request DTO，优先级 path &lt; query &lt; form。
    /// </summary>
    public static class DtoBinder
    {
        public static string GetSanitizedPathInfo(string pathInfo, out string contentType)
        {
            contentType = null;
            var pos = pathInfo.LastIndexOf('.');
            if (pos >= 0)
            {
                var format = pathInfo.Substring(pos + 1);
                contentType = GetFormatContentType(format);
                if (contentType != null)
                {
                    pathInfo = pathInfo.Substring(0, pos);
                }
            }
            return pathInfo;
        }

        private static string GetFormatContentType(string format)
        {
            //built-in formats
            if (format == "json")
                return "application/json";
            if (format == "xml")
                return "application/xml";

            return null;
        }

        public static bool RequiresRequestStream(Type requestType)
        {
            var requiresRequestStreamTypeInfo = typeof(IRequiresRequestStream).GetTypeInfo();
            return requiresRequestStreamTypeInfo.IsAssignableFrom(requestType.GetTypeInfo());
        }

        /// <summary>
        /// 创建并填充 request DTO（等价旧 ServiceHandler.CreateRequest）。
        /// </summary>
        public static async Task<object> CreateRequest(
            Func<Type, object> createInstanceFn,
            Func<Type, Stream, Task<object>> deserializeJsonFn,
            Func<Type, Stream, Task<object>> deserializeXmlFn,
            IRequest httpReq,
            RestPath restPath)
        {
            var requestType = restPath.RequestType;

            if (RequiresRequestStream(requestType))
            {
                // Used by IRequiresRequestStream
                var requestParams = await GetRequestParams(httpReq).ConfigureAwait(false);
                var request = restPath.CreateRequest(httpReq.PathInfo, requestParams, createInstanceFn(requestType));

                var rawReq = (IRequiresRequestStream)request;
                rawReq.RequestStream = httpReq.InputStream;
                return request;
            }
            else
            {
                var requestParams = await GetFlattenedRequestParams(httpReq).ConfigureAwait(false);

                var requestDto = await CreateContentTypeRequest(createInstanceFn, deserializeJsonFn, deserializeXmlFn, httpReq, restPath.RequestType, httpReq.ContentType).ConfigureAwait(false);

                return CreateRequest(httpReq, restPath, requestParams, requestDto);
            }
        }

        private static async Task<object> CreateContentTypeRequest(
            Func<Type, object> createInstanceFn,
            Func<Type, Stream, Task<object>> deserializeJsonFn,
            Func<Type, Stream, Task<object>> deserializeXmlFn,
            IRequest httpReq,
            Type requestType,
            string contentType)
        {
            if (!string.IsNullOrEmpty(contentType) && httpReq.ContentLength > 0)
            {
                var realContentType = GetRealContentType(contentType);
                switch (realContentType)
                {
                    case "application/xml":
                    case "text/xml":
                        return await deserializeXmlFn(requestType, httpReq.InputStream).ConfigureAwait(false);
                    case "application/json":
                    case "text/json":
                        return await deserializeJsonFn(requestType, httpReq.InputStream).ConfigureAwait(false);
                }
            }
            return createInstanceFn(requestType);
        }

        public static string GetRealContentType(string contentType)
        {
            return contentType == null
                       ? null
                       : contentType.Split(';')[0].ToLower().Trim();
        }

        public static object CreateRequest(IRequest httpReq, RestPath restPath, Dictionary<string, string> requestParams, object requestDto)
        {
            string contentType;
            var pathInfo = !restPath.IsWildCardPath
                ? GetSanitizedPathInfo(httpReq.PathInfo, out contentType)
                : httpReq.PathInfo;

            return restPath.CreateRequest(pathInfo, requestParams, requestDto);
        }

        /// <summary>
        /// Duplicate Params are given a unique key by appending a #1 suffix
        /// </summary>
        private static async Task<Dictionary<string, string>> GetRequestParams(IRequest request)
        {
            var map = new Dictionary<string, string>();

            foreach (var name in request.QueryString.Keys)
            {
                if (name == null) continue;

                var values = request.QueryString.GetValues(name);
                if (values.Count == 1)
                {
                    map[name] = values[0];
                }
                else
                {
                    for (var i = 0; i < values.Count; i++)
                    {
                        map[name + (i == 0 ? "" : "#" + i)] = values[i];
                    }
                }
            }

            if ((IsMethod(request.Verb, "POST") || IsMethod(request.Verb, "PUT")))
            {
                var formData = await request.GetFormData().ConfigureAwait(false);
                if (formData != null)
                {
                    foreach (var name in formData.Keys)
                    {
                        if (name == null) continue;

                        var values = formData.GetValues(name);
                        if (values.Count == 1)
                        {
                            map[name] = values[0];
                        }
                        else
                        {
                            for (var i = 0; i < values.Count; i++)
                            {
                                map[name + (i == 0 ? "" : "#" + i)] = values[i];
                            }
                        }
                    }
                }
            }

            return map;
        }

        private static bool IsMethod(string method, string expected)
        {
            return string.Equals(method, expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Duplicate params have their values joined together in a comma-delimited string
        /// </summary>
        private static async Task<Dictionary<string, string>> GetFlattenedRequestParams(IRequest request)
        {
            var map = new Dictionary<string, string>();

            foreach (var name in request.QueryString.Keys)
            {
                if (name == null) continue;
                map[name] = request.QueryString[name];
            }

            if ((IsMethod(request.Verb, "POST") || IsMethod(request.Verb, "PUT")))
            {
                var formData = await request.GetFormData().ConfigureAwait(false);
                if (formData != null)
                {
                    foreach (var name in formData.Keys)
                    {
                        if (name == null) continue;
                        map[name] = formData[name];
                    }
                }
            }

            return map;
        }
    }
}
