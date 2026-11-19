using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.System;
using SMBLibrary;
using SMBLibrary.Client;
using SMBLibrary.SMB2;
using FileAttributes = SMBLibrary.FileAttributes;
using OperatingSystem = MediaBrowser.Model.System.OperatingSystem;

namespace Emby.Server.Implementations.IO
{
    /// <summary>
    /// Provides the SMB file system surface backed by SMBLibrary's SMB2/SMB3 client.
    /// </summary>
    public class SmbFileSystem
    {
        // SMBLibrary 1.5.8.1 does not surface the negotiated dialect or a configurable port
        // through its public API. Both are only needed for diagnostics / loopback testing.
        private static readonly FieldInfo DialectField = typeof(SMB2Client).GetField("m_dialect", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ConnectWithPort = typeof(SMB2Client).GetMethod(
            "Connect",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(IPAddress), typeof(SMBTransportType), typeof(int) },
            null);

        private readonly OperatingSystem _operatingSystem;
        private readonly int _port;

        internal SMB2Dialect? NegotiatedDialect { get; private set; }

        public SmbFileSystem(OperatingSystem operatingSystem)
            : this(operatingSystem, SMB2Client.DirectTCPPort)
        {
        }

        internal SmbFileSystem(OperatingSystem operatingSystem, int port)
        {
            _operatingSystem = operatingSystem;
            _port = port;
        }

        public bool IsEnabledForPath(string path)
        {
            if (_operatingSystem == OperatingSystem.Windows)
            {
                return false;
            }

            return path.StartsWith("smb://", StringComparison.OrdinalIgnoreCase) || IsUncPath(path);
        }

        public string NormalizePath(string path)
        {
            if (path.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            if (IsUncPath(path))
            {
                return ConvertUncToSmb(path);
            }

            return path;
        }

        public string GetDirectoryName(string path)
        {
            var separator = GetDirectorySeparatorChar(path);
            var result = Path.GetDirectoryName(path);

            if (separator == '/')
            {
                result = result.Replace('\\', '/');

                if (result.StartsWith("smb:/", StringComparison.OrdinalIgnoreCase) && !result.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Replace("smb:/", "smb://");
                }
            }

            return result;
        }

        public char GetDirectorySeparatorChar(string path)
        {
            if (path.IndexOf('/') != -1)
            {
                return '/';
            }

            return '\\';
        }

        public FileSystemMetadata GetFileSystemInfo(string path)
        {
            return ToMetadata(Parse(path), null);
        }

        public FileSystemMetadata GetFileInfo(string path)
        {
            return ToMetadata(Parse(path), false);
        }

        public FileSystemMetadata GetDirectoryInfo(string path)
        {
            return ToMetadata(Parse(path), true);
        }

        public void SetHidden(string path, bool isHidden)
        {
            var parsed = Parse(path);
            var attributes = GetAttributes(parsed);
            SetAttributes(parsed, path, isHidden, (attributes & FileAttributes.ReadOnly) != 0);
        }

        public void SetReadOnly(string path, bool isReadOnly)
        {
            var parsed = Parse(path);
            var attributes = GetAttributes(parsed);
            SetAttributes(parsed, path, (attributes & FileAttributes.Hidden) != 0, isReadOnly);
        }

        public void SetAttributes(string path, bool isHidden, bool isReadOnly)
        {
            SetAttributes(Parse(path), path, isHidden, isReadOnly);
        }

        private void SetAttributes(SmbPath parsed, string path, bool isHidden, bool isReadOnly)
        {
            using (var session = CreateSession(parsed))
            {
                var handle = Open(session, parsed, AccessMask.GENERIC_READ | AccessMask.READ_CONTROL, CreateDisposition.FILE_OPEN, (CreateOptions)0);
                try
                {
                    var status = session.Store.GetFileInformation(out var information, handle, FileInformationClass.FileBasicInformation);
                    ThrowIfFailed(status, path, "query attributes");

                    var basic = (FileBasicInformation)information;
                    var attributes = basic.FileAttributes;
                    SetFlag(ref attributes, FileAttributes.Hidden, isHidden);
                    SetFlag(ref attributes, FileAttributes.ReadOnly, isReadOnly);

                    basic.FileAttributes = attributes;
                    basic.CreationTime = new SetFileTime(true);
                    basic.LastAccessTime = new SetFileTime(true);
                    basic.LastWriteTime = new SetFileTime(true);
                    basic.ChangeTime = new SetFileTime(true);

                    status = session.Store.SetFileInformation(handle, basic);
                    ThrowIfFailed(status, path, "set attributes");
                }
                finally
                {
                    session.Store.CloseFile(handle);
                }
            }
        }

        public void DeleteFile(string path)
        {
            var parsed = Parse(path);
            AssertExists(parsed, path);

            using (var session = CreateSession(parsed))
            {
                var handle = Open(session, parsed, AccessMask.DELETE | AccessMask.GENERIC_WRITE, CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE);
                try
                {
                    var status = session.Store.SetFileInformation(handle, new FileDispositionInformation { DeletePending = true });
                    ThrowIfFailed(status, path, "delete");
                }
                finally
                {
                    session.Store.CloseFile(handle);
                }
            }
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            var parsed = Parse(path);
            AssertDirectoryExists(parsed, path);

            if (recursive)
            {
                foreach (var child in GetFileSystemEntryPaths(path, true).OrderByDescending(i => i.Length))
                {
                    var childPath = Parse(child);
                    if (childPath.RelativePath == parsed.RelativePath)
                    {
                        continue;
                    }

                    Delete(childPath, child, CreateOptions.FILE_NON_DIRECTORY_FILE);
                }
            }

            Delete(parsed, path, CreateOptions.FILE_DIRECTORY_FILE);
        }

        public void CreateDirectory(string path)
        {
            var parsed = Parse(path);

            using (var session = CreateSession(parsed))
            {
                var handle = Open(session, parsed, AccessMask.GENERIC_WRITE, CreateDisposition.FILE_CREATE, CreateOptions.FILE_DIRECTORY_FILE);
                session.Store.CloseFile(handle);
            }
        }

        public string[] ReadAllLines(string path)
        {
            var lines = new List<string>();

            using (var stream = OpenRead(path))
            using (var reader = new StreamReader(stream))
            {
                while (!reader.EndOfStream)
                {
                    lines.Add(reader.ReadLine());
                }
            }

            return lines.ToArray();
        }

        public void WriteAllLines(string path, IEnumerable<string> lines)
        {
            using (var stream = GetFileStream(path, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.None))
            using (var writer = new StreamWriter(stream))
            {
                foreach (var line in lines)
                {
                    writer.WriteLine(line);
                }
            }
        }

        public Stream OpenRead(string path)
        {
            var parsed = Parse(path);
            AssertExists(parsed, path);

            return OpenForStream(parsed, FileAccessMode.Read, FileOpenMode.Open, FileShareMode.Read);
        }

        public void CopyFile(string source, string target, bool overwrite)
        {
            if (string.Equals(source, target, StringComparison.Ordinal))
            {
                throw new ArgumentException("Cannot CopyFile when source and target are the same");
            }

            var sourcePath = Parse(source);
            var targetPath = Parse(target);

            using (var input = OpenRead(source))
            using (var output = GetFileStream(target, overwrite ? FileOpenMode.Create : FileOpenMode.CreateNew, FileAccessMode.Write, FileShareMode.None))
            {
                input.CopyTo(output);
            }
        }

        public void MoveFile(string source, string target)
        {
            if (string.Equals(source, target, StringComparison.Ordinal))
            {
                throw new ArgumentException("Cannot MoveFile when source and target are the same");
            }

            var sourcePath = Parse(source);
            var targetPath = Parse(target);

            if (string.Equals(sourcePath.Host, targetPath.Host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(sourcePath.Share, targetPath.Share, StringComparison.OrdinalIgnoreCase))
            {
                // An SMB rename is atomic and keeps the file's metadata, unlike the old
                // copy-and-delete approach.
                AssertExists(sourcePath, source);

                using (var session = CreateSession(sourcePath))
                {
                    var handle = Open(session, sourcePath, AccessMask.DELETE | AccessMask.GENERIC_WRITE, CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE);
                    try
                    {
                        var status = session.Store.SetFileInformation(
                            handle,
                            new FileRenameInformationType2 { ReplaceIfExists = false, FileName = @"\" + targetPath.RelativePath });
                        ThrowIfFailed(status, source, "rename");
                    }
                    finally
                    {
                        session.Store.CloseFile(handle);
                    }
                }

                return;
            }

            CopyFile(source, target, false);
            DeleteFile(source);
        }

        public void MoveDirectory(string source, string target)
        {
            var sourcePath = Parse(source);
            var targetPath = Parse(target);

            if (!string.Equals(sourcePath.Host, targetPath.Host, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(sourcePath.Share, targetPath.Share, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotImplementedException();
            }

            AssertDirectoryExists(sourcePath, source);

            using (var session = CreateSession(sourcePath))
            {
                var handle = Open(session, sourcePath, AccessMask.DELETE | AccessMask.GENERIC_WRITE, CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE);
                try
                {
                    var status = session.Store.SetFileInformation(
                        handle,
                        new FileRenameInformationType2 { ReplaceIfExists = false, FileName = @"\" + targetPath.RelativePath });
                    ThrowIfFailed(status, source, "rename");
                }
                finally
                {
                    session.Store.CloseFile(handle);
                }
            }
        }

        public bool DirectoryExists(string path)
        {
            var parsed = Parse(path);
            var metadata = ToMetadata(parsed, true);
            return metadata.Exists && metadata.IsDirectory;
        }

        public bool FileExists(string path)
        {
            var parsed = Parse(path);
            var metadata = ToMetadata(parsed, false);
            return metadata.Exists && !metadata.IsDirectory;
        }

        public string ReadAllText(string path, Encoding encoding)
        {
            using (var stream = OpenRead(path))
            using (var reader = new StreamReader(stream, encoding))
            {
                return reader.ReadToEnd();
            }
        }

        public Stream GetFileStream(string path, FileOpenMode mode, FileAccessMode access, FileShareMode share)
        {
            return OpenForStream(Parse(path), access, mode, share);
        }

        public void WriteAllBytes(string path, byte[] bytes)
        {
            using (var stream = GetFileStream(path, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.None))
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        public void WriteAllText(string path, string text)
        {
            using (var stream = GetFileStream(path, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(text);
            }
        }

        public void WriteAllText(string path, string text, Encoding encoding)
        {
            using (var stream = GetFileStream(path, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.None))
            using (var writer = new StreamWriter(stream, encoding))
            {
                writer.Write(text);
            }
        }

        public string ReadAllText(string path)
        {
            using (var stream = OpenRead(path))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        public byte[] ReadAllBytes(string path)
        {
            using (var stream = OpenRead(path))
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public IEnumerable<FileSystemMetadata> GetDirectories(string path, bool recursive = false)
        {
            return ListEntries(path, recursive).Where(i => i.IsDirectory).ToList();
        }

        public IEnumerable<FileSystemMetadata> GetFiles(string path, string[] extensions, bool enableCaseSensitiveExtensions, bool recursive = false)
        {
            var comparison = enableCaseSensitiveExtensions ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

            return ListEntries(path, recursive)
                .Where(i => !i.IsDirectory && MatchesExtension(i.Extension, extensions, comparison))
                .ToList();
        }

        public IEnumerable<FileSystemMetadata> GetFileSystemEntries(string path, bool recursive = false)
        {
            return ListEntries(path, recursive);
        }

        public List<string> GetFileSystemEntryPaths(string path, bool recursive = false)
        {
            return ListEntries(path, recursive).Select(i => i.FullName).ToList();
        }

        public List<string> GetFilePaths(string path, string[] extensions, bool enableCaseSensitiveExtensions, bool recursive = false)
        {
            return GetFiles(path, extensions, enableCaseSensitiveExtensions, recursive).Select(i => i.FullName).ToList();
        }

        public List<string> GetDirectoryPaths(string path, bool recursive = false)
        {
            return GetDirectories(path, recursive).Select(i => i.FullName).ToList();
        }

        private static bool MatchesExtension(string extension, string[] extensions, StringComparer comparison)
        {
            return extensions == null || extensions.Length == 0 || extensions.Contains(extension ?? string.Empty, comparison);
        }

        private static bool IsUncPath(string path)
        {
            return path.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase);
        }

        private static string ConvertUncToSmb(string path)
        {
            if (IsUncPath(path))
            {
                path = path.Replace('\\', '/');
                path = "smb:" + path;
            }

            return path;
        }

        private static void SetFlag(ref FileAttributes attributes, FileAttributes flag, bool enabled)
        {
            if (enabled)
            {
                attributes |= flag;
            }
            else
            {
                attributes &= ~flag;
            }
        }

        private static SmbPath Parse(string path)
        {
            var normalized = ConvertUncToSmb(path);

            if (!normalized.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Invalid SMB path: " + path, nameof(path));
            }

            var rest = normalized.Substring("smb://".Length);
            var separatorIndex = rest.IndexOf('/');
            var authority = separatorIndex < 0 ? rest : rest.Substring(0, separatorIndex);
            var relative = separatorIndex < 0 ? string.Empty : rest.Substring(separatorIndex + 1);

            var domain = string.Empty;
            var user = string.Empty;
            var secret = string.Empty;
            var atIndex = authority.LastIndexOf('@');
            if (atIndex >= 0)
            {
                var userInfo = authority.Substring(0, atIndex);
                authority = authority.Substring(atIndex + 1);

                var semicolonIndex = userInfo.IndexOf(';');
                if (semicolonIndex >= 0)
                {
                    domain = userInfo.Substring(0, semicolonIndex);
                    userInfo = userInfo.Substring(semicolonIndex + 1);
                }

                var colonIndex = userInfo.IndexOf(':');
                if (colonIndex >= 0)
                {
                    user = userInfo.Substring(0, colonIndex);
                    secret = userInfo.Substring(colonIndex + 1);
                }
                else
                {
                    user = userInfo;
                }
            }

            var portIndex = authority.LastIndexOf(':');
            var port = SMB2Client.DirectTCPPort;
            var host = authority;
            if (portIndex > 0 && int.TryParse(authority.Substring(portIndex + 1), out var parsedPort))
            {
                host = authority.Substring(0, portIndex);
                port = parsedPort;
            }

            var segments = relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var share = segments.Length == 0 ? string.Empty : segments[0];
            var shareRelative = segments.Length <= 1 ? string.Empty : string.Join(@"\", segments.Skip(1)).TrimEnd('\\');

            var canonical = "smb://" + authority + "/" + string.Join("/", segments);
            return new SmbPath(host, port, domain, user, secret, share, shareRelative, canonical.TrimEnd('/'));
        }

        private SmbSession CreateSession(SmbPath path)
        {
            var client = new SMB2Client();

            bool connected;
            if (path.Port == SMB2Client.DirectTCPPort)
            {
                connected = client.Connect(path.Host, SMBTransportType.DirectTCPTransport);
            }
            else if (ConnectWithPort != null)
            {
                connected = (bool)ConnectWithPort.Invoke(client, new object[] { IPAddress.Parse(path.Host), SMBTransportType.DirectTCPTransport, path.Port });
            }
            else
            {
                throw new IOException("SMBLibrary does not support connecting to a non-default port.");
            }

            if (!connected)
            {
                client.Disconnect();
                throw new IOException("Unable to connect to SMB host " + path.Host);
            }

            if (DialectField != null && DialectField.GetValue(client) is SMB2Dialect dialect)
            {
                NegotiatedDialect = dialect;
            }

            var status = client.Login(path.Domain, path.User, path.Password);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                client.Disconnect();
                ThrowIfFailed(status, path.CanonicalPath, "login");
            }

            var store = client.TreeConnect(@"\\" + path.Host + @"\" + path.Share, out status);
            if (store == null || status != NTStatus.STATUS_SUCCESS)
            {
                client.Disconnect();
                ThrowIfFailed(status, path.CanonicalPath, "tree connect");
            }

            return new SmbSession(client, store);
        }

        private static object Open(SmbSession session, SmbPath path, AccessMask access, CreateDisposition disposition, CreateOptions options)
        {
            var status = session.Store.CreateFile(
                out var handle,
                out _,
                path.RelativePath,
                access,
                FileAttributes.Normal,
                ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                disposition,
                options,
                null);

            ThrowIfFailed(status, path.CanonicalPath, "open");
            return handle;
        }

        private SmbFileStream OpenForStream(SmbPath path, FileAccessMode access, FileOpenMode mode, FileShareMode share)
        {
            var streamSession = CreateSession(path);

            var disposition = mode switch
            {
                FileOpenMode.CreateNew => CreateDisposition.FILE_CREATE,
                FileOpenMode.Create => CreateDisposition.FILE_OVERWRITE_IF,
                FileOpenMode.OpenOrCreate => CreateDisposition.FILE_OPEN_IF,
                _ => CreateDisposition.FILE_OPEN
            };

            var desiredAccess = access == FileAccessMode.Read
                ? AccessMask.GENERIC_READ
                : AccessMask.GENERIC_WRITE | AccessMask.DELETE;

            var options = CreateOptions.FILE_NON_DIRECTORY_FILE;
            var status = streamSession.Store.CreateFile(
                out var handle,
                out _,
                path.RelativePath,
                desiredAccess,
                FileAttributes.Normal,
                ToShareAccess(share),
                disposition,
                options,
                null);

            if (status != NTStatus.STATUS_SUCCESS)
            {
                streamSession.Dispose();
                ThrowIfFailed(status, path.CanonicalPath, "open");
            }

            return new SmbFileStream(streamSession, handle, access == FileAccessMode.Read, access == FileAccessMode.Write);
        }

        private static ShareAccess ToShareAccess(FileShareMode share)
        {
            return share switch
            {
                FileShareMode.Read => ShareAccess.Read,
                FileShareMode.Write => ShareAccess.Write,
                FileShareMode.ReadWrite => ShareAccess.Read | ShareAccess.Write,
                FileShareMode.ReadWriteDelete => ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                _ => ShareAccess.None
            };
        }

        private FileSystemMetadata ToMetadata(SmbPath path, bool? isDirectory)
        {
            var result = new FileSystemMetadata
            {
                FullName = path.CanonicalPath,
                Name = path.Name,
                Extension = Path.GetExtension(path.CanonicalPath),
                IsDirectory = isDirectory ?? false
            };

            using (var session = CreateSession(path))
            {
                var status = session.Store.CreateFile(
                    out var handle,
                    out _,
                    path.RelativePath,
                    AccessMask.GENERIC_READ,
                    FileAttributes.Normal,
                    ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                    CreateDisposition.FILE_OPEN,
                    (CreateOptions)0,
                    null);

                if (status != NTStatus.STATUS_SUCCESS)
                {
                    if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND
                        || status == NTStatus.STATUS_OBJECT_PATH_NOT_FOUND
                        || status == NTStatus.STATUS_NO_SUCH_FILE)
                    {
                        return result;
                    }

                    ThrowIfFailed(status, path.CanonicalPath, "get file information");
                }

                try
                {
                    status = session.Store.GetFileInformation(out var information, handle, FileInformationClass.FileNetworkOpenInformation);
                    ThrowIfFailed(status, path.CanonicalPath, "get file information");

                    var info = (FileNetworkOpenInformation)information;
                    result.Exists = true;
                    result.IsDirectory = info.IsDirectory;
                    result.Length = info.IsDirectory ? 0 : info.EndOfFile;
                    result.DirectoryName = path.DirectoryPath;
                    result.CreationTimeUtc = info.CreationTime ?? DateTime.MinValue;
                    result.LastWriteTimeUtc = info.LastWriteTime ?? DateTime.MinValue;
                }
                finally
                {
                    session.Store.CloseFile(handle);
                }
            }

            return result;
        }

        private FileAttributes GetAttributes(SmbPath path)
        {
            using (var session = CreateSession(path))
            {
                var handle = Open(session, path, AccessMask.GENERIC_READ, CreateDisposition.FILE_OPEN, (CreateOptions)0);
                try
                {
                    var status = session.Store.GetFileInformation(out var information, handle, FileInformationClass.FileBasicInformation);
                    ThrowIfFailed(status, path.CanonicalPath, "get attributes");
                    return ((FileBasicInformation)information).FileAttributes;
                }
                finally
                {
                    session.Store.CloseFile(handle);
                }
            }
        }

        private void AssertExists(SmbPath path, string originalPath)
        {
            if (!ToMetadata(path, false).Exists)
            {
                throw new FileNotFoundException("File not found.", originalPath);
            }
        }

        private void AssertDirectoryExists(SmbPath path, string originalPath)
        {
            if (!ToMetadata(path, true).Exists)
            {
                throw new FileNotFoundException("File not found.", originalPath);
            }
        }

        private void Delete(SmbPath path, string originalPath, CreateOptions options)
        {
            using (var session = CreateSession(path))
            {
                var handle = Open(session, path, AccessMask.DELETE | AccessMask.GENERIC_WRITE, CreateDisposition.FILE_OPEN, options);
                try
                {
                    var status = session.Store.SetFileInformation(handle, new FileDispositionInformation { DeletePending = true });
                    ThrowIfFailed(status, originalPath, "delete");
                }
                finally
                {
                    session.Store.CloseFile(handle);
                }
            }
        }

        private List<FileSystemMetadata> ListEntries(string path, bool recursive)
        {
            var parsed = Parse(path);
            AssertDirectoryExists(parsed, path);

            var result = new List<FileSystemMetadata>();

            using (var session = CreateSession(parsed))
            {
                CollectEntries(session, parsed, result, recursive);
            }

            return result;
        }

        private void CollectEntries(SmbSession session, SmbPath directory, List<FileSystemMetadata> result, bool recursive)
        {
            var handle = Open(session, directory, AccessMask.GENERIC_READ, CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE);
            var children = new List<SmbPath>();

            try
            {
                var status = session.Store.QueryDirectory(out var entries, handle, "*", FileInformationClass.FileFullDirectoryInformation);
                if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES && status != NTStatus.STATUS_NO_SUCH_FILE)
                {
                    ThrowIfFailed(status, directory.CanonicalPath, "enumerate directory");
                }

                foreach (var entry in entries ?? new List<QueryDirectoryFileInformation>())
                {
                    if (!(entry is FileFullDirectoryInformation info))
                    {
                        continue;
                    }

                    var child = directory.CreateChild(info.FileName);
                    var metadata = new FileSystemMetadata
                    {
                        Exists = true,
                        FullName = child.CanonicalPath,
                        Name = child.Name,
                        Extension = Path.GetExtension(child.CanonicalPath),
                        DirectoryName = directory.CanonicalPath,
                        IsDirectory = (info.FileAttributes & FileAttributes.Directory) != 0,
                        Length = info.EndOfFile,
                        CreationTimeUtc = info.CreationTime,
                        LastWriteTimeUtc = info.LastWriteTime
                    };

                    result.Add(metadata);
                    children.Add(child);
                }
            }
            finally
            {
                session.Store.CloseFile(handle);
            }

            if (recursive)
            {
                foreach (var child in children.Where(i => result.Any(r => r.FullName == i.CanonicalPath && r.IsDirectory)))
                {
                    CollectEntries(session, child, result, true);
                }
            }
        }

        private static void ThrowIfFailed(NTStatus status, string path, string operation)
        {
            if (status == NTStatus.STATUS_SUCCESS)
            {
                return;
            }

            if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND
                || status == NTStatus.STATUS_OBJECT_PATH_NOT_FOUND
                || status == NTStatus.STATUS_NO_SUCH_FILE)
            {
                throw new FileNotFoundException("File not found: " + path);
            }

            if (status == NTStatus.STATUS_ACCESS_DENIED)
            {
                throw new UnauthorizedAccessException("Access denied: " + path);
            }

            if (status == NTStatus.STATUS_OBJECT_NAME_COLLISION)
            {
                throw new IOException("Already exists: " + path);
            }

            throw new IOException("SMB " + operation + " failed for '" + path + "' with status " + status + ".");
        }

        private sealed class SmbPath
        {
            public SmbPath(string host, int port, string domain, string user, string secret, string share, string relativePath, string canonicalPath)
            {
                Host = host;
                Port = port;
                Domain = domain;
                User = user;
                Password = secret;
                Share = share;
                RelativePath = relativePath;
                CanonicalPath = canonicalPath;
            }

            public string Host { get; }
            public int Port { get; }
            public string Domain { get; }
            public string User { get; }
            public string Password { get; }
            public string Share { get; }
            public string RelativePath { get; }
            public string CanonicalPath { get; }
            public string Name => RelativePath.Length == 0 ? Share : RelativePath.Split('\\').Last();
            public string DirectoryPath
            {
                get
                {
                    var index = CanonicalPath.LastIndexOf('/');
                    return index < 0 ? CanonicalPath : CanonicalPath.Substring(0, index);
                }
            }

            public SmbPath CreateChild(string name)
            {
                var relative = RelativePath.Length == 0 ? name : RelativePath + @"\" + name;
                var canonical = CanonicalPath.TrimEnd('/') + "/" + name.Replace('\\', '/');
                return new SmbPath(Host, Port, Domain, User, Password, Share, relative, canonical);
            }
        }

        private sealed class SmbSession : IDisposable
        {
            private readonly SMB2Client _client;

            public SmbSession(SMB2Client client, ISMBFileStore store)
            {
                _client = client;
                Store = store;
            }

            public ISMBFileStore Store { get; }

            public void Dispose()
            {
                try
                {
                    Store.Disconnect();
                    _client.Logoff();
                }
                finally
                {
                    _client.Disconnect();
                }
            }
        }

        private sealed class SmbFileStream : Stream
        {
            private readonly SmbSession _session;
            private readonly object _handle;
            private readonly bool _canRead;
            private readonly bool _canWrite;
            private long _position;
            private long _length;
            private bool _disposed;

            public SmbFileStream(SmbSession session, object handle, bool canRead, bool canWrite)
            {
                _session = session;
                _handle = handle;
                _canRead = canRead;
                _canWrite = canWrite;
            }

            public override bool CanRead => !_disposed && _canRead;
            public override bool CanWrite => !_disposed && _canWrite;
            public override bool CanSeek => !_disposed;

            public override long Length
            {
                get
                {
                    EnsureOpen();
                    var status = _session.Store.GetFileInformation(out var information, _handle, FileInformationClass.FileStandardInformation);
                    ThrowIfFailed(status, "stream", "get length");
                    _length = ((FileStandardInformation)information).EndOfFile;
                    return _length;
                }
            }

            public override long Position
            {
                get
                {
                    EnsureOpen();
                    return _position;
                }
                set
                {
                    EnsureOpen();
                    if (value < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value));
                    }

                    _position = value;
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                EnsureOpen();
                if (!_canRead)
                {
                    throw new NotSupportedException("Stream was opened for writing.");
                }

                ValidateBufferArguments(buffer, offset, count);
                if (count == 0 || _position >= Length)
                {
                    return 0;
                }

                var toRead = (int)Math.Min(count, Math.Min(_session.Store.MaxReadSize, int.MaxValue));
                var status = _session.Store.ReadFile(out var data, _handle, _position, toRead);
                ThrowIfFailed(status, "stream", "read");

                if (data == null || data.Length == 0)
                {
                    return 0;
                }

                Array.Copy(data, 0, buffer, offset, data.Length);
                _position += data.Length;
                return data.Length;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                EnsureOpen();
                if (!_canWrite)
                {
                    throw new NotSupportedException("Stream was opened for reading.");
                }

                ValidateBufferArguments(buffer, offset, count);

                var maxWrite = (int)Math.Min(_session.Store.MaxWriteSize, int.MaxValue);
                var written = 0;
                while (written < count)
                {
                    var chunk = Math.Min(maxWrite, count - written);
                    var data = new byte[chunk];
                    Array.Copy(buffer, offset + written, data, 0, chunk);

                    var status = _session.Store.WriteFile(out var countWritten, _handle, _position + written, data);
                    ThrowIfFailed(status, "stream", "write");

                    written += countWritten;
                    if (countWritten == 0)
                    {
                        throw new IOException("SMB write made no progress.");
                    }
                }

                _position += written;
                _length = Math.Max(_length, _position);
            }

            public override void Flush()
            {
                EnsureOpen();
                var status = _session.Store.FlushFileBuffers(_handle);
                ThrowIfFailed(status, "stream", "flush");
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                EnsureOpen();
                var reference = origin switch
                {
                    SeekOrigin.Begin => 0,
                    SeekOrigin.Current => _position,
                    SeekOrigin.End => Length,
                    _ => throw new ArgumentOutOfRangeException(nameof(origin))
                };

                var position = reference + offset;
                if (position < 0)
                {
                    throw new IOException("An attempt was made to move the position before the beginning of the stream.");
                }

                _position = position;
                return _position;
            }

            public override void SetLength(long value)
            {
                EnsureOpen();
                var status = _session.Store.SetFileInformation(_handle, new FileEndOfFileInformation { EndOfFile = value });
                ThrowIfFailed(status, "stream", "set length");
                _length = value;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_disposed)
                {
                    _disposed = true;
                    try
                    {
                        _session.Store.CloseFile(_handle);
                    }
                    finally
                    {
                        _session.Dispose();
                    }
                }

                base.Dispose(disposing);
            }

            private void EnsureOpen()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(SmbFileStream));
                }
            }
        }
    }
}
