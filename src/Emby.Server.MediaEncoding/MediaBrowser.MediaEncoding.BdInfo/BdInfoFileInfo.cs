using System;
using System.IO;
using BDInfo.IO;
using MediaBrowser.Model.IO;

namespace MediaBrowser.MediaEncoding.BdInfo;

/// <summary>
/// 把 Emby 的 <see cref="FileSystemMetadata"/> 映射为 BDInfo 的文件视图。
/// </summary>
public class BdInfoFileInfo : IFileInfo
{
	private readonly IFileSystem _fileSystem;
	private readonly FileSystemMetadata _impl;

	public BdInfoFileInfo(IFileSystem fileSystem, FileSystemMetadata impl)
	{
		_fileSystem = fileSystem;
		_impl = impl;
	}

	public string Name => _impl.Name;

	public string FullName => _impl.FullName;

	public string Extension => _impl.Extension;

	public long Length => _impl.Length;

	public bool IsDir => _impl.IsDirectory;

	public Stream OpenRead()
	{
		// playlist / clipinfo / 元数据都是小块二进制或 XML，BDInfo 解析时会整块读入；
		// 走 IFileSystem.ReadAllBytes 以保留本地与 SMB 的统一路由。
		// m2ts / ssif 可能达到数十 GB，必须保持流式读取，不能一次性加载。
		if (IsMetadataFile(_impl.Extension))
		{
			return new MemoryStream(_fileSystem.ReadAllBytes(_impl.FullName), false);
		}

		return _fileSystem.OpenRead(_impl.FullName);
	}

	public StreamReader OpenText()
	{
		// StreamReader 默认使用带 BOM 探测的 UTF-8（无 BOM 时回退 UTF-8）。
		return new StreamReader(OpenRead());
	}

	private static bool IsMetadataFile(string extension)
	{
		return extension.Equals(".mpls", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".clpi", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".bdmv", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
	}
}
