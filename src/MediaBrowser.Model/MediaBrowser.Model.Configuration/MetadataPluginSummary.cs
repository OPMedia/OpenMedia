using System;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Configuration;

public class MetadataPluginSummary
{
	public string ItemType { get; set; }

	public MetadataPlugin[] Plugins { get; set; }

	public ImageType[] SupportedImageTypes { get; set; }

	public MetadataPluginSummary()
	{
		SupportedImageTypes = Array.Empty<ImageType>();
		Plugins = Array.Empty<MetadataPlugin>();
	}
}
