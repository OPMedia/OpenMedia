using System;
using MediaBrowser.Model.Configuration;

namespace MediaBrowser.Model.Entities;

public class VirtualFolderInfo
{
	public string Name { get; set; }

	public string[] Locations { get; set; }

	public string CollectionType { get; set; }

	public LibraryOptions LibraryOptions { get; set; }

	public string ItemId { get; set; }

	public string PrimaryImageItemId { get; set; }

	public double? RefreshProgress { get; set; }

	public string RefreshStatus { get; set; }

	public VirtualFolderInfo()
	{
		Locations = Array.Empty<string>();
	}
}
