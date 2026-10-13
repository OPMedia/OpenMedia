using System;
using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Providers;

public class ItemLookupInfo : IHasProviderIds
{
	protected static string[] EmptyStringArray = Array.Empty<string>();

	public string Name { get; set; }

	public string MetadataLanguage { get; set; }

	public string MetadataCountryCode { get; set; }

	public Dictionary<string, string> ProviderIds { get; set; }

	public int? Year { get; set; }

	public int? IndexNumber { get; set; }

	public int? ParentIndexNumber { get; set; }

	public DateTime? PremiereDate { get; set; }

	public bool IsAutomated { get; set; }

	public ItemLookupInfo()
	{
		IsAutomated = true;
		ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}
}
