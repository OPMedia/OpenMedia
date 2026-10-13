using System;
using System.Text;

namespace MediaBrowser.Model.Extensions;

public static class StringHelper
{
	public static bool EqualsIgnoreCase(string str1, string str2)
	{
		return string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
	}

	public static string Replace(this string str, string oldValue, string newValue, StringComparison comparison)
	{
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		int num2;
		for (num2 = str.IndexOf(oldValue, comparison); num2 != -1; num2 = str.IndexOf(oldValue, num2, comparison))
		{
			stringBuilder.Append(str.Substring(num, num2 - num));
			stringBuilder.Append(newValue);
			num2 += oldValue.Length;
			num = num2;
		}
		stringBuilder.Append(str.Substring(num));
		return stringBuilder.ToString();
	}

	public static string FirstToUpper(this string str)
	{
		if (!string.IsNullOrEmpty(str))
		{
			return str.Substring(0, 1).ToUpper() + str.Substring(1);
		}
		return "";
	}
}
