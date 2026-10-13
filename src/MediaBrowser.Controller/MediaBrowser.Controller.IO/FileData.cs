using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace MediaBrowser.Controller.IO;

public static class FileData
{
	private static Dictionary<string, FileSystemMetadata> GetFileSystemDictionary(FileSystemMetadata[] list)
	{
		Dictionary<string, FileSystemMetadata> dictionary = new Dictionary<string, FileSystemMetadata>(StringComparer.OrdinalIgnoreCase);
		foreach (FileSystemMetadata val in list)
		{
			dictionary[val.FullName] = val;
		}
		return dictionary;
	}

	public static FileSystemMetadata[] GetFilteredFileSystemEntries(IDirectoryService directoryService, string path, IFileSystem fileSystem, IServerApplicationHost appHost, ILogger logger, ItemResolveArgs args, int flattenFolderDepth = 0, bool resolveShortcuts = true)
	{
		if (string.IsNullOrEmpty(path))
		{
			throw new ArgumentNullException("path");
		}
		if (args == null)
		{
			throw new ArgumentNullException("args");
		}
		FileSystemMetadata[] fileSystemEntries = directoryService.GetFileSystemEntries(path);
		if (!resolveShortcuts && flattenFolderDepth == 0)
		{
			return fileSystemEntries;
		}
		Dictionary<string, FileSystemMetadata> dictionary = new Dictionary<string, FileSystemMetadata>(StringComparer.OrdinalIgnoreCase);
		FileSystemMetadata[] array = fileSystemEntries;
		foreach (FileSystemMetadata val in array)
		{
			bool isDirectory = val.IsDirectory;
			string fullName = val.FullName;
			if (resolveShortcuts && fileSystem.IsShortcut(fullName))
			{
				try
				{
					string text = appHost.ExpandVirtualPath(fileSystem.ResolveShortcut(fullName));
					if (string.IsNullOrEmpty(text))
					{
						logger.Warn("Encountered invalid shortcut: " + fullName, Array.Empty<object>());
						continue;
					}
					FileSystemMetadata directoryInfo = fileSystem.GetDirectoryInfo(text);
					args.AddAdditionalLocation(text);
					dictionary[text] = directoryInfo;
				}
				catch (Exception ex)
				{
					logger.ErrorException("Error resolving shortcut from {0}", ex, new object[1] { fullName });
				}
			}
			else if ((flattenFolderDepth > 0) & isDirectory)
			{
				FileSystemMetadata[] filteredFileSystemEntries = GetFilteredFileSystemEntries(directoryService, fullName, fileSystem, appHost, logger, args, flattenFolderDepth - 1, resolveShortcuts);
				foreach (FileSystemMetadata val2 in filteredFileSystemEntries)
				{
					dictionary[val2.FullName] = val2;
				}
			}
			else
			{
				dictionary[fullName] = val;
			}
		}
		FileSystemMetadata[] array2 = new FileSystemMetadata[dictionary.Count];
		int num = 0;
		foreach (FileSystemMetadata value in dictionary.Values)
		{
			array2[num] = value;
			num++;
		}
		return array2;
	}
}
