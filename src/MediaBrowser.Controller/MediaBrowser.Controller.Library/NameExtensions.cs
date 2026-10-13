using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Model.Extensions;

namespace MediaBrowser.Controller.Library;

public static class NameExtensions
{
	private static string RemoveDiacritics(string name)
	{
		if (name == null)
		{
			return string.Empty;
		}
		return name.RemoveDiacritics();
	}

	public static IEnumerable<string> DistinctNames(this IEnumerable<string> names)
	{
		return Enumerable.DistinctBy<string, string>(names, (Func<string, string>)RemoveDiacritics, (IEqualityComparer<string>)StringComparer.OrdinalIgnoreCase);
	}
}
