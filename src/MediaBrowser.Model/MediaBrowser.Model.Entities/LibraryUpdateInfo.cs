using System;

namespace MediaBrowser.Model.Entities;

public class LibraryUpdateInfo
{
	public string[] FoldersAddedTo { get; set; }

	public string[] FoldersRemovedFrom { get; set; }

	public string[] ItemsAdded { get; set; }

	public string[] ItemsRemoved { get; set; }

	public string[] ItemsUpdated { get; set; }

	public string[] CollectionFolders { get; set; }

	public bool IsEmpty
	{
		get
		{
			if (FoldersAddedTo.Length == 0 && FoldersRemovedFrom.Length == 0 && ItemsAdded.Length == 0 && ItemsRemoved.Length == 0 && ItemsUpdated.Length == 0)
			{
				return CollectionFolders.Length == 0;
			}
			return false;
		}
	}

	public LibraryUpdateInfo()
	{
		FoldersAddedTo = Array.Empty<string>();
		FoldersRemovedFrom = Array.Empty<string>();
		ItemsAdded = Array.Empty<string>();
		ItemsRemoved = Array.Empty<string>();
		ItemsUpdated = Array.Empty<string>();
		CollectionFolders = Array.Empty<string>();
	}
}
