using System;

namespace MediaBrowser.Model.Extensions;

public static class ListHelper
{
	public static bool ContainsIgnoreCase(string[] list, string value)
	{
		for (int i = 0; i < list.Length; i++)
		{
			if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}
