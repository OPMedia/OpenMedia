using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Extensions;

namespace MediaBrowser.Model.Services;

public class QueryParamCollection : List<NameValuePair>
{
	public IEnumerable<string> Keys
	{
		get
		{
			string[] array = new string[Count];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = base[i].Name;
			}
			return array;
		}
	}

	public string this[string name]
	{
		get
		{
			return Get(name);
		}
		set
		{
			Set(name, value);
		}
	}

	public QueryParamCollection()
	{
	}

	public QueryParamCollection(IDictionary<string, string> headers)
	{
		foreach (KeyValuePair<string, string> header in headers)
		{
			Add(header.Key, header.Value);
		}
	}

	private StringComparison GetStringComparison()
	{
		return StringComparison.OrdinalIgnoreCase;
	}

	private StringComparer GetStringComparer()
	{
		return StringComparer.OrdinalIgnoreCase;
	}

	public string GetKey(int index)
	{
		return base[index].Name;
	}

	public string Get(int index)
	{
		return base[index].Value;
	}

	public virtual string[] GetValues(int index)
	{
		return new string[1] { Get(index) };
	}

	public virtual void Add(string key, string value)
	{
		Add(new NameValuePair(key, value));
	}

	public virtual void Set(string key, string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			foreach (NameValuePair item in GetItems(key))
			{
				Remove(item);
			}
			return;
		}
		using (Enumerator enumerator = GetEnumerator())
		{
			while (enumerator.MoveNext())
			{
				NameValuePair current2 = enumerator.Current;
				StringComparison stringComparison = GetStringComparison();
				if (string.Equals(key, current2.Name, stringComparison))
				{
					current2.Value = value;
					return;
				}
			}
		}
		Add(key, value);
	}

	public virtual int Remove(string name)
	{
		return RemoveAll((NameValuePair p) => p.Name == name);
	}

	public string Get(string name)
	{
		StringComparison stringComparison = GetStringComparison();
		using (Enumerator enumerator = GetEnumerator())
		{
			while (enumerator.MoveNext())
			{
				NameValuePair current = enumerator.Current;
				if (string.Equals(current.Name, name, stringComparison))
				{
					return current.Value;
				}
			}
		}
		return null;
	}

	public virtual List<NameValuePair> GetItems(string name)
	{
		StringComparison stringComparison = GetStringComparison();
		List<NameValuePair> list = new List<NameValuePair>();
		using Enumerator enumerator = GetEnumerator();
		while (enumerator.MoveNext())
		{
			NameValuePair current = enumerator.Current;
			if (string.Equals(current.Name, name, stringComparison))
			{
				list.Add(current);
			}
		}
		return list;
	}

	public virtual List<string> GetValues(string name)
	{
		StringComparison stringComparison = GetStringComparison();
		List<string> list = new List<string>();
		using Enumerator enumerator = GetEnumerator();
		while (enumerator.MoveNext())
		{
			NameValuePair current = enumerator.Current;
			if (string.Equals(current.Name, name, stringComparison))
			{
				list.Add(current.Value);
			}
		}
		return list;
	}

	public Dictionary<string, string> ToDictionary()
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(GetStringComparer());
		using Enumerator enumerator = GetEnumerator();
		while (enumerator.MoveNext())
		{
			NameValuePair current = enumerator.Current;
			dictionary[current.Name] = current.Value;
		}
		return dictionary;
	}

	private string GetQueryStringValue(NameValuePair pair)
	{
		return pair.Name + "=" + pair.Value;
	}

	public override string ToString()
	{
		string[] value = this.Select(GetQueryStringValue).ToArray(Count);
		return string.Join("&", value);
	}
}
