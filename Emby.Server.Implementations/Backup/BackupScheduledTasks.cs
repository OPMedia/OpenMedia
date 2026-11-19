using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Api.System;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;

namespace Emby.Server.Implementations.Backup
{
    /// <summary>启动时执行待恢复标记；由 IScheduledTask 反射发现，无需额外注册。</summary>
    internal sealed class BackupPendingRestoreTask : IScheduledTask
    {
        private readonly IBackupService _backups;

        public BackupPendingRestoreTask(IBackupService backups)
        {
            _backups = backups;
        }

        public string Name => "应用待恢复备份";

        public string Key => "ApplyPendingBackupRestore";

        public string Description => "下次启动时执行已标记的备份恢复，执行前自动快照当前状态。";

        public string Category => "备份";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[] { new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerStartup } };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            await _backups.ApplyPendingRestoreAsync(cancellationToken).ConfigureAwait(false);
            progress.Report(100);
        }
    }

    /// <summary>可选周期备份，间隔为 0 时不注册触发器（任务仍可见但不会自动跑）。</summary>
    internal sealed class PeriodicBackupTask : IScheduledTask
    {
        private readonly IBackupService _backups;
        private readonly IServerConfigurationManager _configurationManager;

        public PeriodicBackupTask(IBackupService backups, IServerConfigurationManager configurationManager)
        {
            _backups = backups;
            _configurationManager = configurationManager;
        }

        public string Name => "周期性全量备份";

        public string Key => "PeriodicBackup";

        public string Description => "按 Backup.IntervalHours 创建全量备份（数据库 + 配置 + 元数据）。";

        public string Category => "备份";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            var options = _configurationManager.Configuration.Backup;
            if (options == null || options.IntervalHours <= 0)
            {
                return Array.Empty<TaskTriggerInfo>();
            }

            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = TimeSpan.FromHours(options.IntervalHours).Ticks
                }
            };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            await _backups.CreateAsync(BackupMode.Full, cancellationToken).ConfigureAwait(false);
            progress.Report(100);
        }
    }
}
