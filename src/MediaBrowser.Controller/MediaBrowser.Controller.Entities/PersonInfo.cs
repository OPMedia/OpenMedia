using System;
using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Entities;

public class PersonInfo : IHasProviderIds
{
	public Guid ItemId { get; set; }

	public string Name { get; set; }

	public string Role { get; set; }

	public string Type { get; set; }

	public int? SortOrder { get; set; }

	public string ImageUrl { get; set; }

	public Dictionary<string, string> ProviderIds { get; set; }

	public PersonInfo()
	{
		ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public override string ToString()
	{
		return Name;
	}

	public bool IsType(string type)
	{
		if (!string.Equals(Type, type, StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(Role, type, StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
