using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Notifications;

namespace Emby.Notifications
{
    /// <summary>
    /// Delivers notifications to arbitrary HTTP webhook endpoints (Discord,
    /// Telegram, ntfy, Home Assistant, ...). Delivery is fire-and-forget: the
    /// notification pipeline never waits for a remote endpoint or its retries.
    /// </summary>
    public class WebhookNotificationService : INotificationService
    {
        private const int MaxRetryLimit = 5;
        private const int MaxRetryDelayMs = 30000;
        private const int MaxTimeoutMs = 120000;

        private readonly IServerConfigurationManager _configuration;
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;

        public WebhookNotificationService(IServerConfigurationManager configuration, ILogManager logManager)
            : this(configuration, logManager, new HttpClientHandler())
        {
        }

        internal WebhookNotificationService(
            IServerConfigurationManager configuration,
            ILogManager logManager,
            HttpMessageHandler httpHandler)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            if (logManager == null)
            {
                throw new ArgumentNullException(nameof(logManager));
            }

            _logger = logManager.GetLogger(nameof(WebhookNotificationService));
            _httpClient = new HttpClient(httpHandler ?? throw new ArgumentNullException(nameof(httpHandler)));
        }

        public string Name => "Webhook";

        // Per-user routing is handled by NotificationOptions in the core notification
        // manager; this transport is a server-wide delivery channel.
        public bool IsEnabledForUser(User user)
        {
            return true;
        }

        public Task SendNotification(UserNotification notification, CancellationToken cancellationToken)
        {
            if (notification == null)
            {
                return Task.CompletedTask;
            }

            try
            {
                var options = _configuration.GetConfiguration<WebhookOptions>(
                    WebhookNotificationConfigurationFactory.ConfigurationKey);
                var webhooks = options?.Webhooks ?? Array.Empty<WebhookEndpoint>();

                foreach (var webhook in webhooks)
                {
                    if (IsEnabled(webhook, notification))
                    {
                        // Explicit discard: every exception is handled inside DeliverAsync,
                        // and no remote call is awaited by the notification pipeline.
                        _ = DeliverAsync(webhook, notification, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error scheduling webhook notification", ex);
            }

            return Task.CompletedTask;
        }

        private bool IsEnabled(WebhookEndpoint webhook, UserNotification notification)
        {
            if (webhook == null || !webhook.Enabled || string.IsNullOrWhiteSpace(webhook.Url))
            {
                return false;
            }

            if (!Uri.TryCreate(webhook.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                _logger.Warn("Webhook {0} has an invalid URL and was skipped", webhook.Name);
                return false;
            }

            var eventTypes = webhook.EventTypes;
            if (eventTypes == null || eventTypes.Length == 0)
            {
                return true;
            }

            if (string.IsNullOrEmpty(notification.NotificationType))
            {
                return false;
            }

            return eventTypes.Any(i => !string.IsNullOrWhiteSpace(i)
                && string.Equals(i.Trim(), notification.NotificationType.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private async Task DeliverAsync(
            WebhookEndpoint webhook,
            UserNotification notification,
            CancellationToken cancellationToken)
        {
            try
            {
                var maxRetries = Math.Clamp(webhook.MaxRetries, 0, MaxRetryLimit);
                var retryDelayMs = Math.Clamp(webhook.RetryDelayMs, 0, MaxRetryDelayMs);
                var timeoutMs = Math.Clamp(webhook.TimeoutMs, 100, MaxTimeoutMs);
                var body = BuildBody(webhook, notification);
                Exception lastError = null;

                for (var attempt = 0; attempt <= maxRetries; attempt++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (attempt > 0)
                    {
                        var delayMs = (int)Math.Min(retryDelayMs * Math.Pow(2, attempt - 1), MaxRetryDelayMs);
                        try
                        {
                            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }
                    }

                    using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        timeoutSource.CancelAfter(timeoutMs);

                        using (var request = BuildRequest(webhook, body))
                        {
                            try
                            {
                                using (var response = await _httpClient.SendAsync(request, timeoutSource.Token).ConfigureAwait(false))
                                {
                                    if (response.IsSuccessStatusCode)
                                    {
                                        return;
                                    }

                                    lastError = new HttpRequestException("Webhook responded with HTTP " + (int)response.StatusCode);
                                    _logger.Warn(
                                        "Webhook {0} attempt {1}/{2} failed with HTTP {3}",
                                        webhook.Name,
                                        attempt + 1,
                                        maxRetries + 1,
                                        (int)response.StatusCode);
                                }
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                return;
                            }
                            catch (Exception ex)
                            {
                                lastError = ex;
                                _logger.Warn(
                                    "Webhook {0} attempt {1}/{2} failed: {3}",
                                    webhook.Name,
                                    attempt + 1,
                                    maxRetries + 1,
                                    ex.Message);
                            }
                        }
                    }
                }

                _logger.ErrorException(
                    "Webhook {0} delivery failed after {1} attempts",
                    lastError ?? new InvalidOperationException("Webhook delivery failed"),
                    webhook.Name,
                    maxRetries + 1);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Unexpected error delivering webhook {0}", ex, webhook.Name);
            }
        }

        private static HttpRequestMessage BuildRequest(WebhookEndpoint webhook, string body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            if (webhook.Headers != null)
            {
                foreach (var header in webhook.Headers)
                {
                    if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrEmpty(header.Value))
                    {
                        continue;
                    }

                    // Never let configuration inject another header or break the request line.
                    if (header.Key.IndexOfAny(new[] { '\r', '\n' }) >= 0
                        || header.Value.IndexOfAny(new[] { '\r', '\n' }) >= 0
                        || header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
                        || header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                        || header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return request;
        }

        private string BuildBody(WebhookEndpoint webhook, UserNotification notification)
        {
            var title = notification.Name ?? string.Empty;
            var message = notification.Description ?? string.Empty;
            var type = notification.NotificationType ?? string.Empty;
            var level = notification.Level.ToString();
            var date = notification.Date.ToUniversalTime().ToString("O");
            var user = notification.User?.Name ?? string.Empty;
            var url = notification.Url ?? string.Empty;
            var template = (webhook.Template ?? "json").Trim().ToLowerInvariant();

            switch (template)
            {
                case "discord":
                    return JsonSerializer.Serialize(new { content = FormatText(title, message, url) });
                case "telegram":
                    return JsonSerializer.Serialize(new
                    {
                        chat_id = webhook.ChatId ?? string.Empty,
                        text = FormatText(title, message, url)
                    });
                case "ntfy":
                    return JsonSerializer.Serialize(new
                    {
                        title,
                        message,
                        priority = MapPriority(notification.Level)
                    });
                case "homeassistant":
                    return JsonSerializer.Serialize(new
                    {
                        title,
                        message,
                        notification_type = type,
                        level
                    });
                case "json":
                    return JsonSerializer.Serialize(new
                    {
                        notificationType = type,
                        title,
                        message,
                        level,
                        date,
                        user,
                        url
                    });
                default:
                    _logger.Warn("Webhook {0} uses unknown template {1}; using json", webhook.Name, webhook.Template);
                    return JsonSerializer.Serialize(new
                    {
                        notificationType = type,
                        title,
                        message,
                        level,
                        date,
                        user,
                        url
                    });
            }
        }

        private static string MapPriority(NotificationLevel level)
        {
            switch (level)
            {
                case NotificationLevel.Warning:
                    return "high";
                case NotificationLevel.Error:
                    return "urgent";
                default:
                    return "default";
            }
        }

        private static string FormatText(string title, string message, string url)
        {
            var text = title + Environment.NewLine + message;
            return string.IsNullOrEmpty(url) ? text : text + Environment.NewLine + url;
        }
    }
}
