using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

namespace Emby.Server.HttpServer.Routing
{
    /// <summary>
    /// 路由表（等价旧 ServiceController）：
    /// 服务类型注册（扫描 [Route] 特性 + /emby、/mediabrowser、/emby/emby 三前缀克隆）、
    /// hash 索引查找、MatchScore 评分、服务执行。
    /// </summary>
    public class RouteTable
    {
        private readonly Func<Type, object> _createInstanceFn;
        private readonly Func<Type, Func<string, object>> _getParseFn;

        private readonly Dictionary<Type, Type> ServiceOperationsMap = new Dictionary<Type, Type>();

        public RouteTable(Func<Type, object> createInstanceFn, Func<Type, Func<string, object>> getParseFn)
        {
            _createInstanceFn = createInstanceFn;
            _getParseFn = getParseFn;
        }

        public readonly RestPath.RestPathMap RestPathMap = new RestPath.RestPathMap();

        public int RestPathCount
        {
            get
            {
                return RestPathMap.Values.Sum(v => v.Count);
            }
        }

        public void RegisterService(Type serviceType)
        {
            var processedReqs = new HashSet<Type>();

            var actions = ServiceExecutor.Reset(serviceType);

            foreach (var mi in serviceType.GetActions())
            {
                var requestType = mi.GetParameters()[0].ParameterType;
                if (processedReqs.Contains(requestType)) continue;
                processedReqs.Add(requestType);

                ServiceExecutor.CreateServiceRunnersFor(requestType, actions);

                RegisterRestPaths(requestType, serviceType);

                AddServiceInfo(serviceType, requestType);
            }
        }

        public Type GetServiceTypeByRequest(Type requestType)
        {
            Type serviceType;
            ServiceOperationsMap.TryGetValue(requestType, out serviceType);
            return serviceType;
        }

        public void AddServiceInfo(Type serviceType, Type requestType)
        {
            ServiceOperationsMap[requestType] = serviceType;
        }

        public void RegisterRestPaths(Type requestType, Type serviceType)
        {
            var attrs = GetRouteAttributes(requestType);
            foreach (RouteAttribute attr in attrs)
            {
                var restPath = new RestPath(_createInstanceFn, _getParseFn, requestType, serviceType, attr.Path, attr.Verbs, attr.IsHidden, attr.Summary, attr.Description);

                RegisterRestPath(restPath);
            }
        }

        /// <summary>
        /// 获取路由特性并克隆 /emby、/mediabrowser、/emby/emby 三前缀版本（等价旧 GetRouteAttributes）。
        /// </summary>
        public RouteAttribute[] GetRouteAttributes(Type requestType)
        {
            var routes = requestType.GetTypeInfo().GetCustomAttributes<RouteAttribute>(true).ToList();
            var clone = routes.ToList();

            foreach (var route in clone)
            {
                routes.Add(new RouteAttribute(NormalizeEmbyRoutePath(route.Path), route.Verbs)
                {
                    Notes = route.Notes,
                    Priority = route.Priority,
                    Summary = route.Summary
                });

                routes.Add(new RouteAttribute(NormalizeMediaBrowserRoutePath(route.Path), route.Verbs)
                {
                    Notes = route.Notes,
                    Priority = route.Priority,
                    Summary = route.Summary
                });

                // needed because apps add /emby, and some users also add /emby, thereby double prefixing
                routes.Add(new RouteAttribute(DoubleNormalizeEmbyRoutePath(route.Path), route.Verbs)
                {
                    Notes = route.Notes,
                    Priority = route.Priority,
                    Summary = route.Summary
                });
            }

            return routes.ToArray();
        }

        private static string NormalizeEmbyRoutePath(string path)
        {
            if (path.StartsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                return "/emby" + path;
            }

            return "emby/" + path;
        }

        private static string NormalizeMediaBrowserRoutePath(string path)
        {
            if (path.StartsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                return "/mediabrowser" + path;
            }

            return "mediabrowser/" + path;
        }

        private static string DoubleNormalizeEmbyRoutePath(string path)
        {
            if (path.StartsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                return "/emby/emby" + path;
            }

            return "emby/emby/" + path;
        }

        private static readonly char[] InvalidRouteChars = new[] { '?', '&' };

        public void RegisterRestPath(RestPath restPath)
        {
            if (!restPath.Path.StartsWith("/"))
                throw new ArgumentException(string.Format("Route '{0}' on '{1}' must start with a '/'", restPath.Path, restPath.RequestType.GetMethodName()));
            if (restPath.Path.IndexOfAny(InvalidRouteChars) != -1)
                throw new ArgumentException(string.Format("Route '{0}' on '{1}' contains invalid chars. ", restPath.Path, restPath.RequestType.GetMethodName()));

            List<RestPath> pathsAtFirstMatch;
            if (!RestPathMap.TryGetValue(restPath.FirstMatchHashKey, out pathsAtFirstMatch))
            {
                pathsAtFirstMatch = new List<RestPath>();
                RestPathMap[restPath.FirstMatchHashKey] = pathsAtFirstMatch;
            }
            pathsAtFirstMatch.Add(restPath);
        }

        public RestPath GetRestPathForRequest(string httpMethod, string pathInfo, ILogger logger)
        {
            var matchUsingPathParts = RestPath.GetPathPartsForMatching(pathInfo);

            List<RestPath> firstMatches;

            var yieldedHashMatches = RestPath.GetFirstMatchHashKeys(matchUsingPathParts);
            foreach (var potentialHashMatch in yieldedHashMatches)
            {
                if (!this.RestPathMap.TryGetValue(potentialHashMatch, out firstMatches))
                {
                    continue;
                }

                var bestScore = -1;
                RestPath bestMatch = null;
                foreach (var restPath in firstMatches)
                {
                    var score = restPath.MatchScore(httpMethod, matchUsingPathParts, logger);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = restPath;
                    }
                }

                if (bestScore > 0 && bestMatch != null)
                {
                    return bestMatch;
                }
            }

            var yieldedWildcardMatches = RestPath.GetFirstMatchWildCardHashKeys(matchUsingPathParts);
            foreach (var potentialHashMatch in yieldedWildcardMatches)
            {
                if (!this.RestPathMap.TryGetValue(potentialHashMatch, out firstMatches)) continue;

                var bestScore = -1;
                RestPath bestMatch = null;
                foreach (var restPath in firstMatches)
                {
                    var score = restPath.MatchScore(httpMethod, matchUsingPathParts, logger);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = restPath;
                    }
                }

                if (bestScore > 0 && bestMatch != null)
                {
                    return bestMatch;
                }
            }

            return null;
        }

        /// <summary>
        /// 执行服务：创建服务实例、注入 IRequiresRequest、按 Verb 调用方法（等价旧 ServiceController.Execute）。
        /// </summary>
        public Task<object> Execute(object requestDto, IRequest req)
        {
            req.Dto = requestDto;
            var requestType = requestDto.GetType();
            req.OperationName = requestType.Name;

            var serviceType = GetServiceTypeByRequest(requestType);

            var service = _createInstanceFn(serviceType);

            var serviceRequiresContext = service as IRequiresRequest;
            if (serviceRequiresContext != null)
            {
                serviceRequiresContext.Request = req;
            }

            if (req.Dto == null) // Don't override existing batched DTO[]
                req.Dto = requestDto;

            //Executes the service and returns the result
            return ServiceExecutor.Execute(serviceType, req, service, requestDto, requestType.GetMethodName());
        }
    }
}
