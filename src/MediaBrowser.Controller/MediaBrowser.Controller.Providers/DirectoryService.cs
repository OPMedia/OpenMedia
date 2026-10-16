using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace MediaBrowser.Controller.Providers;

/// <summary>
/// S10（soak-diagnosis / Reviewer 形状约束）：三个 per-path 缓存字典的治理。
/// 形状——大容量上限兜底（500k，超限整体 Clear；per-path 缓存重建成本低——
/// 下次访问时 FS 枚举一次）。1.18M 路径单轮扫描中途清 2-3 次，良性。
/// 注：ClearCache 已按 Chief 处置①撤除——D2 构造点终判证实 DirectoryService
/// 无长命持有者（DI 注册零、字段持有零、6 个构造点全部方法级 new），
/// 唯一长命引用链是在途扫描的 async 状态机（扫描结束即随根死亡），
/// 653k 是扫描在飞时 gcdump 的合法可达工作集，非泄漏。
/// </summary>
public class DirectoryService : IDirectoryService
{
	/// <summary>兜底容量上限（880k 文件树 × 0.57 余量；超限整体 Clear）。</summary>
	private const int CacheCapacityLimit = 500000;

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
	}

	private void EnforceCapacity(IDictionary<string, FileSystemMetadata[]> cache)
	{
		if (cache.Count >= CacheCapacityLimit)
		{
			cache.Clear();
		}
	}

	private void EnforceCapacity(IDictionary<string, FileSystemMetadata> cache)
	{
		if (cache.Count >= CacheCapacityLimit)
		{
			cache.Clear();
		}
	}

	private void EnforceFilePathCapacity()
	{
		if (_filePathCache.Count >= CacheCapacityLimit)
		{
			_filePathCache.Clear();
		}
	}

	public FileSystemMetadata[] GetFileSystemEntries(string path)
	{
		if (!_cache.TryGetValue(path, out var value))
		{
			EnforceCapacity(_cache);
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
			EnforceCapacity(_fileCache);
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
			EnforceFilePathCapacity();
			value = _fileSystem.GetFilePaths(path, false).ToList();
			_filePathCache[path] = value;
		}
		return value;
	}
}
