using System;
using System.Collections.Generic;

namespace MediaBrowser.Model.Extensions;

public static class LinqExtensions
{
	public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
	{
		return source.DistinctBy(keySelector, null);
	}

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

	public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
	{
		if (source == null)
		{
			throw new ArgumentNullException("source");
		}
		if (keySelector == null)
		{
			throw new ArgumentNullException("keySelector");
		}
		return DistinctByImpl(source, keySelector, comparer);
	}

	private static IEnumerable<TSource> DistinctByImpl<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
	{
		HashSet<TKey> knownKeys = new HashSet<TKey>(comparer);
		foreach (TSource item in source)
		{
			if (knownKeys.Add(keySelector(item)))
			{
				yield return item;
			}
		}
	}
}
