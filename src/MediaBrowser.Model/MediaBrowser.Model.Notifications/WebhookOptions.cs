using System;
using System.Collections.Generic;

namespace MediaBrowser.Model.Notifications;

/// <summary>
/// Root configuration for the webhook notification transport.
/// Stored under the named configuration key "notifications.webhook" and edited
/// through the dashboard's generic named-configuration page mechanism.
/// </summary>
public class WebhookOptions
{
    public WebhookEndpoint[] Webhooks { get; set; }

    public WebhookOptions()
    {
        Webhooks = Array.Empty<WebhookEndpoint>();
    }
}

/// <summary>
/// A single HTTP webhook destination.
/// </summary>
public class WebhookEndpoint
{
    /// <summary>Short label used in logs and in the configuration page.</summary>
    public string Name { get; set; }

    /// <summary>Whether this destination receives notifications.</summary>
    public bool Enabled { get; set; }

    /// <summary>Absolute http/https URL that receives the POST.</summary>
    public string Url { get; set; }

    /// <summary>Optional extra request headers, for example an authorization token.</summary>
    public Dictionary<string, string> Headers { get; set; }

    /// <summary>
    /// Optional case-insensitive NotificationType whitelist (e.g. NewLibraryContent).
    /// An empty list means every event type.
    /// </summary>
    public string[] EventTypes { get; set; }

    /// <summary>
    /// Built-in body format: json (default), discord, telegram, ntfy or homeassistant.
    /// </summary>
    public string Template { get; set; }

    /// <summary>Telegram chat_id, when the telegram template is selected.</summary>
    public string ChatId { get; set; }

    /// <summary>Number of retries after the first failed attempt (0-5).</summary>
    public int MaxRetries { get; set; }

    /// <summary>Base retry delay in milliseconds; each retry doubles it, capped at 30s.</summary>
    public int RetryDelayMs { get; set; }

    /// <summary>Per-attempt timeout in milliseconds.</summary>
    public int TimeoutMs { get; set; }

    public WebhookEndpoint()
    {
        Name = "Webhook";
        Enabled = true;
        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        EventTypes = Array.Empty<string>();
        Template = "json";
        MaxRetries = 2;
        RetryDelayMs = 500;
        TimeoutMs = 10000;
    }
}
