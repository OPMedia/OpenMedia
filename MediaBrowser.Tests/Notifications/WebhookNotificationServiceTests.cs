using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emby.Notifications;
using Emby.Server.Implementations.Cryptography;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Notifications;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleInjector;

namespace MediaBrowser.Tests.Notifications
{
    [TestClass]
    public class WebhookNotificationServiceTests
    {
        private static readonly DateTime NotificationDate =
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        [TestMethod]
        public async Task SendNotification_PostsExpectedJsonBodyAndHeaders()
        {
            var handler = new RecordingHandler(HttpStatusCode.NoContent, 1);
            var config = CreateConfig(new WebhookOptions
            {
                Webhooks = new[]
                {
                    new WebhookEndpoint
                    {
                        Name = "test",
                        Url = "https://example.invalid/hook",
                        Template = "json",
                        Headers = new Dictionary<string, string> { ["X-Test-Token"] = "secret-value" }
                    }
                }
            });
            var service = new WebhookNotificationService(config, new RecordingLogManager(), handler);

            await service.SendNotification(CreateNotification("NewLibraryContent"), CancellationToken.None)
                .ConfigureAwait(false);
            await handler.WaitForRequestsAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            var request = handler.Requests.Single();
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://example.invalid/hook", request.Uri.ToString());
            Assert.AreEqual("application/json", request.ContentType);
            Assert.AreEqual("secret-value", request.Headers["X-Test-Token"]);

            using var json = JsonDocument.Parse(request.Body);
            var root = json.RootElement;
            Assert.AreEqual("NewLibraryContent", root.GetProperty("notificationType").GetString());
            Assert.AreEqual("入库完成", root.GetProperty("title").GetString());
            Assert.AreEqual("电影已加入媒体库", root.GetProperty("message").GetString());
            Assert.AreEqual("Warning", root.GetProperty("level").GetString());
            Assert.AreEqual("alice", root.GetProperty("user").GetString());
            Assert.AreEqual("https://example.invalid/item", root.GetProperty("url").GetString());
            Assert.AreEqual(NotificationDate.ToString("O"), root.GetProperty("date").GetString());
        }

        [TestMethod]
        public async Task SendNotification_EventTypeFilterSkipsUnmatchedEvents()
        {
            var handler = new RecordingHandler(HttpStatusCode.OK, 1);
            var config = CreateConfig(new WebhookOptions
            {
                Webhooks = new[]
                {
                    new WebhookEndpoint
                    {
                        Url = "https://example.invalid/hook",
                        EventTypes = new[] { "newlibrarycontent" }
                    }
                }
            });
            var service = new WebhookNotificationService(config, new RecordingLogManager(), handler);

            await service.SendNotification(CreateNotification("TaskFailed"), CancellationToken.None)
                .ConfigureAwait(false);
            await Task.Delay(150).ConfigureAwait(false);
            Assert.AreEqual(0, handler.Requests.Count, "未匹配事件类型的通知不应发出 HTTP 请求");

            await service.SendNotification(CreateNotification("NewLibraryContent"), CancellationToken.None)
                .ConfigureAwait(false);
            await handler.WaitForRequestsAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task SendNotification_RetriesToLimitLogsAndNeverThrows()
        {
            var handler = new RecordingHandler(HttpStatusCode.InternalServerError, 3);
            var logs = new RecordingLogManager();
            var config = CreateConfig(new WebhookOptions
            {
                Webhooks = new[]
                {
                    new WebhookEndpoint
                    {
                        Name = "flaky",
                        Url = "https://example.invalid/hook",
                        MaxRetries = 2,
                        RetryDelayMs = 1
                    }
                }
            });
            var service = new WebhookNotificationService(config, logs, handler);

            // The public call returns immediately and does not surface delivery failures.
            await service.SendNotification(CreateNotification("NewLibraryContent"), CancellationToken.None)
                .ConfigureAwait(false);
            await handler.WaitForRequestsAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            for (var i = 0; i < 50 && logs.Logger.ErrorExceptionCount == 0; i++)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }

            Assert.AreEqual(3, handler.Requests.Count, "首次投递 + 2 次重试");
            Assert.AreEqual(3, logs.Logger.WarningCount);
            Assert.AreEqual(1, logs.Logger.ErrorExceptionCount);
            StringAssert.Contains(logs.Logger.ErrorMessage, "flaky");
        }

        [TestMethod]
        public async Task SendNotification_DiscordTemplateUsesBuiltInContentShape()
        {
            var handler = new RecordingHandler(HttpStatusCode.OK, 1);
            var config = CreateConfig(new WebhookOptions
            {
                Webhooks = new[]
                {
                    new WebhookEndpoint
                    {
                        Url = "https://example.invalid/discord",
                        Template = "discord"
                    }
                }
            });
            var service = new WebhookNotificationService(config, new RecordingLogManager(), handler);

            await service.SendNotification(CreateNotification("NewLibraryContent"), CancellationToken.None)
                .ConfigureAwait(false);
            await handler.WaitForRequestsAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            using var json = JsonDocument.Parse(handler.Requests.Single().Body);
            var content = json.RootElement.GetProperty("content").GetString();
            StringAssert.Contains(content, "入库完成");
            StringAssert.Contains(content, "电影已加入媒体库");
            Assert.IsFalse(json.RootElement.TryGetProperty("title", out _));
        }

        [TestMethod]
        public async Task FrameworkDiscoversServiceAndNotificationManagerExposesIt()
        {
            var config = CreateConfig(new WebhookOptions());
            var logs = new RecordingLogManager();

            // Same concrete-type filter ApplicationHost.GetExportTypes<T>() uses over
            // the Emby.Notifications assembly, which GetComposablePartAssemblies includes.
            var exportTypes = typeof(NotificationManager).Assembly.GetTypes()
                .Where(i => i.IsClass && !i.IsAbstract && !i.IsInterface && !i.IsGenericType
                    && typeof(INotificationService).IsAssignableFrom(i))
                .ToArray();
            Assert.AreEqual(1, exportTypes.Length);
            Assert.AreEqual(nameof(WebhookNotificationService), exportTypes[0].Name);

            // Same DI container options as ApplicationHost.CreateContainer(): the export
            // must be constructible with only registered dependencies.
            using var container = new Container();
            container.Options.ResolveUnregisteredConcreteTypes = true;
            container.RegisterInstance<IServerConfigurationManager>(config);
            container.RegisterInstance<ILogManager>(logs);
            var service = (INotificationService)container.GetInstance(exportTypes[0]);

            // ApplicationHost does this during startup; GetNotificationServices hashes
            // the service name to build the same Id the API returns.
            BaseExtensions.CryptographyProvider = new CryptographyProvider();
            var manager = new NotificationManager(logs, null, config);
            manager.AddParts(new[] { service }, Enumerable.Empty<INotificationTypeFactory>());

            var registered = manager.GetNotificationServices().ToList();
            Assert.AreEqual(1, registered.Count);
            Assert.AreEqual("Webhook", registered[0].Name);

            var pageContract = new WebhookNotificationConfigurationFactory().GetConfigurations().Single();
            Assert.AreEqual("notifications.webhook", pageContract.Key);
            Assert.AreEqual(typeof(WebhookOptions), pageContract.ConfigurationType);
        }

        [TestMethod]
        public async Task NotificationManagerPropagatesEventTypeToWebhookFilter()
        {
            var handler = new RecordingHandler(HttpStatusCode.OK, 1);
            var config = CreateConfig(new WebhookOptions
            {
                Webhooks = new[]
                {
                    new WebhookEndpoint
                    {
                        Url = "https://example.invalid/hook",
                        EventTypes = new[] { "NewLibraryContent" }
                    }
                }
            });
            config.SaveConfiguration("notifications", new NotificationOptions());
            var logs = new RecordingLogManager();
            var userManager = DispatchProxy.Create<IUserManager, EmptyUserManagerProxy>();
            ((EmptyUserManagerProxy)userManager).Users = new[] { new MediaBrowser.Controller.Entities.User { Name = "alice" } };
            var manager = new NotificationManager(logs, userManager, config);
            manager.AddParts(
                new INotificationService[] { new WebhookNotificationService(config, logs, handler) },
                Enumerable.Empty<INotificationTypeFactory>());

            await manager.SendNotification(new NotificationRequest
            {
                NotificationType = "NewLibraryContent",
                Name = "入库完成",
                Description = "电影已加入媒体库",
                SendToUserMode = SendToUserType.Custom,
                UserIds = new[] { Guid.Empty }
            }, CancellationToken.None).ConfigureAwait(false);
            await handler.WaitForRequestsAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            using var json = JsonDocument.Parse(handler.Requests.Single().Body);
            Assert.AreEqual("NewLibraryContent", json.RootElement.GetProperty("notificationType").GetString());
        }

        private static UserNotification CreateNotification(string notificationType)
        {
            return new UserNotification
            {
                NotificationType = notificationType,
                Name = "入库完成",
                Description = "电影已加入媒体库",
                Level = NotificationLevel.Warning,
                Date = NotificationDate,
                Url = "https://example.invalid/item",
                User = new MediaBrowser.Controller.Entities.User { Name = "alice" }
            };
        }

        private static WebhookConfigManager CreateConfig(WebhookOptions webhooks)
        {
            return new WebhookConfigManager(webhooks);
        }

        private sealed class CapturedRequest
        {
            public HttpMethod Method { get; set; }
            public Uri Uri { get; set; }
            public string Body { get; set; }
            public string ContentType { get; set; }
            public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly int _expectedRequests;
            private readonly object _sync = new object();
            private readonly TaskCompletionSource<bool> _requestsReached =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly List<CapturedRequest> _requests = new List<CapturedRequest>();

            public IReadOnlyList<CapturedRequest> Requests
            {
                get
                {
                    lock (_sync)
                    {
                        return _requests.ToArray();
                    }
                }
            }

            public RecordingHandler(HttpStatusCode statusCode, int expectedRequests)
            {
                _statusCode = statusCode;
                _expectedRequests = expectedRequests;
            }

            public Task WaitForRequestsAsync(TimeSpan timeout)
            {
                return _requestsReached.Task.WaitAsync(timeout);
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var captured = new CapturedRequest
                {
                    Method = request.Method,
                    Uri = request.RequestUri,
                    Body = request.Content == null
                        ? string.Empty
                        : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
                    ContentType = request.Content?.Headers.ContentType?.MediaType
                };

                foreach (var header in request.Headers)
                {
                    captured.Headers[header.Key] = string.Join(",", header.Value);
                }

                lock (_sync)
                {
                    _requests.Add(captured);
                    if (_requests.Count >= _expectedRequests)
                    {
                        _requestsReached.TrySetResult(true);
                    }
                }

                return new HttpResponseMessage(_statusCode);
            }
        }

        private sealed class WebhookConfigManager : IServerConfigurationManager
        {
            private readonly Dictionary<string, object> _configurations = new Dictionary<string, object>();

            public WebhookConfigManager(WebhookOptions webhooks)
            {
                SaveConfiguration(WebhookNotificationConfigurationFactory.ConfigurationKey, webhooks);
            }

            public IApplicationPaths CommonApplicationPaths => null;
            public BaseApplicationConfiguration CommonConfiguration => null;
            public IServerApplicationPaths ApplicationPaths => null;
            public ServerConfiguration Configuration => null;

            public event EventHandler<ConfigurationUpdateEventArgs> NamedConfigurationUpdating
            {
                add { }
                remove { }
            }

            public event EventHandler<EventArgs> ConfigurationUpdated
            {
                add { }
                remove { }
            }

            public event EventHandler<ConfigurationUpdateEventArgs> NamedConfigurationUpdated
            {
                add { }
                remove { }
            }

            public bool SetOptimalValues() => false;

            public void SaveConfiguration()
            {
            }

            public void ReplaceConfiguration(BaseApplicationConfiguration newConfiguration)
            {
            }

            public object GetConfiguration(string key)
            {
                return _configurations.TryGetValue(key, out var value) ? value : null;
            }

            public Type GetConfigurationType(string key) => typeof(WebhookOptions);

            public void SaveConfiguration(string key, object configuration)
            {
                _configurations[key] = configuration;
            }

            public void AddParts(IEnumerable<IConfigurationFactory> factories)
            {
            }
        }

        private sealed class RecordingLogManager : ILogManager
        {
            public RecordingLogger Logger { get; } = new RecordingLogger();

            public LogSeverity LogSeverity { get; set; }
            public string ExceptionMessagePrefix { get; set; }

            public event EventHandler LoggerLoaded
            {
                add { }
                remove { }
            }

            public ILogger GetLogger(string name) => Logger;

            public Task ReloadLogger(LogSeverity severity, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public void Flush()
            {
            }

            public void AddConsoleOutput()
            {
            }

            public void RemoveConsoleOutput()
            {
            }
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly object _sync = new object();

            public int WarningCount { get; private set; }
            public int ErrorExceptionCount { get; private set; }
            public string ErrorMessage { get; private set; }

            public void Info(string message, params object[] paramList) { }
            public void Debug(string message, params object[] paramList) { }
            public void Fatal(string message, params object[] paramList) { }
            public void LogMultiline(string message, LogSeverity severity, System.Text.StringBuilder additionalContent) { }
            public void Log(LogSeverity severity, string message, params object[] paramList) { }

            public void Warn(string message, params object[] paramList)
            {
                lock (_sync)
                {
                    WarningCount++;
                }
            }

            public void Error(string message, params object[] paramList)
            {
                lock (_sync)
                {
                    ErrorMessage = string.Format(message, paramList);
                }
            }

            public void FatalException(string message, Exception exception, params object[] paramList) { }

            public void ErrorException(string message, Exception exception, params object[] paramList)
            {
                lock (_sync)
                {
                    ErrorExceptionCount++;
                    ErrorMessage = string.Format(message, paramList);
                }
            }
        }

        /// <summary>
        /// DispatchProxy avoids hand-writing the whole IUserManager interface; only the
        /// two members NotificationManager uses need real behavior.
        /// </summary>
        public class EmptyUserManagerProxy : DispatchProxy
        {
            public MediaBrowser.Controller.Entities.User[] Users = Array.Empty<MediaBrowser.Controller.Entities.User>();

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod.Name == "get_Users")
                {
                    return Users;
                }

                if (targetMethod.Name == "GetUserById" && args.Length > 0 && args[0] is Guid id)
                {
                    return Users.FirstOrDefault(i => i.Id == id);
                }

                return null;
            }
        }
    }
}
