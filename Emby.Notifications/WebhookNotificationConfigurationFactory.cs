using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Notifications;

namespace Emby.Notifications
{
    /// <summary>
    /// Registers the webhook transport's named configuration. The key is the
    /// configuration-page contract: dashboard pages read and save it through the
    /// existing /System/Configuration/{Key} endpoints, without new API endpoints.
    /// </summary>
    public class WebhookNotificationConfigurationFactory : IConfigurationFactory
    {
        public const string ConfigurationKey = "notifications.webhook";

        public IEnumerable<ConfigurationStore> GetConfigurations()
        {
            return new ConfigurationStore[]
            {
                new ConfigurationStore
                {
                    Key = ConfigurationKey,
                    ConfigurationType = typeof(WebhookOptions)
                }
            };
        }
    }
}
