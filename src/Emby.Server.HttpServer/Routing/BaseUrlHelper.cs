using System;

namespace Emby.Server.HttpServer.Routing
{
    /// <summary>
    /// ServerConfiguration.BaseUrl 的路径规范化工具。规范化后的值要么为空字符串
    /// （根路径部署），要么是以 '/' 开头、不以 '/' 结尾的前缀（例如 /media）。
    /// </summary>
    public static class BaseUrlHelper
    {
        public static string Normalize(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return string.Empty;
            }

            var value = baseUrl.Trim();
            if (value.IndexOf('?') >= 0 || value.IndexOf('#') >= 0 || value.IndexOf(' ') >= 0)
            {
                return string.Empty;
            }

            if (value[0] != '/')
            {
                value = "/" + value;
            }

            value = "/" + value.Trim('/');
            return value == "/" ? string.Empty : value;
        }

        public static string Prepend(string baseUrl, string path)
        {
            var normalized = Normalize(baseUrl);
            if (normalized.Length == 0)
            {
                return path;
            }

            return path.StartsWith("/", StringComparison.Ordinal)
                ? normalized + path
                : normalized + "/" + path;
        }

        /// <summary>
        /// 去掉部署前缀，供重定向等兼容旧路径的逻辑使用。
        /// </summary>
        public static string Strip(string path, string baseUrl)
        {
            var normalized = Normalize(baseUrl);
            if (normalized.Length == 0 || path == null)
            {
                return path;
            }

            if (string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase))
            {
                // 与根路径 "" 走同一条重定向分支，保证 Location 带回部署前缀。
                return string.Empty;
            }

            if (path.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(normalized.Length);
            }

            return path;
        }
    }
}
