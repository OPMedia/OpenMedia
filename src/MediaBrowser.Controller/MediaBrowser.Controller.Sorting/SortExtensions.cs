using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MediaBrowser.Controller.Sorting;

public static class SortExtensions
{
	private class AlphanumComparator : IComparer<string>
	{
		private enum ChunkType
		{
			Alphanumeric,
			Numeric
		}

		private static bool InChunk(char ch, char otherCh)
		{
			ChunkType chunkType = ChunkType.Alphanumeric;
			if (char.IsDigit(otherCh))
			{
				chunkType = ChunkType.Numeric;
			}
			if ((chunkType == ChunkType.Alphanumeric && char.IsDigit(ch)) || (chunkType == ChunkType.Numeric && !char.IsDigit(ch)))
			{
				return false;
			}
			return true;
		}

		public static int CompareValues(string s1, string s2)
		{
			if (s1 == null || s2 == null)
			{
				return 0;
			}
			int num = 0;
			int result = 0;
			int num2 = 0;
			int result2 = 0;
			while (num < s1.Length || num2 < s2.Length)
			{
				if (num >= s1.Length)
				{
					return -1;
				}
				if (num2 >= s2.Length)
				{
					return 1;
				}
				char c = s1[num];
				char c2 = s2[num2];
				StringBuilder stringBuilder = new StringBuilder();
				StringBuilder stringBuilder2 = new StringBuilder();
				while (num < s1.Length && (stringBuilder.Length == 0 || InChunk(c, stringBuilder[0])))
				{
					stringBuilder.Append(c);
					num++;
					if (num < s1.Length)
					{
						c = s1[num];
					}
				}
				while (num2 < s2.Length && (stringBuilder2.Length == 0 || InChunk(c2, stringBuilder2[0])))
				{
					stringBuilder2.Append(c2);
					num2++;
					if (num2 < s2.Length)
					{
						c2 = s2[num2];
					}
				}
				int num3 = 0;
				if (char.IsDigit(stringBuilder[0]) && char.IsDigit(stringBuilder2[0]))
				{
					if (!int.TryParse(stringBuilder.ToString(), out result))
					{
						return 0;
					}
					if (!int.TryParse(stringBuilder2.ToString(), out result2))
					{
						return 0;
					}
					if (result < result2)
					{
						num3 = -1;
					}
					if (result > result2)
					{
						num3 = 1;
					}
				}
				else
				{
					num3 = stringBuilder.ToString().CompareTo(stringBuilder2.ToString());
				}
				if (num3 != 0)
				{
					return num3;
				}
			}
			return 0;
		}

		public int Compare(string x, string y)
		{
			return CompareValues(x, y);
		}
	}

	public static IEnumerable<T> OrderByString<T>(this IEnumerable<T> list, Func<T, string> getName)
	{
		return list.OrderBy(getName, new AlphanumComparator());
	}

	public static IEnumerable<T> OrderByStringDescending<T>(this IEnumerable<T> list, Func<T, string> getName)
	{
		return list.OrderByDescending(getName, new AlphanumComparator());
	}

	public static IOrderedEnumerable<T> ThenByString<T>(this IOrderedEnumerable<T> list, Func<T, string> getName)
	{
		return list.ThenBy(getName, new AlphanumComparator());
	}

	public static IOrderedEnumerable<T> ThenByStringDescending<T>(this IOrderedEnumerable<T> list, Func<T, string> getName)
	{
		return list.ThenByDescending(getName, new AlphanumComparator());
	}
}
