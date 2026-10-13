using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Extensions;

namespace MediaBrowser.Controller.Entities;

public static class TagExtensions
{
	public static void AddTag(this BaseItem item, string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentNullException("name");
		}
		string[] tags = item.Tags;
		if (!tags.Contains(name, StringComparer.OrdinalIgnoreCase))
		{
			if (tags.Length == 0)
			{
				item.Tags = new string[1] { name };
			}
			else
			{
				string[] array = LinqExtensions.ToArray<string>((IEnumerable<string>)tags, tags.Length + 1);
				array[^1] = name;
				item.Tags = array;
			}
		}
	}
}
