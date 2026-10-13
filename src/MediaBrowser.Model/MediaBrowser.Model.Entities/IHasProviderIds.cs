using System.Collections.Generic;

namespace MediaBrowser.Model.Entities;

public interface IHasProviderIds
{
	Dictionary<string, string> ProviderIds { get; set; }
}
