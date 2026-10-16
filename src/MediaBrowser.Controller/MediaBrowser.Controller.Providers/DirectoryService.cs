using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace MediaBrowser.Controller.Providers;

/// <summary>
/// S10（soak-diagnosis / Reviewer 形状约束）：三个 per-path 缓存字典的治理。
/// 形状——ClearCache（扫描收尾 finally 接线）+ 大容量上限兜底（500k，超限整体
/// Clear；per-path 缓存重建成本低——下次访问时 FS 枚举一次）。
/// D2 僵尸滞留终判：30min 定时器 × 队列触发打断在跑扫描（0 Completed 实证），
/// 被取消段的 async 状态机经 TaskManager 间接持有三字典不释放——653k 是
/// 7 段僵尸缓存的累积。ClearCache 接 PerformLibraryValidation 的 finally
/// （完成/取消/异常三态都清）。
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
	/// S10：三个 per-path 缓存字典全清。扫描收尾（finally 三态）调用——
	/// 30min 定时器 × 队列触发会打断在跑的扫描（0 Completed 实证），被取消段的
	/// async 状态机经 TaskManager 间接持有不随扫描死亡；清空后 metadata 可回收，
	/// 僵尸状态机留着也无害。
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
