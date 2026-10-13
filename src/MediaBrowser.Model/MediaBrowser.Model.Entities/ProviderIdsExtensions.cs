using System;
using System.Collections.Generic;

namespace MediaBrowser.Model.Entities;

public static class ProviderIdsExtensions
{
	public static bool HasProviderId(this IHasProviderIds instance, MetadataProviders provider)
	{
		return !string.IsNullOrEmpty(instance.GetProviderId(provider.ToString()));
	}

	public static string GetProviderId(this IHasProviderIds instance, MetadataProviders provider)
	{
		return instance.GetProviderId(provider.ToString());
	}

	public static string GetProviderId(this IHasProviderIds instance, string name)
	{
		if (instance.ProviderIds == null)
		{
			return null;
		}
		instance.ProviderIds.TryGetValue(name, out var value);
		return value;
	}

	public static void SetProviderId(this IHasProviderIds instance, string name, string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			if (instance.ProviderIds != null && instance.ProviderIds.ContainsKey(name))
			{
				instance.ProviderIds.Remove(name);
			}
			return;
		}
		if (instance.ProviderIds == null)
		{
			instance.ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		}
		instance.ProviderIds[name] = value;
	}

	public static void SetProviderId(this IHasProviderIds instance, MetadataProviders provider, string value)
	{
		instance.SetProviderId(provider.ToString(), value);
	}
}
