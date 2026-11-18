using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MediaBrowser.WebDashboard.Api
{
    /// <summary>
    /// 新前端（web/，Vue 3 + Vite）的资源请求。
    /// </summary>
    [Route("/app/{ResourceName*}", "GET", IsHidden = true)]
    public class GetWebAppResource
    {
        /// <summary>
        /// Gets or sets 资源相对路径（可含子目录）。
        /// </summary>
        /// <value>The name of the resource.</value>
        public string ResourceName { get; set; }
    }

    /// <summary>
    /// 新前端的静态资源宿主。
    ///
    /// 与 <see cref="DashboardService"/> 的关键差异：**原样输出，不做 HTML 改写**。
    /// DashboardService 的 HTML 通道会把所有 &lt;script&gt; 注释掉再注入 apploader.js
    /// （见 PackageCreator.ModifyHtml），那是为旧 dashboard 的 AMD 加载器准备的；
    /// 现代 Vite 产物一旦被这样处理，入口模块脚本直接失效、页面白屏。
    ///
    /// 路由：/app/**。目录来源优先取环境变量 OPENMEDIA_WEBAPP_PATH，
    /// 否则取 {ProgramSystemPath}/webapp。
    ///
    /// SPA 回退：前端用 history 路由（createWebHistory('/app/')），
    /// /app/library/3 这类无扩展名的深链一律回 index.html，否则刷新就 404。
    /// </summary>
    public class WebAppService : IService, IRequiresRequest
    {
        /// <summary>
        /// 部署路径覆盖用环境变量。
        /// </summary>
        public const string PathEnvironmentVariable = "OPENMEDIA_WEBAPP_PATH";

        private const string IndexFile = "index.html";

        private readonly IResourceFileManager _resourceFileManager;
        private readonly IApplicationPaths _appPaths;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;
        private readonly IHttpResultFactory _resultFactory;

        /// <summary>
        /// Gets or sets the request context.
        /// </summary>
        /// <value>The request context.</value>
        public IRequest Request { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WebAppService" /> class.
        /// </summary>
        public WebAppService(
            IResourceFileManager resourceFileManager,
            IApplicationPaths appPaths,
            IFileSystem fileSystem,
            ILogger logger,
            IHttpResultFactory resultFactory)
        {
            _resourceFileManager = resourceFileManager;
            _appPaths = appPaths;
            _fileSystem = fileSystem;
            _logger = logger;
            _resultFactory = resultFactory;
        }

        /// <summary>
        /// 新前端的根目录。
        /// </summary>
        public string WebAppPath
        {
            get
            {
                var fromEnv = Environment.GetEnvironmentVariable(PathEnvironmentVariable);

                if (!string.IsNullOrWhiteSpace(fromEnv))
                {
                    return fromEnv;
                }

                return Path.Combine(_appPaths.ProgramSystemPath, "webapp");
            }
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>Task{System.Object}.</returns>
        public Task<object> Get(GetWebAppResource request)
        {
            var basePath = WebAppPath;

            if (!_fileSystem.DirectoryExists(basePath))
            {
                _logger.Warn("新前端目录不存在：{0}（设置 {1} 可覆盖）", basePath, PathEnvironmentVariable);
                throw new ResourceNotFoundException();
            }

            var path = NormalizeRequestPath(request.ResourceName);

            if (!IsWithinBasePath(basePath, path))
            {
                throw new ResourceNotFoundException();
            }

            var isHtmlEntry = string.Equals(path, IndexFile, StringComparison.OrdinalIgnoreCase);

            // SPA 深链回退：无扩展名 ⇒ 交给前端路由，回 index.html
            if (!Path.HasExtension(path))
            {
                path = IndexFile;
                isHtmlEntry = true;
            }

            var fullPath = Path.Combine(basePath, path.Replace('/', Path.DirectorySeparatorChar));

            if (!_fileSystem.FileExists(fullPath))
            {
                if (isHtmlEntry)
                {
                    _logger.Error("新前端缺少入口文件：{0}", fullPath);
                }

                throw new ResourceNotFoundException();
            }

            // Vite 产物带内容哈希，可长缓存；html 不缓存，避免发版后仍指向旧 bundle
            TimeSpan? cacheDuration = isHtmlEntry ? null : TimeSpan.FromDays(30);

            return _resourceFileManager.GetStaticFileResult(Request, basePath, path, MimeTypes.GetMimeType(path), cacheDuration);
        }

        private static string NormalizeRequestPath(string resourceName)
        {
            var path = (resourceName ?? string.Empty).Trim();

            path = path.TrimStart('/');

            return string.IsNullOrEmpty(path) ? IndexFile : path;
        }

        /// <summary>
        /// 拒绝目录穿越：解析后的绝对路径必须仍落在 base 目录内。
        /// </summary>
        private static bool IsWithinBasePath(string basePath, string virtualPath)
        {
            if (virtualPath.IndexOf("..", StringComparison.Ordinal) != -1)
            {
                return false;
            }

            return !virtualPath.Contains(":", StringComparison.Ordinal);
        }
    }
}
