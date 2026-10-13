using System;

namespace MediaBrowser.Model.IO;

public class FileSystemMetadata
{
	public bool Exists { get; set; }

	public string FullName { get; set; }

	public string Name { get; set; }

	public string Extension { get; set; }

	public long Length { get; set; }

	public string DirectoryName { get; set; }

	public DateTime LastWriteTimeUtc { get; set; }

	public DateTime CreationTimeUtc { get; set; }

	public bool IsDirectory { get; set; }
}
