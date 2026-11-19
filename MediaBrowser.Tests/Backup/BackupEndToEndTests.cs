using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.Backup;
using MediaBrowser.Api.System;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace MediaBrowser.Tests.Backup
{
    /// <summary>
    /// 真库端到端：使用 OPENMEDIA_TEST_PGCONN 所指的 PostgreSQL 实例，但只在其中创建/销毁
    /// 独立的 om_bk_e2e_* 数据库，绝不碰 openmedia_dev / openmedia_perf / openmedia。
    /// pg_dump/pg_restore 路径可由 OPENMEDIA_PG_BIN 指定。
    /// </summary>
    [TestClass]
    public sealed class BackupEndToEndTests
    {
        private string _adminConnectionString;
        private string _databaseName;
        private string _testConnectionString;
        private string _root;
        private string _programData;
        private BackupManager _manager;
        private string _pgBin;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public async Task SetUpAsync()
        {
            _adminConnectionString = Environment.GetEnvironmentVariable("OPENMEDIA_TEST_PGCONN");
            if (string.IsNullOrWhiteSpace(_adminConnectionString))
            {
                Assert.Inconclusive("未设置 OPENMEDIA_TEST_PGCONN，跳过真库备份/恢复验证。");
            }

            _root = Path.Combine(Path.GetTempPath(), "om-backup-e2e-" + Guid.NewGuid().ToString("N"));
            _programData = Path.Combine(_root, "programdata");
            Directory.CreateDirectory(Path.Combine(_programData, "config"));
            Directory.CreateDirectory(Path.Combine(_programData, "metadata"));
            File.WriteAllText(Path.Combine(_programData, "config", "system.xml"), "<System><Secret>abc</Secret></System>");
            File.WriteAllText(Path.Combine(_programData, "metadata", "art.jpg"), "artwork-bytes");

            _pgBin = Environment.GetEnvironmentVariable(PostgresToolLocator.BinDirectoryEnvironmentVariable) ?? string.Empty;
            try
            {
                TestContext.WriteLine("pg_dump 路径: " + PostgresToolLocator.Locate("pg_dump", _pgBin));
                TestContext.WriteLine("pg_restore 路径: " + PostgresToolLocator.Locate("pg_restore", _pgBin));
            }
            catch (BackupValidationException ex)
            {
                Assert.Inconclusive("pg_dump/pg_restore 不可用: " + ex.Message);
            }

            _databaseName = "om_bk_e2e_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            try
            {
                await using (var admin = new NpgsqlConnection(_adminConnectionString))
                {
                    await admin.OpenAsync().ConfigureAwait(false);
                    await using (var command = new NpgsqlCommand("CREATE DATABASE " + _databaseName, admin))
                    {
                        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }

                _testConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
                {
                    Database = _databaseName
                }.ConnectionString;

                await using (var connection = new NpgsqlConnection(_testConnectionString))
                {
                    await connection.OpenAsync().ConfigureAwait(false);
                    await using (var command = new NpgsqlCommand(
                        "CREATE TABLE backup_e2e_item (id integer PRIMARY KEY, label text NOT NULL); "
                        + "INSERT INTO backup_e2e_item (id, label) VALUES (1, 'kept-after-restore')", connection))
                    {
                        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is NpgsqlException || ex is InvalidOperationException)
            {
                Assert.Inconclusive("无法在测试实例创建独立数据库: " + ex.Message);
            }

            var options = new BackupOptions
            {
                Directory = Path.Combine(_root, "backups"),
                PostgresBinDirectory = _pgBin,
                RetentionCount = 10,
                MaxTotalBytes = 0,
                IntervalHours = 0
            };

            _manager = new BackupManager(
                new NullLogger(),
                _testConnectionString,
                _programData,
                () => options,
                new Version(1, 0),
                7,
                null,
                new ProcessBackupToolRunner());
        }

        [TestCleanup]
        public async Task TearDownAsync()
        {
            if (string.IsNullOrEmpty(_databaseName) || string.IsNullOrEmpty(_adminConnectionString))
            {
                return;
            }

            try
            {
                NpgsqlConnection.ClearAllPools();
                await using (var admin = new NpgsqlConnection(_adminConnectionString))
                {
                    await admin.OpenAsync().ConfigureAwait(false);
                    await using (var terminate = new NpgsqlCommand(
                        "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '" + _databaseName + "' AND pid <> pg_backend_pid()",
                        admin))
                    {
                        await terminate.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }

                    await using (var drop = new NpgsqlCommand("DROP DATABASE IF EXISTS " + _databaseName, admin))
                    {
                        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                TestContext.WriteLine("清理测试数据库失败: " + ex.Message);
            }

            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        [TestMethod]
        public async Task EndToEnd_DeletedRowComesBackAfterPendingRestore()
        {
            var created = await _manager.CreateAsync(BackupMode.Full, CancellationToken.None).ConfigureAwait(false);
            var backupDirectory = Path.Combine(_manager.BackupDirectoryPath, created.Id);
            var listing = await _manager.ListAsync(CancellationToken.None).ConfigureAwait(false);
            var info = listing.Backups.Single(i => i.Id == created.Id);

            TestContext.WriteLine("备份目录: " + backupDirectory);
            foreach (var component in info.Components)
            {
                TestContext.WriteLine("  {0}: {1} 字节, {2} 文件, sha256={3}", component.Name, component.Bytes, component.FileCount, component.Sha256);
            }

            Assert.AreEqual(3, info.Components.Count);
            Assert.IsTrue(info.Components.Single(i => i.Name == "database").Bytes > 0);
            Assert.IsTrue(info.TotalBytes > 0);

            await using (var connection = new NpgsqlConnection(_testConnectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                await using (var command = new NpgsqlCommand("DELETE FROM backup_e2e_item WHERE id = 1", connection))
                {
                    Assert.AreEqual(1, await command.ExecuteNonQueryAsync().ConfigureAwait(false));
                }
            }

            File.Delete(Path.Combine(_programData, "config", "system.xml"));
            File.Delete(Path.Combine(_programData, "metadata", "art.jpg"));
            // 当前状态里新增两个「只在快照中存在」的文件，用来证明快照捕获的是恢复前状态。
            File.WriteAllText(Path.Combine(_programData, "config", "drift.xml"), "<CurrentState/>");
            File.WriteAllText(Path.Combine(_programData, "metadata", "drift.jpg"), "current-state-artwork");

            var request = await _manager.RequestRestoreAsync(created.Id, CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(request.RestartRequired);
            Assert.IsTrue(File.Exists(_manager.PendingRestorePath));
            TestContext.WriteLine("待恢复标记: " + _manager.PendingRestorePath);

            var applied = await _manager.ApplyPendingRestoreAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(applied.RestartRequired);
            Assert.IsFalse(File.Exists(_manager.PendingRestorePath));

            await using (var connection = new NpgsqlConnection(_testConnectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                await using (var command = new NpgsqlCommand("SELECT label FROM backup_e2e_item WHERE id = 1", connection))
                {
                    Assert.AreEqual("kept-after-restore", (string)await command.ExecuteScalarAsync().ConfigureAwait(false));
                }
            }

            Assert.AreEqual("<System><Secret>abc</Secret></System>", File.ReadAllText(Path.Combine(_programData, "config", "system.xml")));
            Assert.AreEqual("artwork-bytes", File.ReadAllText(Path.Combine(_programData, "metadata", "art.jpg")));
            Assert.IsFalse(File.Exists(Path.Combine(_programData, "config", "drift.xml")));
            Assert.IsFalse(File.Exists(Path.Combine(_programData, "metadata", "drift.jpg")));

            var snapshots = Directory.GetDirectories(_manager.BackupDirectoryPath)
                .Select(TryLoadManifest)
                .Where(i => i != null && string.Equals(i.Kind, "PreRestore", StringComparison.Ordinal))
                .ToList();
            Assert.AreEqual(1, snapshots.Count, "恢复前快照应当恰好生成一份");
            foreach (var snapshot in snapshots)
            {
                TestContext.WriteLine("恢复前快照 manifest: " + snapshot.Id);
            }

            var snapshotDirectory = Path.Combine(_manager.BackupDirectoryPath, snapshots[0].Id);
            foreach (var file in Directory.GetFiles(snapshotDirectory, "*", SearchOption.AllDirectories).OrderBy(i => i, StringComparer.Ordinal))
            {
                TestContext.WriteLine("  " + Path.GetRelativePath(snapshotDirectory, file) + " (" + new FileInfo(file).Length + " 字节)");
            }
        }

        [TestMethod]
        public async Task Restore_RejectsCorruptedDumpAndLeavesDataUntouched()
        {
            var created = await _manager.CreateAsync(BackupMode.DatabaseOnly, CancellationToken.None).ConfigureAwait(false);
            var dump = Path.Combine(_manager.BackupDirectoryPath, created.Id, "database.dump");
            var bytes = File.ReadAllBytes(dump);
            bytes[bytes.Length - 1] ^= 0xFF;
            File.WriteAllBytes(dump, bytes);

            var exception = await Assert.ThrowsExceptionAsync<BackupValidationException>(
                () => _manager.RestoreNowAsync(created.Id, CancellationToken.None)).ConfigureAwait(false);
            TestContext.WriteLine("损坏 dump 被拒绝: " + exception.Message);
            StringAssert.Contains(exception.Message, "校验和");

            await using (var connection = new NpgsqlConnection(_testConnectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                await using (var command = new NpgsqlCommand("SELECT COUNT(*) FROM backup_e2e_item WHERE id = 1", connection))
                {
                    Assert.AreEqual(1L, (long)await command.ExecuteScalarAsync().ConfigureAwait(false));
                }
            }
        }

        [TestMethod]
        public async Task Restore_RejectsTamperedManifestByte()
        {
            var created = await _manager.CreateAsync(BackupMode.DatabaseOnly, CancellationToken.None).ConfigureAwait(false);
            var manifestPath = Path.Combine(_manager.BackupDirectoryPath, created.Id, "manifest.json");
            var text = File.ReadAllText(manifestPath);
            var modified = text.Replace("\"SchemaVersion\": 7", "\"SchemaVersion\": 8", StringComparison.Ordinal);
            Assert.AreNotEqual(text, modified);
            File.WriteAllText(manifestPath, modified);

            var exception = await Assert.ThrowsExceptionAsync<BackupValidationException>(
                () => _manager.RestoreNowAsync(created.Id, CancellationToken.None)).ConfigureAwait(false);
            TestContext.WriteLine("损坏 manifest 被拒绝: " + exception.Message);
            StringAssert.Contains(exception.Message, "manifest 校验失败");
        }

        private static BackupManifest TryLoadManifest(string directory)
        {
            try
            {
                return BackupManifestCodec.Deserialize(File.ReadAllText(Path.Combine(directory, "manifest.json")));
            }
            catch
            {
                return null;
            }
        }
    }
}
