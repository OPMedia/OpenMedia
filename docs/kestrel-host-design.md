# C13: Emby.Server.HttpServer（Kestrel 主机）设计文档

> 任务：用 ASP.NET Core Kestrel 替换自研 SocketHttpListener HTTP 栈（决策 D4）。
> 基线：Emby 3.5.3 开源代码（`Emby.Server.Implementations/HttpServer/` + `Emby.Server.Implementations/Services/` + `MediaBrowser.ServerApplication/SocketSharp/` + `SocketHttpListener/`）。
> 新项目：`src/Emby.Server.HttpServer/`（SDK 风格，net10.0，FrameworkReference=Microsoft.AspNetCore.App）。
> 约束：`MediaBrowser.Api` 61 个服务类**一行不改**；`IHttpServer` 接口签名**一个不改**（Controller 已定稿）。

---

## 1. 旧 HttpListenerHost 职责全清单

旧栈分四层：`HttpListenerHost`（IHttpServer 实现，业务编排）→ `IHttpListener`（WebSocketSharpListener，SocketHttpListener 包装）→ `ServiceHandler/ServiceController/RestPath`（ServiceStack 风格路由分发）→ `HttpResultFactory/StreamWriter/FileWriter/RangeRequestWriter`（响应写出）。逐项职责如下。

### 1.1 服务注册与路由表构建（Init）

| 旧代码位置 | 职责 |
|---|---|
| `HttpListenerHost.Init(services, listeners)` | 接收 ApplicationHost 的 MEF 导出：`IEnumerable<IService>`（61 个 API 服务 + DashboardService 等）与 `IEnumerable<IWebSocketListener>`；构建 ServiceController |
| `ServiceController.RegisterService` | 对每个服务类型 `GetActions()`：扫描 public 实例方法，**恰好 1 个参数**且方法名在 HTTP 动词集合内（Get/Post/Put/Delete/Patch/Head/Options/Any…，`ServiceExecExtensions.AllVerbs` 40 个） |
| `HttpListenerHost.GetRouteAttributes` | 对 request DTO 上的每个 `[Route("/Users/{Id}","GET")]` 特性**克隆 3 份**并加前缀：`/emby{path}`、`/mediabrowser{path}`、`/emby/emby{path}`（客户端双前缀兼容） |
| `RestPath` 构造 | 路径模板解析：`/` 分段；`{Var}` 变量段；`{Var*}` 通配符段（消耗多段，必须在末尾或后跟 literal）；`{a}.{b}` 点分子段；`FirstMatchHashKey`（首 literal + 段数）做字典索引 |
| `ServiceController.RegisterRestPath` | 校验（必须 `/` 开头、无 `?`/`&`），按 FirstMatchHashKey 归入 `RestPathMap`（SortedDictionary，忽略大小写） |
| `AddServiceInfo(requestType, serviceType)` | DTO 类型 → 服务类型映射（执行期反查） |

### 1.2 请求入口验证（RequestHandler 前置段）

| 检查 | 旧逻辑 | 失败响应 |
|---|---|---|
| disposed | `_disposed` | 503 "Server shutting down" |
| ValidateHost | `LocalNetworkAddresses` 配置（去前导路径），私网地址时追加 localhost/127.0.0.1 白名单 | 400 "Invalid host" |
| ValidateRequest | `EnableRemoteAccess` + `RemoteIPFilter`（`IsRemoteIPFilterBlacklist` 决定黑/白名单）+ `IsInLocalNetwork`；本地请求直通 | 403 "Forbidden" |
| ValidateSsl | `RequireHttps && EnableHttps && !IsBehindProxy` → 302 到 https 公网端口；`system/ping`、`dlna/` 例外 | 302 redirect |
| OPTIONS 预检 | 任何 OPTIONS 直接 200 + CORS 头（`*`，方法 GET/POST/PUT/DELETE/PATCH/OPTIONS，头 Content-Type/Authorization/Range/X-MediaBrowser-Token/X-Emby-Authorization） | — |

### 1.3 重定向与兼容规则（按顺序）

1. `/emby/`、`/mediabrowser/` → 302 `DefaultRedirectPath`（= `web/index.html`）
2. `/emby`、`/mediabrowser` → 302 `emby/web/index.html`
3. `mediabrowser/web`、`dashboard/`（非 `web/dashboard`）→ 200 HTML "请更新书签"（URL 改写提示）
4. `/web` → 302 `DefaultRedirectPath`；`/web/` → 302 `../DefaultRedirectPath`
5. `/`、空路径 → 302 `DefaultRedirectPath`
6. query `r != "0"` 时：`web/dashboard.html` → 302 `index.html#!/dashboard.html`；`web/home.html` → 302 `index.html`
7. `GlobalResponse` 非空（启动加载中文案）→ 503 HTML，`system/ping` 豁免（保活探测）

### 1.4 路由匹配（GetServiceHandler）

- `PathInfo`（RawUrl 去 query、UrlDecode）→ `GetSanitizedPathInfo`：末尾 `.json`/`.xml` 扩展剥离并推导 `ResponseContentType`（`/Users.json` → JSON）
- `FindMatchingRestPath`：路径小写分段 → 生成候选 hash 键（每段 + 点分子段）→ `RestPathMap` 查找 → 候选集内 `MatchScore` 评分取最高：
  - 通配符越少分越高（`100-wildcardCount` × 1000）
  - 变量段越少分越高（`10-VariableArgsCount` × 100）
  - 精确动词匹配 > ANY（+10 vs +1）
- 未匹配 → `FileNotFoundException` → 404

### 1.5 DTO 绑定（ServiceHandler.CreateRequest）

- **IRequiresRequestStream 特例**：DTO 声明原始流（如上传），`RequestStream = httpReq.InputStream`，仍绑定 query/path 变量
- **body 反序列化**（`CreateContentTypeRequest`）：`ContentLength > 0` 且 Content-Type 为 json/xml → `DeserializeJson/DeserializeXml`；否则空实例
- **query/form 收集**（`GetFlattenedRequestParams`）：query 全量 + POST/PUT 的 form-data（`application/x-www-form-urlencoded`），重复键逗号连接
- **path 变量提取**（`RestPath.CreateRequest`）：按模板段位提取；通配符段拼接剩余路径（含 `/`）；变量名 → DTO 属性名（忽略大小写映射表；`{ignore}` 跳过）
- **属性填充**（`StringMapTypeDeserializer.PopulateFromMap`）：优先级 **path < query < form**（后写覆盖）；跳过空值、`Stream` 类型属性、`IgnoreDataMember`/`JsonIgnore` 标注属性；bool 取 `LeftPart(',')`（MVC checkbox 双值兼容）；解析委托来自 `GetParseFn`（旧为 ServiceStack JsvReader）
- 绑定结果挂到 `httpReq.Dto`；`req.Items["__route"] = restPath`

### 1.6 认证过滤器链（ApplyRequestFilters）

- 收集 DTO 类型 + 服务类型上的 `IHasRequestFilter` 特性（含继承），按 `Priority` 排序，负优先级先执行
- 核心过滤器：`[Authenticated]`（`AuthenticatedAttribute.RequestFilter` → `AuthService.Authenticate(request, this)`；Roles/EscapeParentalControl/AllowLocal 等策略参数）——**认证在服务执行前、DTO 绑定后**
- `ServiceExecGeneral.Execute` 内还有方法级过滤器（`actionCtx.RequestFilters`，同样按 Priority）

### 1.7 服务执行（ServiceController.Execute）

- `req.OperationName = requestType.Name`
- `appHost.CreateInstance(serviceType)` 每请求新建服务实例（MEF/DI）
- `IRequiresRequest.Request = req` 注入（BaseApiService 依赖此机制拿到 `Request`）
- `ServiceExecGeneral.Execute`：`execMap[serviceType+Verb+requestName]` → 表达式编译的强类型调用委托（`Get(GetUsers request)`）；返回 `Task` 则解包 Result（`Task<object>` 直取；泛型 Task 反射取 `Result`；void Task → null）
- 方法缺失 → `NotImplementedException`（提示期望 `Get/Any` 方法名）

### 1.8 响应写出（ResponseHelper.WriteToResponse，类型分派）

| 返回类型 | 行为 |
|---|---|
| `null` | 200→204，Content-Length 0 |
| `IHttpResult` | 取 Status/StatusCode/ContentType/Headers；`HttpResult.WriteToAsync` 递归写内含 Response |
| `IHasHeaders` | 头复制到响应（`Content-Length` 键走 `SetContentLength`） |
| `IAsyncStreamWriter` | `WriteToAsync(OutputStream, ct)`（StreamWriter/RangeRequestWriter 走此分支） |
| `IStreamWriter` | 同步 `WriteTo` |
| `FileWriter` | `WriteToAsync` → `response.TransmitFile(path, offset, count, fileShare, ct)` |
| `Stream` | `CopyToAsync` 并 dispose 源 |
| `byte[]` | `application/octet-stream` + 长度 |
| `string` | UTF-8 |
| 其他 DTO | 按 `ResponseContentType` JSON/XML 序列化到内存再写（`RequestHelper.GetResponseWriter` → `SerializeToJson/SerializeToXml`） |

- ContentType 规则：响应未显式设置或为 `text/html` 时用 `request.ResponseContentType`（默认 `application/json`，`format` query/路径前缀可改）；`application/json` 追加 `; charset=utf-8`

### 1.9 响应过滤器（ResponseFilter.FilterResponse）

- 追加 CORS 三头（Allow-Headers 含 X-MediaBrowser-Token/X-Emby-Authorization 等 27 个头）
- `Server` 头补默认值
- DTO 为 `Exception` 时：记日志 + `X-Application-Error-Code` 头（去控制字符）
- `IHasHeaders.Content-Length > 0` → `res.SetContentLength` + `SendChunked = false`

### 1.10 异常处理（ErrorHandler）

- `GetActualException`：递归解包 AggregateException
- 状态码映射：ResourceNotFound 404 / RemoteServiceUnavailable 502 / FileNotFound 404 / Security 401 / PaymentRequired 402 / Argument 400 / DirectoryNotFound 404 / 其他 500
- `NormalizeExceptionMessage`：剥离 ProgramSystemPath/ProgramDataPath（防信息泄露）
- 响应 `text/html`，写异常消息；响应已关闭则跳过
- 捕获策略：OperationCanceled/IO/Socket 静默；SecurityException 记消息；其他记堆栈（SocketException 类名特判）

### 1.11 WebSocket

- **入口**：任何携带 WebSocket upgrade 的请求（旧栈不检查路径；客户端连 `/embywebsocket`，query 带 api_key）
- **流程**：`WebSocketConnecting` 事件（`WebSocketConnectingEventArgs.AllowConnection`，默认 true）→ `AcceptWebSocketAsync` → `WebSocketConnected` 事件 → `WebSocketConnection` 包装入连接列表 → 接收循环
- **连接对象**（`WebSocketConnection : IWebSocketConnection`）：Id（Guid）、LastActivityDate、Url、QueryString、RemoteEndPoint、State、Closed 事件、`OnReceive(WebSocketMessageInfo)`、`SendAsync<T>(WebSocketMessage<T>)`/`SendAsync(byte[])`/`SendAsync(string)`
- **消息协议**：JSON 文本帧 `WebSocketMessage<T> { MessageType, MessageId, ServerId, Data }`；接收侧反序列化为 `WebSocketMessage<object>` 取 `Data.ToString()` → `WebSocketMessageInfo { MessageType, Data, Connection }`
- **编码检测**：`ITextEncoding.GetDetectedEncodingName`（UTF-8 / ASCII 回退）；非 `{` 开头消息丢弃
- **分发**：`ProcessWebSocketMessageReceived` → 并行 `Task.Run` 广播给全部 `IWebSocketListener.ProcessMessage`（Session/ActivityLog/LiveTv 等监听器，`BasePeriodicWebSocketListener<T,S>` 周期推送模式）
- **关闭**：连接 Closed → 从列表移除；`Stop()` 时全部 Dispose

### 1.12 静态资源（dashboard-ui）

- **路由**：`[Route("/web/{ResourceName*}", "GET")]`（DashboardService，通配符）+ `/favicon.ico` + `/robots.txt`
- **语义**（不能丢）：
  - 启动向导未完成 → 核心 HTML 重定向 `index.html?start=wizard#!/wizardstart.html`（bower_components 豁免）
  - HTML 经 `PackageCreator.GetResource` 动态改写（版本号、UICulture 本地化注入）
  - 图片/字体或带 `V` query → 缓存 365 天；`EnableDashboardResponseCaching=false` 时非图片不缓存
  - `ResourceFileManager.GetResourcePath`：`ContainsSubPath` 防路径穿越
- **文件响应**（`HttpResultFactory.GetStaticFileResult` → `FileWriter`）：
  - ETag = MD5(path+lastWriteTicks)；`If-None-Match` 命中 → 304；`If-Modified-Since` 秒级比较
  - `Accept-Ranges: bytes`；Range 头 → 206 + `Content-Range: bytes s-e/total`（多 range 只取第一个）；`0-` 全量走 TransmitFile 优化
  - HEAD 请求只回头不写体

### 1.13 压缩

- 条件：Accept-Encoding 含 deflate/gzip（deflate 优先）**且** 内容为 text/、json、javascript、xml、x-mpegURL **且** ≥1024 字节
- 实现：`GetCompressedResult` 预压缩为 byte[]，`Content-Encoding` + `Vary: Accept-Encoding`（br 预留未启用）

### 1.14 请求日志

- `EnableLogging`：跳过 .js/.css/.woff/.woff2/.ttf/.html 与 `system/ping`
- `GetUrlToLog`：URL 中 `api_key` 参数脱敏
- `/videos/` 路径额外记录请求/响应头（logHeaders）
- `LoggerUtils.LogRequest/LogResponse` 记录方法/UA/状态码/耗时

---

## 2. Kestrel 中间件管线设计

### 2.1 管线总览

```
WebApplication (Kestrel)
  http://+:8096  https://+:8920（UrlPrefixes 解析）
│
├─ ① WebSocketMiddleware            upgrade 检测（等价旧 IsWebSocketRequest 分支）
│     Accept → KestrelWebSocketConnection → WebSocketConnected 事件
│     → 接收循环 → ProcessWebSocketMessageReceived → IWebSocketListener 广播
│     （非 upgrade 请求直接 next()）
│
└─ ② ServiceRoutingMiddleware       全部分发核心（复刻 RequestHandler 顺序）
      1.  disposed → 503
      2.  ValidateHost → 400
      3.  ValidateRequest(IP 过滤) → 403
      4.  ValidateSsl → 302
      5.  OPTIONS → CORS 200
      6.  重定向规则（§1.3 全表）
      7.  GlobalResponse → 503
      8.  路由匹配（RouteTable.FindMatchingRestPath）
      9.  DTO 绑定（path/query/form/body → requestDto）
      10. ApplyRequestFilters（认证过滤器链）
      11. ServiceExecutor.Execute（Verb → Get/Post 方法）
      12. ResponseFilters（CORS/Server/Content-Length）
      13. ResponseWriter.WriteToResponse（§1.8 类型分派）
      14. 异常处理（§1.10 映射）+ 请求日志（§1.14）
      （未匹配路由 → 404，与旧 ErrorHandler(FileNotFoundException) 一致）
```

### 2.2 静态文件为何不走独立 StaticFiles 中间件（设计决策）

方案文档 §2 提到"dashboard-ui 用静态文件中间件"，实现时**有意收敛**为"静态资源仍由 DashboardService 路由承载"：

1. `/web/*` 不是纯静态：启动向导重定向、`PackageCreator` HTML 动态改写（版本/语言注入）、插件配置页（`GetDashboardConfigurationPage`）、`/web/Package` 打包接口都挂在同一通配符路由上——独立静态中间件会在这些逻辑之前短路，行为漂移；
2. 范围请求/缓存/ETag 语义由 `FileWriter`/`RangeRequestWriter`（本项目重写）承载，与 API 层共享同一套 `IHttpResultFactory`，客户端（视频流拖动、图片缓存）无感；
3. 61 服务类零改动目标下，`/web/{ResourceName*}` 本身就是一个服务路由。

结论：**静态资源 = 路由分发层的一个服务 + 范围请求写出的文件结果**，而非独立中间件。此决策记入风险表 R6（若后续要拆，可加 `UseStaticFiles` 前置并保留 DashboardService 兜底）。

### 2.3 各中间件职责边界

| 组件 | 职责 | 不负责 |
|---|---|---|
| `KestrelHost` | IHttpServer 实现；UrlPrefixes/GlobalResponse；Init 构建路由表；StartServer/Stop 生命周期；持有 WebApplication | 请求处理细节 |
| `WebSocketMiddleware` | upgrade 检测、Accept、连接生命周期、消息分发 | 消息业务（归 IWebSocketListener） |
| `ServiceRoutingMiddleware` | §1.2–§1.10 全部请求期职责 | WebSocket |
| `RouteTable`（=旧 ServiceController） | 路由注册/查找/评分 | 执行 |
| `ServiceExecutor`（=旧 ServiceExecGeneral） | Verb→方法调用、Task 解包 | 路由 |
| `DtoBinder`（=旧 CreateRequest+StringMapTypeDeserializer） | path/query/form/body → DTO | 序列化响应 |
| `ResponseWriter`（=旧 ResponseHelper） | 结果类型分派写出 | 压缩决策（归 HttpResultFactory） |
| `KestrelHttpResultFactory`（=旧 HttpResultFactory） | IHttpResultFactory 实现：GetResult/GetStaticResult/压缩/缓存头 | 写 socket |
| `KestrelRequest/KestrelResponse` | IRequest/IHttpRequest/IResponse/IHttpResponse 适配 HttpContext | 业务 |

### 2.4 Kestrel 适配层关键映射

| 旧（SocketHttpListener/HttpListenerContext） | 新（ASP.NET Core HttpContext） |
|---|---|
| `request.IsWebSocketRequest` | `context.WebSockets.IsWebSocketRequest` |
| `ctx.AcceptWebSocketAsync(null)` | `context.WebSockets.AcceptWebSocketAsync()` |
| `request.InputStream` | `context.Request.Body`（enable buffering 后可重复读） |
| `request.Headers`（QueryParamCollection） | `context.Request.Headers` → 适配为 QueryParamCollection |
| `request.QueryString` | `context.Request.QueryString` → `MyHttpUtility.ParseQueryString` |
| `request.Url`（Uri） | `context.Request.GetUri()`（scheme/host/port 拼装，X-Forwarded-* 优先） |
| `request.RemoteEndPoint` | `context.Connection.RemoteIpAddress:Port`（::ffff: 前缀归一化） |
| `request.IsLocal` | `context.Connection.RemoteIpAddress` 为 loopback |
| `response.OutputStream` | `context.Response.Body` |
| `response.StatusCode/ContentLength64` | `context.Response.StatusCode/ContentLength` |
| `response.SendChunked` | Kestrel 自动（显式 ContentLength 时关闭分块） |
| `response.TransmitFile` | 自实现：`FileStream` + `Seek` + `CopyToAsync`（带 offset/count） |
| `response.AddHeader` | `context.Response.Headers.Append`（Content-Type 特判） |
| `Prefixes.Add("http://+:8096/")` | `ListenAnyIP(8096)` / `ListenLocalhost` / `Listen(IPAddress, port)` 解析 |
| 证书 `LoadCert(X509Certificate)` | `UseHttps(certificate)` |

### 2.5 序列化策略（System.Text.Json）

- **响应 DTO 序列化**：经注入的 `IJsonSerializer`（C09 完成后即为 System.Text.Json 实现，字段命名/行为全库一致）；本项目不私建第二套 STJ 配置，避免与 C09 漂移
- **DTO 绑定的字符串解析**（query/path/form → 属性）：本项目内建 `TypeParser`（string/bool/各整型/浮点/Guid/DateTime/TimeSpan/enum/Nullable<T>/IEnumerable 逗号分隔），替代旧 ServiceStack `JsvReader.GetParseFn`——这是纯基元解析，无 JSON 语义
- **XML**：`IXmlSerializer` 注入（DataContractSerializer 语义保持）

---

## 3. IHttpServer 接口逐方法映射表

接口定义（`MediaBrowser.Controller.Net.IHttpServer`，**不改**）：

```csharp
public interface IHttpServer : IDisposable
{
    string[] UrlPrefixes { get; }
    string GlobalResponse { get; set; }
    event EventHandler<GenericEventArgs<IWebSocketConnection>> WebSocketConnected;
    void Stop();
    void Init(IEnumerable<IService> services, IEnumerable<IWebSocketListener> listeners);
}
```

| 接口成员 | 旧实现（HttpListenerHost） | 新实现（KestrelHost） |
|---|---|---|
| `UrlPrefixes` | `StartServer` 时由 ApplicationHost 传入并保存 | 同：`StartServer(string[] urlPrefixes)` 保存并解析为 Kestrel Listen 端点 |
| `GlobalResponse` | 公共字段，RequestHandler 读 | 同：属性存取，ServiceRoutingMiddleware 每请求读（volatile 语义） |
| `WebSocketConnected` | `OnWebSocketConnected`（IHttpListener 回调触发） | `WebSocketMiddleware` 接受连接后触发（`KestrelWebSocketConnection` 包装） |
| `Stop()` | Dispose 全部 WS 连接 + `_listener.Stop()` 同步等待 | Dispose 全部 WS 连接 + `_webApplication.StopAsync()` + CTS 取消接收循环 |
| `Init(services, listeners)` | 构建 ServiceController、注册路由、存 listeners | 构建 `RouteTable`（扫描 [Route] + 3 前缀克隆）、存 listeners、初始化 ResponseFilters |
| `Dispose()`（IDisposable） | Dispose(true) → Stop | 同 |

**非接口公共面**（旧分发栈内部依赖，新项目内等价物）：

| 旧 HttpListenerHost 成员 | 新位置 |
|---|---|
| `Instance`（static） | `KestrelHost.Instance` |
| `CreateInstance(Type)` | `KestrelHost.CreateInstance`（委托 `_appHost.CreateInstance`） |
| `ApplyRequestFilters` | `ServiceRoutingMiddleware.ApplyRequestFilters`（逻辑等价） |
| `GetServiceTypeByRequest` / `AddServiceInfo` | `RouteTable` 内部字典 |
| `GetRouteAttributes`（3 前缀克隆） | `RouteTable.RegisterService`（逻辑等价移植） |
| `GetParseFn` | `TypeParser.GetParseFn`（内建基元解析） |
| `SerializeToJson/SerializeToXml/DeserializeJson/DeserializeXml` | `KestrelHost` 同名方法（委托注入的 IJsonSerializer/IXmlSerializer） |
| `ResponseFilters` | `KestrelHost.ResponseFilters`（含新 `ResponseFilter`） |
| `ServiceController` | `RouteTable`（+ `ServiceExecutor`） |
| `StartServer(urlPrefixes, IHttpListener)` | `StartServer(string[] urlPrefixes)`——**IHttpListener 参数随 SocketHttpListener 退役删除**（ApplicationHost 的 `CreateHttpListener()` 抽象方法同步退役，属 Lead 集成提交范围） |

**构造函数**：保持与旧 HttpListenerHost 完全同参（`IServerApplicationHost, ILogger, IServerConfigurationManager, string defaultRedirectPath, INetworkManager, ITextEncoding, IJsonSerializer, IXmlSerializer, Func<Type, Func<string, object>> funcParseFn`），ApplicationHost 仅改类名即可切换；`funcParseFn` 传 null 时回退内建 `TypeParser`。

---

## 4. 与 MediaBrowser.Api 61 个服务类的兼容策略

**总原则：服务类零改动，全部兼容性由分发层吸收。**

1. **路由发现**：只依赖 DTO 上的 `[Route]` 特性（`MediaBrowser.Model.Services.RouteAttribute`，Model 层已有）与服务方法签名约定（public、1 参数、方法名=动词）。扫描 `GetExports<IService>` 的运行时类型，无需编译期引用 MediaBrowser.Api。
2. **方法调用**：`ServiceExecutor` 用表达式编译强类型委托（等价旧 `ServiceExecGeneral.CreateExecFn`），支持 `object Get(Xxx)`、`Task<object> Post(Xxx)`、`Task<T> …`、void 返回；`Any` 动词兜底。
3. **Request 注入**：`IRequiresRequest.Request`（BaseApiService 的 `Request` 属性、`GetHeader`、`ToOptimizedResult` 全依赖）在执行前注入。
4. **ResultFactory**：`IHttpResultFactory`（Controller 接口）由本项目 `KestrelHttpResultFactory` 实现，DI 注册替换旧 `Emby.Server.Implementations.HttpServer.HttpResultFactory`——服务代码引用接口，无感切换。
5. **认证**：`[Authenticated]` 等 `IHasRequestFilter` 特性在 DTO 绑定后、执行前统一执行（顺序/Priority 语义等价）；`AuthenticatedAttribute.AuthService` 静态注入不变。
6. **IRequiresRequestStream**（上传类 DTO）：绑定层特判，`RequestStream` 指向 `HttpContext.Request.Body`。
7. **路径前缀**：`/emby`、`/mediabrowser`、`/emby/emby` 三前缀克隆注册，客户端 URL 不变。
8. **格式后缀**：`/Users.json`、`?format=xml` 响应类型推导保持。
9. **返回类型全覆盖**：`ResponseWriter` 分派表与旧 `ResponseHelper` 逐行对齐（IHttpResult/IAsyncStreamWriter/IStreamWriter/Stream/byte[]/string/DTO）。
10. **异常契约**：服务抛 `ResourceNotFoundException`/`SecurityException`/`ArgumentException` 等的 HTTP 状态码映射保持（§1.10）。

**验证口径**（P5 冒烟）：`/emby/system/info`、`/Users`、`/Users/{Id}`、`/web/index.html`、`/embywebsocket`、`/Videos/{Id}/stream` Range 请求。

---

## 5. 新项目结构（src/Emby.Server.HttpServer/）

```
src/Emby.Server.HttpServer/
├── Emby.Server.HttpServer.csproj        FrameworkReference(Microsoft.AspNetCore.App)
│                                        + ProjectReference: Model / Common / Controller
├── KestrelHost.cs                       IHttpServer 实现 + Kestrel 生命周期 + 序列化委托
├── KestrelHostOptions.cs?               （并入 KestrelHost，不单列）
├── Routing/
│   ├── RouteTable.cs                    服务注册、[Route] 扫描、3 前缀克隆、hash 索引、MatchScore 查找
│   ├── RestPath.cs                      路径模板（literal/变量/通配符/点分子段）+ CreateRequest 变量提取
│   ├── ServiceMethod.cs                 执行键（serviceType+verb+dtoName）与编译委托
│   ├── ServiceExecutor.cs               Verb→方法调用、Task 解包、方法级过滤器
│   └── TypeParser.cs                    字符串→基元/枚举/Guid/集合 解析（替代 JsvReader）
├── DtoBinding/
│   └── DtoBinder.cs                     path/query/form/body 绑定 + StringMapTypeDeserializer 等价
├── Middleware/
│   ├── WebSocketMiddleware.cs           upgrade 检测 + Accept + 连接分发
│   └── ServiceRoutingMiddleware.cs      §2.1-② 全流程
├── RequestResponse/
│   ├── KestrelRequest.cs                IHttpRequest 适配（RemoteIp/X-Forwarded/PathInfo/ContentType 推导）
│   ├── KestrelResponse.cs               IHttpResponse 适配（TransmitFile/SetCookie/AddHeader）
│   ├── ResponseWriter.cs                结果类型分派写出（=旧 ResponseHelper）
│   └── ResponseFilter.cs                CORS/Server/Content-Length 响应过滤器
├── Results/
│   ├── HttpResult.cs                    IHttpResult+IAsyncStreamWriter 通用结果
│   ├── StreamWriter.cs                  byte[]/Stream 写出（含 OnComplete/OnError）
│   ├── FileWriter.cs                    文件结果（Range 解析、206、TransmitFile）
│   ├── RangeRequestWriter.cs            流范围结果（视频拖动）
│   └── KestrelHttpResultFactory.cs      IHttpResultFactory（压缩/缓存/ETag/304/静态文件）
├── WebSockets/
│   ├── KestrelWebSocket.cs              IWebSocket 适配 System.Net.WebSockets.WebSocket
│   └── WebSocketConnection.cs           IWebSocketConnection（消息协议/编码检测/分发）
└── Logging/
    └── LoggerUtils.cs                   请求/响应日志 + api_key 脱敏
```

---

## 6. 风险清单与缓解

| # | 风险 | 等级 | 缓解 |
|---|---|---|---|
| R1 | **路由匹配语义漂移**（RestPath 评分/通配符/点分子段规则复杂，移植走样导致 404 或错路由） | 高 | RestPath/RouteTable 逐行移植旧算法（含 FirstMatchHashKey、MatchScore 权重、LiteralsEqual 土耳其 i 处理）；P5 用 `/emby/system/info`、`/Users/{Id}`、`/web/{ResourceName*}` 通配符路由冒烟 |
| R2 | **DTO 绑定差异**（旧 JsvReader 解析 `List<Guid>`、`Nullable<DateTime>` 等的行为） | 中 | TypeParser 覆盖全部基元+枚举+Guid+可空+IEnumerable（逗号分隔）；绑定优先级 path<query<form 保持；空值跳过、bool LeftPart(',') 兼容保持 |
| R3 | **流式响应/范围请求**（视频拖动依赖 Range/206/TransmitFile 语义） | 中 | FileWriter/RangeRequestWriter 语义等价重写；TransmitFile 用 FileStream+Seek+CopyToAsync；Kestrel 显式 ContentLength 避免分块；P5 冒烟 `/Videos/{Id}/stream` + `Range: bytes=100-200` |
| R4 | **WebSocket 消息协议兼容**（客户端/监听器两侧都不能改） | 中 | WebSocketMessage<T> JSON 协议不变；编码检测（ITextEncoding）保持；非 `{` 开头丢弃；广播并行度保持 |
| R5 | **JSON 字段命名漂移**（STJ 默认 PascalCase vs ServiceStack camelCase 期望） | 中 | 序列化统一走 IJsonSerializer（C09 负责命名策略对齐）；本项目不私建 STJ 选项；冒烟对比 `/Users` 响应体字段名 |
| R6 | **静态资源语义**（向导重定向/PackageCreator HTML 改写依赖服务路由） | 低 | 设计决策 §2.2：静态资源保留在 DashboardService 路由内；若未来拆 StaticFiles 中间件需先迁移向导/改写逻辑 |
| R7 | **Kestrel 头部语义差异**（旧 HttpListener 允许的重复头/Content-Length 手工设置） | 低 | AddHeader→Headers.Append；SetContentLength→ContentLength；Content-Type 特判；响应过滤器统一收口 |
| R8 | **双栈并存期冲突**（旧 Emby.Server.Implementations/HttpServer 与新项目同名类型） | 低 | 新项目命名空间 `Emby.Server.HttpServer.*` 与旧 `Emby.Server.Implementations.HttpServer` 隔离；集成提交（Lead）删除旧目录 + 切换 ApplicationHost 注册；本任务不碰旧代码 |
| R9 | **IJsonSerializer 依赖时序**（Init 前序列化调用） | 低 | KestrelHost 构造期只存引用；序列化仅发生在请求期 |
| R10 | **端口绑定失败重试**（旧 StartServer 有默认端口回退逻辑，在 ApplicationHost） | 低 | StartServer 异常上抛给 ApplicationHost（其重试逻辑不变）；Kestrel Listen 失败抛 IOException 语义对齐 |

---

## 7. 集成切换点（Lead 的 C13 提交范围，本任务只提供新件）

1. `ApplicationHost`：`new HttpListenerHost(...)` → `new KestrelHost(...)`（同参构造）
2. `ApplicationHost.StartServer()`：`((HttpListenerHost)HttpServer).StartServer(prefixes, CreateHttpListener())` → `((KestrelHost)HttpServer).StartServer(prefixes)`；删除 `CreateHttpListener()` 抽象方法及 WindowsAppHost/MonoAppHost 实现
3. DI：`HttpResultFactory` 注册替换为 `KestrelHttpResultFactory`
4. 删除：`Emby.Server.Implementations/HttpServer/`、`Emby.Server.Implementations/Services/`（ServiceStack 分发栈）、`MediaBrowser.ServerApplication/SocketSharp/`、`SocketHttpListener/` 项目
5. `Emby.Server.Implementations.csproj` 增加 `ProjectReference` → `src/Emby.Server.HttpServer`
