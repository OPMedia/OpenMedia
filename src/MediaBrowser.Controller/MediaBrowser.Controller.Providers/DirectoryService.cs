using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace MediaBrowser.Controller.Providers;

/// <summary>
/// S10（soak-diagnosis / Reviewer 形状约束）：三个 per-path 缓存字典的治理。
/// 形状选择——不是小 LRU（880k 路径工作集下 10k 容量 = 每轮扫描全 miss + 逐出风暴），
/// 而是"扫描感知清理 + 大容量上限兜底"：
/// - ClearCache()：扫描收尾时调用（_filePathCache 的 clearCache 参数是旧先例，
///   扩展到三个字典）——每轮扫描后干净起步，跨轮不累积
/// - 容量上限 500k（兜底防异常场景）：超限整体 Clear（per-path 缓存重建成本低——
///   下次访问时 FS 枚举一次）
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

	/// <summary>
	/// S10 扫描感知清理：扫描收尾调用——三个字典全清。
	/// DirectoryService 每次 new 本身就是短生命周期（方法级创建），
	/// 但 soak 实证 8 次扫描的 metadata 死对象在 gen2/LOH 惰性堆积（653k/4h）。
	/// 此方法给扫描编排方一个显式清理点（配合容量兜底双保险）。
	/// </summary>
	public void ClearCache()
	{
		_cache.Clear();
		_fileCache.Clear();
		_filePathCache.Clear();
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
