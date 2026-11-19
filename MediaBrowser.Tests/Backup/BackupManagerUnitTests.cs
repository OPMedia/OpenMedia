using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.Backup;
using MediaBrowser.Api.System;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.Backup
{
    [TestClass]
    public sealed class BackupManagerUnitTests
    {
        private string _root;
        private string _programData;
        private BackupOptions _options;
        private FakeToolRunner _runner;
        private BackupManager _manager;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "om-backup-unit-" + Guid.NewGuid().ToString("N"));
            _programData = Path.Combine(_root, "programdata");
            var bin = Path.Combine(_root, "bin");
            Directory.CreateDirectory(bin);
            Directory.CreateDirectory(Path.Combine(_programData, "config"));
            Directory.CreateDirectory(Path.Combine(_programData, "metadata"));

            File.WriteAllText(Path.Combine(bin, "pg_dump"), "stub");
            File.WriteAllText(Path.Combine(bin, "pg_restore"), "stub");
            File.WriteAllText(Path.Combine(_programData, "config", "system.xml"), "<System><Secret>abc</Secret></System>");
            File.WriteAllText(Path.Combine(_programData, "metadata", "art.jpg"), "art");

            _options = new BackupOptions
            {
                Directory = Path.Combine(_root, "backups"),
                PostgresBinDirectory = bin,
                RetentionCount = 5,
                MaxTotalBytes = 0,
                IntervalHours = 0
            };

            _runner = new FakeToolRunner();
            _manager = new BackupManager(
                new NullLogger(),
                "Host=127.0.0.1;Port=5432;Database=backup_unit;Username=postgres;password=secret",
                _programData,
                () => _options,
                new Version(1, 2),
                7,
                null,
                _runner);
        }

        [TestCleanup]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        [TestMethod]
        public void ToolLocator_UsesConfiguredDirectory()
        {
            var path = PostgresToolLocator.Locate("pg_dump", _options.PostgresBinDirectory);
            Assert.AreEqual(Path.Combine(_options.PostgresBinDirectory, "pg_dump"), path);
        }

        [TestMethod]
        public void ToolLocator_FailureListsSearchedPathsAndHint()
        {
            var savedPath = Environment.GetEnvironmentVariable("PATH");
            var savedBin = Environment.GetEnvironmentVariable(PostgresToolLocator.BinDirectoryEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable("PATH", string.Empty);
                Environment.SetEnvironmentVariable(PostgresToolLocator.BinDirectoryEnvironmentVariable, null);

                var exception = Assert.ThrowsException<BackupValidationException>(
                    () => PostgresToolLocator.Locate("pg_dump", Path.Combine(_root, "missing")));

                StringAssert.Contains(exception.Message, "找不到 pg_dump");
                StringAssert.Contains(exception.Message, "OPENMEDIA_PG_BIN");
                StringAssert.Contains(exception.Message, Path.Combine(_root, "missing"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", savedPath);
                Environment.SetEnvironmentVariable(PostgresToolLocator.BinDirectoryEnvironmentVariable, savedBin);
            }
        }

        [TestMethod]
        public async Task Create_DatabaseOnlyOmitsConfigAndMetadata()
        {
            var result = await _manager.CreateAsync(BackupMode.DatabaseOnly, CancellationToken.None).ConfigureAwait(false);

            var list = await _manager.ListAsync(CancellationToken.None).ConfigureAwait(false);
            var backup = list.Backups.Single(i => i.Id == result.Id);
            CollectionAssert.AreEquivalent(new[] { "database" }, backup.Components.Select(i => i.Name).ToArray());
            Assert.IsFalse(Directory.Exists(Path.Combine(_manager.BackupDirectoryPath, result.Id, "metadata")));
        }

        [TestMethod]
        public async Task Create_FullBackupUsesPrivateFileModes()
        {
            var result = await _manager.CreateAsync(BackupMode.Full, CancellationToken.None).ConfigureAwait(false);
            var backupDirectory = Path.Combine(_manager.BackupDirectoryPath, result.Id);

            if (!OperatingSystem.IsWindows())
            {
                foreach (var file in Directory.GetFiles(backupDirectory, "*", SearchOption.AllDirectories))
                {
                    var mode = File.GetUnixFileMode(file);
                    Assert.IsTrue((mode & UnixFileMode.UserRead) != 0, file);
                    Assert.IsTrue((mode & UnixFileMode.UserWrite) != 0, file);
                    Assert.IsFalse((mode & UnixFileMode.GroupRead) != 0, file);
                    Assert.IsFalse((mode & UnixFileMode.OtherRead) != 0, file);
                }
            }
            else
            {
                Assert.Inconclusive("Unix 文件权限断言仅适用于 Unix。");
            }
        }

        [TestMethod]
        public async Task Create_ConcurrentSecondCallIsRejected()
        {
            _runner.BlockDump = true;
            // CreateAsync 在第一个未完成的 await 之前已同步拿到互斥锁，返回时锁必然被占用。
            var first = _manager.CreateAsync(BackupMode.DatabaseOnly, CancellationToken.None);

            var second = await Assert.ThrowsExceptionAsync<BackupBusyException>(
                () => _manager.CreateAsync(BackupMode.DatabaseOnly, CancellationToken.None)).ConfigureAwait(false);
            StringAssert.Contains(second.Message, "另一个备份");
            TestContext.WriteLine("并发拒绝: " + second.Message);

            var result = await first.ConfigureAwait(false);
            Assert.IsFalse(string.IsNullOrEmpty(result.Id));
        }

        [TestMethod]
        public async Task Create_RetentionKeepsOnlyConfiguredCount()
        {
            _options.RetentionCount = 2;
            await _manager.CreateAsync(BackupMode.DatabaseOnly, CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(20).ConfigureAwait(false);
            await _manager.CreateAsync(BackupMode.DatabaseOnly, CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(20).ConfigureAwait(false);
            await _manager.CreateAsync(BackupMode.DatabaseOnly, CancellationToken.None).ConfigureAwait(false);

            var list = await _manager.ListAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(2, list.Backups.Count);
        }

        [TestMethod]
        public async Task Validate_DetectsCorruptedDatabaseChecksum()
        {
            var result = await _manager.CreateAsync(BackupMode.DatabaseOnly, CancellationToken.None).ConfigureAwait(false);
            var dump = Path.Combine(_manager.BackupDirectoryPath, result.Id, "database.dump");
            var bytes = File.ReadAllBytes(dump);
            bytes[0] ^= 0x01;
            File.WriteAllBytes(dump, bytes);

            var exception = await Assert.ThrowsExceptionAsync<BackupValidationException>(
                () => _manager.RestoreNowAsync(result.Id, CancellationToken.None)).ConfigureAwait(false);
            StringAssert.Contains(exception.Message, "校验和");
        }

        [TestMethod]
        public async Task Validate_DetectsCorruptedManifestByte()
        {
            var result = await _manager.CreateAsync(BackupMode.DatabaseOnly, CancellationToken.None).ConfigureAwait(false);
            var manifestPath = Path.Combine(_manager.BackupDirectoryPath, result.Id, "manifest.json");
            var text = File.ReadAllText(manifestPath);
            File.WriteAllText(manifestPath, text.Replace("\"Id\": \"" + result.Id, "\"Id\": \"" + result.Id.Substring(0, 1) + "X" + result.Id.Substring(2), StringComparison.Ordinal));

            var exception = await Assert.ThrowsExceptionAsync<BackupValidationException>(
                () => _manager.RestoreNowAsync(result.Id, CancellationToken.None)).ConfigureAwait(false);
            StringAssert.Contains(exception.Message, "manifest 校验失败");
        }

        [TestMethod]
        public async Task Validate_RejectsSchemaMismatchWithReason()
        {
            var result = await _manager.CreateAsync(BackupMode.DatabaseOnly, CancellationToken.None).ConfigureAwait(false);
            var incompatible = new BackupManager(
                new NullLogger(),
                "Host=127.0.0.1;Port=5432;Database=backup_unit;Username=postgres",
                _programData,
                () => _options,
                new Version(1, 2),
                8,
                null,
                _runner);

            var exception = await Assert.ThrowsExceptionAsync<BackupValidationException>(
                () => incompatible.ValidateAsync(result.Id, CancellationToken.None)).ConfigureAwait(false);
            StringAssert.Contains(exception.Message, "schema 版本不兼容");
            StringAssert.Contains(exception.Message, "当前为 8");
        }

        private sealed class FakeToolRunner : IBackupToolRunner
        {
            private readonly List<IReadOnlyList<string>> _invocations = new List<IReadOnlyList<string>>();

            public bool BlockDump { get; set; }

            public IReadOnlyList<IReadOnlyList<string>> Invocations => _invocations;

            public async Task<BackupToolResult> RunAsync(
                string executablePath,
                IReadOnlyList<string> arguments,
                IDictionary<string, string> environment,
                CancellationToken cancellationToken)
            {
                var list = arguments.ToList();
                lock (_invocations)
                {
                    _invocations.Add(list);
                }

                if (list.Contains("--version"))
                {
                    return Ok("pg_dump (PostgreSQL) 16.16");
                }

                var fileIndex = list.IndexOf("--file");
                if (fileIndex >= 0)
                {
                    File.WriteAllBytes(list[fileIndex + 1], Encoding.UTF8.GetBytes("fake pg_dump custom format"));
                    if (BlockDump)
                    {
                        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    }
                }

                return Ok(string.Empty);
            }

            private static BackupToolResult Ok(string output)
            {
                return new BackupToolResult { ExitCode = 0, StandardOutput = output, StandardError = string.Empty };
            }
        }
    }
}
