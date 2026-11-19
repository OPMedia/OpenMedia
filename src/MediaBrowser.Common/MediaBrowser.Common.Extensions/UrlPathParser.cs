using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaBrowser.Common.Extensions
{
    /// <summary>
    /// 把 PathInfo / 绝对 URL 拆成路由段，供 API 与 DLNA 服务共用。
    /// </summary>
    public static class UrlPathParser
    {
        /// <summary>
        /// 保留历史语义：scheme 后的第一段（URL 的 host 或路径首段）被丢弃，query 从最后一个 '?' 起截断。
        /// </summary>
        public static List<string> ParsePathInfo(string pathUri)
        {
            var actionParts = pathUri.Split(new[] { "://" }, StringSplitOptions.None);

            var pathInfo = actionParts[actionParts.Length - 1];

            var optionsPos = pathInfo.LastIndexOf('?');
            if (optionsPos != -1)
            {
                pathInfo = pathInfo.Substring(0, optionsPos);
            }

            var args = pathInfo.Split('/');

            return args.Skip(1).ToList();
        }
    }
}
