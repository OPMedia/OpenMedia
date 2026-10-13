using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MediaBrowser.Model.IO;

public interface IFileSystem
{
	string DefaultDirectory { get; }

	char DirectorySeparatorChar { get; }

	void AddShortcutHandler(IShortcutHandler handler);

	bool IsShortcut(string filename);

	string ResolveShortcut(string filename);

	void CreateShortcut(string shortcutPath, string target);

	string MakeAbsolutePath(string folderPath, string filePath);

	FileSystemMetadata GetFileSystemInfo(string path);

	FileSystemMetadata GetFileInfo(string path);

	FileSystemMetadata GetDirectoryInfo(string path);

	string GetValidFilename(string filename);

	DateTime GetCreationTimeUtc(FileSystemMetadata info);

	DateTime GetCreationTimeUtc(string path);

	DateTime GetLastWriteTimeUtc(FileSystemMetadata info);

	DateTime GetLastWriteTimeUtc(string path);

	Stream GetFileStream(string path, FileOpenMode mode, FileAccessMode access, FileShareMode share, bool isAsync = false);

	Stream GetFileStream(string path, FileOpenMode mode, FileAccessMode access, FileShareMode share, FileOpenOptions fileOpenOptions);

	Stream OpenRead(string path);

	void SwapFiles(string file1, string file2);

	bool AreEqual(string path1, string path2);

	bool ContainsSubPath(string parentPath, string path);

	bool IsRootPath(string path);

	string NormalizePath(string path);

	string GetDirectoryName(string path);

	string GetFileNameWithoutExtension(FileSystemMetadata info);

	string GetFileNameWithoutExtension(string path);

	bool IsPathFile(string path);

	void DeleteFile(string path);

	void DeleteDirectory(string path, bool recursive);

	IEnumerable<FileSystemMetadata> GetDirectories(string path, bool recursive = false);

	IEnumerable<FileSystemMetadata> GetFiles(string path, bool recursive = false);

	IEnumerable<FileSystemMetadata> GetFiles(string path, string[] extensions, bool enableCaseSensitiveExtensions, bool recursive);

	IEnumerable<FileSystemMetadata> GetFileSystemEntries(string path, bool recursive = false);

	void CreateDirectory(string path);

	void CopyFile(string source, string target, bool overwrite);

	void MoveFile(string source, string target);

	void MoveDirectory(string source, string target);

	bool DirectoryExists(string path);

	bool FileExists(string path);

	string ReadAllText(string path);

	byte[] ReadAllBytes(string path);

	void WriteAllBytes(string path, byte[] bytes);

	void WriteAllText(string path, string text);

	void WriteAllText(string path, string text, Encoding encoding);

	string ReadAllText(string path, Encoding encoding);

	string[] ReadAllLines(string path);

	void WriteAllLines(string path, IEnumerable<string> lines);

	IEnumerable<string> GetDirectoryPaths(string path, bool recursive = false);

	IEnumerable<string> GetFilePaths(string path, bool recursive = false);

	IEnumerable<string> GetFilePaths(string path, string[] extensions, bool enableCaseSensitiveExtensions, bool recursive);

	IEnumerable<string> GetFileSystemEntryPaths(string path, bool recursive = false);

	void SetHidden(string path, bool isHidden);

	void SetReadOnly(string path, bool readOnly);

	void SetAttributes(string path, bool isHidden, bool readOnly);

	string GetFullPath(string path);

	List<FileSystemMetadata> GetDrives();

	void SetExecutable(string path);
}
