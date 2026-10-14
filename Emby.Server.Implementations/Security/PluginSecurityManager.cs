using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Security;
using MediaBrowser.Controller;
using MediaBrowser.Model.Cryptography;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace Emby.Server.Implementations.Security
{
    /// <summary>
    /// Class PluginSecurityManager
    /// </summary>
    public class PluginSecurityManager : ISecurityManager
    {
        public async Task<bool> IsSupporter()
        {
            var result = await GetRegistrationStatusInternal("MBSupporter", false, _appHost.ApplicationVersion.ToString(), CancellationToken.None).ConfigureAwait(false);

            return result.IsRegistered;
        }

        private MBLicenseFile _licenseFile;
        private MBLicenseFile LicenseFile
        {
            get { return _licenseFile ?? (_licenseFile = new MBLicenseFile(_appPaths, _fileSystem, _cryptographyProvider)); }
        }

        private readonly IServerApplicationHost _appHost;
        private readonly ILogger _logger;
        private readonly IApplicationPaths _appPaths;
        private readonly IFileSystem _fileSystem;
        private readonly ICryptoProvider _cryptographyProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginSecurityManager" /> class.
        /// </summary>
        public PluginSecurityManager(IServerApplicationHost appHost,
            IApplicationPaths appPaths, ILogManager logManager, IFileSystem fileSystem, ICryptoProvider cryptographyProvider)
        {
            _appHost = appHost;
            _appPaths = appPaths;
            _fileSystem = fileSystem;
            _cryptographyProvider = cryptographyProvider;
            _logger = logManager.GetLogger("SecurityManager");
        }

        /// <summary>
        /// Gets the registration status.
        /// This overload supports existing plug-ins.
        /// </summary>
        public Task<MBRegistrationRecord> GetRegistrationStatus(string feature)
        {
            return GetRegistrationStatusInternal(feature, false, null, CancellationToken.None);
        }

        /// <summary>
        /// Gets or sets the supporter key.
        /// </summary>
        /// <value>The supporter key.</value>
        public string SupporterKey
        {
            get
            {
                return LicenseFile.RegKey;
            }
            set
            {
                throw new Exception("Please call UpdateSupporterKey");
            }
        }

        public async Task UpdateSupporterKey(string newValue)
        {
            if (newValue != null)
            {
                newValue = newValue.Trim();
            }

            if (!string.Equals(newValue, LicenseFile.RegKey, StringComparison.Ordinal))
            {
                LicenseFile.RegKey = newValue;
                LicenseFile.Save();

                // Reset this
                await GetRegistrationStatusInternal("MBSupporter", true, _appHost.ApplicationVersion.ToString(), CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Register an app store sale with our back-end.  It will validate the transaction with the store
        /// and then register the proper feature and then fill in the supporter key on success.
        /// </summary>
        /// <param name="parameters">Json parameters to send to admin server</param>
        public Task RegisterAppStoreSale(string parameters)
        {
            // OpenMedia（去 Emby 化，用户决策）：应用商店注册（Emby 云）已移除，保留签名兼容现有调用方。
            _logger.Info("App store registration is disabled in OpenMedia (Emby cloud decoupled).");
            return Task.CompletedTask;
        }

        private async Task<MBRegistrationRecord> GetRegistrationStatusInternal(string feature, bool forceCallToServer, string version, CancellationToken cancellationToken)
        {
            // OpenMedia（去 Emby 化，用户决策）：Emby 云注册体系已移除，所有功能门禁（DVR/系列录制/硬件加速等）
            // 恒为已注册且有效，不再向 mb3admin.com 发起任何校验请求。
            await Task.CompletedTask.ConfigureAwait(false);

            return new MBRegistrationRecord
            {
                IsRegistered = true,
                IsValid = true,
                RegChecked = true,
                RegError = false,
                TrialVersion = false,
                ExpirationDate = new DateTime(2099, 12, 31, 0, 0, 0, DateTimeKind.Utc)
            };
        }
    }
}