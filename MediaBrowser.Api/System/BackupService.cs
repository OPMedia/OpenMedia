using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Services;

namespace MediaBrowser.Api.System
{
    /// <summary>备份模式：Full 包含数据库 + config + metadata；DatabaseOnly 只含数据库与 manifest。</summary>
    public enum BackupMode
    {
        Full = 0,
        DatabaseOnly = 1
    }

    public sealed class BackupComponentInfo
    {
        public string Name { get; set; }

        public string Path { get; set; }

        public long Bytes { get; set; }

        public int FileCount { get; set; }

        public string Sha256 { get; set; }
    }

    public sealed class BackupArchiveInfo
    {
        public string Id { get; set; }

        public string Kind { get; set; }

        public string CreatedUtc { get; set; }

        public string Mode { get; set; }

        public int FormatVersion { get; set; }

        public string ApplicationVersion { get; set; }

        public int SchemaVersion { get; set; }

        public string DatabaseName { get; set; }

        public int DatabaseMajorVersion { get; set; }

        public long TotalBytes { get; set; }

        public string Integrity { get; set; }

        public List<BackupComponentInfo> Components { get; set; }
    }

    public sealed class BackupListResult
    {
        public string BackupDirectory { get; set; }

        public long TotalBytes { get; set; }

        public bool RestorePending { get; set; }

        public List<BackupArchiveInfo> Backups { get; set; }
    }

    public sealed class BackupResult
    {
        public string Id { get; set; }

        public string Message { get; set; }

        public bool RestartRequired { get; set; }
    }

    /// <summary>
    /// 备份子系统的服务契约。实现放在 Emby.Server.Implementations.Backup，
    /// 这里只放 API 能引用的 DTO 与接口，保持 MediaBrowser.Api 不反向依赖实现层。
    /// </summary>
    public interface IBackupService
    {
        Task<BackupListResult> ListAsync(CancellationToken cancellationToken);

        Task<BackupResult> CreateAsync(BackupMode mode, CancellationToken cancellationToken);

        Task<BackupArchiveInfo> ValidateAsync(string id, CancellationToken cancellationToken);

        Task<string> GetDownloadPathAsync(string id, CancellationToken cancellationToken);

        Task DeleteAsync(string id, CancellationToken cancellationToken);

        Task<BackupResult> RequestRestoreAsync(string id, CancellationToken cancellationToken);

        Task<BackupResult> RestoreNowAsync(string id, CancellationToken cancellationToken);

        Task<BackupResult> ApplyPendingRestoreAsync(CancellationToken cancellationToken);
    }

    /// <summary>校验失败（含 manifest 损坏、schema 不兼容、工具缺失）——由现有异常映射转成 HTTP 400。</summary>
    public sealed class BackupValidationException : ArgumentException
    {
        public BackupValidationException(string message)
            : base(message)
        {
        }
    }

    /// <summary>已有备份/恢复任务在跑，拒绝并发操作。</summary>
    public sealed class BackupBusyException : ArgumentException
    {
        public BackupBusyException(string message)
            : base(message)
        {
        }
    }

    [Route("/System/Backup", "GET", Summary = "列举全量备份")]
    [Authenticated(Roles = "Admin")]
    public class GetBackups : IReturn<BackupListResult>
    {
    }

    [Route("/System/Backup", "POST", Summary = "创建一个全量备份（或直接执行恢复的轻量备份）")]
    [Authenticated(Roles = "Admin")]
    public class CreateBackup : IReturn<BackupResult>
    {
        [ApiMember(Name = "Mode", Description = "Full 或 DatabaseOnly，默认 Full", ParameterType = "query", Verb = "POST")]
        public string Mode { get; set; }
    }

    [Route("/System/Backup/{Id}/Validate", "GET", Summary = "校验备份 manifest 与当前版本是否兼容")]
    [Authenticated(Roles = "Admin")]
    public class ValidateBackup : IReturn<BackupArchiveInfo>
    {
        [ApiMember(Name = "Id", IsRequired = true, ParameterType = "path", Verb = "GET")]
        public string Id { get; set; }
    }

    [Route("/System/Backup/{Id}/Download", "GET", Summary = "下载备份（zip 包）")]
    [Authenticated(Roles = "Admin")]
    public class DownloadBackup
    {
        [ApiMember(Name = "Id", IsRequired = true, ParameterType = "path", Verb = "GET")]
        public string Id { get; set; }
    }

    [Route("/System/Backup/{Id}", "DELETE", Summary = "删除一个备份")]
    [Authenticated(Roles = "Admin")]
    public class DeleteBackup : IReturnVoid
    {
        [ApiMember(Name = "Id", IsRequired = true, ParameterType = "path", Verb = "DELETE")]
        public string Id { get; set; }
    }

    [Route("/System/Backup/{Id}/Restore", "POST", Summary = "标记待恢复：校验通过后下次启动执行，执行前自动生成快照")]
    [Authenticated(Roles = "Admin")]
    public class RestoreBackup : IReturn<BackupResult>
    {
        [ApiMember(Name = "Id", IsRequired = true, ParameterType = "path", Verb = "POST")]
        public string Id { get; set; }
    }

    /// <summary>系统备份 API。</summary>
    public class BackupService : BaseApiService
    {
        private readonly IBackupService _backups;

        public BackupService(IBackupService backups)
        {
            _backups = backups;
        }

        public async Task<object> Get(GetBackups request)
        {
            var result = await _backups.ListAsync(CancellationToken.None).ConfigureAwait(false);
            return ToOptimizedResult(result);
        }

        public async Task<object> Post(CreateBackup request)
        {
            BackupMode mode;
            if (string.IsNullOrWhiteSpace(request.Mode)
                || string.Equals(request.Mode, "Full", StringComparison.OrdinalIgnoreCase))
            {
                mode = BackupMode.Full;
            }
            else if (string.Equals(request.Mode, "DatabaseOnly", StringComparison.OrdinalIgnoreCase))
            {
                mode = BackupMode.DatabaseOnly;
            }
            else
            {
                throw new ArgumentException("Mode 只支持 Full 或 DatabaseOnly: " + request.Mode, nameof(request.Mode));
            }

            var result = await _backups.CreateAsync(mode, CancellationToken.None).ConfigureAwait(false);
            return ToOptimizedResult(result);
        }

        public async Task<object> Get(ValidateBackup request)
        {
            var result = await _backups.ValidateAsync(request.Id, CancellationToken.None).ConfigureAwait(false);
            return ToOptimizedResult(result);
        }

        public Task<object> Get(DownloadBackup request)
        {
            return GetDownloadResultAsync(request.Id);
        }

        private async Task<object> GetDownloadResultAsync(string id)
        {
            var path = await _backups.GetDownloadPathAsync(id, CancellationToken.None).ConfigureAwait(false);
            return await ResultFactory.GetStaticFileResult(Request, path, FileShareMode.Read).ConfigureAwait(false);
        }

        public async Task<object> Delete(DeleteBackup request)
        {
            await _backups.DeleteAsync(request.Id, CancellationToken.None).ConfigureAwait(false);
            return ToOptimizedResult(new BackupResult { Id = request.Id, Message = "已删除" });
        }

        public async Task<object> Post(RestoreBackup request)
        {
            var result = await _backups.RequestRestoreAsync(request.Id, CancellationToken.None).ConfigureAwait(false);
            return ToOptimizedResult(result);
        }
    }
}
