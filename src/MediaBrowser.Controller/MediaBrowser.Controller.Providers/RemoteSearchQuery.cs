using System;

namespace MediaBrowser.Controller.Providers;

public class RemoteSearchQuery<T> where T : ItemLookupInfo
{
	public T SearchInfo { get; set; }

	public Guid ItemId { get; set; }

	public string SearchProviderName { get; set; }

	public bool IncludeDisabledProviders { get; set; }
}
