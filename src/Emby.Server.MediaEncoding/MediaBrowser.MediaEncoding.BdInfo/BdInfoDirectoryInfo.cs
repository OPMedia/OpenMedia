using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BDInfo.IO;
using MediaBrowser.Model.IO;

namespace MediaBrowser.MediaEncoding.BdInfo;

/// <summary>
/// 把 Emby 的 <see cref="IFileSystem"/> 映射为 BDInfo 的目录视图。
/// </summary>
public class BdInfoDirectoryInfo : IDirectoryInfo
{
	private readonly IFileSystem _fileSystem;
	private readonly FileSystemMetadata _impl;

	public BdInfoDirectoryInfo(IFileSystem fileSystem, string path)
	{
		_fileSystem = fileSystem;
		_impl = _fileSystem.GetDirectoryInfo(path);
	}

	private BdInfoDirectoryInfo(IFileSystem fileSystem, FileSystemMetadata impl)
	{
		_fileSystem = fileSystem;
		_impl = impl;
	}

	public string Name => _impl.Name;

	public string FullName => _impl.FullName;

	public IDirectoryInfo Parent
	{
		get
		{
			var parentPath = _fileSystem.GetDirectoryName(_impl.FullName);
			return string.IsNullOrEmpty(parentPath)
				? null
				: new BdInfoDirectoryInfo(_fileSystem, parentPath);
		}
	}

	public IDirectoryInfo[] GetDirectories()
	{
		return _fileSystem.GetDirectories(_impl.FullName)
			.Select(directory => (IDirectoryInfo)new BdInfoDirectoryInfo(_fileSystem, directory))
			.ToArray();
	}

	public IFileInfo[] GetFiles()
	{
		return ToFiles(_fileSystem.GetFiles(_impl.FullName));
	}

	public IFileInfo[] GetFiles(string searchPattern)
	{
		return GetFiles(searchPattern, false);
	}

	public IFileInfo[] GetFiles(string searchPattern, SearchOption searchOption)
	{
		return GetFiles(searchPattern, searchOption == SearchOption.AllDirectories);
	}

	private IFileInfo[] GetFiles(string searchPattern, bool recursive)
	{
		IEnumerable<FileSystemMetadata> files;

		// BDInfo 主要传扩展名（".mpls"、".clpi"、".m2ts"）；IFileSystem 的过滤
		// 参数接受的正是这种带点的扩展名，交给它可以在枚举阶段就完成过滤。
		if (searchPattern.StartsWith('.'))
		{
			files = _fileSystem.GetFiles(_impl.FullName, new[] { searchPattern }, false, recursive);
		}
		else
		{
			// 少数调用传的是完整文件名（如 bdmt_eng.xml）或 "*.ext" 通配形式。
			var pattern = searchPattern.StartsWith("*.", StringComparison.Ordinal)
				? searchPattern[1..]
				: searchPattern;
			files = _fileSystem.GetFiles(_impl.FullName, recursive)
				.Where(file => pattern.StartsWith('.')
					? string.Equals(file.Extension, pattern, StringComparison.OrdinalIgnoreCase)
					: string.Equals(file.Name, pattern, StringComparison.OrdinalIgnoreCase));
		}

		return ToFiles(files);
	}

	private IFileInfo[] ToFiles(IEnumerable<FileSystemMetadata> files)
	{
		return files
			.Select(file => (IFileInfo)new BdInfoFileInfo(_fileSystem, file))
			.ToArray();
	}
}
