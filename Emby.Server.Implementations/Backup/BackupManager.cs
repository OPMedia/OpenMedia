using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Api.System;
using MediaBrowser.Common;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;
using Npgsql;

namespace Emby.Server.Implementations.Backup
{
    public sealed class PendingRestoreMarker
    {
        public string BackupId { get; set; }

        public string Integrity { get; set; }

        public string RequestedUtc { get; set; }
    }

    /// <summary>
    /// 全量备份/恢复：数据库走 pg_dump -Fc / pg_restore，文件走受控目录复制。
    /// 任何时刻只允许一个写操作（创建 / 删除 / 恢复），由 SemaphoreSlim 保护。
    /// </summary>
    public sealed class BackupManager : IBackupService
    {
        public const string BackupDirectoryEnvironmentVariable = "OPENMEDIA_BACKUP_DIR";

        private static readonly Regex PostgresVersionRegex = new Regex(@"PostgreSQL\s+(\d+)", RegexOptions.Compiled);
        private static readonly char[] ForbiddenIdCharacters = { '/', '\\', '.', ':', '\0' };

        private readonly ILogger _logger;
        private readonly string _connectionString;
        private readonly string _programDataPath;
        private readonly Func<BackupOptions> _optionsProvider;
        private readonly Version _applicationVersion;
        private readonly int _schemaVersion;
        private readonly IApplicationHost _appHost;
        private readonly IBackupToolRunner _toolRunner;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public BackupManager(
            ILogger logger,
            string connectionString,
            string programDataPath,
            Func<BackupOptions> optionsProvider,
            Version applicationVersion,
            int schemaVersion,
            IApplicationHost appHost = null,
            IBackupToolRunner toolRunner = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _programDataPath = programDataPath ?? throw new ArgumentNullException(nameof(programDataPath));
            _optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
            _applicationVersion = applicationVersion ?? new Version(0, 0);
            _schemaVersion = schemaVersion;
            _appHost = appHost;
            _toolRunner = toolRunner ?? new ProcessBackupToolRunner();
        }

        public string BackupDirectoryPath
        {
            get
            {
                var options = GetOptions();
                var configured = !string.IsNullOrWhiteSpace(options.Directory)
                    ? options.Directory
                    : Environment.GetEnvironmentVariable(BackupDirectoryEnvironmentVariable);

                return Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
                    ? Path.Combine(_programDataPath, "backups")
                    : configured);
            }
        }

        public string PendingRestorePath => Path.Combine(BackupDirectoryPath, ".pending-restore.json");

        private sealed class LoadedBackup
        {
            public string DirectoryPath { get; set; }

            public BackupManifest Manifest { get; set; }
        }

        public Task<BackupListResult> ListAsync(CancellationToken cancellationToken)
        {
            var result = new BackupListResult
            {
                BackupDirectory = BackupDirectoryPath,
                RestorePending = File.Exists(PendingRestorePath),
                Backups = new List<BackupArchiveInfo>()
            };

            var root = BackupDirectoryPath;
            if (!Directory.Exists(root))
            {
                return Task.FromResult(result);
            }

            foreach (var directory in Directory.GetDirectories(root).OrderByDescending(i => i, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var manifest = LoadManifest(directory);
                    var info = ToArchiveInfo(manifest);
                    result.Backups.Add(info);
                    result.TotalBytes += info.TotalBytes;
                }
                catch (Exception ex) when (ex is IOException || ex is JsonException || ex is BackupValidationException)
                {
                    _logger.Warn("跳过无法读取的备份目录 {0}: {1}", directory, ex.Message);
                }
            }

            return Task.FromResult(result);
        }

        public async Task<BackupArchiveInfo> ValidateAsync(string id, CancellationToken cancellationToken)
        {
            var loaded = LoadBackup(id);
            await ValidateAsync(loaded, cancellationToken).ConfigureAwait(false);
            return ToArchiveInfo(loaded.Manifest);
        }

        public async Task<BackupResult> CreateAsync(BackupMode mode, CancellationToken cancellationToken)
        {
            if (mode != BackupMode.Full && mode != BackupMode.DatabaseOnly)
            {
                throw new BackupValidationException("未知的备份模式: " + mode);
            }

            await EnterGateAsync(cancellationToken, false).ConfigureAwait(false);
            try
            {
                var loaded = await CreateCoreAsync(mode, "Manual", cancellationToken).ConfigureAwait(false);
                ApplyRetention();
                return new BackupResult
                {
                    Id = loaded.Manifest.Id,
                    Message = "备份完成: " + loaded.DirectoryPath,
                    RestartRequired = false
                };
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DeleteAsync(string id, CancellationToken cancellationToken)
        {
            ValidateBackupId(id);
            if (File.Exists(PendingRestorePath))
            {
                var marker = ReadPendingMarker();
                if (string.Equals(marker.BackupId, id, StringComparison.Ordinal))
                {
                    throw new BackupValidationException("该备份已被标记为待恢复，请先删除待恢复标记或执行恢复。");
                }
            }

            await EnterGateAsync(cancellationToken, false).ConfigureAwait(false);
            try
            {
                var directory = GetBackupDirectory(id);
                Directory.Delete(directory, true);
                var download = Path.Combine(BackupDirectoryPath, ".downloads", id + ".zip");
                if (File.Exists(download))
                {
                    File.Delete(download);
                }

                _logger.Info("已删除备份 {0}", id);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<string> GetDownloadPathAsync(string id, CancellationToken cancellationToken)
        {
            ValidateBackupId(id);
            await EnterGateAsync(cancellationToken, true).ConfigureAwait(false);
            try
            {
                var loaded = LoadBackup(id);
                var downloadDirectory = Path.Combine(BackupDirectoryPath, ".downloads");
                Directory.CreateDirectory(downloadDirectory);
                SetPrivatePath(downloadDirectory);

                var archivePath = Path.Combine(downloadDirectory, id + ".zip");
                using (var stream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    var allFiles = Directory.GetFiles(loaded.DirectoryPath, "*", SearchOption.AllDirectories)
                        .OrderBy(i => i, StringComparer.Ordinal);
                    foreach (var file in allFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(loaded.DirectoryPath, file).Replace('\\', '/');
                        archive.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                    }
                }

                SetPrivatePath(archivePath);
                return archivePath;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<BackupResult> RequestRestoreAsync(string id, CancellationToken cancellationToken)
        {
            var loaded = LoadBackup(id);
            await ValidateAsync(loaded, cancellationToken).ConfigureAwait(false);

            var marker = new PendingRestoreMarker
            {
                BackupId = id,
                Integrity = loaded.Manifest.Integrity,
                RequestedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };

            var markerPath = PendingRestorePath;
            var temporaryPath = markerPath + ".tmp";
            WritePrivateText(temporaryPath, JsonSerializer.Serialize(marker));
            File.Move(temporaryPath, markerPath, true);
            SetPrivatePath(markerPath);

            _logger.Warn("备份 {0} 已标记为待恢复；下次启动时执行，执行前会自动快照当前状态。", id);
            return new BackupResult
            {
                Id = id,
                Message = "待恢复标记已写入，请重启服务。",
                RestartRequired = true
            };
        }

        /// <summary>启动时的恢复入口：只在存在待恢复标记时动作。</summary>
        public async Task<BackupResult> ApplyPendingRestoreAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(PendingRestorePath))
            {
                return new BackupResult { Message = "没有待恢复标记。", RestartRequired = false };
            }

            var marker = ReadPendingMarker();
            _logger.Warn("发现待恢复标记，开始从备份 {0} 恢复。", marker.BackupId);
            return await RestoreNowAsync(marker.BackupId, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 直接恢复（供启动任务与端到端验证使用）。会先创建当前状态快照，成功/失败均不删除快照。
        /// </summary>
        public async Task<BackupResult> RestoreNowAsync(string id, CancellationToken cancellationToken)
        {
            await EnterGateAsync(cancellationToken, true).ConfigureAwait(false);
            try
            {
                var loaded = LoadBackup(id);
                await ValidateAsync(loaded, cancellationToken).ConfigureAwait(false);

                var snapshotMode = string.Equals(loaded.Manifest.Mode, "Full", StringComparison.Ordinal)
                    ? BackupMode.Full
                    : BackupMode.DatabaseOnly;
                var snapshot = await CreateCoreAsync(snapshotMode, "PreRestore", cancellationToken).ConfigureAwait(false);
                _logger.Warn("恢复前快照已生成: {0}", snapshot.DirectoryPath);

                try
                {
                    await RestoreDatabaseAsync(loaded, cancellationToken).ConfigureAwait(false);
                    RestoreFileComponents(loaded);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("恢复失败，尝试回滚到快照 " + snapshot.Manifest.Id, ex);
                    string rollbackError = null;
                    try
                    {
                        await RestoreDatabaseAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
                        RestoreFileComponents(snapshot);
                    }
                    catch (Exception rollbackEx)
                    {
                        rollbackError = rollbackEx.Message;
                        _logger.ErrorException("回滚到快照失败，需人工介入: " + snapshot.DirectoryPath, rollbackEx);
                    }

                    throw new BackupValidationException(
                        "恢复失败：" + ex.Message
                        + "；已尝试回滚到快照 " + snapshot.Manifest.Id
                        + (rollbackError == null ? "，回滚成功。" : "，但回滚也失败：" + rollbackError));
                }

                if (File.Exists(PendingRestorePath))
                {
                    var marker = ReadPendingMarker();
                    if (string.Equals(marker.BackupId, id, StringComparison.Ordinal))
                    {
                        File.Delete(PendingRestorePath);
                    }
                }

                _appHost?.NotifyPendingRestart();
                return new BackupResult
                {
                    Id = id,
                    Message = "恢复完成；恢复前快照 " + snapshot.Manifest.Id + " 已保留。请重启服务以重建缓存。",
                    RestartRequired = true
                };
            }
            finally
            {
                _gate.Release();
            }
        }

        private BackupOptions GetOptions()
        {
            var options = _optionsProvider();
            return options ?? new BackupOptions();
        }

        private async Task EnterGateAsync(CancellationToken cancellationToken, bool queue)
        {
            if (queue)
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            {
                throw new BackupBusyException("另一个备份/恢复任务正在运行，请稍后重试。");
            }
        }

        private async Task<LoadedBackup> CreateCoreAsync(BackupMode mode, string kind, CancellationToken cancellationToken)
        {
            var options = GetOptions();
            var root = BackupDirectoryPath;
            Directory.CreateDirectory(root);
            SetPrivatePath(root);

            EnsureDbSizeHeadroom(options, root);

            var dumpTool = PostgresToolLocator.Locate("pg_dump", options.PostgresBinDirectory);
            var versionResult = await _toolRunner.RunAsync(
                dumpTool,
                new[] { "--version" },
                BuildToolEnvironment(),
                cancellationToken).ConfigureAwait(false);
            if (versionResult.ExitCode != 0)
            {
                throw new BackupValidationException("pg_dump --version 失败: " + versionResult.StandardError);
            }

            var major = 0;
            var versionMatch = PostgresVersionRegex.Match(versionResult.StandardOutput ?? string.Empty);
            if (versionMatch.Success)
            {
                int.TryParse(versionMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out major);
            }

            var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var backupDirectory = Path.Combine(root, id);
            Directory.CreateDirectory(backupDirectory);
            SetPrivatePath(backupDirectory);

            try
            {
                var components = new List<BackupManifestComponent>();
                var dumpPath = Path.Combine(backupDirectory, "database.dump");
                var builder = new NpgsqlConnectionStringBuilder(_connectionString);
                var dumpArguments = new List<string>
                {
                    "--format=custom",
                    "--no-owner",
                    "--no-privileges",
                    "--file", dumpPath,
                    "--host", string.IsNullOrEmpty(builder.Host) ? "localhost" : builder.Host,
                    "--port", builder.Port.ToString(CultureInfo.InvariantCulture),
                    "--dbname", builder.Database
                };
                if (!string.IsNullOrEmpty(builder.Username))
                {
                    dumpArguments.Add("--username");
                    dumpArguments.Add(builder.Username);
                }

                var dumpResult = await _toolRunner.RunAsync(
                    dumpTool, dumpArguments, BuildToolEnvironment(), cancellationToken).ConfigureAwait(false);
                if (dumpResult.ExitCode != 0)
                {
                    throw new BackupValidationException(
                        "pg_dump 失败（退出码 " + dumpResult.ExitCode.ToString(CultureInfo.InvariantCulture) + "）：" + dumpResult.StandardError);
                }

                SetPrivatePath(dumpPath);
                components.Add(new BackupManifestComponent
                {
                    Name = "database",
                    Path = "database.dump",
                    Sha256 = BackupManifestCodec.ComputeFileHash(dumpPath, out var dumpBytes),
                    Bytes = dumpBytes,
                    FileCount = 1
                });

                if (mode == BackupMode.Full)
                {
                    components.Add(CreateFileComponent(backupDirectory, "config", Path.Combine(_programDataPath, "config")));
                    components.Add(CreateFileComponent(backupDirectory, "metadata", Path.Combine(_programDataPath, "metadata")));
                }

                var manifest = new BackupManifest
                {
                    FormatVersion = BackupManifestCodec.FormatVersion,
                    Id = id,
                    Kind = kind,
                    CreatedUtc = DateTime.UtcNow,
                    Mode = mode == BackupMode.Full ? "Full" : "DatabaseOnly",
                    Schema = new BackupManifestSchema
                    {
                        ApplicationVersion = _applicationVersion.ToString(),
                        SchemaVersion = _schemaVersion,
                        DatabaseName = builder.Database,
                        DatabaseMajorVersion = major
                    },
                    Components = components,
                    TotalBytes = components.Sum(i => i.Bytes)
                };
                manifest.Integrity = BackupManifestCodec.ComputeIntegrity(manifest);

                WritePrivateText(
                    Path.Combine(backupDirectory, "manifest.json"),
                    BackupManifestCodec.Serialize(manifest));

                _logger.Info("备份 {0} 创建完成，大小 {1} 字节，模式 {2}", id, manifest.TotalBytes, manifest.Mode);
                return new LoadedBackup { DirectoryPath = backupDirectory, Manifest = manifest };
            }
            catch
            {
                try
                {
                    Directory.Delete(backupDirectory, true);
                }
                catch (Exception ex)
                {
                    _logger.Warn("清理失败备份目录 {0} 失败: {1}", backupDirectory, ex.Message);
                }

                throw;
            }
        }

        private static BackupManifestComponent CreateFileComponent(string backupDirectory, string name, string sourceDirectory)
        {
            var relativePath = name;
            var destination = Path.Combine(backupDirectory, relativePath);
            if (Directory.Exists(sourceDirectory))
            {
                CopyDirectory(sourceDirectory, destination);
            }
            else
            {
                Directory.CreateDirectory(destination);
                SetPrivatePath(destination);
            }

            var hash = BackupManifestCodec.ComputeDirectoryHash(destination, out var bytes, out var fileCount);
            return new BackupManifestComponent
            {
                Name = name,
                Path = relativePath,
                Bytes = bytes,
                FileCount = fileCount,
                Sha256 = hash
            };
        }

        private async Task ValidateAsync(LoadedBackup loaded, CancellationToken cancellationToken)
        {
            var manifest = loaded.Manifest;
            var expectedIntegrity = BackupManifestCodec.ComputeIntegrity(manifest);
            if (!string.Equals(manifest.Integrity, expectedIntegrity, StringComparison.OrdinalIgnoreCase))
            {
                throw new BackupValidationException(
                    "manifest 校验失败：文件被改动。期望 " + expectedIntegrity + "，实际 " + manifest.Integrity);
            }

            if (manifest.FormatVersion != BackupManifestCodec.FormatVersion)
            {
                throw new BackupValidationException(
                    "备份格式版本不兼容：manifest 为 " + manifest.FormatVersion + "，当前支持 " + BackupManifestCodec.FormatVersion);
            }

            var currentVersion = _applicationVersion.Major + "." + _applicationVersion.Minor;
            var backupVersion = manifest.Schema?.ApplicationVersion ?? string.Empty;
            if (!string.Equals(backupVersion, currentVersion, StringComparison.Ordinal))
            {
                throw new BackupValidationException(
                    "应用版本不兼容：备份来自 " + backupVersion + "，当前为 " + currentVersion);
            }

            if (manifest.Schema == null || manifest.Schema.SchemaVersion != _schemaVersion)
            {
                throw new BackupValidationException(
                    "schema 版本不兼容：备份为 "
                    + (manifest.Schema == null ? "未知" : manifest.Schema.SchemaVersion.ToString(CultureInfo.InvariantCulture))
                    + "，当前为 " + _schemaVersion.ToString(CultureInfo.InvariantCulture));
            }

            var builder = new NpgsqlConnectionStringBuilder(_connectionString);
            if (!string.Equals(manifest.Schema.DatabaseName, builder.Database, StringComparison.Ordinal))
            {
                throw new BackupValidationException(
                    "目标数据库不匹配：备份来自 " + manifest.Schema.DatabaseName + "，当前为 " + builder.Database);
            }

            if (manifest.Schema.DatabaseMajorVersion > 0)
            {
                var currentMajor = await GetDatabaseMajorVersionAsync(cancellationToken).ConfigureAwait(false);
                if (currentMajor > 0 && currentMajor != manifest.Schema.DatabaseMajorVersion)
                {
                    throw new BackupValidationException(
                        "PostgreSQL 主版本不兼容：备份为 " + manifest.Schema.DatabaseMajorVersion + "，当前为 " + currentMajor);
                }
            }

            var options = GetOptions();
            PostgresToolLocator.Locate("pg_dump", options.PostgresBinDirectory);
            PostgresToolLocator.Locate("pg_restore", options.PostgresBinDirectory);

            if (manifest.Components == null || manifest.Components.Count == 0)
            {
                throw new BackupValidationException("manifest 缺少组件清单。");
            }

            foreach (var component in manifest.Components)
            {
                if (string.Equals(component.Name, "database", StringComparison.Ordinal)
                    && !string.Equals(component.Path, "database.dump", StringComparison.Ordinal))
                {
                    throw new BackupValidationException("数据库组件路径非法: " + component.Path);
                }

                if (!string.Equals(component.Name, "database", StringComparison.Ordinal)
                    && !string.Equals(component.Name, "config", StringComparison.Ordinal)
                    && !string.Equals(component.Name, "metadata", StringComparison.Ordinal))
                {
                    throw new BackupValidationException("未知备份组件: " + component.Name);
                }

                var path = ResolveBackupRelativePath(loaded.DirectoryPath, component.Path, component.Name);
                var exists = string.Equals(component.Name, "database", StringComparison.Ordinal)
                    ? File.Exists(path)
                    : Directory.Exists(path);
                if (!exists)
                {
                    throw new BackupValidationException("备份组件缺失: " + component.Name + " (" + component.Path + ")");
                }

                string actualHash;
                long actualBytes;
                int actualFiles;
                if (string.Equals(component.Name, "database", StringComparison.Ordinal))
                {
                    actualHash = BackupManifestCodec.ComputeFileHash(path, out actualBytes);
                    actualFiles = 1;
                }
                else
                {
                    actualHash = BackupManifestCodec.ComputeDirectoryHash(path, out actualBytes, out actualFiles);
                }

                if (!string.Equals(actualHash, component.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BackupValidationException(
                        "备份组件校验和不匹配: " + component.Name + "（期望 " + component.Sha256 + "，实际 " + actualHash + "）");
                }

                if (actualBytes != component.Bytes || actualFiles != component.FileCount)
                {
                    throw new BackupValidationException(
                        "备份组件大小/文件数不匹配: " + component.Name);
                }
            }
        }

        private async Task RestoreDatabaseAsync(LoadedBackup loaded, CancellationToken cancellationToken)
        {
            var dump = ResolveBackupRelativePath(loaded.DirectoryPath, "database.dump", "database");
            if (!File.Exists(dump))
            {
                throw new BackupValidationException("数据库 dump 不存在: " + dump);
            }

            await TerminateDatabaseConnectionsAsync(cancellationToken).ConfigureAwait(false);

            var options = GetOptions();
            var restoreTool = PostgresToolLocator.Locate("pg_restore", options.PostgresBinDirectory);
            var builder = new NpgsqlConnectionStringBuilder(_connectionString);
            var arguments = new List<string>
            {
                "--clean",
                "--if-exists",
                "--exit-on-error",
                "--single-transaction",
                "--no-owner",
                "--no-privileges",
                "--host", string.IsNullOrEmpty(builder.Host) ? "localhost" : builder.Host,
                "--port", builder.Port.ToString(CultureInfo.InvariantCulture),
                "--dbname", builder.Database
            };
            if (!string.IsNullOrEmpty(builder.Username))
            {
                arguments.Add("--username");
                arguments.Add(builder.Username);
            }

            arguments.Add(dump);

            var result = await _toolRunner.RunAsync(
                restoreTool, arguments, BuildToolEnvironment(), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new BackupValidationException(
                    "pg_restore 失败（退出码 " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + "）：" + result.StandardError);
            }
        }

        private void RestoreFileComponents(LoadedBackup loaded)
        {
            foreach (var component in loaded.Manifest.Components ?? new List<BackupManifestComponent>())
            {
                if (string.Equals(component.Name, "database", StringComparison.Ordinal))
                {
                    continue;
                }

                var source = ResolveBackupRelativePath(loaded.DirectoryPath, component.Path, component.Name);
                if (!Directory.Exists(source))
                {
                    throw new BackupValidationException("待恢复的文件组件不存在: " + component.Path);
                }

                var destination = Path.GetFullPath(Path.Combine(_programDataPath, component.Name));
                var programData = Path.GetFullPath(_programDataPath);
                if (!destination.StartsWith(programData + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new BackupValidationException("非法的文件恢复目标: " + destination);
                }

                CopyDirectory(source, destination);
                DeleteExtraFiles(source, destination);
            }
        }

        private async Task TerminateDatabaseConnectionsAsync(CancellationToken cancellationToken)
        {
            await using (var connection = new NpgsqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using (var command = new NpgsqlCommand(
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid()",
                    connection))
                {
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            NpgsqlConnection.ClearAllPools();
        }

        private async Task<int> GetDatabaseMajorVersionAsync(CancellationToken cancellationToken)
        {
            await using (var connection = new NpgsqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using (var command = new NpgsqlCommand("SHOW server_version_num", connection))
                {
                    var value = (string)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                        ? number / 10000
                        : 0;
                }
            }
        }

        private Dictionary<string, string> BuildToolEnvironment()
        {
            var environment = new Dictionary<string, string>
            {
                ["PGCONNECT_TIMEOUT"] = "10"
            };

            var builder = new NpgsqlConnectionStringBuilder(_connectionString);
            if (!string.IsNullOrEmpty(builder.Password))
            {
                environment["PGPASSWORD"] = builder.Password;
            }

            switch (builder.SslMode)
            {
                case SslMode.Disable:
                    environment["PGSSLMODE"] = "disable";
                    break;
                case SslMode.Allow:
                    environment["PGSSLMODE"] = "allow";
                    break;
                case SslMode.Require:
                    environment["PGSSLMODE"] = "require";
                    break;
                case SslMode.VerifyCA:
                    environment["PGSSLMODE"] = "verify-ca";
                    break;
                case SslMode.VerifyFull:
                    environment["PGSSLMODE"] = "verify-full";
                    break;
            }

            return environment;
        }

        private void ApplyRetention()
        {
            var options = GetOptions();
            if (options.RetentionCount <= 0 && options.MaxTotalBytes <= 0)
            {
                return;
            }

            var root = BackupDirectoryPath;
            if (!Directory.Exists(root))
            {
                return;
            }

            var backups = Directory.GetDirectories(root)
                .Select(i => new { Path = i, Manifest = TryLoadManifest(i) })
                .Where(i => i.Manifest != null && !string.Equals(i.Manifest.Kind, "PreRestore", StringComparison.Ordinal))
                .OrderByDescending(i => i.Manifest.CreatedUtc)
                .ToList();

            if (File.Exists(PendingRestorePath))
            {
                var pendingId = ReadPendingMarker().BackupId;
                backups = backups.Where(i => !string.Equals(i.Manifest.Id, pendingId, StringComparison.Ordinal)).ToList();
            }

            var total = backups.Sum(i => i.Manifest.TotalBytes);
            for (var index = options.RetentionCount; index < backups.Count; index++)
            {
                DeleteBackupDirectory(backups[index].Path, backups[index].Manifest, ref total);
            }

            for (var index = backups.Count - 1; index >= 0 && options.MaxTotalBytes > 0 && total > options.MaxTotalBytes; index--)
            {
                DeleteBackupDirectory(backups[index].Path, backups[index].Manifest, ref total);
            }
        }

        private void DeleteBackupDirectory(string path, BackupManifest manifest, ref long total)
        {
            try
            {
                Directory.Delete(path, true);
                total -= manifest.TotalBytes;
                _logger.Info("磁盘保护：清理旧备份 {0}", manifest.Id);
            }
            catch (Exception ex)
            {
                _logger.Warn("清理旧备份 {0} 失败: {1}", manifest.Id, ex.Message);
            }
        }

        private BackupManifest TryLoadManifest(string directory)
        {
            try
            {
                return LoadManifest(directory);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is BackupValidationException)
            {
                return null;
            }
        }

        private void EnsureDbSizeHeadroom(BackupOptions options, string root)
        {
            if (options.MaxTotalBytes <= 0 || !Directory.Exists(root))
            {
                return;
            }

            long total = 0;
            foreach (var directory in Directory.GetDirectories(root))
            {
                var manifest = TryLoadManifest(directory);
                if (manifest != null)
                {
                    total += manifest.TotalBytes;
                }
            }

            if (total >= options.MaxTotalBytes)
            {
                throw new BackupValidationException(
                    "备份目录已达总大小上限 " + options.MaxTotalBytes.ToString(CultureInfo.InvariantCulture)
                    + " 字节（当前 " + total.ToString(CultureInfo.InvariantCulture) + "），请先清理旧备份。");
            }
        }

        private static BackupManifest LoadManifest(string directory)
        {
            var path = Path.Combine(directory, "manifest.json");
            var manifest = BackupManifestCodec.Deserialize(File.ReadAllText(path));
            if (manifest == null)
            {
                throw new BackupValidationException("manifest 为空: " + path);
            }

            return manifest;
        }

        private LoadedBackup LoadBackup(string id)
        {
            ValidateBackupId(id);
            var directory = GetBackupDirectory(id);
            if (!File.Exists(Path.Combine(directory, "manifest.json")))
            {
                throw new ResourceNotFoundException("备份不存在: " + id);
            }

            return new LoadedBackup { DirectoryPath = directory, Manifest = LoadManifest(directory) };
        }

        private string GetBackupDirectory(string id)
        {
            var directory = Path.Combine(BackupDirectoryPath, id);
            if (!Directory.Exists(directory))
            {
                throw new ResourceNotFoundException("备份不存在: " + id);
            }

            return directory;
        }

        private static void ValidateBackupId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)
                || id.Length > 128
                || id.IndexOfAny(ForbiddenIdCharacters) >= 0
                || id.Contains("..", StringComparison.Ordinal))
            {
                throw new BackupValidationException("非法的备份 ID: " + (id ?? "<null>"));
            }
        }

        private static string ResolveBackupRelativePath(string root, string relativePath, string componentName)
        {
            if (string.IsNullOrWhiteSpace(relativePath)
                || Path.IsPathRooted(relativePath)
                || relativePath.IndexOfAny(new[] { '/', '\\' }) >= 0
                || relativePath.Contains("..", StringComparison.Ordinal))
            {
                throw new BackupValidationException("manifest 中的组件路径非法: " + componentName + " -> " + relativePath);
            }

            var rootFull = Path.GetFullPath(root);
            var full = Path.GetFullPath(Path.Combine(rootFull, relativePath));
            if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new BackupValidationException("manifest 中的组件路径越界: " + relativePath);
            }

            return full;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            SetPrivatePath(destination);

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var file in Directory.EnumerateFiles(source, "*", options))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new BackupValidationException("拒绝处理符号链接: " + file);
                }

                var relative = Path.GetRelativePath(source, file);
                var target = Path.GetFullPath(Path.Combine(destination, relative));
                var destinationFull = Path.GetFullPath(destination);
                if (!target.StartsWith(destinationFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new BackupValidationException("文件复制目标越界: " + target);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                SetPrivatePath(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
                SetPrivatePath(target);
            }
        }

        private static void DeleteExtraFiles(string source, string destination)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            var expected = new HashSet<string>(
                Directory.EnumerateFiles(source, "*", options)
                    .Select(i => Path.GetRelativePath(source, i)),
                StringComparer.Ordinal);

            foreach (var file in Directory.EnumerateFiles(destination, "*", options))
            {
                if (!expected.Contains(Path.GetRelativePath(destination, file)))
                {
                    File.Delete(file);
                }
            }
        }

        private static void WritePrivateText(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
            SetPrivatePath(path);
        }

        /// <summary>配置 XML 含密钥/口令哈希，备份文件一律 0600（目录 0700）。</summary>
        private static void SetPrivatePath(string path)
        {
            if (string.IsNullOrEmpty(path) || OperatingSystem.IsWindows())
            {
                return;
            }

            if (Directory.Exists(path))
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            else if (File.Exists(path))
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        private PendingRestoreMarker ReadPendingMarker()
        {
            var marker = JsonSerializer.Deserialize<PendingRestoreMarker>(File.ReadAllText(PendingRestorePath));
            if (marker == null || string.IsNullOrWhiteSpace(marker.BackupId))
            {
                throw new BackupValidationException("待恢复标记损坏: " + PendingRestorePath);
            }

            return marker;
        }

        private static BackupArchiveInfo ToArchiveInfo(BackupManifest manifest)
        {
            return new BackupArchiveInfo
            {
                Id = manifest.Id,
                Kind = manifest.Kind,
                CreatedUtc = manifest.CreatedUtc.ToString("o", CultureInfo.InvariantCulture),
                Mode = manifest.Mode,
                FormatVersion = manifest.FormatVersion,
                ApplicationVersion = manifest.Schema?.ApplicationVersion,
                SchemaVersion = manifest.Schema?.SchemaVersion ?? 0,
                DatabaseName = manifest.Schema?.DatabaseName,
                DatabaseMajorVersion = manifest.Schema?.DatabaseMajorVersion ?? 0,
                TotalBytes = manifest.TotalBytes,
                Integrity = manifest.Integrity,
                Components = (manifest.Components ?? new List<BackupManifestComponent>())
                    .Select(i => new BackupComponentInfo
                    {
                        Name = i.Name,
                        Path = i.Path,
                        Bytes = i.Bytes,
                        FileCount = i.FileCount,
                        Sha256 = i.Sha256
                    })
                    .ToList()
            };
        }
    }
}
