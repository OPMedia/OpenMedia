using System;
using System.Collections.Generic;

namespace MediaBrowser.Model.Extensions;

public static class LinqExtensions
{
	// DistinctBy 已由 .NET 6+ 内置 System.Linq.Enumerable.DistinctBy 提供，移除避免二义性

	public static TSource[] ToArray<TSource>(this IEnumerable<TSource> source, int count)
	{
		if (source == null)
		{
			throw new ArgumentNullException("source");
		}
		if (count < 0)
		{
			throw new ArgumentOutOfRangeException("count");
		}
		TSource[] array = new TSource[count];
		int num = 0;
		foreach (TSource item in source)
		{
			array[num++] = item;
		}
		return array;
	}
}
