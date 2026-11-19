using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMBLibrary;
using FileAttributes = SMBLibrary.FileAttributes;

namespace MediaBrowser.Tests.IO
{
    /// <summary>
    /// Minimal INTFileStore backed by a local directory. SMBLibrary's server assembly exposes
    /// the server implementation but no cross-platform disk-backed store, so the in-process
    /// test server needs this adapter.
    /// </summary>
    internal sealed class LoopbackSmbFileStore : INTFileStore
    {
        private readonly string _root;

        public LoopbackSmbFileStore(string root)
        {
            _root = Path.GetFullPath(root);
        }

        public NTStatus CreateFile(
            out object handle,
            out FileStatus fileStatus,
            string path,
            AccessMask desiredAccess,
            FileAttributes fileAttributes,
            ShareAccess shareAccess,
            CreateDisposition createDisposition,
            CreateOptions createOptions,
            SecurityContext securityContext)
        {
            handle = null;
            fileStatus = FileStatus.FILE_DOES_NOT_EXIST;

            var relative = NormalizeRelativePath(path);
            if (!TryGetFullPath(relative, out var fullPath))
            {
                return NTStatus.STATUS_ACCESS_DENIED;
            }

            var directoryExists = Directory.Exists(fullPath);
            var isDirectory = (createOptions & CreateOptions.FILE_DIRECTORY_FILE) != 0 || relative.Length == 0 || directoryExists;
            var exists = directoryExists || File.Exists(fullPath);

            switch (createDisposition)
            {
                case CreateDisposition.FILE_CREATE:
                    if (exists)
                    {
                        return NTStatus.STATUS_OBJECT_NAME_COLLISION;
                    }

                    break;
                case CreateDisposition.FILE_OPEN:
                    if (!exists)
                    {
                        return NTStatus.STATUS_OBJECT_NAME_NOT_FOUND;
                    }

                    break;
                case CreateDisposition.FILE_OPEN_IF:
                    break;
                case CreateDisposition.FILE_OVERWRITE:
                    if (!exists)
                    {
                        return NTStatus.STATUS_OBJECT_NAME_NOT_FOUND;
                    }

                    break;
                case CreateDisposition.FILE_SUPERSEDE:
                case CreateDisposition.FILE_OVERWRITE_IF:
                    break;
            }

            var status = NTStatus.STATUS_SUCCESS;
            var created = false;
            var overwritten = false;

            if (isDirectory)
            {
                if (!exists)
                {
                    if (createDisposition == CreateDisposition.FILE_OPEN)
                    {
                        return NTStatus.STATUS_OBJECT_PATH_NOT_FOUND;
                    }

                    try
                    {
                        Directory.CreateDirectory(fullPath);
                        created = true;
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        return NTStatus.STATUS_ACCESS_DENIED;
                    }
                }
            }
            else
            {
                FileStream stream;
                var mode = !exists
                        ? FileMode.CreateNew
                        : createDisposition == CreateDisposition.FILE_OVERWRITE
                            || createDisposition == CreateDisposition.FILE_OVERWRITE_IF
                            || createDisposition == CreateDisposition.FILE_SUPERSEDE
                            ? FileMode.Create
                            : FileMode.Open;
                try
                {
                    if (!exists && !Directory.Exists(Path.GetDirectoryName(fullPath)))
                    {
                        return NTStatus.STATUS_OBJECT_PATH_NOT_FOUND;
                    }

                    stream = new FileStream(fullPath, mode, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                }
                catch (FileNotFoundException)
                {
                    return NTStatus.STATUS_OBJECT_NAME_NOT_FOUND;
                }
                catch (DirectoryNotFoundException)
                {
                    return NTStatus.STATUS_OBJECT_PATH_NOT_FOUND;
                }
                catch (IOException e) when (e.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    return NTStatus.STATUS_OBJECT_NAME_COLLISION;
                }
                catch (UnauthorizedAccessException)
                {
                    return NTStatus.STATUS_ACCESS_DENIED;
                }

                created = mode == FileMode.CreateNew;
                overwritten = mode == FileMode.Create;
                handle = new Handle(relative, fullPath, false, stream);
            }

            if (isDirectory)
            {
                handle = new Handle(relative, fullPath, true, null);
            }

            fileStatus = created
                ? FileStatus.FILE_CREATED
                : overwritten
                    ? FileStatus.FILE_OVERWRITTEN
                    : FileStatus.FILE_OPENED;
            return status;
        }

        public NTStatus CloseFile(object handle)
        {
            var file = GetHandle(handle, out var status);
            if (file == null)
            {
                return status;
            }

            file.Stream?.Dispose();

            if (file.DeleteOnClose)
            {
                try
                {
                    if (file.IsDirectory)
                    {
                        Directory.Delete(file.FullPath, false);
                    }
                    else if (File.Exists(file.FullPath))
                    {
                        File.Delete(file.FullPath);
                    }
                }
                catch (DirectoryNotFoundException)
                {
                }
                catch (FileNotFoundException)
                {
                }
            }

            return NTStatus.STATUS_SUCCESS;
        }

        public NTStatus ReadFile(out byte[] data, object handle, long offset, int maxCount)
        {
            data = null;
            var file = GetHandle(handle, out var status);
            if (file == null)
            {
                return status;
            }

            if (file.Stream == null)
            {
                return NTStatus.STATUS_INVALID_DEVICE_REQUEST;
            }

            try
            {
                file.Stream.Position = offset;
                var buffer = new byte[maxCount];
                var read = 0;
                while (read < maxCount)
                {
                    var count = file.Stream.Read(buffer, read, maxCount - read);
                    if (count == 0)
                    {
                        break;
                    }

                    read += count;
                }

                data = read == buffer.Length ? buffer : buffer.Take(read).ToArray();
                return NTStatus.STATUS_SUCCESS;
            }
            catch (IOException)
            {
                return NTStatus.STATUS_INVALID_DEVICE_REQUEST;
            }
        }

        public NTStatus WriteFile(out int numberOfBytesWritten, object handle, long offset, byte[] data)
        {
            numberOfBytesWritten = 0;
            var file = GetHandle(handle, out var status);
            if (file == null)
            {
                return status;
            }

            if (file.Stream == null)
            {
                return NTStatus.STATUS_INVALID_DEVICE_REQUEST;
            }

            try
            {
                file.Stream.Position = offset;
                file.Stream.Write(data, 0, data.Length);
                numberOfBytesWritten = data.Length;
                return NTStatus.STATUS_SUCCESS;
            }
            catch (IOException)
            {
                return NTStatus.STATUS_IO_TIMEOUT;
            }
        }

        public NTStatus FlushFileBuffers(object handle)
        {
            var file = GetHandle(handle, out var status);
            if (file == null)
            {
                return status;
            }

            file.Stream?.Flush();
            return NTStatus.STATUS_SUCCESS;
        }

        public NTStatus QueryDirectory(
            out List<QueryDirectoryFileInformation> result,
            object handle,
            string fileName,
            FileInformationClass informationClass)
        {
            result = new List<QueryDirectoryFileInformation>();
            var file = GetHandle(handle, out var status);
            if (file == null)
            {
                return status;
            }

            if (!file.IsDirectory || !Directory.Exists(file.FullPath))
            {
                return NTStatus.STATUS_INVALID_DEVICE_REQUEST;
            }

            if (informationClass != FileInformationClass.FileFullDirectoryInformation)
            {
                return NTStatus.STATUS_INVALID_INFO_CLASS;
            }

            var pattern = string.IsNullOrEmpty(fileName) || fileName == "*" ? "*" : fileName;
            var index = 0u;
            foreach (var entry in Directory.GetFileSystemEntries(file.FullPath).OrderBy(i => i, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(entry);
                if (!Matches(name, pattern))
                {
                    continue;
                }

                var info = File.GetAttributes(entry);
                var isDirectory = (info & System.IO.FileAttributes.Directory) != 0;
                var length = isDirectory ? 0 : new FileInfo(entry).Length;

                result.Add(new FileFullDirectoryInformation
                {
                    FileIndex = index++,
                    FileName = name,
                    CreationTime = File.GetCreationTimeUtc(entry),
                    LastAccessTime = File.GetLastAccessTimeUtc(entry),
                    LastWriteTime = File.GetLastWriteTimeUtc(entry),
                    ChangeTime = File.GetLastWriteTimeUtc(entry),
                    EndOfFile = length,
                    AllocationSize = length,
                    FileAttributes = ToSmbAttributes(info),
                    EaSize = 0
                });
            }

            return NTStatus.STATUS_SUCCESS;
        }

        public NTStatus GetFileInformation(out FileInformation result, object handle, FileInformationClass informationClass)
        {
            result = null;
            var file = GetHandle(handle, out var status);
            if (file == null)
            {
                return status;
            }

            var attributes = GetAttributes(file.FullPath, out status);
            if (attributes == null)
            {
                return status;
            }

            var isDirectory = file.IsDirectory;
            var length = isDirectory ? 0 : new FileInfo(file.FullPath).Length;
            var creation = File.GetCreationTimeUtc(file.FullPath);
            var lastAccess = File.GetLastAccessTimeUtc(file.FullPath);
            var lastWrite = File.GetLastWriteTimeUtc(file.FullPath);

            switch (informationClass)
            {
                case FileInformationClass.FileNetworkOpenInformation:
                    result = new FileNetworkOpenInformation
                    {
                        CreationTime = creation,
                        LastAccessTime = lastAccess,
                        LastWriteTime = lastWrite,
                        ChangeTime = lastWrite,
                        EndOfFile = length,
                        AllocationSize = length,
                        FileAttributes = attributes.Value,
                        IsDirectory = isDirectory
                    };
                    return NTStatus.STATUS_SUCCESS;
                case FileInformationClass.FileBasicInformation:
                    result = new FileBasicInformation
                    {
                        CreationTime = creation,
                        LastAccessTime = lastAccess,
                        LastWriteTime = lastWrite,
                        ChangeTime = lastWrite,
                        FileAttributes = attributes.Value
                    };
                    return NTStatus.STATUS_SUCCESS;
                case FileInformationClass.FileStandardInformation:
                    result = new FileStandardInformation
                    {
                        EndOfFile = length,
                        AllocationSize = length,
                        NumberOfLinks = 1,
                        Directory = isDirectory
                    };
                    return NTStatus.STATUS_SUCCESS;
                default:
                    return NTStatus.STATUS_INVALID_INFO_CLASS;
            }
        }

        public NTStatus SetFileInformation(object handle, FileInformation information)
        {
            var file = GetHandle(handle, out var status);
            if (file == null)
            {
                return status;
            }

            switch (information)
            {
                case FileDispositionInformation disposition:
                    file.DeleteOnClose = disposition.DeletePending;
                    return NTStatus.STATUS_SUCCESS;
                case FileEndOfFileInformation endOfFile:
                    if (file.Stream == null)
                    {
                        return NTStatus.STATUS_INVALID_DEVICE_REQUEST;
                    }

                    file.Stream.SetLength(endOfFile.EndOfFile);
                    return NTStatus.STATUS_SUCCESS;
                case FileBasicInformation basic:
                    ApplyTimes(file.FullPath, basic);
                    ApplyAttributes(file, basic.FileAttributes);
                    return NTStatus.STATUS_SUCCESS;
                case FileRenameInformationType2 rename:
                    return Rename(file, rename.FileName);
                default:
                    return NTStatus.STATUS_NOT_IMPLEMENTED;
            }
        }

        public NTStatus GetFileSystemInformation(out FileSystemInformation result, FileSystemInformationClass informationClass)
        {
            result = null;
            return NTStatus.STATUS_NOT_IMPLEMENTED;
        }

        public NTStatus SetFileSystemInformation(FileSystemInformation information)
        {
            return NTStatus.STATUS_NOT_IMPLEMENTED;
        }

        public NTStatus GetSecurityInformation(out SecurityDescriptor result, object handle, SecurityInformation securityInformation)
        {
            result = null;
            return NTStatus.STATUS_NOT_IMPLEMENTED;
        }

        public NTStatus SetSecurityInformation(object handle, SecurityInformation securityInformation, SecurityDescriptor securityDescriptor)
        {
            return NTStatus.STATUS_NOT_IMPLEMENTED;
        }

        public NTStatus LockFile(object handle, long byteOffset, long length, bool exclusiveLock)
        {
            return NTStatus.STATUS_NOT_IMPLEMENTED;
        }

        public NTStatus UnlockFile(object handle, long byteOffset, long length)
        {
            return NTStatus.STATUS_NOT_IMPLEMENTED;
        }

        public NTStatus NotifyChange(
            out object ioRequest,
            object handle,
            NotifyChangeFilter completionFilter,
            bool watchTree,
            int outputBufferSize,
            OnNotifyChangeCompleted onNotifyChangeCompleted,
            object context)
        {
            ioRequest = null;
            return NTStatus.STATUS_NOT_IMPLEMENTED;
        }

        public NTStatus Cancel(object ioRequest)
        {
            return NTStatus.STATUS_NOT_IMPLEMENTED;
        }

        public NTStatus DeviceIOControl(object handle, uint ctlCode, byte[] input, out byte[] output, int maxOutputLength)
        {
            output = null;
            return NTStatus.STATUS_NOT_IMPLEMENTED;
        }

        private NTStatus Rename(Handle file, string newName)
        {
            var relative = NormalizeRelativePath(newName);
            if (!TryGetFullPath(relative, out var target))
            {
                return NTStatus.STATUS_ACCESS_DENIED;
            }

            try
            {
                if (file.IsDirectory)
                {
                    Directory.Move(file.FullPath, target);
                }
                else
                {
                    file.Stream?.Dispose();
                    File.Move(file.FullPath, target);
                    file.Stream = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                }

                file.RelativePath = relative;
                file.FullPath = target;
                return NTStatus.STATUS_SUCCESS;
            }
            catch (IOException e) when (e.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                return NTStatus.STATUS_OBJECT_NAME_COLLISION;
            }
            catch (DirectoryNotFoundException)
            {
                return NTStatus.STATUS_OBJECT_PATH_NOT_FOUND;
            }
            catch (UnauthorizedAccessException)
            {
                return NTStatus.STATUS_ACCESS_DENIED;
            }
        }

        private static void ApplyTimes(string path, FileBasicInformation basic)
        {
            SetTime(basic.CreationTime, time => File.SetCreationTimeUtc(path, time));
            SetTime(basic.LastAccessTime, time => File.SetLastAccessTimeUtc(path, time));
            SetTime(basic.LastWriteTime, time => File.SetLastWriteTimeUtc(path, time));
        }

        private static void SetTime(SetFileTime value, Action<DateTime> setter)
        {
            var time = (DateTime?)value;
            if (time.HasValue)
            {
                setter(time.Value.ToUniversalTime());
            }
        }

        private static void ApplyAttributes(Handle file, FileAttributes attributes)
        {
            if (attributes == 0)
            {
                return;
            }

            var value = System.IO.FileAttributes.Normal;
            if ((attributes & FileAttributes.Hidden) != 0)
            {
                value |= System.IO.FileAttributes.Hidden;
            }

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                value |= System.IO.FileAttributes.ReadOnly;
            }

            try
            {
                File.SetAttributes(file.FullPath, value);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        private static FileAttributes? GetAttributes(string path, out NTStatus status)
        {
            status = NTStatus.STATUS_SUCCESS;
            try
            {
                return ToSmbAttributes(File.GetAttributes(path));
            }
            catch (FileNotFoundException)
            {
                status = NTStatus.STATUS_OBJECT_NAME_NOT_FOUND;
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                status = NTStatus.STATUS_OBJECT_PATH_NOT_FOUND;
                return null;
            }
        }

        private static FileAttributes ToSmbAttributes(System.IO.FileAttributes attributes)
        {
            var result = FileAttributes.Normal;
            if ((attributes & System.IO.FileAttributes.Directory) != 0)
            {
                result |= FileAttributes.Directory;
            }

            if ((attributes & System.IO.FileAttributes.Hidden) != 0)
            {
                result |= FileAttributes.Hidden;
            }

            if ((attributes & System.IO.FileAttributes.ReadOnly) != 0)
            {
                result |= FileAttributes.ReadOnly;
            }

            if ((attributes & System.IO.FileAttributes.Archive) != 0)
            {
                result |= FileAttributes.Archive;
            }

            return result;
        }

        private static bool Matches(string name, string pattern)
        {
            if (pattern == "*")
            {
                return true;
            }

            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static string NormalizeRelativePath(string path)
        {
            return path.Replace('/', '\\').TrimStart('\\');
        }

        private bool TryGetFullPath(string relative, out string fullPath)
        {
            fullPath = string.Empty;
            if (relative.Length == 0)
            {
                fullPath = _root;
                return true;
            }

            var candidate = Path.GetFullPath(Path.Combine(_root, relative.Replace('\\', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !string.Equals(candidate, _root, StringComparison.Ordinal))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }

        private static Handle GetHandle(object handle, out NTStatus status)
        {
            status = NTStatus.STATUS_SUCCESS;
            if (handle is Handle file)
            {
                return file;
            }

            status = NTStatus.STATUS_INVALID_HANDLE;
            return null;
        }

        private sealed class Handle
        {
            public Handle(string relativePath, string fullPath, bool isDirectory, FileStream stream)
            {
                RelativePath = relativePath;
                FullPath = fullPath;
                IsDirectory = isDirectory;
                Stream = stream;
            }

            public string RelativePath { get; set; }
            public string FullPath { get; set; }
            public bool IsDirectory { get; }
            public FileStream Stream { get; set; }
            public bool DeleteOnClose { get; set; }
        }
    }
}
