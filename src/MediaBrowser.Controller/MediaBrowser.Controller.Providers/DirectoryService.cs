using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace MediaBrowser.Controller.Providers;

public class DirectoryService : IDirectoryService
{
	private readonly ILogger _logger;

	private readonly IFileSystem _fileSystem;

	private readonly Dictionary<string, FileSystemMetadata[]> _cache = new Dictionary<string, FileSystemMetadata[]>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, FileSystemMetadata> _fileCache = new Dictionary<string, FileSystemMetadata>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, List<string>> _filePathCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

	public DirectoryService(ILogger logger, IFileSystem fileSystem)
	{
		_logger = logger;
		_fileSystem = fileSystem;
	}

	public DirectoryService(IFileSystem fileSystem)
		: this((ILogger)new NullLogger(), fileSystem)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
	}

	public FileSystemMetadata[] GetFileSystemEntries(string path)
	{
		if (!_cache.TryGetValue(path, out var value))
		{
			value = _fileSystem.GetFileSystemEntries(path, false).ToArray();
			_cache[path] = value;
		}
		return value;
	}

	public List<FileSystemMetadata> GetFiles(string path)
	{
		List<FileSystemMetadata> list = new List<FileSystemMetadata>();
		FileSystemMetadata[] fileSystemEntries = GetFileSystemEntries(path);
		foreach (FileSystemMetadata val in fileSystemEntries)
		{
			if (!val.IsDirectory)
			{
				list.Add(val);
			}
		}
		return list;
	}

	public FileSystemMetadata GetFile(string path)
	{
		if (!_fileCache.TryGetValue(path, out var value))
		{
			value = _fileSystem.GetFileInfo(path);
			if (value == null || !value.Exists)
			{
				return null;
			}
			_fileCache[path] = value;
		}
		return value;
	}

	public List<string> GetFilePaths(string path)
	{
		return GetFilePaths(path, clearCache: false);
	}

	public List<string> GetFilePaths(string path, bool clearCache)
	{
		if (clearCache || !_filePathCache.TryGetValue(path, out var value))
		{
			value = _fileSystem.GetFilePaths(path, false).ToList();
			_filePathCache[path] = value;
		}
		return value;
	}
}
