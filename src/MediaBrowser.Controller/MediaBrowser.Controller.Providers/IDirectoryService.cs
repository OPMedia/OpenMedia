using System.Collections.Generic;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Controller.Providers;

public interface IDirectoryService
{
	FileSystemMetadata[] GetFileSystemEntries(string path);

	List<FileSystemMetadata> GetFiles(string path);

	FileSystemMetadata GetFile(string path);

	List<string> GetFilePaths(string path);

	List<string> GetFilePaths(string path, bool clearCache);

	/// <summary>S10 扫描感知清理：扫描收尾调用，三个 per-path 缓存字典全清。</summary>
	void ClearCache();
}
