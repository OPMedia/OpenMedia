using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Emby.Server.Implementations.IO;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SMBLibrary;
using SMBLibrary.Authentication.GSSAPI;
using SMBLibrary.Authentication.NTLM;
using SMBLibrary.Client;
using SMBLibrary.Server;
using SMBLibrary.SMB2;
using OperatingSystem = MediaBrowser.Model.System.OperatingSystem;

namespace MediaBrowser.Tests.IO
{
    internal sealed class LoopbackSmbServer : SMBServer
    {
        public LoopbackSmbServer(SMBShareCollection shares, GSSProvider security)
            : base(shares, security)
        {
        }

        public void Start(IPAddress address, SMBTransportType transport, int port, bool smb1, bool smb2, bool smb3, TimeSpan? timeout)
        {
            base.Start(address, transport, port, smb1, smb2, smb3, timeout);
        }

        /// <summary>
        /// SMBLibrary 1.5.8.1 的 ConnectionManager.ReleaseConnection 在「连接刚关闭、
        /// 仍在释放流程中」时会对已清理的 state 解引用并抛 NullReferenceException，
        /// 导致 Stop() 失败。这是库内部问题，与我们的代码无关；这里吞掉该异常并把
        /// 它记为已知库缺陷，避免单个测试的清理阶段掩盖真正的失败。
        /// </summary>
        public new void Stop()
        {
            try
            {
                base.Stop();
            }
            catch (NullReferenceException)
            {
                // 已知库缺陷：见上方注释
            }
        }
    }

    [TestClass]
    public class SmbFileSystemTests
    {
        private const int Port = 4455;
        private const string UserName = "smbtest";
        private const string Password = "password";
        private const string ShareName = "Media";

        private string _root;
        private LoopbackSmbServer _server;
        private SmbFileSystem _fileSystem;
        private string _shareUrl;

        [TestInitialize]
        public void Initialize()
        {
            _root = Path.Combine(Path.GetTempPath(), "openmedia-smb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            File.WriteAllBytes(Path.Combine(_root, "movie.mkv"), new byte[] { 1, 2, 3, 4, 5 });
            Directory.CreateDirectory(Path.Combine(_root, "Series"));
            File.WriteAllText(Path.Combine(_root, "Series", "episode.mkv"), "episode");

            var shares = new SMBShareCollection();
            shares.Add(new FileSystemShare(ShareName, new LoopbackSmbFileStore(_root)));

            var security = new GSSProvider(
                new IndependentNTLMAuthenticationProvider(user => string.Equals(user, UserName, StringComparison.Ordinal) ? Password : null));
            _server = new LoopbackSmbServer(shares, security);
            _server.Start(IPAddress.Loopback, SMBTransportType.DirectTCPTransport, Port, false, true, true, null);

            _fileSystem = new SmbFileSystem(OperatingSystem.Linux, Port);
            _shareUrl = Url("");
        }

        [TestCleanup]
        public void Cleanup()
        {
            _server?.Stop();
            if (_root != null && Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [TestMethod]
        [DataRow("smb://host/share/path", true)]
        [DataRow("SMB://host/share/path", true)]
        [DataRow("\\\\host\\share\\path", true)]
        [DataRow("/tmp/movie.mkv", false)]
        [DataRow("smb:/host/share/path", false)]
        public void IsEnabledForPath_MatchesSmbAndUncPaths(string path, bool expected)
        {
            Assert.AreEqual(expected, _fileSystem.IsEnabledForPath(path));
        }

        [TestMethod]
        public void NegotiatedDialect_IsSmb2OrHigher()
        {
            Assert.IsTrue(_fileSystem.GetFileSystemEntries(_shareUrl).Any(), "The SMB enumeration did not return any entries.");

            var dialect = _fileSystem.NegotiatedDialect;
            Assert.IsTrue(dialect.HasValue, "SmbFileSystem did not record a negotiated dialect.");
            Assert.IsTrue(dialect.Value >= SMB2Dialect.SMB202, "Negotiated dialect was " + dialect.Value + ", expected SMB2 or higher.");
        }

        [TestMethod]
        public void EnumerateDirectory_ReturnsFilesAndSubdirectories()
        {
            var entries = _fileSystem.GetFileSystemEntries(_shareUrl).ToList();
            Assert.AreEqual(2, entries.Count);
            Assert.IsTrue(entries.Any(i => i.Name == "movie.mkv" && !i.IsDirectory));
            Assert.IsTrue(entries.Any(i => i.Name == "Series" && i.IsDirectory));

            var files = _fileSystem.GetFiles(_shareUrl, new[] { ".MKV" }, false).ToList();
            Assert.AreEqual(1, files.Count);
            Assert.AreEqual("movie.mkv", files[0].Name);

            var directories = _fileSystem.GetDirectories(_shareUrl).ToList();
            Assert.AreEqual(1, directories.Count);
            Assert.AreEqual("Series", directories[0].Name);

            var allPaths = _fileSystem.GetFileSystemEntryPaths(_shareUrl, true);
            CollectionAssert.AreEquivalent(
                new[] { CanonicalUrl("movie.mkv"), CanonicalUrl("Series"), CanonicalUrl("Series/episode.mkv") },
                allPaths);
        }

        [TestMethod]
        public void ReadFile_ReturnsBytesAndMetadata()
        {
            var path = Url("movie.mkv");
            var info = _fileSystem.GetFileInfo(path);

            Assert.IsTrue(info.Exists);
            Assert.IsFalse(info.IsDirectory);
            Assert.AreEqual(5L, info.Length);
            Assert.AreEqual("movie.mkv", info.Name);
            Assert.AreEqual(".mkv", info.Extension);
            Assert.AreEqual(CanonicalUrl("").TrimEnd('/'), info.DirectoryName);
            Assert.AreEqual(
                File.GetLastWriteTimeUtc(Path.Combine(_root, "movie.mkv")),
                info.LastWriteTimeUtc);

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, _fileSystem.ReadAllBytes(path));

            using (var stream = _fileSystem.OpenRead(path))
            {
                var buffer = new byte[3];
                Assert.AreEqual(3, stream.Read(buffer, 0, 3));
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, buffer);
                Assert.AreEqual(5, stream.Length);
            }
        }

        [TestMethod]
        public void WriteNewFile_CanBeReadBack()
        {
            var path = Url("new-file.txt");
            var bytes = Encoding.UTF8.GetBytes("hello smb");

            _fileSystem.WriteAllBytes(path, bytes);

            Assert.IsTrue(_fileSystem.FileExists(path));
            CollectionAssert.AreEqual(bytes, _fileSystem.ReadAllBytes(path));
            Assert.AreEqual("hello smb", _fileSystem.ReadAllText(path));
            Assert.AreEqual(bytes.Length, _fileSystem.GetFileSystemInfo(path).Length);
        }

        [TestMethod]
        public void DeleteFile_RemovesItFromTheShare()
        {
            var path = Url("movie.mkv");
            Assert.IsTrue(_fileSystem.FileExists(path));

            _fileSystem.DeleteFile(path);

            Assert.IsFalse(_fileSystem.FileExists(path));
            Assert.IsFalse(File.Exists(Path.Combine(_root, "movie.mkv")));
        }

        [TestMethod]
        public void CreateDirectoryAndMoveFile_WorkOverSmb()
        {
            var directory = Url("NewFolder");
            _fileSystem.CreateDirectory(directory);
            Assert.IsTrue(_fileSystem.DirectoryExists(directory));

            var target = Url("NewFolder/movie.mkv");
            _fileSystem.MoveFile(Url("movie.mkv"), target);

            Assert.IsFalse(_fileSystem.FileExists(Url("movie.mkv")));
            Assert.IsTrue(_fileSystem.FileExists(target));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, _fileSystem.ReadAllBytes(target));
        }

        /// <summary>
        /// 输入路径：凭据写在 URL 里（这是用户配置媒体库路径时的真实形态）。
        /// </summary>
        private static string Url(string relativePath)
        {
            var prefix = "smb://" + UserName + ":" + Password + "@127.0.0.1:" + Port + "/" + ShareName;
            return relativePath.Length == 0 ? prefix + "/" : prefix + "/" + relativePath;
        }

        /// <summary>
        /// 输出路径：与旧实现 SharpCifs 的 GetCanonicalPath 一致 ——
        /// "smb://" + Url.Authority + 路径，**不含 userinfo**（凭据不进返回值）。
        /// </summary>
        private static string CanonicalUrl(string relativePath)
        {
            var prefix = "smb://127.0.0.1:" + Port + "/" + ShareName;
            return relativePath.Length == 0 ? prefix + "/" : prefix + "/" + relativePath;
        }
    }
}
