using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Controller.Library;

public class ItemResolveArgs : EventArgs
{
	private readonly IServerApplicationPaths _appPaths;

	public IDirectoryService DirectoryService { get; private set; }

	public FileSystemMetadata[] FileSystemChildren { get; set; }

	public LibraryOptions LibraryOptions { get; set; }

	public Folder Parent { get; set; }

	public FileSystemMetadata FileInfo { get; set; }

	public string Path { get; set; }

	public bool IsDirectory => FileInfo.IsDirectory;

	public bool IsVf
	{
		get
		{
			if (!IsDirectory)
			{
				return false;
			}
			string text = BaseItem.FileSystem.GetDirectoryName(Path) ?? string.Empty;
			if (text.Length > _appPaths.RootFolderPath.Length)
			{
				return text.StartsWith(_appPaths.RootFolderPath, StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}
	}

	public bool IsPhysicalRoot
	{
		get
		{
			if (IsDirectory)
			{
				return BaseItem.FileSystem.AreEqual(Path, _appPaths.RootFolderPath);
			}
			return false;
		}
	}

	private List<string> AdditionalLocations { get; set; }

	public string[] PhysicalLocations
	{
		get
		{
			string[] array = (string.IsNullOrEmpty(Path) ? Array.Empty<string>() : new string[1] { Path });
			if (AdditionalLocations != null)
			{
				return array.Concat(AdditionalLocations).ToArray();
			}
			return array;
		}
	}

	public string CollectionType { get; set; }

	public ItemResolveArgs(IServerApplicationPaths appPaths, IDirectoryService directoryService)
	{
		_appPaths = appPaths;
		DirectoryService = directoryService;
	}

	public LibraryOptions GetLibraryOptions()
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		return LibraryOptions ?? (LibraryOptions = ((Parent != null) ? BaseItem.LibraryManager.GetLibraryOptions(Parent) : new LibraryOptions()));
	}

	public bool HasParent<T>() where T : Folder
	{
		Folder parent = Parent;
		if (parent != null)
		{
			T val = parent as T;
			if (val == null)
			{
				foreach (BaseItem parent2 in parent.GetParents())
				{
					if (parent2 is T)
					{
						return true;
					}
				}
			}
			return val != null;
		}
		return false;
	}

	public void AddAdditionalLocation(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			throw new ArgumentNullException();
		}
		if (AdditionalLocations == null)
		{
			AdditionalLocations = new List<string>();
		}
		AdditionalLocations.Add(path);
	}

	public FileSystemMetadata GetFileSystemEntryByName(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			throw new ArgumentNullException();
		}
		return GetFileSystemEntryByPath(System.IO.Path.Combine(Path, name));
	}

	public FileSystemMetadata GetFileSystemEntryByPath(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			throw new ArgumentNullException();
		}
		FileSystemMetadata[] fileSystemChildren = FileSystemChildren;
		foreach (FileSystemMetadata val in fileSystemChildren)
		{
			if (string.Equals(val.FullName, path, StringComparison.Ordinal))
			{
				return val;
			}
		}
		return null;
	}

	public bool ContainsFileSystemEntryByName(string name)
	{
		return GetFileSystemEntryByName(name) != null;
	}

	public string GetCollectionType()
	{
		return CollectionType;
	}

	public override bool Equals(object obj)
	{
		return Equals(obj as ItemResolveArgs);
	}

	public override int GetHashCode()
	{
		return Path.GetHashCode();
	}

	protected bool Equals(ItemResolveArgs args)
	{
		if (args != null)
		{
			if (args.Path == null && Path == null)
			{
				return true;
			}
			if (args.Path != null)
			{
				return BaseItem.FileSystem.AreEqual(args.Path, Path);
			}
			return false;
		}
		return false;
	}
}
